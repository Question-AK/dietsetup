using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace dietsetup;

[HarmonyPatch(typeof(CollectibleObject), "tryEatBegin")]
internal static class DietEatStartRefusalPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ItemSlot slot, EntityAgent byEntity) =>
        byEntity.Controls.ShiftKey || !DietEatFeedback.RefusesStart(byEntity, slot);
}

[HarmonyPatch(typeof(BlockMeal), "tryHeldBeginEatMeal")]
internal static class DietHeldMealStartRefusalPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ItemSlot slot, EntityAgent byEntity, ref bool __result)
    {
        if (byEntity.Controls.ShiftKey || !DietEatFeedback.RefusesStart(byEntity, slot, meal: true)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(BlockMeal), "tryPlacedBeginEatMeal")]
internal static class DietPlacedMealStartRefusalPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ItemSlot slot, IPlayer byPlayer, ref bool __result)
    {
        if (!DietEatFeedback.RefusesStart(byPlayer?.Entity, slot, meal: true)) return true;
        __result = false;
        return false;
    }
}
