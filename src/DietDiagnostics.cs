using System;
using System.Collections.Generic;
using System.Linq;
using dietsetup.Binding;
using dietsetup.Rules;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace dietsetup;

internal sealed class DietIngredientTrace
{
    internal string Ingredient = "";
    internal string Diet = "";
    internal string Snapshot = "";
    internal string Tags = "";
    internal float Spoil;
    internal string Rule = "";
    internal DietVerdict Verdict;
    internal EnumFoodCategory Category;
    internal float SatietyContribution;
    internal float NutritionMultiplier;
    internal float CapacityScale;
    internal float? SubmittedSatiety;
    internal float? CreditedSatiety;
    internal float? ActualNutrition;
    internal string Format() => $"{Ingredient}: diet={Diet} snapshot={Snapshot} tags=[{Tags}] spoil={Spoil:F3} rule={Rule} verdict={Verdict} "
        + $"category={Category} satietyContribution={SatietyContribution:F4} nutritionMult={NutritionMultiplier:F4} capacityScale={CapacityScale:F4} "
        + $"uncappedNutrition={SatietyContribution / 2.5f * NutritionMultiplier * CapacityScale:F4} "
        + $"submittedSatiety={SubmittedSatiety?.ToString("F4") ?? "not consumed"} creditedSatiety={CreditedSatiety?.ToString("F4") ?? "not consumed"} actualNutrition={ActualNutrition?.ToString("F4") ?? "not consumed"}";
}

internal static class DietDiagnostics
{
    [ThreadStatic] private static List<DietIngredientTrace>? capture;

    internal static DietIngredientTrace Row(DietRuntimeSnapshot snapshot, EntityAgent entity, ItemStack stack,
        ulong mask, float spoil, DietResolveResult result, EnumFoodCategory category, float satiety)
    {
        var diet = DietIdResolver.ResolveDiet(entity, snapshot);
        var row = new DietIngredientTrace { Ingredient = stack.Collectible.Code.ToString(), Diet = diet?.Id ?? "base",
            Snapshot = $"{snapshot.Revision}/{snapshot.Hash}", Tags = string.Join(",", snapshot.Tags.TagNames(mask)),
            Spoil = spoil, Rule = result.WinningRule, Verdict = result.Verdict, Category = category,
            SatietyContribution = satiety, NutritionMultiplier = result.Nutrition,
            CapacityScale = diet != null && diet.Categories.TryGetValue(category, out var c) ? c.NutritionGainScale : 1f };
        capture?.Add(row);
        return row;
    }

    internal static string Inspect(ICoreAPI api, EntityAgent entity, ItemSlot slot)
    {
        if (slot.Itemstack == null) return "No food in that slot.";
        using var snapshotScope = DietRuntimeSnapshot.Read(api);
        var snapshot = DietRuntimeSnapshot.For(api);
        if (!snapshot.Config.EnableDietSystem) return "Diet system disabled; vanilla consumption applies.";
        var previous = capture; bool previousDisplay = DietMealFactsContext.DisplayOnly;
        var rows = new List<DietIngredientTrace>(); capture = rows; DietMealFactsContext.DisplayOnly = true;
        try
        {
            var stack = slot.Itemstack;
            if (stack.Collectible is BlockMeal meal)
                meal.GetContentNutritionProperties(api.World, slot, entity);
            else
            {
                var content = stack.Collectible is BlockLiquidContainerBase liquid && !liquid.IsEmpty(stack) ? liquid.GetContent(stack) : stack;
                if (content == null) return "No liquid contents.";
                var contentSlot = ReferenceEquals(content, stack) ? slot : new DummySlot(content, slot.Inventory);
                ulong mask = snapshot.Tags.GetTagMask(api.World, contentSlot, out float spoil, out bool determined);
                if (!determined) return "Spoilage unavailable; retry after the transition error is resolved.";
                var diet = DietIdResolver.ResolveDiet(entity, snapshot);
                if (diet == null) return "No compiled diet.";
                var result = DietResolver.Resolve(diet, mask, spoil);
                var props = stack.Collectible.GetNutritionProperties(api.World, stack, entity);
                if (props == null) return "No nutrition properties.";
                float satiety = props.Satiety;
                if (ReferenceEquals(content, stack)) satiety *= Vintagestory.API.Config.GlobalConstants.FoodSpoilageSatLossMul(spoil, stack, entity);
                Row(snapshot, entity, content, mask, spoil, result, props.FoodCategory, satiety);
            }
            return rows.Count == 0 ? "No nutritious ingredients." : string.Join("\n", rows.Select(row => row.Format()));
        }
        finally { capture = previous; DietMealFactsContext.DisplayOnly = previousDisplay; }
    }

    internal static float Level(EntityBehaviorHunger hunger, EnumFoodCategory category) => category switch
    {
        EnumFoodCategory.Fruit => hunger.FruitLevel, EnumFoodCategory.Vegetable => hunger.VegetableLevel,
        EnumFoodCategory.Grain => hunger.GrainLevel, EnumFoodCategory.Protein => hunger.ProteinLevel,
        EnumFoodCategory.Dairy => hunger.DairyLevel, _ => 0
    };
}
