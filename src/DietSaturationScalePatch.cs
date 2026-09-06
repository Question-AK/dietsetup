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
        out (DietIngredientTrace? Row, float Before, float BeforeSatiety) __state)
    {
        __state = (null, 0, 0);
        var snapshot = DietRuntimeSnapshot.For(__instance.entity.Api);
        if (!snapshot.Config.EnableDietSystem) return;
        if (__instance.entity is EntityAgent agent && DietConsumption.Current?.TryCredit(agent,
            out float nutritionMult) == true) nutritionGainMultiplier *= nutritionMult;
        var trace = ReferenceEquals(DietConsumption.Current?.Entity, __instance.entity) ? DietConsumption.Current?.ActiveTrace : null;
        if (trace != null)
        {
            trace.SubmittedSatiety = saturation;
            __state = (trace, DietDiagnostics.Level(__instance, foodCat), __instance.Saturation);
        }
        var diet = DietIdResolver.ResolveDiet(__instance.entity, snapshot);
        if (diet != null && diet.Categories.TryGetValue(foodCat, out var category))
            nutritionGainMultiplier *= category.NutritionGainScale;
    }

    [HarmonyPostfix]
    internal static void Postfix(EntityBehaviorHunger __instance, EnumFoodCategory foodCat, bool __runOriginal,
        (DietIngredientTrace? Row, float Before, float BeforeSatiety) __state)
    {
        if (__state.Row == null) return;
        __state.Row.ActualNutrition = __runOriginal ? DietDiagnostics.Level(__instance, foodCat) - __state.Before : 0f;
        __state.Row.CreditedSatiety = __runOriginal ? __instance.Saturation - __state.BeforeSatiety : 0f;
    }
}
