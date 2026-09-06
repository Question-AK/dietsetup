using System.Collections.Generic;
using System.Collections.Immutable;

namespace dietsetup.Rules;
public readonly struct DietResolveResult
{
    public readonly DietVerdict Verdict;
    public readonly float Satiety;
    public readonly float Nutrition;
    public readonly ImmutableArray<CompiledEffect> Effects;
    public readonly string WinningRule;
    public readonly bool Matched;
    public readonly bool ReplacesSpoilage;

    public DietResolveResult(DietVerdict verdict, float satiety, float nutrition, IEnumerable<CompiledEffect> effects, bool matched, bool replacesSpoilage = false, string winningRule = "fallback")
    {
        WinningRule = winningRule;
        Verdict = verdict;
        Satiety = satiety;
        Nutrition = nutrition;
        Effects = effects.ToImmutableArray();
        Matched = matched;
        ReplacesSpoilage = replacesSpoilage;
    }
}
