using System.Collections.Generic;
using System.Linq;
using dietsetup.Rules;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace dietsetup;

// BlockMeal passes the bowl identity to vanilla spoilage; fillings need their own source and the pie's state.
[HarmonyPatch(typeof(BlockMeal), nameof(BlockMeal.GetContentNutritionProperties),
    new[] { typeof(IWorldAccessor), typeof(ItemSlot), typeof(ItemStack[]), typeof(EntityAgent), typeof(bool), typeof(float), typeof(float) })]
public static class DietMealContentNutritionPatch
{
    [HarmonyPrefix]
    public static bool Prefix(IWorldAccessor world, ItemSlot inSlot, ItemStack?[]? contentStacks, EntityAgent? forEntity, bool mulWithStacksize, float nutritionMul, float healthMul, ref FoodNutritionProperties[] __result)
    {
        using var snapshotScope = DietRuntimeSnapshot.Read(world.Api);
        if (!DietRuntimeSnapshot.For(world.Api).Config.EnableDietSystem) return true;

        var list = new List<FoodNutritionProperties>();
        var ingredientTraces = new List<DietIngredientTrace>();
        var ingredientResults = new List<DietResolveResult>();
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
            List<CookingRecipeIngredient>? recipeIngredients = world.Api.GetCookingRecipe(recipeCode)?.Ingredients?
                .Select(ing => ing.Clone()).ToList();

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
                DietResolveResult? ingredientResolved = null;
                // Vanilla keeps pie fillings fresh and preserves their original codes; the baked pie owns their age and state.
                if (bowlIsPie) spoilState = pieSpoilLevel;
                using (DietSpoilageResolution.PieContext(bowlIsPie ? bowlStack : null))
                {
                    ingredientSatietyMult = GlobalConstants.FoodSpoilageSatLossMul(spoilState, contentStack, forEntity);
                    ingredientHealthMult = GlobalConstants.FoodSpoilageHealthLossMul(spoilState, contentStack, forEntity);
                    if (DietSpoilageResolution.TryResolve(spoilState, contentStack, forEntity, out DietResolveResult resolved))
                    {
                        ingredientResolved = resolved;
                    }
                }
                props.Satiety *= ingredientSatietyMult * nutritionMul * quantity;
                props.Health *= ingredientHealthMult * healthMul * quantity;
                props.Intoxication *= quantity;
                props.Psychedelic *= quantity;
                list.Add(props);

                var finalResult = ingredientResolved ?? new DietResolveResult(
                    DietVerdict.Edible, 1f, 1f, System.Array.Empty<CompiledEffect>(), false);
                ingredientResults.Add(finalResult);
                if (forEntity != null)
                {
                    var snapshot = DietRuntimeSnapshot.For(world.Api);
                    ulong mask = bowlIsPie ? snapshot.Tags.GetPieFillingTagMask(contentStack.Collectible, bowlStack.Collectible, spoilState)
                        : snapshot.Tags.GetTagMaskForSpoilState(contentStack.Collectible, spoilState);
                    ingredientTraces.Add(DietDiagnostics.Row(snapshot, forEntity, contentStack, mask, spoilState, finalResult, props.FoodCategory, props.Satiety));
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
