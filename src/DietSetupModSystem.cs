using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using dietsetup.Binding;
using dietsetup.Diet;
using dietsetup.Grants;
using dietsetup.Rules;
using dietsetup.Tags;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace dietsetup;

public class DietSetupModSystem : ModSystem
{
    public static string AttrIntake(string tag) => $"dietsetup:intake:{tag}";
    public static string AttrIntakeUpdatedHours(string tag) => $"dietsetup:intake:{tag}:updatedHours";
    private const string OldAttrRotIntake = "dietsetup:rotIntake";
    private const string OldAttrRotIntakeUpdatedHours = "dietsetup:rotIntakeUpdatedHours";

    private const string HarmonyId = "dietsetup";

    private DietRuntimeSnapshot snapshot = DietRuntimeSnapshot.Empty;
    internal DietRuntimeSnapshot Snapshot => Volatile.Read(ref snapshot);
    internal void Publish(DietRuntimeSnapshot value) => Volatile.Write(ref snapshot, value);
    private DietSetupConfig initialConfig = new();
    private DietSetupConfig Config => Snapshot.Config;
    private ICoreAPI? ownerApi;

    private readonly Dictionary<long, string> lastConsumption = new();
    internal void RecordConsumption(long entity, string report) => lastConsumption[entity] = report;
    internal string LastConsumption(long entity) => lastConsumption.TryGetValue(entity, out var text) ? text : "No recorded consumption this session.";
    private ICoreServerAPI? sapi;
    private ICoreClientAPI? capi;
    private Harmony? harmony;

    private const string BindingsChannelName = "dietsetup-bindings";
    private IServerNetworkChannel? serverBindingsChannel;

    public BindingsFile CurrentBindings => new() { SchemaVersion = Snapshot.Bindings.SchemaVersion,
        Default = Snapshot.Bindings.Default, Bindings = new(Snapshot.Bindings.Bindings) };
    private static readonly object patchLock = new();
    private static int patchOwners;
    private bool ownsPatches;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        ownerApi = api;
        initialConfig = api.Side == EnumAppSide.Server ? LoadConfig(api) : new DietSetupConfig();

        api.Logger.Notification("[{0}] Build {1} ({2}{3})", Mod.Info.ModID, Mod.Info.Version,
            GitInfo.Sha, GitInfo.Dirty ? "-dirty" : "");

        if (!api.ModLoader.IsModEnabled("raceframework"))
        {
            api.Logger.Notification("[{0}] raceframework not detected — trait-based diet bindings will never match; running in Mods-solo mode.", Mod.Info.ModID);
        }
        DietEffects.Register("dietsetup:debuglog", new DebugLogConsequenceEffect());
        lock (patchLock)
        {
            if (patchOwners == 0)
            {
                harmony = new Harmony(HarmonyId);
                InstallPatches(harmony, () => harmony.PatchAll(Assembly.GetExecutingAssembly()));
            }
            patchOwners++;
            ownsPatches = true;
        }
    }

    internal static void InstallPatches(Harmony owner, Action install)
    {
        try { install(); }
        catch (Exception ex)
        {
            owner.UnpatchAll(owner.Id);
            throw new InvalidOperationException("DietSetup startup failed; all DietSetup patches were rolled back.", ex);
        }
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        base.AssetsFinalize(api);
        DietLoadPipeline.RunAndLog(api, initialConfig);
    }

    public override void Dispose()
    {
        lock (patchLock)
        {
            if (ownsPatches && --patchOwners == 0) new Harmony(HarmonyId).UnpatchAll(HarmonyId);
            ownsPatches = false;
        }
        DietSpoilageResolution.ClearCache();
        if (ownerApi != null) FoodOverrideRegistry.Reset(ownerApi);
        lastConsumption.Clear();
        Publish(DietRuntimeSnapshot.Empty);
        base.Dispose();
    }
    private static DietSetupConfig LoadConfig(ICoreAPI api)
    {
        const string filename = "dietsetup.json";
        string configPath = Path.Combine(GamePaths.ModConfig, filename);
        bool fileExisted = File.Exists(configPath);

        DietSetupConfig? loaded;
        bool malformed = false;
        try
        {
            loaded = api.LoadModConfig<DietSetupConfig>(filename);
        }
        catch (Exception ex)
        {
            api.Logger.Error("[dietsetup] Failed to parse dietsetup.json, using defaults without overwriting the file: {0}", ex);
            loaded = null;
            malformed = true;
        }
        if (loaded == null && fileExisted && !malformed)
        {
            malformed = true;
            api.Logger.Error("[dietsetup] {0} exists but produced no usable data on parse (empty or unrecognized content) -- using defaults without overwriting the file.", filename);
        }

        var config = loaded ?? new DietSetupConfig();
        try { config.Validate(); }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"Invalid dietsetup.json: {ex.Message}", ex);
        }

        if (malformed)
        {
            return config;
        }
        bool safeToOverwrite = loaded == null || WarnAndBackupIfFieldsWillBeDropped(api, filename);
        if (!safeToOverwrite)
        {
            api.Logger.Warning("[dietsetup] Skipping rewrite of {0} this session -- couldn't confirm a backup of fields that would be dropped. Will retry next load.", filename);
            return config;
        }

        api.StoreModConfig(config, filename);
        return config;
    }
    private static bool WarnAndBackupIfFieldsWillBeDropped(ICoreAPI api, string filename)
    {
        try
        {
            JsonObject raw = api.LoadModConfig(filename);
            if (raw.Token is not JObject rawObj) return true;

            var knownKeys = new HashSet<string>(
                typeof(DietSetupConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name),
                StringComparer.OrdinalIgnoreCase);
            List<string> droppedKeys = rawObj.Properties().Select(p => p.Name).Where(k => !knownKeys.Contains(k)).ToList();

            if (droppedKeys.Count == 0) return true;

            string configPath = Path.Combine(GamePaths.ModConfig, filename);
            string backupPath = configPath + ".bak";
            File.Copy(configPath, backupPath, true);
            api.Logger.Warning(
                "[dietsetup] {0} contains fields this version no longer uses ({1}) -- they will be dropped when the file is rewritten. A backup of the pre-update file was saved to {2}.",
                filename, string.Join(", ", droppedKeys), backupPath);
            return true;
        }
        catch (Exception ex)
        {
            api.Logger.Warning("[dietsetup] Could not back up {0} before rewrite (fields would be dropped with no backup): {1}", filename, ex);
            return false;
        }
    }
    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
        sapi = api;

        serverBindingsChannel = api.Network.RegisterChannel(BindingsChannelName)
            .RegisterMessageType<DietConfigurationPacket>();

        api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
        api.Event.PlayerDisconnect += OnPlayerDisconnect;

        RegisterDrainSatietyCommand(api);
        RegisterRotIntakeDebugCommand(api);
        RegisterSetNutritionCommand(api);
        RegisterAssignRulesDietCommand(api);
        RegisterDiagCommand(api);
        RegisterDietReloadCommand(api);
        RegisterDietShowCommand(api);
        RegisterFactsQueueDiagCommand(api);
        RegisterFoodDiagnostic(api);
    }
    private void OnPlayerDisconnect(IServerPlayer byPlayer)
    {
        DietProfileRegistry.RemoveNutritionMultiplierQueue(byPlayer.Entity.EntityId);
        lastConsumption.Remove(byPlayer.Entity.EntityId);

    }

    private void OnPlayerNowPlaying(IServerPlayer byPlayer)
    {
        MigrateLegacyRotIntakeIfNeeded(byPlayer);
        serverBindingsChannel?.SendPacket(DietConfigurationPacket.From(Snapshot), byPlayer);
    }
    private static void MigrateLegacyRotIntakeIfNeeded(IServerPlayer byPlayer)
    {
        ITreeAttribute wa = byPlayer.Entity.WatchedAttributes;
        if (!wa.HasAttribute(OldAttrRotIntake)) return;

        wa.SetDouble(AttrIntake("rot"), wa.GetDouble(OldAttrRotIntake, 0.0));
        wa.RemoveAttribute(OldAttrRotIntake);

        if (wa.HasAttribute(OldAttrRotIntakeUpdatedHours))
        {
            wa.SetDouble(AttrIntakeUpdatedHours("rot"), wa.GetDouble(OldAttrRotIntakeUpdatedHours, 0.0));
            wa.RemoveAttribute(OldAttrRotIntakeUpdatedHours);
        }
    }
    private void RegisterDrainSatietyCommand(ICoreServerAPI api)
    {
        api.ChatCommands.Create("dietdrainsatiety")
            .WithDescription("Debug: zero your own satiety without touching nutrition levels, to speed up testing")
            .RequiresPrivilege(Privilege.controlserver)
            .HandleWith(args =>
            {
                IPlayer caller = args.Caller.Player;
                EntityBehaviorHunger? hunger = caller?.Entity?.GetBehavior<EntityBehaviorHunger>();
                if (hunger == null)
                {
                    return TextCommandResult.Error("No hunger behavior found on your entity.");
                }

                hunger.Saturation = 0f;
                return TextCommandResult.Success("Satiety drained to 0. Nutrition levels untouched.");
            });
    }
    private void RegisterRotIntakeDebugCommand(ICoreServerAPI api)
    {
        api.ChatCommands.Create("dietrotintake")
            .WithDescription("Debug: get/set/clear your own rot-intake accumulator (dietsetup:intake:rot), for testing rfmechanics' goblin rot aura without eating rotten food and waiting for decay.")
            .RequiresPrivilege(Privilege.controlserver)
            .WithArgs(api.ChatCommands.Parsers.OptionalFloat("value"))
            .HandleWith(args =>
            {
                IPlayer caller = args.Caller.Player;
                ITreeAttribute wa = caller.Entity.WatchedAttributes;
                string valueKey = AttrIntake("rot");
                string updatedKey = AttrIntakeUpdatedHours("rot");
                double halfLife = Config.IntakeHalfLifeHours.TryGetValue("rot", out double h) ? h : 48.0;

                if (args.Parsers[0].IsMissing)
                {
                    double nowHours = caller.Entity.World.Calendar.TotalHours;
                    double lastHours = wa.GetDouble(updatedKey, nowHours);
                    double raw = wa.GetDouble(valueKey, 0.0);
                    return TextCommandResult.Success($"{valueKey}={raw:F4}, elapsed {nowHours - lastHours:F2}h since last write (halfLife={halfLife:F1}h).");
                }

                float value = (float)args[0];
                wa.SetDouble(valueKey, value);
                wa.SetDouble(updatedKey, caller.Entity.World.Calendar.TotalHours);
                return TextCommandResult.Success($"Set {valueKey}={value:F4} (timestamp reset to now, cap is {Config.RotIntakeCap:F2}). Check rfmechanics' /rfrotdiag to see the resulting aura shape.");
            });
    }
    private void RegisterSetNutritionCommand(ICoreServerAPI api)
    {
        api.ChatCommands.Create("dietsetnutrition")
            .WithDescription("Debug: set a nutrition level (or 'all' for all five) directly, clamped to your live maxsaturation. Prints all five levels and the current max-health bonus.")
            .RequiresPrivilege(Privilege.controlserver)
            .WithArgs(
                api.ChatCommands.Parsers.WordRange("category", "Fruit", "Vegetable", "Protein", "Grain", "Dairy", "all"),
                api.ChatCommands.Parsers.Float("value"))
            .HandleWith(args =>
            {
                IPlayer caller = args.Caller.Player;
                EntityBehaviorHunger? hunger = caller?.Entity?.GetBehavior<EntityBehaviorHunger>();
                if (hunger == null)
                {
                    return TextCommandResult.Error("No hunger behavior found on your entity.");
                }

                string category = (string)args[0];
                float value = Math.Clamp((float)args[1], 0f, hunger.MaxSaturation);

                switch (category)
                {
                    case "all":
                        hunger.FruitLevel = value;
                        hunger.VegetableLevel = value;
                        hunger.ProteinLevel = value;
                        hunger.GrainLevel = value;
                        hunger.DairyLevel = value;
                        break;
                    case "Fruit": hunger.FruitLevel = value; break;
                    case "Vegetable": hunger.VegetableLevel = value; break;
                    case "Protein": hunger.ProteinLevel = value; break;
                    case "Grain": hunger.GrainLevel = value; break;
                    case "Dairy": hunger.DairyLevel = value; break;
                }
                hunger.UpdateNutrientHealthBoost();
                CompiledDiet? diet = DietIdResolver.ResolveDiet(hunger.entity);
                float nutrientHealthMod = diet == null ? 0f : DietNutrientHealthBoostPatch.ComputeBonus(diet, hunger);

                return TextCommandResult.Success(
                    $"Fruit={hunger.FruitLevel:F2} Vegetable={hunger.VegetableLevel:F2} Protein={hunger.ProteinLevel:F2} " +
                    $"Grain={hunger.GrainLevel:F2} Dairy={hunger.DairyLevel:F2} | nutrientHealthMod={nutrientHealthMod:F4}");
            });
    }
    private void RegisterAssignRulesDietCommand(ICoreServerAPI api)
    {
        api.ChatCommands.Create("dietassignrules")
            .WithDescription("Admin: set your own diet override to a rules-engine diet id (bypassing trait/default resolution), or 'clear' to remove the override")
            .RequiresPrivilege(Privilege.controlserver)
            .WithArgs(api.ChatCommands.Parsers.Word("dietId"))
            .HandleWith(args =>
            {
                IPlayer caller = args.Caller.Player;
                string dietId = (string)args[0];

                if (dietId == DietIdResolver.ClearKeyword)
                {
                    caller.Entity.WatchedAttributes.RemoveAttribute(DietIdResolver.OverrideAttribute);
                    string resolved = DietIdResolver.Resolve(caller.Entity);
                    return TextCommandResult.Success($"{DietIdResolver.OverrideAttribute} cleared, now resolving to '{resolved}' (trait/default).");
                }

                if (Snapshot.GetDiet(dietId) == null)
                {
                    return TextCommandResult.Error($"No rules-engine diet registered for id '{dietId}'.");
                }

                caller.Entity.WatchedAttributes.SetString(DietIdResolver.OverrideAttribute, dietId);
                return TextCommandResult.Success($"{DietIdResolver.OverrideAttribute} set to '{dietId}' (rules-engine diet, bypasses trait/default resolution).");
            });
    }
    private void RegisterDiagCommand(ICoreServerAPI api)
    {
        api.ChatCommands.Create("dietdiag")
            .WithDescription("Diagnostic: dump diet state for the calling player, or resolution for a given item code")
            .RequiresPrivilege(Privilege.commandplayer)
            .WithArgs(api.ChatCommands.Parsers.OptionalWord("itemcode"))
            .HandleWith(args =>
            {
                string? itemCode = args[0] as string;
                var caller = (IServerPlayer)args.Caller.Player;
                return string.IsNullOrEmpty(itemCode) ? DiagPlayerState(api, caller) : DiagItem(api, caller, itemCode);
            });
    }
    private void RegisterDietReloadCommand(ICoreServerAPI api)
    {
        api.ChatCommands.Create("dietreload")
            .WithDescription("Admin: re-run the diet load pipeline (tags, diets, extends, compile, validate); full table goes to server-main.log")
            .RequiresPrivilege(Privilege.controlserver)
            .HandleWith(args =>
            {
                DietLoadResult result;
                try { result = DietLoadPipeline.RunAndLog(api, LoadConfig(api)); }
                catch (Exception ex) { FoodOverrideRegistry.SetEnabled(api, Snapshot.Config.EnableDietSystem); api.Logger.Error("[dietsetup] Reload rejected: {0}", ex); return TextCommandResult.Error($"Reload rejected; previous snapshot retained: {ex.Message}"); }
                serverBindingsChannel?.BroadcastPacket(DietConfigurationPacket.From(Snapshot));

                return TextCommandResult.Success($"Reloaded. {result.DietCount} diets, {result.RefusedCount} refused, {result.WarningCount} warnings. Table in server-main.log.");
            });
    }
    private void RegisterDietShowCommand(ICoreServerAPI api)
    {
        api.ChatCommands.Create("dietshow")
            .WithDescription("Print one compiled diet: capacities, derived values, fallback and rules in win order")
            .RequiresPrivilege(Privilege.commandplayer)
            .WithArgs(api.ChatCommands.Parsers.Word("id"))
            .HandleWith(args =>
            {
                string id = (string)args[0];
                CompiledDiet? diet = Snapshot.GetDiet(id);
                if (diet == null) return TextCommandResult.Error($"No compiled diet for id '{id}'.");

                return TextCommandResult.Success(FormatDietShow(diet));
            });
    }

    private string FormatDietShow(CompiledDiet diet)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"diet '{diet.Id}' (domain '{diet.SourceDomain}')");

        foreach (EnumFoodCategory cat in new[] { EnumFoodCategory.Fruit, EnumFoodCategory.Vegetable, EnumFoodCategory.Grain, EnumFoodCategory.Protein, EnumFoodCategory.Dairy })
        {
            CompiledCategory c = diet.Categories[cat];
            sb.AppendLine($"  {cat,-10} capacity={c.Capacity:F3} gainScale={c.NutritionGainScale:F3} healthWeight={c.HealthWeight:F3}");
        }

        sb.AppendLine($"  fallback: satietyMult={diet.FallbackSatietyMult:F2} nutritionMult={diet.FallbackNutritionMult:F2}");
        sb.AppendLine($"  rules ({diet.Rules.Length}, win order):");

        for (int i = 0; i < diet.Rules.Length; i++)
        {
            CompiledRule r = diet.Rules[i];
            string requires = string.Join(",", Snapshot.Tags.TagNames(r.RequiresMask));
            string excludes = string.Join(",", Snapshot.Tags.TagNames(r.ExcludesMask));
            string inedibleNote = r.Verdict == DietVerdict.Inedible ? " (Inedible: Resolve() forces satiety/nutrition to 0, not the values above)" : "";
            sb.AppendLine($"    [{i}] priority={r.Priority} requires=[{requires}] excludes=[{excludes}] verdict={r.Verdict} satietyMult={FormatValue(r.SatietyMult)} nutritionMult={FormatValue(r.NutritionMult)}{inedibleNote}");
        }

        return sb.ToString().TrimEnd();
    }
    private static string FormatValue(CompiledValue value) =>
        value.IsCurve ? $"curve[{value.Evaluate(0f):F2}..{value.Evaluate(1f):F2}]" : $"{value.Evaluate(0f):F2}";
    private void RegisterFoodDiagnostic(ICoreServerAPI api)
    {
        api.ChatCommands.Create("dietfood").WithDescription("Inspect held/target food ingredients, or last actual consumption")
            .RequiresPrivilege(Privilege.commandplayer).RequiresPlayer()
            .WithArgs(api.ChatCommands.Parsers.OptionalWord("held|target|last"))
            .HandleWith(args =>
            {
                var entity = args.Caller.Player.Entity;
                string mode = args[0] as string ?? "held";
                if (mode == "last") return TextCommandResult.Success(LastConsumption(entity.EntityId));
                ItemSlot? slot = entity.RightHandItemSlot;
                if (mode == "target")
                {
                    var selection = args.Caller.Player.CurrentBlockSelection;
                    var container = selection == null ? null : api.World.BlockAccessor.GetBlockEntity(selection.Position) as BlockEntityContainer;
                    slot = container?.Inventory.FirstOrDefault(s => !s.Empty);
                }
                else if (mode != "held") return TextCommandResult.Error("Use /dietfood held, target, or last.");
                if (slot == null || slot.Empty) return TextCommandResult.Error("No food in the selected slot.");
                try { return TextCommandResult.Success(DietDiagnostics.Inspect(api, entity, slot)); }
                catch (Exception ex) { return TextCommandResult.Error($"Food facts unavailable; retry after resolving: {ex.Message}"); }
            });
    }

    private void RegisterFactsQueueDiagCommand(ICoreServerAPI api)
    {
        api.ChatCommands.Create("dietfactsqueue")
            .WithDescription("Diagnostic: print your pending real-eat nutrition-multiplier queue counts (stays 0 across hovers; only a real eat should move them)")
            .RequiresPrivilege(Privilege.commandplayer)
            .HandleWith(args =>
            {
                IPlayer caller = args.Caller.Player;
                long entityId = caller.Entity.EntityId;
                int profileQueueCount = DietProfileRegistry.PeekNutritionMultiplierQueueCount(entityId);
                return TextCommandResult.Success($"nutritionMultiplierQueue={profileQueueCount}");
            });
    }

    private TextCommandResult DiagPlayerState(ICoreServerAPI api, IServerPlayer caller)
    {
        var entity = caller.Entity;
        var hunger = entity.GetBehavior<EntityBehaviorHunger>();
        var health = entity.GetBehavior<EntityBehaviorHealth>();

        string dietId = DietIdResolver.ResolveDetailed(entity, out DietIdResolver.ResolvePath dietPath, out string? matchedTrait);
        string dietSummary = dietPath switch
        {
            DietIdResolver.ResolvePath.ExplicitOverride =>
                $"id={dietId} source=explicit override ({DietIdResolver.OverrideAttribute}) override={dietId}",
            DietIdResolver.ResolvePath.RaceTrait => $"id={dietId} source=race trait trait={matchedTrait}",
            _ => $"id={dietId} source=default"
        };

        string hungerSummary = hunger == null
            ? "unavailable (no hunger behavior on this entity)"
            : $"Sat={hunger.Saturation:F1}/{hunger.MaxSaturation:F1} FruitLvl={hunger.FruitLevel:F1} VegLvl={hunger.VegetableLevel:F1} ProteinLvl={hunger.ProteinLevel:F1} GrainLvl={hunger.GrainLevel:F1} DairyLvl={hunger.DairyLevel:F1}";

#pragma warning disable CS0618 // MaxHealthModifiers is obsolete for writing; reading it here is fine
        string healthSummary = health == null
            ? "unavailable (no health behavior on this entity)"
            : health.MaxHealthModifiers != null && health.MaxHealthModifiers.TryGetValue("nutrientHealthMod", out float nutrientBonus)
                ? $"nutrientHealthMod={nutrientBonus:F2}/12.50 MaxHealth={health.MaxHealth:F1}"
                : $"nutrientHealthMod=(not set) MaxHealth={health.MaxHealth:F1}";
#pragma warning restore CS0618

        ItemSlot? heldSlot = entity.RightHandItemSlot;
        string heldSummary;
        if (heldSlot?.Itemstack == null)
        {
            heldSummary = "not holding an item";
        }
        else
        {
            ItemStack heldStack = heldSlot.Itemstack;
            ulong tagMask = Snapshot.Tags.GetTagMask(api.World, heldSlot, out bool determined);
            string tags;
            if (!determined)
            {
                tags = "transition state unavailable, try again";
            }
            else
            {
                string joined = string.Join(", ", Snapshot.Tags.TagNames(tagMask));
                tags = joined.Length == 0 ? "(no tags)" : joined;
            }

            FoodNutritionProperties? afterTag = heldStack.Collectible.GetNutritionProperties(api.World, heldStack, entity);
            string satietySummary = afterTag == null ? "no nutrition data" : DescribeSatietyFold(entity, afterTag);
            heldSummary = $"{heldStack.Collectible.Code} tags=[{tags}] satiety: {satietySummary}";
        }

        string patchSummary = string.Join(", ", new[]
        {
            SaturationPatchDiagnostic(api),
            $"UpdateNutrientHealthBoost(prefix)={PatchCount(typeof(EntityBehaviorHunger), nameof(EntityBehaviorHunger.UpdateNutrientHealthBoost), prefix: true)}",
            $"BlockMeal.Consume(prefix)={PatchCount(typeof(BlockMeal), nameof(BlockMeal.Consume), prefix: true)}",
            $"BlockLiquidContainerBase.tryEatStop(prefix)={PatchCount(typeof(BlockLiquidContainerBase), "tryEatStop", prefix: true)}"
        });

        string msg = string.Format(
            "EnableDietSystem={0}\ndiet: {1}\nhunger: {2}\n{3}\nheld: {4}\npatches: {5}",
            Config.EnableDietSystem,
            dietSummary + $" snapshot={Snapshot.Revision}/{Snapshot.Hash}",
            hungerSummary,
            healthSummary,
            heldSummary,
            patchSummary);

        return TextCommandResult.Success(msg);
    }

    private TextCommandResult DiagItem(ICoreServerAPI api, IServerPlayer caller, string itemCode)
    {
        var loc = new AssetLocation(itemCode);
        CollectibleObject? collectible = (CollectibleObject?)api.World.GetItem(loc) ?? api.World.GetBlock(loc);
        if (collectible == null)
        {
            return TextCommandResult.Success($"No item or block registered with code '{itemCode}'.");
        }

        var entity = caller.Entity;
        var stack = new ItemStack(collectible);
        FoodNutritionProperties? vanilla = collectible.GetNutritionProperties(api.World, stack, entity);
        if (vanilla == null)
        {
            return TextCommandResult.Success($"{itemCode}: no nutrition data (not food, and no grant rule matched).");
        }

        string satietySummary = DescribeSatietyFold(entity, vanilla);
        return TextCommandResult.Success($"{itemCode}: category={vanilla.FoodCategory} satiety={vanilla.Satiety:F1} health={vanilla.Health:F2} | satiety fold: {satietySummary}");
    }
    private static string DescribeSatietyFold(Entity entity, FoodNutritionProperties afterTag)
    {
        return $"nutritionPropertiesSatiety={afterTag.Satiety:F2}";
    }

    private static int PatchCount(Type type, string methodName, bool prefix)
    {
        MethodInfo? method = AccessTools.Method(type, methodName);
        if (method == null) return 0;
        Patches? info = Harmony.GetPatchInfo(method);
        return (prefix ? info?.Prefixes?.Count : info?.Postfixes?.Count) ?? 0;
    }
    private static string SaturationPatchDiagnostic(ICoreAPI api)
    {
        MethodInfo? method = typeof(EntityBehaviorHunger).GetMethod(nameof(EntityBehaviorHunger.OnEntityReceiveSaturation));
        IList<Patch>? prefixes = method == null ? null : Harmony.GetPatchInfo(method)?.Prefixes;
        string byOwner = prefixes == null || prefixes.Count == 0
            ? "0"
            : string.Join("+", prefixes.GroupBy(p => p.owner).Select(g => $"{g.Key}:{g.Count()}"));

        return $"OnEntityReceiveSaturation(prefix)={byOwner} (side={api.Side}, harmonyPatched={patchOwners > 0})";
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        capi = api;

        api.Network.RegisterChannel(BindingsChannelName)
            .RegisterMessageType<DietConfigurationPacket>()
            .SetMessageHandler<DietConfigurationPacket>(packet => OnConfigurationPacket(api, packet));

        RegisterHandbookPage(api);
        RegisterTagDiagCommand(api);
        RegisterDietResolveCommand(api);
    }
    private void OnConfigurationPacket(ICoreClientAPI api, DietConfigurationPacket packet)
    {
        if (packet.Revision <= Snapshot.Revision) return;
        try
        {
            EffectiveDietConfiguration effective = packet.Read();
            effective.Config.Validate();
            var tags = new FoodTagRegistry();
            tags.LoadFrom(effective.Tags);
            var diets = new Dictionary<string, CompiledDiet>();
            foreach (var (id, document) in effective.Diets)
            {
                var fatal = new List<DietValidationMessage>();
                var warnings = new List<DietValidationMessage>();
                var diet = DietCompiler.Compile(tags, id, document, effective.Domains[id],
                    effective.Config.CapacityFloor, fatal, warnings);
                if (diet == null) throw new InvalidOperationException($"Server diet '{id}' failed compilation: {string.Join(", ", fatal)}");
                diets.Add(id, diet);
            }
            var log = new List<string>();
            FoodOverrideRegistry.ApplyFromPacket(api, effective.Grants, log);
            FoodOverrideRegistry.SetEnabled(api, effective.Config.EnableDietSystem);
            tags.ResolveStaticTags(api);
            Publish(new DietRuntimeSnapshot(effective.Config, tags, diets, effective.Bindings,
                packet.Revision, packet.Hash, packet.Payload));
            api.Logger.Notification("[dietsetup] received server snapshot revision={0} hash={1}", packet.Revision, packet.Hash);
        }
        catch (Exception ex)
        {
            FoodOverrideRegistry.SetEnabled(api, Snapshot.Config.EnableDietSystem);
            api.Logger.Error("[dietsetup] Server snapshot rejected; previous snapshot retained: {0}", ex);
        }
    }

    private void RegisterTagDiagCommand(ICoreClientAPI api)
    {
        api.ChatCommands.Create("diettags")
            .WithDescription("Diagnostic: print the resolved food-tag set for the item in your active hotbar slot")
            .HandleWith(args =>
            {
                ItemSlot? slot = api.World.Player?.Entity?.RightHandItemSlot;
                if (slot?.Itemstack == null)
                {
                    return TextCommandResult.Success("Not holding an item.");
                }

                ulong mask = Snapshot.Tags.GetTagMask(api.World, slot, out bool determined);
                if (!determined)
                {
                    return TextCommandResult.Success($"{slot.Itemstack.Collectible.Code}: transition state unavailable, try again.");
                }

                string tags = string.Join(", ", Snapshot.Tags.TagNames(mask));
                return TextCommandResult.Success($"{slot.Itemstack.Collectible.Code}: {(tags.Length == 0 ? "(no tags)" : tags)}");
            });
    }
    private void RegisterDietResolveCommand(ICoreClientAPI api)
    {
        api.ChatCommands.Create("dietresolve")
            .WithDescription("Diagnostic: resolve the item in your active hotbar slot against a diet id (multipliers only, nothing applied)")
            .WithArgs(api.ChatCommands.Parsers.Word("dietId"))
            .HandleWith(args =>
            {
                ItemSlot? slot = api.World.Player?.Entity?.RightHandItemSlot;
                if (slot?.Itemstack == null)
                {
                    return TextCommandResult.Success("Not holding an item.");
                }

                string dietId = (string)args[0];
                CompiledDiet? diet = Snapshot.GetDiet(dietId);
                if (diet == null)
                {
                    return TextCommandResult.Success($"No compiled diet for id '{dietId}'.");
                }

                ulong tagMask = Snapshot.Tags.GetTagMask(api.World, slot, out float spoilLevel, out bool determined);
                if (!determined)
                {
                    return TextCommandResult.Success($"{slot.Itemstack.Collectible.Code}: transition state unavailable, try again.");
                }

                DietResolveResult result = DietResolver.Resolve(diet, tagMask, spoilLevel);
                string tags = string.Join(", ", Snapshot.Tags.TagNames(tagMask));

                return TextCommandResult.Success(
                    $"{slot.Itemstack.Collectible.Code} tags=[{tags}] vs diet '{dietId}': verdict={result.Verdict} satietyMult={result.Satiety:F2} nutritionMult={result.Nutrition:F2} matched={result.Matched} effects={result.Effects.Length}");
            });
    }

    private void RegisterHandbookPage(ICoreClientAPI api)
    {
        var handbookSys = api.ModLoader.GetModSystem<ModSystemSurvivalHandbook>();
        if (handbookSys == null) return;

        handbookSys.OnInitCustomPages += pages =>
        {
            var page = new GuiHandbookTextPage
            {
                pageCode = "dietsetup:diet-guide",
                Title = "dietsetup:handbook-title",
                Text = "dietsetup:handbook-body",
                categoryCode = "guide"
            };
            page.Init(api);
            pages.Add(page);
        };
    }
}
