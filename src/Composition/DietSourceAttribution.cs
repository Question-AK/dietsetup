using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Vintagestory.API.Common;

namespace dietsetup.Composition;

/// <summary>Per-source contributions the runtime actually observed for one stack. A nutrient category
/// total is not one of these: a category says which hunger bar to credit and nothing about what the
/// food is made of, so category rows never displace a declared approximation.</summary>
public readonly struct DietSourceAttribution
{
    public static readonly DietSourceAttribution None = default;

    public ImmutableArray<(string Tag, float Share)> Shares { get; }

    private DietSourceAttribution(ImmutableArray<(string, float)> shares) => Shares = shares;

    public bool HasContributions => !Shares.IsDefaultOrEmpty;

    /// <summary>Refuses a total above one rather than rescaling, so a provider can never credit more
    /// nourishment than the single saturation call delivers.</summary>
    public static DietSourceAttribution FromShares(IEnumerable<KeyValuePair<string, float>> shares)
    {
        var accepted = new List<(string, float)>();
        float total = 0f;
        foreach ((string tag, float share) in shares)
        {
            if (string.IsNullOrWhiteSpace(tag) || !float.IsFinite(share) || share <= 0f || share > 1f) return None;
            total += share;
            accepted.Add((tag.Trim(), share));
        }
        if (accepted.Count == 0 || total > 1f + 1e-4f) return None;
        return new DietSourceAttribution(ImmutableArray.CreateRange(accepted));
    }
}

public delegate DietSourceAttribution DietSourceAttributionProvider(ItemStack stack);

/// <summary>The seam through which a runtime supplies real source attribution. Nothing registers here:
/// A Culinary Artillery exposes nutrient-category totals and un-quantified ingredient codes, neither of
/// which is a source split, so ACA foods keep using declared approximations.</summary>
public static class DietSourceAttributionRegistry
{
    private static volatile (string Id, DietSourceAttributionProvider Provider)[] providers =
        Array.Empty<(string, DietSourceAttributionProvider)>();
    private static readonly object gate = new();

    public static void Register(string id, DietSourceAttributionProvider provider)
    {
        if (string.IsNullOrWhiteSpace(id) || provider == null) throw new ArgumentException("A provider needs an id and a delegate.");
        lock (gate)
        {
            var kept = new List<(string, DietSourceAttributionProvider)>(providers.Length + 1);
            foreach (var row in providers) if (row.Id != id) kept.Add(row);
            kept.Add((id, provider));
            providers = kept.ToArray();
        }
    }

    public static bool Remove(string id)
    {
        lock (gate)
        {
            var kept = new List<(string, DietSourceAttributionProvider)>(providers.Length);
            foreach (var row in providers) if (row.Id != id) kept.Add(row);
            if (kept.Count == providers.Length) return false;
            providers = kept.ToArray();
            return true;
        }
    }

    /// <summary>First provider with a usable answer wins; a throwing provider is ignored so an optional
    /// integration can never break eating.</summary>
    internal static DietSourceAttribution For(ItemStack? stack)
    {
        if (stack == null) return DietSourceAttribution.None;
        foreach ((string _, var provider) in providers)
        {
            DietSourceAttribution attribution;
            try { attribution = provider(stack); }
            catch (Exception) { continue; }
            if (attribution.HasContributions) return attribution;
        }
        return DietSourceAttribution.None;
    }
}
