using System;
using System.Collections.Generic;
using System.Linq;
using dietsetup.Composition;
using dietsetup.Rules;
using dietsetup.Tags;
using Vintagestory.API.Common;

namespace dietsetup;

public enum DietFoodTier { Plain, Feast, Good, Poor, Nothing }

/// <summary>A food's tooltip lines for one eater, as lang keys. <see cref="Tier"/> is always the derived
/// tier, even when an authored line replaces its text.</summary>
public readonly record struct DietFoodLines(DietFoodTier Tier, string? TierKey, string? AgeKey = null, string? AuthoredKey = null)
{
    public static readonly DietFoodLines None = new(DietFoodTier.Plain, null);

    public IEnumerable<string> Keys
    {
        get
        {
            if (TierKey != null) yield return TierKey;
            if (AgeKey != null) yield return AgeKey;
            if (AuthoredKey != null) yield return AuthoredKey;
        }
    }
}

/// <summary>Chooses tooltip lines from the rule that actually wins for the eater. It needs no GUI, so the
/// regression harness checks exactly what the tooltip shows.</summary>
public static class DietFoodVoice
{
    public const string FeastKey = "dietsetup:voice-feast";
    public const string GoodKey = "dietsetup:voice-good";
    public const string PoorKey = "dietsetup:voice-poor";
    public const string PoorHungerKey = "dietsetup:voice-poor-hunger";
    public const string PoorHarmfulKey = "dietsetup:voice-poor-harmful";
    public const string PoorNoBarKey = "dietsetup:voice-poor-nobar";
    public const string NothingKey = "dietsetup:voice-nothing";
    public const string AgePeakKey = "dietsetup:voice-age-peak";
    public const string AgeLaterKey = "dietsetup:voice-age-later";
    public const string AgePastKey = "dietsetup:voice-age-past";

    // Measured on the rule's own multipliers, without vanilla's spoilage loss, so food no rule touches
    // reads Plain at any age. Gain is satiety x nutrition, and nutrition into a bar the eater lacks counts as none.
    public const float NothingSatiety = 0.001f;
    public const float PoorSatiety = 0.75f;
    public const float PoorGain = 0.75f;
    public const float GoodGain = 1.2f;
    public const float FeastGain = 1.75f;
    public const float FeastSatiety = 1f;
    // The age line samples the whole spoil range; a spread within 5% of the best reads as no change.
    public const float AgeStep = 0.05f;
    public const float FlatSpread = 0.05f;
    public const float PeakShare = 0.95f;

    public static bool FeedsBar(CompiledDiet diet, EnumFoodCategory category) =>
        diet.Categories.TryGetValue(category, out CompiledCategory bar) && bar.Capacity > 0f;

    public static DietFoodLines Rate(DietResolveResult result, bool feedsBar) =>
        Rate(result.Satiety, result.Nutrition, feedsBar, result.Verdict);

    /// <summary>A composite item blends its portions by share; its tier has no age line.</summary>
    public static DietFoodLines Rate(DietContributionSet set, bool feedsBar)
    {
        if (!set.IsComposite) return Rate(set.Whole, feedsBar);
        float satiety = 0f, nutrition = 0f;
        foreach (DietContribution part in set.Components)
        {
            float filled = part.Share * part.Result.Satiety;
            satiety += filled;
            nutrition += filled * part.Result.Nutrition;
        }
        return Rate(satiety, satiety > 0f ? nutrition / satiety : 0f, feedsBar, DietVerdict.Edible);
    }

    public static DietFoodLines ForFood(FoodTagRegistry tags, CompiledDiet diet, ulong mask, float spoilLevel, bool perishes, EnumFoodCategory category) =>
        ForFood(tags, diet, mask, spoilLevel, perishes, FeedsBar(diet, category));

    /// <param name="mask">The food's mask at <paramref name="spoilLevel"/>, freshness bit included.</param>
    /// <param name="perishes">False for food with no perish transition: it never ages, so it gets neither an
    /// age line nor an authored one, which would promise a change that cannot happen.</param>
    public static DietFoodLines ForFood(FoodTagRegistry tags, CompiledDiet diet, ulong mask, float spoilLevel, bool perishes, bool feedsBar)
    {
        DietResolveResult now = DietResolver.Resolve(diet, mask, spoilLevel);
        DietFoodLines rated = Rate(now, feedsBar);
        if (!perishes) return rated;
        string? authored = DietResolver.TryFindWinner(diet, mask, out CompiledRule winner) ? winner.LineAt(spoilLevel) : null;
        if (authored != null)
        {
            // An authored line speaks for the rule, but never hides that the body takes nothing or has no bar for it.
            bool keepTier = rated.Tier == DietFoodTier.Nothing || !feedsBar;
            return rated with { TierKey = keepTier ? rated.TierKey : null, AuthoredKey = authored };
        }
        return rated with { AgeKey = AgeLine(tags, diet, mask, spoilLevel, feedsBar, Response(now, feedsBar)) };
    }

    /// <summary>One line only when every ingredient agrees: all Good or better gives the weakest of them,
    /// all Poor or Nothing gives Poor (Nothing if all are). A mixed meal has no single honest line.</summary>
    public static DietFoodLines ForMeal(IEnumerable<DietFoodLines> ingredients)
    {
        var parts = ingredients.ToList();
        if (parts.Count == 0) return DietFoodLines.None;
        if (parts.All(p => p.Tier is DietFoodTier.Good or DietFoodTier.Feast))
            return parts.Any(p => p.Tier == DietFoodTier.Good) ? new(DietFoodTier.Good, GoodKey) : new(DietFoodTier.Feast, FeastKey);
        if (!parts.All(p => p.Tier is DietFoodTier.Poor or DietFoodTier.Nothing)) return DietFoodLines.None;
        if (parts.All(p => p.Tier == DietFoodTier.Nothing)) return new(DietFoodTier.Nothing, NothingKey);
        string? shared = parts[0].TierKey;
        return new(DietFoodTier.Poor, parts.All(p => p.TierKey == shared) ? shared : PoorKey);
    }

    private static DietFoodLines Rate(float satiety, float nutrition, bool feedsBar, DietVerdict verdict)
    {
        if (satiety <= NothingSatiety) return new(DietFoodTier.Nothing, NothingKey);
        float gain = feedsBar ? satiety * nutrition : 0f;
        if (satiety < PoorSatiety || gain < PoorGain)
        {
            string key = verdict == DietVerdict.Harmful ? PoorHarmfulKey
                : satiety < PoorSatiety ? PoorHungerKey
                : gain <= 0f ? PoorNoBarKey
                : PoorKey;
            return new(DietFoodTier.Poor, key);
        }
        if (gain >= FeastGain && satiety >= FeastSatiety) return new(DietFoodTier.Feast, FeastKey);
        if (gain >= GoodGain) return new(DietFoodTier.Good, GoodKey);
        return DietFoodLines.None;
    }

    private static float Response(DietResolveResult result, bool feedsBar) =>
        feedsBar ? result.Satiety * result.Nutrition : result.Satiety;

    /// <summary>Re-resolves the whole diet across the spoil range, so a different rule winning once the
    /// food turns counts as the change it is.</summary>
    private static string? AgeLine(FoodTagRegistry tags, CompiledDiet diet, ulong mask, float spoilLevel, bool feedsBar, float now)
    {
        int steps = (int)Math.Round(1f / AgeStep);
        var responses = new float[steps + 1];
        float max = now, min = now;
        for (int i = 0; i <= steps; i++)
        {
            float spoil = i * AgeStep;
            responses[i] = Response(DietResolver.Resolve(diet, tags.WithSpoilLevel(mask, spoil), spoil), feedsBar);
            max = Math.Max(max, responses[i]);
            min = Math.Min(min, responses[i]);
        }
        if (max <= 0f || max - min <= FlatSpread * max) return null;
        if (now >= PeakShare * max) return AgePeakKey;
        for (int i = 0; i <= steps; i++)
        {
            if (i * AgeStep > spoilLevel && responses[i] >= PeakShare * max) return AgeLaterKey;
        }
        return AgePastKey;
    }
}
