using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace dietsetup;

/// <summary>Optional A Culinary Artillery support, detected by type shape at runtime. Nothing here
/// references the ACA assembly, so Diet Setup loads and behaves identically when ACA is absent.
/// Pinned against ACA 2.0.0-dev.22 / Expanded Foods 2.0.0-dev.14.</summary>
internal static class DietAcaIntegration
{
    internal const string ModId = "aculinaryartillery";
    private const string RawFoodType = "ACulinaryArtillery.ItemExpandedRawFood";
    private const string FoodType = "ACulinaryArtillery.ItemExpandedFood";
    private const string SatsAttribute = "expandedSats";

    private static bool probed;
    private static Type? rawFoodType;
    private static Type? foodType;
    private static MethodInfo? propsFromArray;
    private static MethodInfo? heldInteractStop;

    internal static bool Available
    {
        get { Probe(); return rawFoodType != null && propsFromArray != null; }
    }

    internal static MethodInfo? ExpandedEatMethod
    {
        get { Probe(); return heldInteractStop; }
    }

    private static void Probe()
    {
        if (probed) return;
        probed = true;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            rawFoodType ??= assembly.GetType(RawFoodType, throwOnError: false);
            foodType ??= assembly.GetType(FoodType, throwOnError: false);
            if (rawFoodType != null && foodType != null) break;
        }
        if (rawFoodType == null) return;
        propsFromArray = AccessTools.Method(rawFoodType, "GetPropsFromArray", new[] { typeof(float[]) });
        if (foodType != null)
            heldInteractStop = AccessTools.Method(foodType, "OnHeldInteractStop",
                new[] { typeof(float), typeof(ItemSlot), typeof(EntityAgent),
                    typeof(BlockSelection), typeof(EntitySelection) });
    }

    /// <summary>Reset for tests that install and uninstall the integration in one process.</summary>
    internal static void ResetProbe()
    {
        probed = false; rawFoodType = null; foodType = null; propsFromArray = null; heldInteractStop = null;
    }

    private static float[]? Sats(ItemStack? stack) =>
        (stack?.Attributes?[SatsAttribute] as FloatArrayAttribute)?.value is { Length: 6 } value ? value : null;

    /// <summary>True when the stack carries ACA's per-nutrient-category totals. These say which hunger
    /// bars to credit and nothing about what the food is made of, so they are not source-attributed
    /// contributions and never displace a declared approximation; see Task 4B.</summary>
    internal static bool HasCategoryTotals(ItemStack? stack)
    {
        Probe();
        if (rawFoodType == null || stack?.Collectible == null || !rawFoodType.IsInstanceOfType(stack.Collectible)) return false;
        float[]? sats = Sats(stack);
        if (sats == null) return false;
        for (int i = 1; i <= 5; i++) if (sats[i] != 0f) return true;
        return false;
    }

    /// <summary>ACA's own row expansion, so the rows Diet Setup resolves are exactly the rows ACA credits.</summary>
    internal static FoodNutritionProperties[] ExpandedRows(ItemStack? stack)
    {
        if (!HasCategoryTotals(stack)) return Array.Empty<FoodNutritionProperties>();
        try
        {
            return propsFromArray!.Invoke(stack!.Collectible, new object?[] { Sats(stack) })
                as FoodNutritionProperties[] ?? Array.Empty<FoodNutritionProperties>();
        }
        catch (Exception)
        {
            return Array.Empty<FoodNutritionProperties>();
        }
    }
}
