using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace dietsetup;
// Vanilla omits the viewer on held-meal tooltips; nested or throwing queries must restore the previous display scope.
[HarmonyPatch(typeof(BlockMeal), nameof(BlockMeal.GetContentNutritionFacts),
    new[] { typeof(IWorldAccessor), typeof(ItemSlot), typeof(ItemStack[]), typeof(EntityAgent), typeof(bool), typeof(float), typeof(float) })]
public static class DietMealContentNutritionFactsPatch
{
    [HarmonyPrefix]
    public static void Prefix(IWorldAccessor world, ref EntityAgent? forEntity, out bool __state)
    {
        __state = DietMealFactsContext.DisplayOnly;
        if (!DietRuntimeSnapshot.For(world.Api).Config.EnableDietSystem) return;

        if (forEntity == null && world is IClientWorldAccessor clientWorld && clientWorld.Player?.Entity is EntityAgent viewer)
        {
            forEntity = viewer;
        }

        DietMealFactsContext.DisplayOnly = true;
    }

    [HarmonyFinalizer]
    public static void Finalizer(bool __state)
    {
        DietMealFactsContext.DisplayOnly = __state;
    }
}
