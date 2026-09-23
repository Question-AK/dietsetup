using dietsetup.Binding;
using dietsetup.Composition;
using dietsetup.Grants;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace dietsetup;

// ACA credits its expandedSats rows before calling base, so tryEatStop is too late to open the scope.
// Installed manually because the target type only exists when ACA is loaded.
internal static class DietAcaExpandedFoodPatch
{
    internal static void Install(Harmony harmony)
    {
        var target = DietAcaIntegration.ExpandedEatMethod;
        if (target == null) return;
        harmony.Patch(target,
            prefix: new HarmonyMethod(AccessTools.Method(typeof(DietAcaExpandedFoodPatch), nameof(Prefix))),
            postfix: new HarmonyMethod(AccessTools.Method(typeof(DietAcaExpandedFoodPatch), nameof(Postfix))),
            finalizer: new HarmonyMethod(AccessTools.Method(typeof(DietAcaExpandedFoodPatch), nameof(Finalizer))));
    }

    internal static bool Prefix(float secondsUsed, ItemSlot slot, EntityAgent byEntity, out DietConsumption? __state)
    {
        __state = null;
        if (secondsUsed < 0.95f || slot?.Itemstack == null || byEntity?.World is not IServerWorldAccessor) return true;
        // Early enough to prevent ACA's own pre-base credits and heals, which it applies before it calls
        // base -- there is nothing to withdraw because nothing was ever delivered.
        if (MaterialPermissionGate.Refuses(byEntity, slot.Itemstack)) return false;
        // A nested site is already accounting for this mouthful; joining twice would credit it twice.
        if (DietConsumption.Join(byEntity) != null) return true;

        var stack = slot.Itemstack;
        if (stack.Collectible.GetNutritionProperties(byEntity.World, stack, byEntity) == null) return true;
        var rows = DietAcaIntegration.ExpandedRows(stack);

        __state = DietConsumption.Begin(byEntity);
        if (__state == null) return true;
        // Captured before ACA credits anything, so a refused base eat is told apart from a mouthful
        // whose outcome this site never observed; only the first may withdraw ACA's pre-base rows.
        __state.Stack = stack;
        __state.InitialCount = stack.StackSize;
        var group = new object();
        __state.CreditGroup = group;
        if (rows.Length == 0) return true;

        var snapshot = __state.Snapshot;
        var diet = DietIdResolver.ResolveDiet(byEntity, snapshot);
        if (diet == null) return true;
        snapshot.Tags.GetTagMask(byEntity.World, slot, out float spoil, out bool determined);
        if (!determined) return true;
        // Running the spoilage hook first caches the resolve under vanilla's own multiplier, and
        // yields the multiplier ACA itself is about to apply to every row.
        float satietyMul = GlobalConstants.FoodSpoilageSatLossMul(spoil, stack, byEntity);
        if (!DietSpoilageResolution.TryResolve(spoil, stack, byEntity, out var resolved)) return true;

        // The rows carry per-category nourishment but no identity of their own, so each resolves
        // against the item's own tags and keeps its upstream category. Splitting every row by the
        // same declared shares is the selected approximation; expandedSats records no origin to
        // prefer any other, and upstream categories and totals are preserved either way.
        foreach (var row in rows)
        {
            var set = resolved.WithGroup(group);
            __state.Pending.Enqueue(set);
            __state.TraceQueue.Enqueue(DietDiagnostics.Row(snapshot, byEntity, stack, spoil, set,
                row.FoodCategory, row.Satiety * satietyMul));
        }
        return true;
    }

    internal static void Postfix(bool __runOriginal, DietConsumption? __state) =>
        __state?.Confirm(__runOriginal && __state.Stack != null && __state.Stack.StackSize == __state.InitialCount - 1
            ? DietConsumptionOutcome.Consumed : DietConsumptionOutcome.Refused);

    internal static void Finalizer(DietConsumption? __state) => __state?.Dispose();
}
