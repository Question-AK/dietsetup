using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace dietsetup;

// Pie fillings now own spoilage; only baking quality belongs in the outer multiplier.
[HarmonyPatch(typeof(BlockPie), nameof(BlockPie.GetNutritionHealthMul))]
public static class DietPieNutritionPatch
{
    [HarmonyPrefix]
    public static bool Prefix(BlockPie __instance, ICoreAPI ___api, EntityAgent? forEntity, ref float[] __result)
    {
        if (!DietRuntimeSnapshot.For(forEntity?.Api ?? ___api).Config.EnableDietSystem) return true;
        __result = new[] { __instance.Attributes["nutritionMul"].AsFloat(1f), 1f };
        return false;
    }
}
