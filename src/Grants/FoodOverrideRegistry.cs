using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace dietsetup.Grants;

public static class FoodOverrideRegistry
{
    private const string ModConfigFile = "dietsetup/food-overrides.json";

    private static readonly EnumFoodCategory[] AllCategories =
    {
        EnumFoodCategory.Fruit, EnumFoodCategory.Vegetable, EnumFoodCategory.Grain,
        EnumFoodCategory.Protein, EnumFoodCategory.Dairy
    };

    private readonly record struct CompiledOverride(string Pattern, EnumFoodCategory Category, float BaseSatiety, int Specificity);
    private sealed class SideState
    {
        public bool Applied;
        public string? Hash;
        public List<(string Pattern, string Category, float BaseSatiety)> Rows = new();
        public readonly Dictionary<CollectibleObject, FoodNutritionProperties> Owned = new();
        public readonly List<CollectibleObject> Granted = new();
        public readonly List<(CollectibleObject Collectible, EnumFoodCategory Category, float BaseSatiety)> GrantedRows = new();
    }

    private static readonly ConditionalWeakTable<ICoreAPI, SideState> stateBySide = new();

    private static SideState GetState(ICoreAPI api) => stateBySide.GetValue(api, _ => new SideState());
    internal static void Reset(ICoreAPI api)
    {
        SetEnabled(api, false);
        stateBySide.Remove(api);
    }

    internal static void SetEnabled(ICoreAPI api, bool enabled)
    {
        if (!stateBySide.TryGetValue(api, out var state)) return;
        foreach (var (collectible, props) in state.Owned)
        {
            if (enabled && collectible.NutritionProps == null) collectible.NutritionProps = props;
            else if (!enabled && ReferenceEquals(collectible.NutritionProps, props)) collectible.NutritionProps = null;
        }
    }

    public static IReadOnlyList<CollectibleObject> GrantedCollectibles(ICoreAPI api) =>
        stateBySide.TryGetValue(api, out SideState? s) ? s.Granted : Array.Empty<CollectibleObject>();
    public static IReadOnlyList<(CollectibleObject Collectible, EnumFoodCategory Category, float BaseSatiety)> GrantedRows(ICoreAPI api) =>
        stateBySide.TryGetValue(api, out SideState? s)
            ? s.GrantedRows
            : Array.Empty<(CollectibleObject, EnumFoodCategory, float)>();
    public static void LoadApplyAndLog(ICoreAPI api, List<string> log)
    {
        SideState state = GetState(api);
        string path = Path.Combine(GamePaths.ModConfig, ModConfigFile);

        if (state.Applied)
        {
            CompareAndLogReload(api, log, state, path);
            return;
        }

        if (!File.Exists(path))
        {
            state.Applied = true;
            log.Add("[dietsetup] food-overrides: 0 grants loaded (no ModConfig/dietsetup/food-overrides.json)");
            return;
        }

        string raw;
        try
        {
            raw = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            api.Logger.Error("[dietsetup] food-overrides.json could not be read, 0 grants applied: {0}", ex.Message);
            return;
        }

        state.Hash = ComputeHash(raw);

        FoodOverrideDocumentFile? doc;
        try
        {
            doc = JsonConvert.DeserializeObject<FoodOverrideDocumentFile>(raw);
        }
        catch (Exception ex)
        {
            api.Logger.Error("[dietsetup] food-overrides.json failed to parse, 0 grants applied: {0}", ex.Message);
            return;
        }

        if (doc == null)
        {
            api.Logger.Error("[dietsetup] food-overrides.json produced no usable data on parse, 0 grants applied.");
            return;
        }

        if (doc.SchemaVersion != 1)
        {
            api.Logger.Error("[dietsetup] food-overrides.json: schemaVersion missing or unknown (got {0}), 0 grants applied.",
                doc.SchemaVersion?.ToString() ?? "(missing)");
            return;
        }

        if (doc.Grants == null || doc.Grants.Any(g => g == null))
        {
            api.Logger.Error("[dietsetup] food-overrides.grants: null array or entry refused.");
            return;
        }
        state.Rows = doc.Grants.Select(g => (g.Pattern ?? "", g.Category ?? "", g.BaseSatiety ?? 0f)).ToList();

        if (!ValidateStructure(api, doc.Grants))
        {
            log.Add("[dietsetup] food-overrides: file refused, 0 grants applied (see errors above)");
            return;
        }

        List<CompiledOverride> compiled = doc.Grants
            .Select(g => new CompiledOverride(g.Pattern!, ParseCategory(g.Category!), g.BaseSatiety!.Value, Specificity(g.Pattern!)))
            .ToList();

        state.Applied = true;
        if (compiled.Count == 0)
        {
            log.Add("[dietsetup] food-overrides: 0 grants loaded (empty grants list)");
            return;
        }

        ValidateAndApply(api, log, compiled, state);
    }
    private static bool ValidateStructure(ICoreAPI api, List<FoodOverrideEntryFile> rows)
    {
        bool ok = true;
        for (int i = 0; i < rows.Count; i++)
        {
            FoodOverrideEntryFile row = rows[i];
            string label = string.IsNullOrEmpty(row.Pattern) ? $"row {i}" : row.Pattern;

            if (string.IsNullOrEmpty(row.Pattern) || string.IsNullOrEmpty(row.Category) || row.BaseSatiety == null)
            {
                api.Logger.Error("[dietsetup] food-overrides '{0}': pattern, category and baseSatiety are all required.", label);
                ok = false;
                continue;
            }

            if (!TryParseCategory(row.Category, out _))
            {
                api.Logger.Error("[dietsetup] food-overrides '{0}': category '{1}' is not one of Fruit/Vegetable/Grain/Protein/Dairy.", label, row.Category);
                ok = false;
            }

            if (!float.IsFinite(row.BaseSatiety.Value) || row.BaseSatiety.Value < 0f)
            {
                api.Logger.Error("[dietsetup] food-overrides '{0}': baseSatiety {1} must be finite and non-negative.", label, row.BaseSatiety.Value);
                ok = false;
            }
        }
        return ok;
    }
    private static void ValidateAndApply(ICoreAPI api, List<string> log, List<CompiledOverride> rows, SideState state)
    {
        var matchesByRow = new List<CollectibleObject>[rows.Count];
        for (int i = 0; i < rows.Count; i++) matchesByRow[i] = new List<CollectibleObject>();
        var perCollectible = new List<(CollectibleObject Collectible, List<int> RowIdx)>();

        foreach (CollectibleObject collectible in api.World.Collectibles)
        {
            AssetLocation? code = collectible.Code;
            if (code == null) continue;
            string codeStr = code.ToString();

            List<int>? matchedRows = null;
            for (int i = 0; i < rows.Count; i++)
            {
                if (!WildcardUtil.Match(rows[i].Pattern, codeStr)) continue;
                matchesByRow[i].Add(collectible);
                (matchedRows ??= new List<int>()).Add(i);
            }
            if (matchedRows != null) perCollectible.Add((collectible, matchedRows));
        }

        bool ok = true;
        for (int i = 0; i < rows.Count; i++)
        {
            List<CollectibleObject> matches = matchesByRow[i];
            if (matches.Count > 0 && matches.TrueForAll(c => c.NutritionProps != null))
            {
                api.Logger.Error("[dietsetup] food-overrides '{0}': every matching item already has nutritionProps, this grant would do nothing.", rows[i].Pattern);
                ok = false;
            }
        }
        foreach ((CollectibleObject collectible, List<int> rowIdx) in perCollectible)
        {
            if (rowIdx.Count < 2) continue;
            int maxSpec = rowIdx.Max(i => rows[i].Specificity);
            List<int> tied = rowIdx.Where(i => rows[i].Specificity == maxSpec).ToList();
            if (tied.Count > 1)
            {
                api.Logger.Error("[dietsetup] food-overrides: patterns '{0}' and '{1}' tie at equal specificity for item '{2}'.",
                    rows[tied[0]].Pattern, rows[tied[1]].Pattern, collectible.Code);
                ok = false;
            }
        }

        if (!ok)
        {
            log.Add("[dietsetup] food-overrides: file refused, 0 grants applied (see errors above)");
            return;
        }

        int grantedCount = 0;
        foreach ((CollectibleObject collectible, List<int> rowIdx) in perCollectible)
        {
            int winner = rowIdx.Count == 1 ? rowIdx[0] : rowIdx.OrderByDescending(i => rows[i].Specificity).First();
            CompiledOverride row = rows[winner];
            if (collectible.NutritionProps != null)
            {
                api.Logger.Warning("[dietsetup] food-overrides '{0}': item '{1}' already has nutritionProps, grant skipped.", row.Pattern, collectible.Code);
                continue;
            }

            collectible.NutritionProps = new FoodNutritionProperties
            {
                FoodCategory = row.Category,
                Satiety = row.BaseSatiety,
                Health = 0f,
            };

            state.Owned[collectible] = collectible.NutritionProps;
            state.Granted.Add(collectible);
            state.GrantedRows.Add((collectible, row.Category, row.BaseSatiety));
            grantedCount++;
        }

        log.Add($"[dietsetup] food-overrides: {grantedCount} item(s) granted nutritionProps ({rows.Count} pattern row(s))");
    }
    public static List<CollectibleObject> ApplyFromPacket(ICoreClientAPI capi, DietFoodOverridesPacket packet, List<string> log)
    {
        SideState state = GetState(capi);
        var newlyApplied = new List<CollectibleObject>();
        int alreadyGranted = 0, notFound = 0, catalogMismatch = 0, badCategory = 0;

        int count = Math.Min(packet.ItemCodes.Length, Math.Min(packet.Categories.Length, packet.BaseSatiety.Length));
        for (int i = 0; i < count; i++)
        {
            string itemCode = packet.ItemCodes[i];
            var loc = new AssetLocation(itemCode);
            CollectibleObject? collectible = (CollectibleObject?)capi.World.GetItem(loc) ?? capi.World.GetBlock(loc);

            if (collectible == null)
            {
                capi.Logger.Warning("[dietsetup] food-overrides packet: item '{0}' not found client-side, grant skipped (client/server mod mismatch?).", itemCode);
                notFound++;
                continue;
            }
            if (state.Granted.Contains(collectible))
            {
                alreadyGranted++;
                continue;
            }

            if (collectible.NutritionProps != null)
            {
                capi.Logger.Warning("[dietsetup] food-overrides packet: item '{0}' already has nutritionProps client-side but the server had to grant it -- client/server catalog mismatch, grant skipped.", itemCode);
                catalogMismatch++;
                continue;
            }
            if (!TryParseCategory(packet.Categories[i], out EnumFoodCategory category))
            {
                capi.Logger.Warning("[dietsetup] food-overrides packet: item '{0}' has unrecognized category '{1}', grant skipped.", itemCode, packet.Categories[i]);
                badCategory++;
                continue;
            }

            if (!float.IsFinite(packet.BaseSatiety[i]) || packet.BaseSatiety[i] < 0) continue;
            collectible.NutritionProps = new FoodNutritionProperties
            {
                FoodCategory = category,
                Satiety = packet.BaseSatiety[i],
                Health = 0f,
            };

            state.Owned[collectible] = collectible.NutritionProps;
            state.Granted.Add(collectible);
            state.GrantedRows.Add((collectible, category, packet.BaseSatiety[i]));
            newlyApplied.Add(collectible);
        }

        log.Add($"[dietsetup] food-overrides packet: {newlyApplied.Count} newly applied, {alreadyGranted} already granted, " +
                $"{notFound} not found, {catalogMismatch} catalog mismatch, {badCategory} bad category ({count} row(s) received)");
        return newlyApplied;
    }

    private static bool TryParseCategory(string raw, out EnumFoodCategory category) =>
        Enum.TryParse(raw, true, out category) && Array.IndexOf(AllCategories, category) >= 0;

    private static EnumFoodCategory ParseCategory(string raw)
    {
        TryParseCategory(raw, out EnumFoodCategory category);
        return category;
    }
    private static int Specificity(string pattern) => pattern.Contains('*') ? pattern.Length : int.MaxValue;

    private static string ComputeHash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    private static void CompareAndLogReload(ICoreAPI api, List<string> log, SideState state, string path)
    {
        if (!File.Exists(path))
        {
            if (state.Hash != null)
            {
                log.Add($"[dietsetup] food-overrides: file removed since last apply ({state.Rows.Count} row(s) changed) -- restart required for this to take effect.");
            }
            return;
        }

        string raw;
        try
        {
            raw = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            api.Logger.Warning("[dietsetup] food-overrides.json could not be re-read for reload comparison: {0}", ex.Message);
            return;
        }

        string newHash = ComputeHash(raw);
        if (newHash == state.Hash) return;

        List<(string Pattern, string Category, float BaseSatiety)> newRows;
        try
        {
            FoodOverrideDocumentFile? doc = JsonConvert.DeserializeObject<FoodOverrideDocumentFile>(raw);
            newRows = (doc?.Grants ?? new()).Select(g => (g.Pattern ?? "", g.Category ?? "", g.BaseSatiety ?? 0f)).ToList();
        }
        catch
        {
            newRows = new();
        }

        var oldSet = new HashSet<(string, string, float)>(state.Rows);
        var newSet = new HashSet<(string, string, float)>(newRows);
        int changed = oldSet.Count(r => !newSet.Contains(r)) + newSet.Count(r => !oldSet.Contains(r));

        log.Add($"[dietsetup] food-overrides: file changed since last apply ({changed} row(s) changed) -- restart required for this to take effect.");
    }
}
