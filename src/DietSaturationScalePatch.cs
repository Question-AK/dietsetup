using dietsetup.Binding;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace dietsetup;

// Vanilla converts credited satiety to nutrition here; rule nutrition and capacity each apply once.
[HarmonyPatch(typeof(EntityBehaviorHunger), nameof(EntityBehaviorHunger.OnEntityReceiveSaturation))]
public static class DietSaturationScalePatch
{
    [HarmonyPrefix]
    internal static void Prefix(EntityBehaviorHunger __instance, EnumFoodCategory foodCat, float saturation, ref float nutritionGainMultiplier,
        out (DietConsumption? Operation, DietIngredientTrace? Row, float Before, float BeforeSatiety, float BeforeDelay) __state)
    {
        __state = (null, null, 0, 0, 0);
        var snapshot = DietRuntimeSnapshot.For(__instance.entity.Api);
        if (!snapshot.Config.EnableDietSystem) return;
        if (__instance.entity is EntityAgent agent && DietConsumption.Current?.TryCredit(agent,
            out float nutritionMult) == true) nutritionGainMultiplier *= nutritionMult;
        var operation = ReferenceEquals(DietConsumption.Current?.Entity, __instance.entity) ? DietConsumption.Current : null;
        if (operation != null)
        {
            // Captured for every credit the open transaction sees, not only traced ones, so a refused
            // mouthful can be withdrawn even where no row was queued for it.
            __state = (operation, operation.ActiveTrace, DietDiagnostics.Level(__instance, foodCat),
                __instance.Saturation, DietDiagnostics.LossDelay(__instance, foodCat));
            if (__state.Row != null) __state.Row.SubmittedSatiety = saturation;
        }
        var diet = DietIdResolver.ResolveDiet(__instance.entity, snapshot);
        if (diet != null && diet.Categories.TryGetValue(foodCat, out var category))
            nutritionGainMultiplier *= category.NutritionGainScale;
    }

    [HarmonyPostfix]
    internal static void Postfix(EntityBehaviorHunger __instance, EnumFoodCategory foodCat, bool __runOriginal,
        (DietConsumption? Operation, DietIngredientTrace? Row, float Before, float BeforeSatiety, float BeforeDelay) __state)
    {
        if (__state.Operation == null) return;
        float nutrition = __runOriginal ? DietDiagnostics.Level(__instance, foodCat) - __state.Before : 0f;
        float credited = __runOriginal ? __instance.Saturation - __state.BeforeSatiety : 0f;
        if (__runOriginal) __state.Operation.RecordSubmission(foodCat, nutrition, credited, __state.BeforeDelay);
        if (__state.Row == null) return;
        __state.Row.ActualNutrition = nutrition;
        __state.Row.CreditedSatiety = credited;
    }
}
