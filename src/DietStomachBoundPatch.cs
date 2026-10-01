using HarmonyLib;
using Vintagestory.GameContent;

namespace dietsetup;

// The hunger update, not Initialize: a load-time MaxSaturation can be interim, so excess waits for the first tick.
[HarmonyPatch(typeof(EntityBehaviorHunger), "ReduceSaturation")]
internal static class DietStomachBoundPatch
{
    [HarmonyPrefix]
    private static void Prefix(EntityBehaviorHunger __instance)
    {
        if (DietRuntimeSnapshot.For(__instance.entity.Api).Config.EnableDietSystem) DietNutritionBasis.BoundSatiety(__instance);
    }
}
