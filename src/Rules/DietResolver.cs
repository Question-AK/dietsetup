using System;

namespace dietsetup.Rules;
public static class DietResolver
{
    public static DietResolveResult Resolve(CompiledDiet diet, ulong tagMask, float spoilLevel)
    {
        if (TryFindWinner(diet, tagMask, out CompiledRule winner))
            return Apply(winner.Verdict, winner.SatietyMult, winner.NutritionMult, spoilLevel, winner.Effects, matched: true, winner.ReplacesSpoilage, winner.DebugLabel);

        return Apply(DietVerdict.Edible, CompiledValue.Flat(diet.FallbackSatietyMult), CompiledValue.Flat(diet.FallbackNutritionMult), spoilLevel, Array.Empty<CompiledEffect>(), matched: false);
    }

    public static bool TryFindWinner(CompiledDiet diet, ulong tagMask, out CompiledRule winner)
    {
        foreach (CompiledRule rule in diet.Rules)
        {
            if (!rule.Matches(tagMask)) continue;
            winner = rule;
            return true;
        }
        winner = default;
        return false;
    }

    private static DietResolveResult Apply(DietVerdict verdict, CompiledValue satietyValue, CompiledValue nutritionValue, float spoilLevel, System.Collections.Generic.IEnumerable<CompiledEffect> effects, bool matched, bool replacesSpoilage = false, string winningRule = "fallback")
    {
        float satiety = satietyValue.Evaluate(spoilLevel);
        float nutrition = nutritionValue.Evaluate(spoilLevel);
        if (verdict == DietVerdict.Inedible)
        {
            satiety = 0f;
            nutrition = 0f;
        }

        return new DietResolveResult(verdict, Math.Max(0f, satiety), Math.Max(0f, nutrition), effects, matched, replacesSpoilage, winningRule);
    }
}
