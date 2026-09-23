using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using dietsetup.Rules;

namespace dietsetup.Composition;

public enum DietContributionBasis
{
    /// <summary>Resolved from a real identity the runtime actually has.</summary>
    Actual,
    /// <summary>Resolved from a declared approximate share.</summary>
    Approximated,
    /// <summary>Not apportioned to any identity; passes upstream nourishment through unchanged.</summary>
    Unresolved
}

/// <summary>Vanilla's own spoilage satiety multiplier for one mouthful, recorded by whichever query
/// observes it. Every copy of a set shares one of these, so asking for health, satiety or a preview in
/// any order weights the components identically.</summary>
public sealed class DietSpoilageWeight
{
    public float Value { get; private set; } = 1f;
    public bool Known { get; private set; }

    /// <summary>First observation wins: vanilla's multiplier is deterministic for one stack and spoil
    /// level, so a later caller that never saw it cannot overwrite the real value with a default.</summary>
    internal void Capture(float vanilla)
    {
        if (Known || !float.IsFinite(vanilla) || vanilla < 0f) return;
        Value = vanilla;
        Known = true;
    }
}

public readonly struct DietContribution
{
    public readonly float Share;
    public readonly DietResolveResult Result;
    public readonly string Label;
    public readonly ulong Mask;
    public readonly DietContributionBasis Basis;

    public DietContribution(float share, DietResolveResult result, string label, ulong mask, DietContributionBasis basis)
    {
        Share = share;
        Result = result;
        Label = label;
        Mask = mask;
        Basis = basis;
    }

    internal float EffectiveSatiety(float vanilla) => DietSpoilageResolution.ApplySatiety(vanilla, Result);
}

/// <summary>One physical mouthful of one identifiable food, split into the contributions it is made of.
/// A non-composite item is the degenerate one-contribution case and behaves exactly as before.</summary>
public sealed class DietContributionSet
{
    public ImmutableArray<DietContribution> Components { get; }
    public DietResolveResult Whole { get; }
    public bool IsComposite { get; }
    /// <summary>The whole item's own mask, kept beside the portion masks for diagnostics.</summary>
    public ulong ItemMask { get; }
    /// <summary>Matters only when components of one item disagree about replacing spoilage; otherwise
    /// vanilla's multiplier cancels out of the nutrition weighting.</summary>
    public DietSpoilageWeight Spoilage { get; }
    /// <summary>Contributions sharing a group fire a winning rule's effects once, so splitting one
    /// item into virtual portions cannot multiply an existing consequence.</summary>
    public object Group { get; }

    private DietContributionSet(ImmutableArray<DietContribution> components, DietResolveResult whole,
        bool composite, ulong itemMask, DietSpoilageWeight spoilage, object group)
    {
        Components = components;
        Whole = whole;
        IsComposite = composite;
        ItemMask = itemMask;
        Spoilage = spoilage;
        Group = group;
    }

    public static DietContributionSet Single(DietResolveResult whole, ulong mask, object? group = null) =>
        new(ImmutableArray.Create(new DietContribution(1f, whole, "whole", mask, DietContributionBasis.Actual)),
            whole, false, mask, new DietSpoilageWeight(), group ?? new object());

    public static DietContributionSet Composite(IEnumerable<DietContribution> components, DietResolveResult whole,
        ulong mask, object? group = null)
    {
        var list = ImmutableArray.CreateRange(components);
        return list.Length switch
        {
            0 => Single(whole, mask, group),
            _ => new(list, whole, true, mask, new DietSpoilageWeight(), group ?? new object())
        };
    }

    /// <summary>The satiety multiplier vanilla applies once, for the whole mouthful.</summary>
    public float Satiety(float vanilla)
    {
        if (!IsComposite) return DietSpoilageResolution.ApplySatiety(vanilla, Whole);
        float total = 0f;
        foreach (var component in Components) total += component.Share * component.EffectiveSatiety(vanilla);
        return Math.Max(0f, total);
    }

    /// <summary>The health multiplier, blended over the same shares so a split cannot add healing.</summary>
    public float Health(float vanilla)
    {
        if (!IsComposite) return DietSpoilageResolution.ApplyHealth(vanilla, Whole);
        float total = 0f;
        foreach (var component in Components)
            total += component.Share * DietSpoilageResolution.ApplyHealth(vanilla, component.Result);
        return Math.Max(0f, total);
    }

    /// <summary>The nutrition multiplier that credits every component exactly once through the single
    /// saturation call vanilla makes. Every component carries the item's own category, so one weighted
    /// multiplier is exact rather than a blend that loses information.</summary>
    public float Nutrition()
    {
        if (!IsComposite) return Whole.Nutrition;
        float vanilla = Spoilage.Value;
        float weighted = 0f, total = 0f, byShare = 0f, shares = 0f;
        foreach (var component in Components)
        {
            float effective = component.Share * component.EffectiveSatiety(vanilla);
            total += effective;
            weighted += effective * component.Result.Nutrition;
            shares += component.Share;
            byShare += component.Share * component.Result.Nutrition;
        }
        // Nothing is credited when the mouthful carries no effective satiety, so the share-weighted
        // value only has to stay finite.
        if (total > 0f) return weighted / total;
        return shares > 0f ? byShare / shares : Whole.Nutrition;
    }

    internal DietContributionSet WithGroup(object group) =>
        new(Components, Whole, IsComposite, ItemMask, Spoilage, group);
}
