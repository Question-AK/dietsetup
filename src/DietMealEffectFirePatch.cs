using dietsetup.Grants;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace dietsetup;

// Consume returns the remaining servings, including zero-consumption full-stomach attempts.
[HarmonyPatch(typeof(BlockMeal), nameof(BlockMeal.Consume))]
internal static class DietMealEffectFirePatch
{
    [HarmonyPrefix]
    private static bool Prefix(IPlayer eatingPlayer, ItemSlot inSlot, ItemStack[] contentStacks,
        float remainingServings, ref float __result, out DietConsumption? __state)
    {
        __state = null;
        // The servings we were handed back unchanged: vanilla's own zero-consumption answer, and the
        // value tryFinishEatMeal tests before it replaces or takes out the bowl. Returning the default
        // 0 here would read as "all servings gone" and destroy a container nobody ate from.
        if (MaterialPermissionGate.Refuses(eatingPlayer?.Entity, inSlot?.Itemstack, contentStacks)
            || DietEatFeedback.Refuses(eatingPlayer?.Entity, inSlot, contentStacks))
        {
            __result = remainingServings;
            return false;
        }
        __state = DietConsumption.Begin(eatingPlayer!.Entity);
        if (__state != null) __state.CapturingMeal = true;
        return true;
    }

    [HarmonyPostfix]
    private static void Postfix(float remainingServings, float __result, bool __runOriginal, DietConsumption? __state)
    {
        __state?.Confirm(__runOriginal && __result < remainingServings
            ? DietConsumptionOutcome.Consumed : DietConsumptionOutcome.Refused);
    }

    [HarmonyFinalizer]
    private static void Finalizer(DietConsumption? __state) => __state?.Dispose();
}
