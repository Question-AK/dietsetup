using dietsetup.Binding;
using dietsetup.Rules;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace dietsetup;

// The original stack survives final-item removal and supplies consumption evidence after vanilla runs.
[HarmonyPatch(typeof(CollectibleObject), "tryEatStop", new[] { typeof(float), typeof(ItemSlot), typeof(EntityAgent) })]
internal static class DietEatResolvePatch
{
    [HarmonyPrefix]
    private static void Prefix(float secondsUsed, ItemSlot slot, EntityAgent byEntity, out DietConsumption? __state)
    {
        __state = null;
        if (secondsUsed < 0.95f || slot?.Itemstack == null) return;
        __state = DietConsumption.Begin(byEntity);
        if (__state == null) return;
        var snapshot = __state.Snapshot;
        var diet = DietIdResolver.ResolveDiet(byEntity, snapshot);
        if (diet == null) return;
        var stack = slot.Itemstack;
        var collectible = stack.Collectible;
        int count = stack.StackSize;
        ulong mask = snapshot.Tags.GetTagMask(byEntity.World, slot, out float spoil, out bool determined);
        if (!determined || !ReferenceEquals(stack, slot.Itemstack) || !ReferenceEquals(collectible, stack.Collectible)
            || count != stack.StackSize) return;
        __state.Stack = stack;
        __state.InitialCount = count;
        var result = DietResolver.Resolve(diet, mask, spoil);
        __state.Pending.Enqueue(result);
        var props = collectible.GetNutritionProperties(byEntity.World, stack, byEntity);
        if (props != null) __state.TraceQueue.Enqueue(DietDiagnostics.Row(snapshot, byEntity, stack, mask, spoil, result,
            props.FoodCategory, props.Satiety * Vintagestory.API.Config.GlobalConstants.FoodSpoilageSatLossMul(spoil, stack, byEntity)));
    }

    [HarmonyPostfix]
    private static void Postfix(bool __runOriginal, DietConsumption? __state)
    {
        __state?.Confirm(__runOriginal && __state.Stack != null && __state.Stack.StackSize == __state.InitialCount - 1);
    }

    [HarmonyFinalizer]
    private static void Finalizer(DietConsumption? __state) => __state?.Dispose();
}
