using dietsetup.Grants;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace dietsetup;

// Filled containers bypass CollectibleObject.tryEatStop; the spoilage hook supplies the live liquid resolve.
[HarmonyPatch(typeof(BlockLiquidContainerBase), "tryEatStop")]
internal static class DietLiquidConsumptionPatch
{
    [HarmonyPrefix]
    private static bool Prefix(BlockLiquidContainerBase __instance, float secondsUsed, ItemSlot slot,
        EntityAgent byEntity, out DietConsumption? __state)
    {
        __state = null;
        if (secondsUsed < 0.95f || slot?.Itemstack == null || __instance.IsEmpty(slot.Itemstack)) return true;
        // Denied before SplitStackAndPerformAction, so the vessel keeps its liquid rather than losing a
        // portion to a mouthful that was refused. An empty vessel falls through to the base method, which
        // carries its own gate.
        if (MaterialPermissionGate.Refuses(byEntity, slot.Itemstack)) return false;
        __state = DietConsumption.Begin(byEntity);
        if (__state != null) __state.CapturingLiquid = true;
        return true;
    }

    [HarmonyPostfix]
    private static void Postfix(bool __runOriginal, DietConsumption? __state) =>
        __state?.Confirm(__runOriginal && __state.RemovedLiquid > 0
            ? DietConsumptionOutcome.Consumed : DietConsumptionOutcome.Refused);

    [HarmonyFinalizer]
    private static void Finalizer(DietConsumption? __state) => __state?.Dispose();
}

// This return value is the quantity vanilla checks immediately before ReceiveSaturation, including stacked vessels.
[HarmonyPatch(typeof(BlockLiquidContainerBase), nameof(BlockLiquidContainerBase.SplitStackAndPerformAction))]
internal static class DietLiquidRemovalPatch
{
    [HarmonyPostfix]
    private static void Postfix(Entity byEntity, int __result)
    {
        if (DietConsumption.Current is { CapturingLiquid: true } operation && ReferenceEquals(operation.Entity, byEntity))
            operation.RemovedLiquid = __result;
    }
}
