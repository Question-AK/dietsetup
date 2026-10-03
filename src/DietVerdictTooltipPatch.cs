using System.Collections.Generic;
using System.Linq;
using System.Text;
using dietsetup.Binding;
using dietsetup.Composition;
using dietsetup.Grants;
using dietsetup.Rules;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace dietsetup;
[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetHeldItemInfo))]
public static class DietVerdictTooltipPatch
{
    [HarmonyPostfix]
    public static void Postfix(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world)
    {
        AppendVerdict(inSlot, dsc, world);
    }

    internal static void AppendVerdict(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world)
    {
        using var snapshotScope = DietRuntimeSnapshot.Read(world.Api);
        DietRuntimeSnapshot snapshot = DietRuntimeSnapshot.For(world.Api);
        if (!snapshot.Config.EnableDietSystem) return;
        if (world is not IClientWorldAccessor clientWorld) return;
        if (clientWorld.Player?.Entity is not EntityAgent viewer) return;
        ItemStack? stack = inSlot.Itemstack;
        if (stack?.Collectible == null) return;

        // Read from the same synchronized table the server enforces, and stated before any edibility
        // rating: a material this diet may not consume never reaches a verdict.
        if (MaterialPermissionGate.Denies(world.Api, viewer, stack))
        {
            dsc.AppendLine(Lang.Get("dietsetup:verdict-denied"));
            return;
        }

        CompiledDiet? diet = DietIdResolver.ResolveDiet(viewer, snapshot);
        if (diet == null) return;

        // A meal container calls this base method before its own text; a pie does not, so it arrives
        // through BlockPieVerdictTooltipPatch. A liquid container has no tags of its own: its content is the food.
        DietFoodLines lines = stack.Collectible switch
        {
            IBlockMealContainer container => MealLines(world, inSlot, stack, container, viewer, diet),
            BlockLiquidContainerBase liquids => liquids.GetContent(stack) is { } content
                ? FoodLines(world, new DummySlot(content), viewer, diet, snapshot,
                    BlockLiquidContainerBase.GetContainableProps(content)?.NutritionPropsPerLitre?.FoodCategory
                    ?? content.Collectible.GetNutritionProperties(world, content, viewer)?.FoodCategory)
                : DietFoodLines.None,
            _ => FoodLines(world, inSlot, viewer, diet, snapshot,
                stack.Collectible.GetNutritionProperties(world, stack, viewer)?.FoodCategory),
        };
        foreach (string key in lines.Keys) dsc.AppendLine(Lang.Get(key));
    }

    private static DietFoodLines FoodLines(IWorldAccessor world, ItemSlot slot, EntityAgent viewer, CompiledDiet diet,
        DietRuntimeSnapshot snapshot, EnumFoodCategory? category)
    {
        // Without nutrition props nothing is eaten, so a diet whose fallback is not 1 cannot speak about stone.
        if (category is not { } food) return DietFoodLines.None;
        snapshot.Tags.GetTagMask(world, slot, out float spoil, out bool perishes, out bool determined);
        if (!determined || !DietSpoilageResolution.TryResolve(spoil, slot.Itemstack, viewer, out DietContributionSet set))
            return DietFoodLines.None;
        bool feedsBar = FeedsBar(diet, food, slot.Itemstack);
        return set.IsComposite
            ? DietFoodVoice.Rate(set, feedsBar)
            : DietFoodVoice.ForFood(snapshot.Tags, diet, set.ItemMask, spoil, perishes, feedsBar);
    }

    /// <summary>Each ingredient resolves as DietMealContentNutritionPatch resolves it when eaten: the pie's own
    /// age and state for a filling, cooked state for a heated pot meal, nothing aged while time is frozen.</summary>
    private static DietFoodLines MealLines(IWorldAccessor world, ItemSlot inSlot, ItemStack mealStack,
        IBlockMealContainer container, EntityAgent viewer, CompiledDiet diet)
    {
        bool timeFrozen = mealStack.Attributes.GetBool("timeFrozen");
        bool isPie = mealStack.Collectible is BlockPie;
        float pieSpoil = isPie && !timeFrozen
            ? mealStack.Collectible.UpdateAndGetTransitionState(world, inSlot, EnumTransitionType.Perish)?.TransitionLevel ?? 0f
            : 0f;
        CookingRecipe? recipe = world.Api.GetCookingRecipe(mealStack.Attributes.GetString("recipeCode"));
        bool cooked = !isPie && recipe != null && recipe.CooksInto == null;

        var ingredients = new List<DietFoodLines>();
        foreach (ItemStack content in container.GetNonEmptyContents(world, mealStack))
        {
            FoodNutritionProperties? props = BlockMeal.GetIngredientStackNutritionProperties(world, content, viewer);
            if (props == null) continue;
            float spoil = isPie ? pieSpoil
                : timeFrozen ? 0f
                : content.Collectible.UpdateAndGetTransitionState(world, new DummySlot(content, inSlot.Inventory), EnumTransitionType.Perish)?.TransitionLevel ?? 0f;
            using (DietSpoilageResolution.MealContext(isPie ? mealStack : null, cooked))
            {
                if (DietSpoilageResolution.TryResolve(spoil, content, viewer, out DietContributionSet set))
                    ingredients.Add(DietFoodVoice.Rate(set, FeedsBar(diet, props.FoodCategory, content)));
            }
        }
        return DietFoodVoice.ForMeal(ingredients);
    }

    /// <summary>ACA's expanded rows credit further bars from the same food, so the food feeds a bar if any row does.</summary>
    private static bool FeedsBar(CompiledDiet diet, EnumFoodCategory category, ItemStack? stack) =>
        DietFoodVoice.FeedsBar(diet, category)
        || DietAcaIntegration.ExpandedRows(stack).Any(row => DietFoodVoice.FeedsBar(diet, row.FoodCategory));
}
[HarmonyPatch(typeof(BlockPie), nameof(BlockPie.GetHeldItemInfo))]
public static class BlockPieVerdictTooltipPatch
{
    [HarmonyPostfix]
    public static void Postfix(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world)
    {
        DietVerdictTooltipPatch.AppendVerdict(inSlot, dsc, world);
    }
}
