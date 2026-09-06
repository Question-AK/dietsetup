using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;

namespace dietsetup;

// Vanilla exposes live spoilage here on every eating path, including the liquid contents.
[HarmonyPatch(typeof(GlobalConstants), nameof(GlobalConstants.FoodSpoilageSatLossMul))]
public static class DietSpoilageSatietyPatch
{
    [HarmonyPostfix]
    public static void Postfix(float spoilState, ItemStack stack, EntityAgent byEntity, ref float __result)
    {
        if (!DietSpoilageResolution.TryResolve(spoilState, stack, byEntity, out var result)) return;
        __result = DietSpoilageResolution.ApplySatiety(__result, result);
        if (!DietMealFactsContext.DisplayOnly && DietConsumption.Current is { CapturingLiquid: true } operation
            && ReferenceEquals(operation.Entity, byEntity))
        {
            operation.Pending.Clear();
            operation.Pending.Enqueue(result);
            var props = Vintagestory.GameContent.BlockLiquidContainerBase.GetContainableProps(stack)?.NutritionPropsPerLitre ?? stack.Collectible.NutritionProps;
            if (props != null)
            {
                operation.TraceQueue.Clear();
                operation.TraceQueue.Enqueue(DietDiagnostics.Row(operation.Snapshot, byEntity, stack,
                    operation.Snapshot.Tags.GetTagMaskForSpoilState(stack.Collectible, spoilState), spoilState, result,
                    props.FoodCategory, props.Satiety * __result));
            }
        }
    }
}
