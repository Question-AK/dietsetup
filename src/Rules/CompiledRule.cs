using System.Collections.Generic;
using System.Collections.Immutable;

namespace dietsetup.Rules;
public readonly struct CompiledRule
{
    public readonly ulong RequiresMask;
    public readonly ulong ExcludesMask;
    public readonly int Specificity;
    public readonly int Priority;
    public readonly DietVerdict Verdict;
    public readonly CompiledValue SatietyMult;
    public readonly CompiledValue NutritionMult;
    public readonly ImmutableArray<CompiledEffect> Effects;
    public readonly string DebugLabel;
    public readonly bool ShadowedIntentionally;
    public readonly bool ReplacesSpoilage;
    /// <summary>Sorted by spoil.</summary>
    public readonly ImmutableArray<CompiledLine> Lines;

    public CompiledRule(ulong requiresMask, ulong excludesMask, int specificity, int priority, DietVerdict verdict, CompiledValue satietyMult, CompiledValue nutritionMult, IEnumerable<CompiledEffect> effects, string debugLabel, bool shadowedIntentionally = false, bool replacesSpoilage = false, IEnumerable<CompiledLine>? lines = null)
    {
        RequiresMask = requiresMask;
        ExcludesMask = excludesMask;
        Specificity = specificity;
        Priority = priority;
        Verdict = verdict;
        SatietyMult = satietyMult;
        NutritionMult = nutritionMult;
        Effects = effects.ToImmutableArray();
        DebugLabel = debugLabel;
        ShadowedIntentionally = shadowedIntentionally;
        ReplacesSpoilage = replacesSpoilage;
        Lines = lines?.ToImmutableArray() ?? ImmutableArray<CompiledLine>.Empty;
    }

    public bool Matches(ulong tagMask) => (tagMask & RequiresMask) == RequiresMask && (tagMask & ExcludesMask) == 0;

    /// <summary>The authored line for this spoil level, or null below the first line's spoil.</summary>
    public string? LineAt(float spoilLevel)
    {
        // default(CompiledRule) leaves the array uninitialised.
        if (Lines.IsDefaultOrEmpty) return null;
        string? key = null;
        foreach (CompiledLine line in Lines)
        {
            if (spoilLevel < line.Spoil) break;
            key = line.Key;
        }
        return key;
    }
}
