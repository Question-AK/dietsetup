using System;
using dietsetup.Binding;
using dietsetup.Rules;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace dietsetup;

// Rescales the observed decrement rather than recomputing it, so the result does not depend on whether
// PlayerModelLib's saturation-loss prefix runs before or after this one. Legacy diets are not touched.
[HarmonyPatch(typeof(EntityBehaviorHunger), "ReduceSaturation")]
internal static class DietNutritionDecayPatch
{
    private static readonly EnumFoodCategory[] Categories =
        [EnumFoodCategory.Fruit, EnumFoodCategory.Vegetable, EnumFoodCategory.Protein, EnumFoodCategory.Grain, EnumFoodCategory.Dairy];

    [HarmonyPrefix]
    private static void Prefix(EntityBehaviorHunger __instance, out (CompiledDiet? Diet, float Max, float Demand, float[]? Levels) __state)
    {
        __state = default;
        if (__instance.entity.Api?.Side != EnumAppSide.Server) return;
        var snapshot = DietRuntimeSnapshot.For(__instance.entity.Api);
        if (!snapshot.Config.EnableDietSystem) return;
        var diet = DietIdResolver.ResolveDiet(__instance.entity, snapshot);
        if (diet?.NutritionModel != NutritionModel.DemandNormalised) return;
        // Vanilla reconciles only after its decrements; a changed stomach must rescale the levels they are measured on.
        DietNutritionBasis.Reconcile(__instance, diet);
        var levels = new float[Categories.Length];
        for (int i = 0; i < Categories.Length; i++) levels[i] = DietDiagnostics.Level(__instance, Categories[i]);
        __state = (diet, __instance.MaxSaturation, DemandNutrition.Demand(__instance.entity), levels);
    }

    [HarmonyPostfix]
    private static void Postfix(EntityBehaviorHunger __instance, (CompiledDiet? Diet, float Max, float Demand, float[]? Levels) __state)
    {
        if (__state.Levels == null) return;
        bool changed = false;
        for (int i = 0; i < Categories.Length; i++)
        {
            if (!DietNutritionBasis.Supports(__state.Diet, Categories[i])) continue;
            float before = __state.Levels[i];
            float after = DietDiagnostics.Level(__instance, Categories[i]);
            // A paused bar did not move; a bar vanilla emptied stays empty rather than creeping towards zero.
            if (!(after > 0f) || !(after < before)) continue;
            float ratio = DemandNutrition.DecayRatio(before, __state.Max, __state.Demand);
            if (ratio == 1f) continue;
            DietConsumption.SetLevel(__instance, Categories[i], Math.Max(0f, before - (before - after) * ratio));
            changed = true;
        }
        // Vanilla's own health update ran between its decrements and this correction.
        if (changed) __instance.UpdateNutrientHealthBoost();
    }
}
