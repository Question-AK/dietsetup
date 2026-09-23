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
    private const int MaterialPermissionSchemaVersion = 2;

    /// <summary>Task 5B flips this when consumption enforcement exists. While it is false a
    /// schemaVersion 2 file is refused whole: accepting permission data without enforcing it would
    /// hand every diet a material the author restricted.</summary>
    internal static readonly bool MaterialPermissionEnforcementAvailable = false;

    private static readonly EnumFoodCategory[] AllCategories =
    {
        EnumFoodCategory.Fruit, EnumFoodCategory.Vegetable, EnumFoodCategory.Grain,
        EnumFoodCategory.Protein, EnumFoodCategory.Dairy
    };

    internal readonly record struct CompiledOverride(string Pattern, EnumFoodCategory Category, float BaseSatiety,
        FoodAccessRule Access, int Specificity);

    /// <summary>A validated document that has not been applied to anything. Producing one mutates no
    /// state, so a failed reload candidate can be discarded with the previous grants still standing.</summary>
    internal sealed record FoodOverrideCandidate(
        int SchemaVersion,
        IReadOnlyList<CompiledOverride> Rows,
        IReadOnlyList<(string Pattern, string Category, float BaseSatiety, string Access)> DescribedRows);

    private sealed class SideState
    {
        public bool Applied;
        public string? Hash;
        public List<(string Pattern, string Category, float BaseSatiety, string Access)> Rows = new();
        public readonly Dictionary<CollectibleObject, FoodNutritionProperties> Owned = new();
        public readonly List<CollectibleObject> Granted = new();
        public readonly List<(CollectibleObject Collectible, EnumFoodCategory Category, float BaseSatiety, FoodAccessRule Access)> GrantedRows = new();
        public readonly Dictionary<CollectibleObject, FoodAccessRule> Access = new();
        public int RestrictedCount;
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
    public static IReadOnlyList<(CollectibleObject Collectible, EnumFoodCategory Category, float BaseSatiety, FoodAccessRule Access)> GrantedRows(ICoreAPI api) =>
        stateBySide.TryGetValue(api, out SideState? s)
            ? s.GrantedRows
            : Array.Empty<(CollectibleObject, EnumFoodCategory, float, FoodAccessRule)>();

    /// <summary>The permission on one material, or unrestricted for anything this mod did not make
    /// edible. Permission exists only for granted materials -- restricting food vanilla already feeds
    /// everyone is out of scope -- so ordinary food never reaches a permission check.</summary>
    public static FoodAccessRule AccessFor(ICoreAPI? api, CollectibleObject? collectible) =>
        api != null && collectible != null && stateBySide.TryGetValue(api, out SideState? s)
            && s.Access.TryGetValue(collectible, out FoodAccessRule? rule) ? rule : FoodAccessRule.Unrestricted;

    /// <summary>Lets every consumption path skip permission work entirely on the ordinary configuration,
    /// where no grant restricts anyone.</summary>
    public static bool HasAnyRestriction(ICoreAPI? api) =>
        api != null && stateBySide.TryGetValue(api, out SideState? s) && s.RestrictedCount > 0;

    public static void LoadApplyAndLog(ICoreAPI api, List<string> log, IReadOnlyCollection<string>? knownDietIds)
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

        ApplyDocument(api, log, raw, knownDietIds, MaterialPermissionEnforcementAvailable);
    }

    /// <summary>The validate-and-apply half, separated from reading ModConfig so the same production path
    /// can be exercised against a fixture without a live configuration directory.</summary>
    internal static void ApplyDocument(ICoreAPI api, List<string> log, string raw,
        IReadOnlyCollection<string>? knownDietIds, bool allowMaterialPermissions)
    {
        SideState state = GetState(api);
        state.Hash = ComputeHash(raw);

        bool valid = TryValidateDocument(raw, knownDietIds, allowMaterialPermissions,
            out FoodOverrideCandidate? candidate, out IReadOnlyList<string> errors);
        foreach (string error in errors) api.Logger.Error("{0}", error);

        if (candidate == null) return;
        state.Rows = candidate.DescribedRows.ToList();

        if (!valid)
        {
            log.Add("[dietsetup] food-overrides: file refused, 0 grants applied (see errors above)");
            return;
        }

        state.Applied = true;
        if (candidate.Rows.Count == 0)
        {
            log.Add("[dietsetup] food-overrides: 0 grants loaded (empty grants list)");
            return;
        }

        ValidateAndApply(api, log, candidate.Rows, state);
    }

    /// <summary>Parses and validates a candidate document without touching registry state, the world or
    /// the logger. A null candidate means the document never reached row validation.</summary>
    internal static bool TryValidateDocument(string raw, IReadOnlyCollection<string>? knownDietIds,
        bool allowMaterialPermissions, out FoodOverrideCandidate? candidate, out IReadOnlyList<string> errors)
    {
        var messages = new List<string>();
        errors = messages;
        candidate = null;

        FoodOverrideDocumentFile? doc;
        try
        {
            doc = JsonConvert.DeserializeObject<FoodOverrideDocumentFile>(raw);
        }
        catch (Exception ex)
        {
            messages.Add($"[dietsetup] food-overrides.json failed to parse, 0 grants applied: {ex.Message}");
            return false;
        }

        if (doc == null)
        {
            messages.Add("[dietsetup] food-overrides.json produced no usable data on parse, 0 grants applied.");
            return false;
        }

        if (doc.SchemaVersion is not (1 or MaterialPermissionSchemaVersion))
        {
            messages.Add($"[dietsetup] food-overrides.json: schemaVersion missing or unknown (got {doc.SchemaVersion?.ToString() ?? "(missing)"}), 0 grants applied.");
            return false;
        }

        int schemaVersion = doc.SchemaVersion.Value;
        if (schemaVersion == MaterialPermissionSchemaVersion && !allowMaterialPermissions)
        {
            messages.Add($"[dietsetup] food-overrides.json: schemaVersion {MaterialPermissionSchemaVersion} material permissions are not enforced by this build, "
                + "0 grants applied. Use schemaVersion 1 without any 'access' block until permission enforcement ships.");
            return false;
        }

        if (doc.Grants == null || doc.Grants.Any(g => g == null))
        {
            messages.Add("[dietsetup] food-overrides.grants: null array or entry refused.");
            return false;
        }

        var access = new FoodAccessRule[doc.Grants.Count];
        bool ok = ValidateStructure(doc.Grants, schemaVersion, knownDietIds, access, messages);

        var described = doc.Grants
            .Select(g => (g.Pattern ?? "", g.Category ?? "", g.BaseSatiety ?? 0f, Describe(g.Access)))
            .ToList();

        var rows = ok
            ? doc.Grants.Select((g, i) => new CompiledOverride(g.Pattern!, ParseCategory(g.Category!), g.BaseSatiety!.Value,
                access[i], Specificity(g.Pattern!))).ToList()
            : new List<CompiledOverride>();

        candidate = new FoodOverrideCandidate(schemaVersion, rows, described);
        return ok;
    }

    private static bool ValidateStructure(List<FoodOverrideEntryFile> rows, int schemaVersion,
        IReadOnlyCollection<string>? knownDietIds, FoodAccessRule[] access, List<string> errors)
    {
        bool ok = true;
        for (int i = 0; i < rows.Count; i++)
        {
            FoodOverrideEntryFile row = rows[i];
            string label = string.IsNullOrEmpty(row.Pattern) ? $"row {i}" : row.Pattern;
            access[i] = FoodAccessRule.Unrestricted;

            if (!TryCompileAccess(row.Access, label, schemaVersion, knownDietIds, errors, out access[i])) ok = false;

            if (string.IsNullOrEmpty(row.Pattern) || string.IsNullOrEmpty(row.Category) || row.BaseSatiety == null)
            {
                errors.Add($"[dietsetup] food-overrides '{label}': pattern, category and baseSatiety are all required.");
                ok = false;
                continue;
            }

            if (!TryParseCategory(row.Category, out _))
            {
                errors.Add($"[dietsetup] food-overrides '{label}': category '{row.Category}' is not one of Fruit/Vegetable/Grain/Protein/Dairy.");
                ok = false;
            }

            if (!float.IsFinite(row.BaseSatiety.Value) || row.BaseSatiety.Value < 0f)
            {
                errors.Add($"[dietsetup] food-overrides '{label}': baseSatiety {row.BaseSatiety.Value} must be finite and non-negative.");
                ok = false;
            }
        }
        return ok;
    }

    private static bool TryCompileAccess(FoodAccessFile? file, string label, int schemaVersion,
        IReadOnlyCollection<string>? knownDietIds, List<string> errors, out FoodAccessRule rule)
    {
        rule = FoodAccessRule.Unrestricted;
        if (file == null)
        {
            // schemaVersion 1 never had permissions, so absent is its only meaning. Under 2 the author
            // states the intent: an absent block is not a default, matching every other field here.
            if (schemaVersion < MaterialPermissionSchemaVersion) return true;
            errors.Add($"[dietsetup] food-overrides '{label}': access is required on every grant under schemaVersion "
                + $"{MaterialPermissionSchemaVersion}; an absent block is not a default. Use mode unrestricted to permit every diet.");
            return false;
        }

        if (schemaVersion < MaterialPermissionSchemaVersion)
        {
            errors.Add($"[dietsetup] food-overrides '{label}': access requires schemaVersion {MaterialPermissionSchemaVersion}; "
                + $"a schemaVersion {schemaVersion} file must not carry one.");
            return false;
        }

        if (string.IsNullOrEmpty(file.Mode))
        {
            errors.Add($"[dietsetup] food-overrides '{label}': access.mode is required and must be unrestricted, listed or denied.");
            return false;
        }

        bool listed = string.Equals(file.Mode, "listed", StringComparison.OrdinalIgnoreCase);
        bool denied = string.Equals(file.Mode, "denied", StringComparison.OrdinalIgnoreCase);
        bool unrestricted = string.Equals(file.Mode, "unrestricted", StringComparison.OrdinalIgnoreCase);
        if (!listed && !denied && !unrestricted)
        {
            errors.Add($"[dietsetup] food-overrides '{label}': access.mode '{file.Mode}' is not unrestricted, listed or denied.");
            return false;
        }

        if (!listed)
        {
            if (file.Diets != null)
            {
                errors.Add($"[dietsetup] food-overrides '{label}': access.diets is only valid with mode listed.");
                return false;
            }
            rule = denied ? FoodAccessRule.Denied : FoodAccessRule.Unrestricted;
            return true;
        }

        // An empty list is neither "nobody" nor "everybody": both of those have their own mode.
        if (file.Diets == null || file.Diets.Count == 0)
        {
            errors.Add($"[dietsetup] food-overrides '{label}': access.mode listed requires at least one diet id.");
            return false;
        }

        bool ok = true;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? diet in file.Diets)
        {
            if (string.IsNullOrWhiteSpace(diet))
            {
                errors.Add($"[dietsetup] food-overrides '{label}': access.diets contains a blank diet id.");
                ok = false;
                continue;
            }
            if (!seen.Add(diet))
            {
                errors.Add($"[dietsetup] food-overrides '{label}': access.diets names '{diet}' twice.");
                ok = false;
                continue;
            }
            // A comma would be indistinguishable from the list separator once the rule is encoded.
            if (diet.Contains(','))
            {
                errors.Add($"[dietsetup] food-overrides '{label}': access.diets id '{diet}' may not contain a comma.");
                ok = false;
                continue;
            }
            if (knownDietIds != null && !knownDietIds.Contains(diet))
            {
                errors.Add($"[dietsetup] food-overrides '{label}': access.diets names '{diet}', which is not a configured diet. "
                    + $"Known diets: {string.Join(", ", knownDietIds.OrderBy(d => d, StringComparer.Ordinal))}.");
                ok = false;
            }
        }

        if (ok) rule = FoodAccessRule.Listed(seen);
        return ok;
    }

    private static void ValidateAndApply(ICoreAPI api, List<string> log, IReadOnlyList<CompiledOverride> rows, SideState state)
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

        int grantedCount = 0, restrictedCount = 0;
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
            state.GrantedRows.Add((collectible, row.Category, row.BaseSatiety, row.Access));
            Restrict(state, collectible, row.Access);
            if (row.Access.Restricts) restrictedCount++;
            grantedCount++;
        }

        log.Add($"[dietsetup] food-overrides: {grantedCount} item(s) granted nutritionProps, {restrictedCount} with a diet permission ({rows.Count} pattern row(s))");
    }

    private static void Restrict(SideState state, CollectibleObject collectible, FoodAccessRule rule)
    {
        state.Access[collectible] = rule;
        if (rule.Restricts) state.RestrictedCount++;
    }

    public static List<CollectibleObject> ApplyFromPacket(ICoreClientAPI capi, DietFoodOverridesPacket packet, List<string> log)
    {
        SideState state = GetState(capi);
        var newlyApplied = new List<CollectibleObject>();
        int alreadyGranted = 0, notFound = 0, catalogMismatch = 0, badCategory = 0, badAccess = 0, restricted = 0;

        int count = Math.Min(packet.ItemCodes.Length, Math.Min(packet.Categories.Length, packet.BaseSatiety.Length));
        // An empty column is a sender that predates material permissions; a short one is truncated, and
        // reading the missing rows as unrestricted would publish grants the server meant to restrict.
        bool accessCarried = packet.Access.Length > 0;
        if (accessCarried && packet.Access.Length < count)
        {
            capi.Logger.Error("[dietsetup] food-overrides packet: {0} access value(s) for {1} row(s), whole table refused.",
                packet.Access.Length, count);
            log.Add("[dietsetup] food-overrides packet: refused, truncated access column");
            return newlyApplied;
        }

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
            // An unreadable permission is not a licence to eat: the row is dropped, so the client neither
            // grants the material nor previews it as something this diet may consume.
            string encoded = accessCarried ? packet.Access[i] : "";
            if (!FoodAccessRule.TryDecode(encoded, out FoodAccessRule access))
            {
                capi.Logger.Warning("[dietsetup] food-overrides packet: item '{0}' has unreadable access '{1}', grant skipped.", itemCode, encoded);
                badAccess++;
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
            state.GrantedRows.Add((collectible, category, packet.BaseSatiety[i], access));
            Restrict(state, collectible, access);
            if (access.Restricts) restricted++;
            newlyApplied.Add(collectible);
        }

        log.Add($"[dietsetup] food-overrides packet: {newlyApplied.Count} newly applied ({restricted} restricted), {alreadyGranted} already granted, " +
                $"{notFound} not found, {catalogMismatch} catalog mismatch, {badCategory} bad category, {badAccess} bad access ({count} row(s) received)");
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

    /// <summary>Canonical text for the reload comparison, produced through the same compiler the applied
    /// rows went through so an unchanged file cannot read as changed.</summary>
    private static string Describe(FoodAccessFile? file)
    {
        if (file == null) return FoodAccessRule.Unrestricted.Encode();
        return TryCompileAccess(file, "", MaterialPermissionSchemaVersion, null, new List<string>(), out FoodAccessRule rule)
            ? rule.Encode()
            : "invalid:" + (file.Mode ?? "?") + ":" + string.Join(",", file.Diets ?? new List<string>());
    }

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

        List<(string Pattern, string Category, float BaseSatiety, string Access)> newRows;
        try
        {
            FoodOverrideDocumentFile? doc = JsonConvert.DeserializeObject<FoodOverrideDocumentFile>(raw);
            newRows = (doc?.Grants ?? new()).Select(g => (g.Pattern ?? "", g.Category ?? "", g.BaseSatiety ?? 0f, Describe(g.Access))).ToList();
        }
        catch
        {
            newRows = new();
        }

        var oldSet = new HashSet<(string, string, float, string)>(state.Rows);
        var newSet = new HashSet<(string, string, float, string)>(newRows);
        int changed = oldSet.Count(r => !newSet.Contains(r)) + newSet.Count(r => !oldSet.Contains(r));

        log.Add($"[dietsetup] food-overrides: file changed since last apply ({changed} row(s) changed) -- restart required for this to take effect.");
    }
}
