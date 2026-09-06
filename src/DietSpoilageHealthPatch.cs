using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;

namespace dietsetup;

// Health shares the satiety resolve, but a spoilage benefit must not amplify healing above fresh food.
[HarmonyPatch(typeof(GlobalConstants), nameof(GlobalConstants.FoodSpoilageHealthLossMul))]
public static class DietSpoilageHealthPatch
{
    [HarmonyPostfix]
    public static void Postfix(float spoilState, ItemStack stack, EntityAgent byEntity, ref float __result)
    {
        if (DietSpoilageResolution.TryResolve(spoilState, stack, byEntity, out var result))
            __result = DietSpoilageResolution.ApplyHealth(__result, result);
    }
}
