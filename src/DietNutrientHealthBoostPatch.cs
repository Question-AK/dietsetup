using dietsetup.Binding;
using dietsetup.Rules;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace dietsetup;
[HarmonyPatch(typeof(EntityBehaviorHunger), nameof(EntityBehaviorHunger.UpdateNutrientHealthBoost))]
public static class DietNutrientHealthBoostPatch
{
    public const float MaxBonus = 12.5f;

    [HarmonyPrefix]
    public static bool Prefix(EntityBehaviorHunger __instance)
    {
        using var snapshotScope = DietRuntimeSnapshot.Read(__instance.entity.Api);
        if (!DietRuntimeSnapshot.For(__instance.entity.Api).Config.EnableDietSystem)
        {
            DietNutritionBasis.Follow(__instance);
            return true;
        }

        CompiledDiet? diet = DietIdResolver.ResolveDiet(__instance.entity);
        DietNutritionBasis.Reconcile(__instance, diet);
        float bonus = ComputeBonus(diet, __instance);
        __instance.entity.GetBehavior<EntityBehaviorHealth>()?.SetMaxHealthModifiers("nutrientHealthMod", bonus);

        return false;
    }

    /// <summary>A null diet weighs all five categories equally, as vanilla does.</summary>
    public static float ComputeBonus(CompiledDiet? diet, EntityBehaviorHunger hunger)
    {
        float maxSaturation = hunger.MaxSaturation;
        if (!(maxSaturation > 0f)) return 0f;
        float numerator = 0f;
        float denominator = 0f;

        AddCategory(diet, EnumFoodCategory.Fruit, hunger.FruitLevel, maxSaturation, ref numerator, ref denominator);
        AddCategory(diet, EnumFoodCategory.Vegetable, hunger.VegetableLevel, maxSaturation, ref numerator, ref denominator);
        AddCategory(diet, EnumFoodCategory.Protein, hunger.ProteinLevel, maxSaturation, ref numerator, ref denominator);
        AddCategory(diet, EnumFoodCategory.Grain, hunger.GrainLevel, maxSaturation, ref numerator, ref denominator);
        AddCategory(diet, EnumFoodCategory.Dairy, hunger.DairyLevel, maxSaturation, ref numerator, ref denominator);
        return denominator > 0f ? MaxBonus * (numerator / denominator) : 0f;
    }

    private static void AddCategory(CompiledDiet? diet, EnumFoodCategory cat, float level, float maxSaturation, ref float numerator, ref float denominator)
    {
        if (!DietNutritionBasis.Supports(diet, cat)) return;
        float weight = diet == null ? 1f : diet.Categories[cat].HealthWeight;

        numerator += DietNutritionBasis.Fraction(level, maxSaturation) * weight;
        denominator += weight;
    }
}
