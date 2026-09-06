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
    [HarmonyPrefix]
    public static bool Prefix(EntityBehaviorHunger __instance)
    {
        using var snapshotScope = DietRuntimeSnapshot.Read(__instance.entity.Api);
        if (!DietRuntimeSnapshot.For(__instance.entity.Api).Config.EnableDietSystem) return true;

        CompiledDiet? diet = DietIdResolver.ResolveDiet(__instance.entity);
        if (diet == null) return true;

        float bonus = ComputeBonus(diet, __instance);
        __instance.entity.GetBehavior<EntityBehaviorHealth>()?.SetMaxHealthModifiers("nutrientHealthMod", bonus);

        return false;
    }
    public static float ComputeBonus(CompiledDiet diet, EntityBehaviorHunger hunger)
    {
        float maxSaturation = hunger.MaxSaturation;
        float numerator = 0f;
        float denominator = 0f;

        AddCategory(diet, EnumFoodCategory.Fruit, hunger.FruitLevel, maxSaturation, ref numerator, ref denominator);
        AddCategory(diet, EnumFoodCategory.Vegetable, hunger.VegetableLevel, maxSaturation, ref numerator, ref denominator);
        AddCategory(diet, EnumFoodCategory.Protein, hunger.ProteinLevel, maxSaturation, ref numerator, ref denominator);
        AddCategory(diet, EnumFoodCategory.Grain, hunger.GrainLevel, maxSaturation, ref numerator, ref denominator);
        AddCategory(diet, EnumFoodCategory.Dairy, hunger.DairyLevel, maxSaturation, ref numerator, ref denominator);
        return 12.5f * (numerator / denominator);
    }

    private static void AddCategory(CompiledDiet diet, EnumFoodCategory cat, float level, float maxSaturation, ref float numerator, ref float denominator)
    {
        if (!diet.Categories.TryGetValue(cat, out CompiledCategory category)) return;

        numerator += level / maxSaturation * category.HealthWeight;
        denominator += category.HealthWeight;
    }
}
