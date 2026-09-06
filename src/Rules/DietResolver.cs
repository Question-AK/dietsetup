using System;

namespace dietsetup.Rules;
public static class DietResolver
{
    public static DietResolveResult Resolve(CompiledDiet diet, ulong tagMask, float spoilLevel)
    {
        var rules = diet.Rules;
        for (int i = 0; i < rules.Length; i++)
        {
            if (rules[i].Matches(tagMask))
            {
                CompiledRule winner = rules[i];
                return Apply(winner.Verdict, winner.SatietyMult, winner.NutritionMult, spoilLevel, winner.Effects, matched: true, winner.ReplacesSpoilage, winner.DebugLabel);
            }
        }

        return Apply(DietVerdict.Edible, CompiledValue.Flat(diet.FallbackSatietyMult), CompiledValue.Flat(diet.FallbackNutritionMult), spoilLevel, Array.Empty<CompiledEffect>(), matched: false);
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
