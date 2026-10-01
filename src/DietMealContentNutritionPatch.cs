using System.Collections.Generic;
using System.Linq;
using dietsetup.Composition;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace dietsetup;

// BlockMeal passes the bowl identity to vanilla spoilage; fillings need their own source and the pie's state.
// A Culinary Artillery replaces this same method with its own prefix and declares no priority, so ownership
// would otherwise fall to mod load order: this patch wins deterministically and emits ACA's expanded rows itself.
[HarmonyPatch(typeof(BlockMeal), nameof(BlockMeal.GetContentNutritionProperties),
    new[] { typeof(IWorldAccessor), typeof(ItemSlot), typeof(ItemStack[]), typeof(EntityAgent), typeof(bool), typeof(float), typeof(float) })]
[HarmonyPriority(Priority.First)]
[HarmonyBefore(DietAcaIntegration.HarmonyId, DietAcaIntegration.ModId)]
public static class DietMealContentNutritionPatch
{
    [HarmonyPrefix]
    public static bool Prefix(IWorldAccessor world, ItemSlot inSlot, ItemStack?[]? contentStacks, EntityAgent? forEntity, bool mulWithStacksize, float nutritionMul, float healthMul, ref FoodNutritionProperties[] __result)
    {
        using var snapshotScope = DietRuntimeSnapshot.Read(world.Api);
        if (!DietRuntimeSnapshot.For(world.Api).Config.EnableDietSystem) return true;

        var list = new List<FoodNutritionProperties>();
        var ingredientTraces = new List<DietIngredientTrace>();
        var ingredientResults = new List<DietContributionSet>();
        ItemStack? bowlStack = inSlot.Itemstack;

        if (contentStacks != null && bowlStack != null)
        {
            bool timeFrozen = bowlStack.Attributes.GetBool("timeFrozen");
            bool bowlIsPie = bowlStack.Collectible is BlockPie;
            float pieSpoilLevel = 0f;
            if (bowlIsPie && !timeFrozen)
            {
                pieSpoilLevel = bowlStack.Collectible.UpdateAndGetTransitionState(world, inSlot, EnumTransitionType.Perish)?.TransitionLevel ?? 0f;
            }

            string recipeCode = bowlStack.Attributes.GetString("recipeCode");
            CookingRecipe? recipe = world.Api.GetCookingRecipe(recipeCode);
            List<CookingRecipeIngredient>? recipeIngredients = recipe?.Ingredients?.Select(ing => ing.Clone()).ToList();
            // A pot meal keeps its raw ingredient stacks. ACA mixing-bowl codes are absent from this
            // registry and were never heated, so they keep the ingredient's own state.
            bool cookedMeal = !bowlIsPie && recipe != null && recipe.CooksInto == null;

            foreach (ItemStack? contentStack in contentStacks)
            {
                if (contentStack == null) continue;

                float quantity = contentStack.StackSize;
                ItemStack nutriStack = contentStack.Clone();
                nutriStack.StackSize = 1;

                if (!mulWithStacksize)
                {
                    nutriStack.StackSize = (int)(BlockLiquidContainerBase.GetContainableProps(nutriStack)?.ItemsPerLitre ?? 1f);
                    CookingRecipeIngredient? matched = recipeIngredients?.FirstOrDefault(ing => ing.Matches(nutriStack));
                    if (matched != null)
                    {
                        quantity = matched.GetMatchingStack(nutriStack)?.StackSize ?? 1;
                        nutriStack.StackSize = (int)(nutriStack.StackSize * matched.PortionSizeLitres);
                        matched.MaxQuantity--;
                        if (matched.MaxQuantity == 0) recipeIngredients!.Remove(matched);
                    }
                    else
                    {
                        quantity = 1f;
                    }
                }

                FoodNutritionProperties? ingredientProps = BlockMeal.GetIngredientStackNutritionProperties(world, nutriStack, forEntity);
                if (ingredientProps == null) continue;
                // JsonItemStack.Clone dereferences Code, which vanilla leaves null on liquid-container EatenStack values.
                FoodNutritionProperties props = new FoodNutritionProperties
                {
                    FoodCategory = ingredientProps.FoodCategory,
                    Satiety = ingredientProps.Satiety,
                    Health = ingredientProps.Health,
                    Intoxication = ingredientProps.Intoxication,
                    Psychedelic = ingredientProps.Psychedelic,
                    SaturationLossDelay = ingredientProps.SaturationLossDelay,
                    EatenStack = ingredientProps.EatenStack
                };
                float spoilState = 0f;
                if (!timeFrozen)
                {
                    var dummySlot = new DummySlot(contentStack, inSlot.Inventory);
                    spoilState = contentStack.Collectible.UpdateAndGetTransitionState(world, dummySlot, EnumTransitionType.Perish)?.TransitionLevel ?? 0f;
                }
                float ingredientSatietyMult;
                float ingredientHealthMult;
                DietContributionSet? ingredientResolved = null;
                // Vanilla keeps pie fillings fresh and preserves their original codes; the baked pie owns their age and state.
                if (bowlIsPie) spoilState = pieSpoilLevel;
                using (DietSpoilageResolution.MealContext(bowlIsPie ? bowlStack : null, cookedMeal))
                {
                    ingredientSatietyMult = GlobalConstants.FoodSpoilageSatLossMul(spoilState, contentStack, forEntity);
                    ingredientHealthMult = GlobalConstants.FoodSpoilageHealthLossMul(spoilState, contentStack, forEntity);
                    if (DietSpoilageResolution.TryResolve(spoilState, contentStack, forEntity, out DietContributionSet resolved))
                    {
                        ingredientResolved = resolved;
                    }
                }

                var rows = new List<FoodNutritionProperties>();
                // ACA's expanded rows are real per-category contributions of this same ingredient; owning the
                // method means emitting them here, under one identity resolve and one spoilage application.
                foreach (FoodNutritionProperties expanded in DietAcaIntegration.ExpandedRows(contentStack))
                    rows.Add(new FoodNutritionProperties { FoodCategory = expanded.FoodCategory, Satiety = expanded.Satiety, Health = expanded.Health });
                rows.Add(props);

                var finalResult = ingredientResolved ?? DietContributionSet.Single(DietSpoilageResolution.Neutral, 0);
                // One group per physical ingredient: virtual portions and expanded rows share its consequences.
                var grouped = finalResult.WithGroup(new object());
                foreach (FoodNutritionProperties row in rows)
                {
                    row.Satiety *= ingredientSatietyMult * nutritionMul * quantity;
                    row.Health *= ingredientHealthMult * healthMul * quantity;
                    row.Intoxication *= quantity;
                    row.Psychedelic *= quantity;
                    list.Add(row);
                    ingredientResults.Add(grouped);
                    if (forEntity != null)
                        ingredientTraces.Add(DietDiagnostics.Row(DietRuntimeSnapshot.For(world.Api), forEntity,
                            contentStack, spoilState, grouped, row.FoodCategory, row.Satiety));
                }
            }
        }
        if (!DietMealFactsContext.DisplayOnly && DietConsumption.Current is { CapturingMeal: true } operation
            && ReferenceEquals(operation.Entity, forEntity))
        {
            operation.Pending.Clear();
            operation.TraceQueue.Clear();
            foreach (var row in ingredientTraces) operation.TraceQueue.Enqueue(row);
            foreach (var result in ingredientResults) operation.Pending.Enqueue(result);
        }

        __result = list.ToArray();
        return false;
    }
}
