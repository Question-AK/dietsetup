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

    public CompiledRule(ulong requiresMask, ulong excludesMask, int specificity, int priority, DietVerdict verdict, CompiledValue satietyMult, CompiledValue nutritionMult, IEnumerable<CompiledEffect> effects, string debugLabel, bool shadowedIntentionally = false, bool replacesSpoilage = false)
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
    }

    public bool Matches(ulong tagMask) => (tagMask & RequiresMask) == RequiresMask && (tagMask & ExcludesMask) == 0;
}
