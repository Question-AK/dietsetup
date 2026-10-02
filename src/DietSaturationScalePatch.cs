using dietsetup.Binding;
using dietsetup.Rules;
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
        var diet = DietIdResolver.ResolveDiet(__instance.entity, snapshot);
        // Before vanilla clamps this credit to a changed stomach and before the levels below are captured.
        DietNutritionBasis.Reconcile(__instance, diet);
        // Before the bite decision and the rollback capture, so a refused mouthful cannot restore removed excess.
        DietNutritionBasis.BoundSatiety(__instance);
        var operation = ReferenceEquals(DietConsumption.Current?.Entity, __instance.entity) ? DietConsumption.Current : null;
        if (operation != null)
        {
            // At the first credit, not per credit: one that fills the stomach must not cost the rest of the mouthful.
            operation.StartedFull ??= __instance.Saturation >= __instance.MaxSaturation;
            if (diet?.OverflowNutrition == OverflowNutrition.Proportional)
            {
                operation.OpenProportionalCredit(__instance.MaxSaturation - __instance.Saturation);
                operation.NoteLevelBeforeCredit(foodCat, DietDiagnostics.Level(__instance, foodCat));
            }
            if (operation.TryCredit(operation.Entity, out float nutritionMult)) nutritionGainMultiplier *= nutritionMult;
            // Captured for every credit the open transaction sees, not only traced ones, so a refused
            // mouthful can be withdrawn even where no row was queued for it.
            __state = (operation, operation.ActiveTrace, DietDiagnostics.Level(__instance, foodCat),
                __instance.Saturation, DietDiagnostics.LossDelay(__instance, foodCat));
            if (__state.Row != null) __state.Row.SubmittedSatiety = saturation;
        }
        nutritionGainMultiplier *= DemandNutrition.GainScale(diet, foodCat, __instance);
    }

    [HarmonyPostfix]
    internal static void Postfix(EntityBehaviorHunger __instance, EnumFoodCategory foodCat, bool __runOriginal, float saturation,
        float nutritionGainMultiplier, (DietConsumption? Operation, DietIngredientTrace? Row, float Before, float BeforeSatiety, float BeforeDelay) __state)
    {
        if (__state.Operation == null) return;
        float nutrition = __runOriginal ? DietDiagnostics.Level(__instance, foodCat) - __state.Before : 0f;
        float credited = __runOriginal ? __instance.Saturation - __state.BeforeSatiety : 0f;
        if (__runOriginal) __state.Operation.RecordSubmission(foodCat, nutrition, credited, __state.BeforeDelay);
        // Read here rather than in the prefix: every prefix, PlayerModelLib's satiety stats included, has scaled both by now.
        if (__runOriginal) __state.Operation.RecordProportionalCredit(foodCat, saturation,
            __state.Operation.StartedFull == true ? 0f : saturation / 2.5f * nutritionGainMultiplier);
        if (__state.Row == null) return;
        __state.Row.ActualNutrition = nutrition;
        __state.Row.CreditedSatiety = credited;
    }
}
