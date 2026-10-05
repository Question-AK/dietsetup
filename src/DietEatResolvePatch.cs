using dietsetup.Binding;
using dietsetup.Grants;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;

namespace dietsetup;

// The original stack survives final-item removal and supplies consumption evidence after vanilla runs.
[HarmonyPatch(typeof(CollectibleObject), "tryEatStop", new[] { typeof(float), typeof(ItemSlot), typeof(EntityAgent) })]
internal static class DietEatResolvePatch
{
    [HarmonyPrefix]
    private static bool Prefix(float secondsUsed, ItemSlot slot, EntityAgent byEntity, out DietConsumption? __state)
    {
        __state = null;
        if (secondsUsed < 0.95f || slot?.Itemstack == null) return true;
        // Denying here skips vanilla's whole body: no item taken, no saturation, no intoxication,
        // no psychedelic, no health, and no transaction to take anything back from.
        if (MaterialPermissionGate.Refuses(byEntity, slot.Itemstack) || DietEatFeedback.Refuses(byEntity, slot)) return false;
        // An outer delivery site may already own this mouthful; join it rather than shadowing its queue.
        var joined = DietConsumption.Join(byEntity);
        __state = joined == null ? DietConsumption.Begin(byEntity) : null;
        var operation = joined ?? __state;
        if (operation == null) return true;
        var snapshot = operation.Snapshot;
        var diet = DietIdResolver.ResolveDiet(byEntity, snapshot);
        if (diet == null) return true;
        var stack = slot.Itemstack;
        var collectible = stack.Collectible;
        int count = stack.StackSize;
        snapshot.Tags.GetTagMask(byEntity.World, slot, out float spoil, out bool determined);
        if (!determined || !ReferenceEquals(stack, slot.Itemstack) || !ReferenceEquals(collectible, stack.Collectible)
            || count != stack.StackSize) return true;
        operation.Stack = stack;
        operation.InitialCount = count;
        // Running the spoilage hook first both yields the submitted satiety and caches the resolve built
        // from vanilla's own multiplier, which vanilla itself only computes after this prefix returns.
        float satietyMul = GlobalConstants.FoodSpoilageSatLossMul(spoil, stack, byEntity);
        if (!DietSpoilageResolution.TryResolve(spoil, stack, byEntity, out var resolved)) return true;
        var set = resolved.WithGroup(operation.CreditGroup ?? new object());
        operation.Pending.Enqueue(set);
        var props = collectible.GetNutritionProperties(byEntity.World, stack, byEntity);
        if (props != null) operation.TraceQueue.Enqueue(DietDiagnostics.Row(snapshot, byEntity, stack, spoil, set,
            props.FoodCategory, props.Satiety * satietyMul));
        return true;
    }

    [HarmonyPostfix]
    private static void Postfix(bool __runOriginal, DietConsumption? __state)
    {
        __state?.Confirm(!__runOriginal ? DietConsumptionOutcome.Refused
            : __state.Stack == null ? DietConsumptionOutcome.Unknown
            : __state.Stack.StackSize == __state.InitialCount - 1 ? DietConsumptionOutcome.Consumed
            : DietConsumptionOutcome.Refused);
    }

    [HarmonyFinalizer]
    private static void Finalizer(DietConsumption? __state) => __state?.Dispose();
}
