using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using dietsetup.Rules;
using dietsetup.Tags;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace dietsetup.Composition;

public sealed class CompiledComposition
{
    public string Pattern { get; init; } = "";
    public ImmutableArray<(string Tag, float Share)> Shares { get; init; } = ImmutableArray<(string, float)>.Empty;
    public float UnresolvedRemainder { get; init; }
    public string Note { get; init; } = "";
}

/// <summary>Declared approximate composition for foods whose real per-ingredient contributions the
/// runtime cannot see. Never carries a nutrient category: a share splits delivered nourishment only.</summary>
public sealed class FoodCompositionRegistry
{
    public const string SupportedAppliesWhen = "no-usable-real-contributions";
    private const float Tolerance = 1e-4f;

    private readonly List<CompiledComposition> declared = new();
    private CompiledComposition?[] itemEntries = Array.Empty<CompiledComposition?>();
    private CompiledComposition?[] blockEntries = Array.Empty<CompiledComposition?>();
    private bool frozen;

    public int DeclaredCount => declared.Count;
    public int MatchedCollectibleCount { get; private set; }

    public void LoadFrom(FoodCompositionFile file, string source, List<string> errors)
    {
        EnsureMutable();
        // An absent document is "no composition declared", not a malformed one; only a document that
        // actually says something has to declare a schema.
        if (file.SchemaVersion == null && (file.Entries == null || file.Entries.Count == 0)) return;
        if (file.SchemaVersion != 1)
        {
            errors.Add($"{source}: schemaVersion must be 1 (got {file.SchemaVersion?.ToString() ?? "(missing)"}); file ignored");
            return;
        }
        string appliesWhen = string.IsNullOrWhiteSpace(file.AppliesWhen) ? SupportedAppliesWhen : file.AppliesWhen!;
        if (!string.Equals(appliesWhen, SupportedAppliesWhen, StringComparison.Ordinal))
        {
            errors.Add($"{source}: appliesWhen '{appliesWhen}' is not supported; file ignored");
            return;
        }
        foreach (FoodCompositionEntryFile entry in file.Entries ?? new List<FoodCompositionEntryFile>())
        {
            CompiledComposition? compiled = CompileEntry(entry, source, errors);
            if (compiled == null) continue;
            int existing = declared.FindIndex(row => string.Equals(row.Pattern, compiled.Pattern, StringComparison.Ordinal));
            if (existing >= 0) declared[existing] = compiled;
            else declared.Add(compiled);
        }
    }

    private static CompiledComposition? CompileEntry(FoodCompositionEntryFile entry, string source, List<string> errors)
    {
        string pattern = entry.Pattern?.Trim() ?? "";
        if (pattern.Length == 0 || !pattern.Contains(':'))
        {
            errors.Add($"{source}: entry pattern '{entry.Pattern}' must be a domain-qualified code or pattern");
            return null;
        }
        if (!string.Equals(entry.Basis, "declared-approximate", StringComparison.Ordinal))
        {
            errors.Add($"{source}: '{pattern}' basis must be 'declared-approximate' (got '{entry.Basis ?? "(missing)"}'); real contributions always win");
            return null;
        }
        if (entry.Shares == null || entry.Shares.Count == 0)
        {
            errors.Add($"{source}: '{pattern}' declares no shares");
            return null;
        }
        float total = 0f;
        var shares = new List<(string, float)>();
        foreach ((string tag, float share) in entry.Shares)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                errors.Add($"{source}: '{pattern}' has an empty component tag");
                return null;
            }
            if (!float.IsFinite(share) || share <= 0f || share > 1f)
            {
                errors.Add($"{source}: '{pattern}' share '{tag}'={share} must be finite and within (0, 1]");
                return null;
            }
            total += share;
            shares.Add((tag.Trim(), share));
        }
        if (total > 1f + Tolerance)
        {
            errors.Add($"{source}: '{pattern}' shares total {total:F4}, which would duplicate nourishment; totals are refused, never rescaled");
            return null;
        }
        float remainder = Math.Max(0f, 1f - total);
        if (entry.UnresolvedRemainder is { } declaredRemainder && Math.Abs(declaredRemainder - remainder) > Tolerance)
        {
            errors.Add($"{source}: '{pattern}' unresolvedRemainder {declaredRemainder:F4} disagrees with 1 - total ({remainder:F4})");
            return null;
        }
        return new CompiledComposition
        {
            Pattern = pattern,
            Shares = ImmutableArray.CreateRange(shares),
            UnresolvedRemainder = remainder,
            Note = entry.Note ?? ""
        };
    }

    /// <summary>Component tags must exist on the source axis, or a share would silently resolve as the
    /// item itself and look like a working approximation.</summary>
    public void Validate(FoodTagRegistry tags, List<string> errors)
    {
        for (int i = declared.Count - 1; i >= 0; i--)
        {
            CompiledComposition entry = declared[i];
            foreach ((string tag, float _) in entry.Shares)
            {
                if (tags.IsSourceTag(tag)) continue;
                errors.Add($"food-composition: '{entry.Pattern}' component '{tag}' is not a registered source tag; entry refused");
                declared.RemoveAt(i);
                break;
            }
        }
    }

    public void ResolveStatic(ICoreAPI api)
    {
        EnsureMutable();
        itemEntries = new CompiledComposition?[api.World.Items.Count];
        blockEntries = new CompiledComposition?[api.World.Blocks.Count];
        MatchedCollectibleCount = 0;
        if (declared.Count == 0) return;

        foreach (CollectibleObject collectible in api.World.Collectibles)
        {
            AssetLocation? code = collectible.Code;
            if (code == null) continue;
            string codeStr = code.ToString();
            CompiledComposition? best = null;
            int bestScore = -1;
            foreach (CompiledComposition entry in declared)
            {
                if (!WildcardUtil.Match(entry.Pattern, codeStr)) continue;
                int score = string.Equals(entry.Pattern, codeStr, StringComparison.Ordinal)
                    ? int.MaxValue
                    : entry.Pattern.Count(c => c != '*');
                if (score <= bestScore) continue;
                bestScore = score;
                best = entry;
            }
            if (best == null) continue;
            if (collectible is Block) blockEntries[collectible.Id] = best;
            else itemEntries[collectible.Id] = best;
            MatchedCollectibleCount++;
        }
    }

    public CompiledComposition? For(CollectibleObject collectible)
    {
        CompiledComposition?[] table = collectible is Block ? blockEntries : itemEntries;
        int id = collectible.Id;
        return id >= 0 && id < table.Length ? table[id] : null;
    }

    internal FoodCompositionFile Export() => new()
    {
        SchemaVersion = 1,
        AppliesWhen = SupportedAppliesWhen,
        Entries = declared.Select(entry => new FoodCompositionEntryFile
        {
            Pattern = entry.Pattern,
            Basis = "declared-approximate",
            Shares = entry.Shares.ToDictionary(s => s.Tag, s => s.Share),
            UnresolvedRemainder = entry.UnresolvedRemainder,
            Note = entry.Note
        }).ToList()
    };

    internal void Freeze() => frozen = true;
    private void EnsureMutable()
    {
        if (frozen) throw new InvalidOperationException("Published food composition cannot be modified.");
    }

    /// <summary>Builds the contributions for one mouthful. Real contributions always win: when the caller
    /// already has usable per-identity contributions, the declared approximation is skipped entirely.</summary>
    public DietContributionSet Build(FoodTagRegistry tags, CompiledDiet diet, CollectibleObject collectible,
        ulong mask, float spoilLevel, float vanillaSatLoss, bool realContributionsAvailable, object? group = null)
    {
        DietResolveResult whole = DietResolver.Resolve(diet, mask, spoilLevel);
        CompiledComposition? entry = realContributionsAvailable ? null : For(collectible);
        if (entry == null) return DietContributionSet.Single(whole, mask, vanillaSatLoss, group);

        var components = new List<DietContribution>(entry.Shares.Length + 1);
        foreach ((string tag, float share) in entry.Shares)
        {
            ulong portionMask = tags.PortionMask(mask, tag);
            components.Add(new DietContribution(share, DietResolver.Resolve(diet, portionMask, spoilLevel),
                tag, portionMask, DietContributionBasis.Approximated));
        }
        if (entry.UnresolvedRemainder > Tolerance)
            components.Add(new DietContribution(entry.UnresolvedRemainder, Neutral, "unresolved", mask,
                DietContributionBasis.Unresolved));
        return DietContributionSet.Composite(components, whole, mask, vanillaSatLoss, group);
    }

    /// <summary>An unresolved portion passes upstream nourishment through untouched so no broad rule --
    /// freshness in particular -- reaches a portion no identity was ever claimed for. Its category
    /// capacity still applies, because that comes from the category, not from this result.</summary>
    internal static readonly DietResolveResult Neutral =
        new(DietVerdict.Edible, 1f, 1f, Array.Empty<CompiledEffect>(), matched: false, replacesSpoilage: false, winningRule: "unresolved");
}
