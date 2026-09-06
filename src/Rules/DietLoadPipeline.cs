using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using dietsetup.Binding;
using dietsetup.Grants;
using dietsetup.Tags;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace dietsetup.Rules;

public readonly struct DietLoadResult
{
    public readonly string Log;
    public readonly BindingsFile Bindings;
    public readonly int DietCount;
    public readonly int RefusedCount;
    public readonly int WarningCount;

    public DietLoadResult(string log, BindingsFile bindings, int dietCount, int refusedCount, int warningCount)
    {
        Log = log;
        Bindings = bindings;
        DietCount = dietCount;
        RefusedCount = refusedCount;
        WarningCount = warningCount;
    }
}
public static class DietLoadPipeline
{
    private const string ModConfigDietsDir = "dietsetup/diets";
    private const string ModConfigBindingsFile = "dietsetup/bindings.json";
    private const string ModConfigFoodTagsFile = "dietsetup/foodtags.json";

    public static DietLoadResult RunAndLog(ICoreAPI api, DietSetupConfig config)
    {
        var log = new List<string>();
        int warningCount = 0;
        var tags = new FoodTagRegistry();
        if (api.Side == EnumAppSide.Server) FoodOverrideRegistry.LoadApplyAndLog(api, log);
        FoodOverrideRegistry.SetEnabled(api, config.EnableDietSystem);
        LoadTags(api, tags, log);
        tags.ResolveStaticTags(api);
        var refused = new List<(string Id, DietValidationMessage Reason)>();
        Dictionary<string, (DietDocumentFile Doc, string Domain)> raw = LoadDietDocuments(api, log, refused);
        var compiledTable = new Dictionary<string, CompiledDiet>();

        var rawDocs = raw.ToDictionary(kv => kv.Key, kv => kv.Value.Doc);
        foreach (string id in raw.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var fatal = new List<DietValidationMessage>();
            var warnings = new List<DietValidationMessage>();

            if (id == DietIdResolver.ClearKeyword)
            {
                var reserved = new DietValidationMessage(15, "id 'clear' is reserved for /dietassignrules clear, diet refused");
                refused.Add((id, reserved));
                api.Logger.Error("[dietsetup] diet '{0}': rule {1}, {2}", id, reserved.Rule, reserved.Text);
                continue;
            }

            DietDocumentFile? resolved = DietExtendsResolver.Resolve(id, rawDocs, out string? extendsError);
            if (resolved == null)
            {
                fatal.Add(new DietValidationMessage(3, extendsError ?? "extends resolution failed"));
                refused.Add((id, fatal[0]));
                foreach (DietValidationMessage f in fatal) api.Logger.Error("[dietsetup] diet '{0}': rule {1}, {2}", id, f.Rule, f.Text);
                continue;
            }

            CompiledDiet? compiledDiet = DietCompiler.Compile(tags, id, resolved, raw[id].Domain, config.CapacityFloor, fatal, warnings);

            foreach (DietValidationMessage w in warnings)
            {
                api.Logger.Warning("[dietsetup] diet '{0}': rule {1}, {2}", id, w.Rule, w.Text);
            }
            warningCount += warnings.Count;

            if (compiledDiet == null)
            {
                refused.Add((id, fatal[0]));
                foreach (DietValidationMessage f in fatal) api.Logger.Error("[dietsetup] diet '{0}': rule {1}, {2}", id, f.Rule, f.Text);
                continue;
            }

            compiledTable[id] = compiledDiet;
        }

        warningCount += LogUnmatchedGrantedItems(tags, log, FoodOverrideRegistry.GrantedCollectibles(api), compiledTable.Values);
        log.Add($"[dietsetup] diets: {compiledTable.Count} loaded, {refused.Count} refused");
        int idColumnWidth = compiledTable.Count == 0 ? 0 : compiledTable.Values.Max(d => d.Id.Length) + 1;
        foreach (CompiledDiet diet in compiledTable.Values.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            log.Add(FormatDietRow(diet, idColumnWidth));
        }

        BindingsFile bindings = LoadAndLogBindings(api, log, compiledTable, ref warningCount);

        log.Add($"[dietsetup] untagged nutritious collectibles: {tags.UntaggedNutritiousCount}");

        var effective = new EffectiveDietConfiguration
        {
            Config = config,
            Tags = tags.Export(),
            Diets = compiledTable.Keys.ToDictionary(id => id, id => DietExtendsResolver.Resolve(id, rawDocs, out _)!),
            Domains = compiledTable.Keys.ToDictionary(id => id, id => raw[id].Domain),
            Bindings = bindings,
            Grants = DietFoodOverridesPacket.From(FoodOverrideRegistry.GrantedRows(api))
        };
        string payload = JsonConvert.SerializeObject(effective);
        var owner = api.ModLoader.GetModSystem<DietSetupModSystem>();
        long revision = api.Side == EnumAppSide.Server ? owner.Snapshot.Revision + 1 : 0;
        owner.Publish(new DietRuntimeSnapshot(config, tags, compiledTable, bindings, revision,
            DietConfigurationPacket.ComputeHash(payload), payload));
        log.Add($"[dietsetup] snapshot revision={revision} hash={owner.Snapshot.Hash}");
        foreach (string line in log) api.Logger.Notification(line);
        return new DietLoadResult(string.Join("\n", log), bindings, compiledTable.Count, refused.Count, warningCount);
    }

    private static void LoadTags(ICoreAPI api, FoodTagRegistry tags, List<string> log)
    {
        const string tagsPath = "config/foodtags.json";
        Dictionary<AssetLocation, FoodTagConfigFile> files = api.Assets.GetMany<FoodTagConfigFile>(api.Logger, tagsPath);

        int totalTags = 0;
        foreach ((AssetLocation loc, FoodTagConfigFile file) in files)
        {
            tags.LoadFrom(file);
            int count = file.Source.Count + file.State.Count + file.Form.Count;
            totalTags += count;
            api.Logger.Notification("[dietsetup] tags: domain '{0}' registered {1} tag(s)", loc.Domain, count);
        }
        if (files.Count == 0)
        {
            api.Logger.Warning("[dietsetup] tags: no '{0}' found in any domain -- 0 tags registered", tagsPath);
        }
        else if (totalTags == 0)
        {
            api.Logger.Warning("[dietsetup] tags: '{0}' found in {1} domain(s) but registered 0 tags -- check for empty/malformed content", tagsPath, files.Count);
        }

        string modConfigPath = Path.Combine(GamePaths.ModConfig, ModConfigFoodTagsFile);
        if (api.Side != EnumAppSide.Server || !File.Exists(modConfigPath)) return;

        FoodTagConfigFile? overrideFile;
        try
        {
            overrideFile = JsonConvert.DeserializeObject<FoodTagConfigFile>(File.ReadAllText(modConfigPath));
        }
        catch (Exception ex)
        {
            api.Logger.Error("[dietsetup] ModConfig foodtags override '{0}' failed to parse, asset tags kept: {1}", modConfigPath, ex.Message);
            return;
        }

        if (overrideFile == null)
        {
            api.Logger.Error("[dietsetup] ModConfig foodtags override '{0}' is empty or invalid, asset tags kept", modConfigPath);
            return;
        }

        foreach (string tag in tags.ApplyOverrides(overrideFile))
        {
            log.Add($"[dietsetup] tag '{tag}': ModConfig override wins ({modConfigPath}) over asset ({tagsPath})");
        }
    }
    private static Dictionary<string, (DietDocumentFile Doc, string Domain)> LoadDietDocuments(ICoreAPI api, List<string> log, List<(string Id, DietValidationMessage Reason)> refused)
    {
        var assets = api.Assets.GetMany<DietDocumentFile>(api.Logger, "config/diets/")
            .Select(kv => (Doc: kv.Value, Domain: kv.Key.Domain, Path: kv.Key.ToString())).ToList();
        var overrides = new List<(DietDocumentFile Doc, string Domain, string Path)>();
        string directory = Path.Combine(GamePaths.ModConfig, ModConfigDietsDir);
        if (api.Side == EnumAppSide.Server && Directory.Exists(directory))
            foreach (string path in Directory.GetFiles(directory, "*.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                try
                {
                    var doc = JsonConvert.DeserializeObject<DietDocumentFile>(File.ReadAllText(path));
                    if (doc == null) throw new InvalidDataException("empty diet document");
                    overrides.Add((doc, "ModConfig", path));
                }
                catch (Exception ex) { api.Logger.Error("[dietsetup] Cannot read '{0}': {1}", path, ex.Message); }
            }
        return SelectDocuments(assets, overrides, log, refused);
    }

    internal static Dictionary<string, (DietDocumentFile Doc, string Domain)> SelectDocuments(
        IEnumerable<(DietDocumentFile Doc, string Domain, string Path)> assets,
        IEnumerable<(DietDocumentFile Doc, string Domain, string Path)> overrides,
        List<string> log, List<(string Id, DietValidationMessage Reason)> refused)
    {
        var result = new Dictionary<string, (DietDocumentFile Doc, string Domain)>();
        var assetList = assets.ToList(); var overrideList = overrides.ToList();
        foreach (var row in assetList.Concat(overrideList).Where(r => string.IsNullOrWhiteSpace(r.Doc?.Id)))
            refused.Add((row.Path, new(2, "missing diet id")));
        var assetGroups = assetList.Where(r => !string.IsNullOrWhiteSpace(r.Doc?.Id)).GroupBy(r => r.Doc.Id!).ToDictionary(g => g.Key, g => g.ToList());
        var overrideGroups = overrideList.Where(r => !string.IsNullOrWhiteSpace(r.Doc?.Id)).GroupBy(r => r.Doc.Id!).ToDictionary(g => g.Key, g => g.ToList());
        foreach (string id in assetGroups.Keys.Union(overrideGroups.Keys).OrderBy(id => id, StringComparer.Ordinal))
        {
            var candidates = overrideGroups.TryGetValue(id, out var admin) ? admin : assetGroups[id];
            if (candidates.Count != 1)
            {
                refused.Add((id, new(2, $"duplicate id refused: {string.Join(", ", candidates.Select(r => r.Path))}")));
                continue;
            }
            var winner = candidates[0];
            result.Add(id, (winner.Doc, winner.Domain));
            if (admin != null && assetGroups.TryGetValue(id, out var losing))
                log.Add($"[dietsetup] '{id}': override {winner.Path} replaces {string.Join(", ", losing.Select(r => r.Path))}");
        }
        foreach (var failure in refused) log.Add($"[dietsetup] REFUSED {failure.Id}: {failure.Reason.Text}");
        return result;
    }
    public static int LogUnmatchedGrantedItems(FoodTagRegistry tags, List<string> log, IEnumerable<CollectibleObject> granted, IEnumerable<CompiledDiet> diets)
    {
        List<CollectibleObject> grantedList = granted as List<CollectibleObject> ?? granted.ToList();
        if (grantedList.Count == 0) return 0;

        int dietsWithUnmatched = 0;
        foreach (CompiledDiet diet in diets.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            int unmatched = 0;
            foreach (CollectibleObject collectible in grantedList)
            {
                ulong mask = tags.GetStaticMask(collectible);
                if (!DietResolver.Resolve(diet, mask, 0f).Matched) unmatched++;
            }

            if (unmatched > 0)
            {
                log.Add($"[dietsetup]   diet '{diet.Id}': {unmatched} granted item(s) not matched by any rule");
                dietsWithUnmatched++;
            }
        }
        return dietsWithUnmatched;
    }

    private static string FormatDietRow(CompiledDiet diet, int idColumnWidth)
    {
        string Cap(EnumFoodCategory c) => diet.Categories[c].Capacity.ToString("F2");
        string Gain(EnumFoodCategory c) => diet.Categories[c].NutritionGainScale.ToString("F2");

        return $"[dietsetup]   {diet.Id.PadRight(idColumnWidth)}cap F{Cap(EnumFoodCategory.Fruit)} V{Cap(EnumFoodCategory.Vegetable)} G{Cap(EnumFoodCategory.Grain)} P{Cap(EnumFoodCategory.Protein)} D{Cap(EnumFoodCategory.Dairy)}"
             + $"  gain F{Gain(EnumFoodCategory.Fruit)} V{Gain(EnumFoodCategory.Vegetable)} G{Gain(EnumFoodCategory.Grain)} P{Gain(EnumFoodCategory.Protein)} D{Gain(EnumFoodCategory.Dairy)}"
             + $"  rules {diet.Rules.Length}";
    }
    private static BindingsFile LoadAndLogBindings(ICoreAPI api, List<string> log, Dictionary<string, CompiledDiet> compiledTable, ref int warningCount)
    {
        string path = Path.Combine(GamePaths.ModConfig, ModConfigBindingsFile);
        BindingsFile bindings;

        if (api.Side != EnumAppSide.Server || !File.Exists(path))
        {
            bindings = new BindingsFile { SchemaVersion = 1, Default = "base" };
        }
        else
        {
            try
            {
                bindings = JsonConvert.DeserializeObject<BindingsFile>(File.ReadAllText(path)) ?? new BindingsFile();
            }
            catch (Exception ex)
            {
                api.Logger.Error("[dietsetup] bindings.json failed to parse, treating as empty: {0}", ex.Message);
                bindings = new BindingsFile();
            }

            if (bindings.SchemaVersion != 1)
            {
                api.Logger.Error("[dietsetup] bindings.json: schemaVersion missing or unknown (got {0})", bindings.SchemaVersion?.ToString() ?? "(missing)");
            }
        }

        if (bindings.Bindings == null) throw new ArgumentException("bindings.json: bindings cannot be null");
        foreach ((string trait, string dietId) in bindings.Bindings)
        {
            if (!compiledTable.ContainsKey(dietId))
            {
                api.Logger.Warning("[dietsetup] bindings.json: trait '{0}' maps to diet '{1}', which is not a loaded diet", trait, dietId);
                warningCount++;
            }
        }

        string defaultId = bindings.Default ?? "base";
        if (!compiledTable.ContainsKey(defaultId))
        {
            api.Logger.Warning("[dietsetup] bindings.json: default diet '{0}' is not a loaded diet", defaultId);
            warningCount++;
        }

        log.Add($"[dietsetup] bindings: {bindings.Bindings.Count} mapped, default '{defaultId}'");
        return bindings;
    }
}
