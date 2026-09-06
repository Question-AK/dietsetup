using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace dietsetup;

// Consume returns the remaining servings, including zero-consumption full-stomach attempts.
[HarmonyPatch(typeof(BlockMeal), nameof(BlockMeal.Consume))]
internal static class DietMealEffectFirePatch
{
    [HarmonyPrefix]
    private static void Prefix(IPlayer eatingPlayer, out DietConsumption? __state)
    {
        __state = DietConsumption.Begin(eatingPlayer.Entity);
        if (__state != null) __state.CapturingMeal = true;
    }

    [HarmonyPostfix]
    private static void Postfix(float remainingServings, float __result, bool __runOriginal, DietConsumption? __state)
    {
        __state?.Confirm(__runOriginal && __result < remainingServings);
    }

    [HarmonyFinalizer]
    private static void Finalizer(DietConsumption? __state) => __state?.Dispose();
}
