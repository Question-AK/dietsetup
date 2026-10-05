using System;
using System.Collections.Generic;
using dietsetup.Binding;
using dietsetup.Composition;
using dietsetup.Grants;
using dietsetup.Rules;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace dietsetup;

internal static class DietEatFeedback
{
    internal const string InedibleCode = "dietsetup-inedible";
    internal const string TooFullCode = "dietsetup-too-full";
    internal const float TooFullFraction = 0.5f;

    internal static bool Refuses(EntityAgent? entity, ItemSlot? slot, ItemStack?[]? mealContents = null)
    {
        if (entity?.World?.Side != EnumAppSide.Server || slot?.Itemstack == null
            || !GivesNothing(entity, slot, mealContents)) return false;

        if ((entity as EntityPlayer)?.Player is IServerPlayer player)
            player.SendIngameError(InedibleCode, Text("dietsetup:eat-inedible"));
        if (DietRuntimeSnapshot.For(entity.Api).Config.RecordLastConsumption)
            entity.Api.ModLoader.GetModSystem<DietSetupModSystem>()?.RecordConsumption(entity.EntityId,
                $"consumed=False refused=inedible food={slot.Itemstack.Collectible.Code} diet={DietIdResolver.Resolve(entity)}");
        return true;
    }

    internal static bool RefusesStart(EntityAgent? entity, ItemSlot? slot, bool meal = false)
    {
        if (entity?.Api is not ICoreClientAPI capi || slot?.Itemstack is not { } stack) return false;
        ItemStack?[]? contents = meal
            ? (stack.Collectible as BlockMeal)?.GetNonEmptyContents(entity.World, stack) ?? Array.Empty<ItemStack>()
            : null;
        if (MaterialPermissionGate.Denies(capi, entity, stack) || !GivesNothing(entity, slot, contents)) return false;
        capi.TriggerIngameError(stack.Collectible, InedibleCode, Text("dietsetup:eat-inedible"));
        return true;
    }

    internal static void NoteOverflow(EntityAgent entity, float fraction)
    {
        if (fraction < TooFullFraction && (entity as EntityPlayer)?.Player is IServerPlayer player)
            player.SendIngameError(TooFullCode, Text("dietsetup:eat-too-full"));
    }

    internal static bool GivesNothing(DietContributionSet set, float satiety, FoodNutritionProperties properties) =>
        !(satiety > 0f) && !HasVanillaConsequence(properties) && !HasUnresolvedOrConsequence(set);

    internal static bool GivesNothing(IEnumerable<DietMealPortion> portions)
    {
        bool any = false;
        foreach (DietMealPortion portion in portions)
        {
            any = true;
            if (!GivesNothing(portion.Set, portion.Satiety, portion.Properties)) return false;
        }
        return any;
    }

    private static bool GivesNothing(EntityAgent entity, ItemSlot slot, ItemStack?[]? mealContents)
    {
        if (slot.Itemstack is not { } stack) return false;
        var snapshot = DietRuntimeSnapshot.For(entity.Api);
        if (!snapshot.Config.EnableDietSystem || DietIdResolver.ResolveDiet(entity, snapshot) == null) return false;

        if (mealContents != null)
            return GivesNothing(DietMealContentNutritionPatch.ResolvePortions(entity.World, slot, mealContents, entity));

        ItemStack? nutritionStack = stack;
        FoodNutritionProperties? properties;
        if (stack.Collectible is BlockLiquidContainerBase liquid && !liquid.IsEmpty(stack))
        {
            nutritionStack = liquid.GetContent(stack);
            properties = liquid.GetNutritionPropertiesPerLitre(entity.World, stack, entity);
        }
        else
        {
            properties = stack.Collectible.GetNutritionProperties(entity.World, stack, entity);
        }
        if (nutritionStack?.Collectible == null || properties == null) return false;

        var nutritionSlot = ReferenceEquals(nutritionStack, stack) ? slot : new DummySlot(nutritionStack);
        snapshot.Tags.GetTagMask(entity.World, nutritionSlot, out float spoil, out bool determined);
        if (!determined || !DietSpoilageResolution.TryResolve(spoil, nutritionStack, entity, out DietContributionSet set)) return false;
        float satietyMul = GlobalConstants.FoodSpoilageSatLossMul(spoil, nutritionStack, entity);
        float satiety = properties.Satiety * satietyMul;
        if (!GivesNothing(set, satiety, properties)) return false;
        foreach (FoodNutritionProperties expanded in DietAcaIntegration.ExpandedRows(nutritionStack))
        {
            if (!GivesNothing(set, expanded.Satiety * satietyMul, expanded)) return false;
        }
        return true;
    }

    private static bool HasVanillaConsequence(FoodNutritionProperties properties) =>
        properties.Health != 0f || properties.Intoxication != 0f || properties.Psychedelic != 0f;

    private static bool HasUnresolvedOrConsequence(DietContributionSet set)
    {
        foreach (DietContribution component in set.Components)
        {
            if (component.Basis == DietContributionBasis.Unresolved) return true;
            foreach (CompiledEffect effect in component.Result.Effects)
                if (effect.Type is DietEffectType.Damage or DietEffectType.Custom) return true;
        }
        return false;
    }

    private static string Text(string key)
    {
        try { return Lang.Get(key); }
        catch (Exception) { return key; }
    }
}

internal readonly record struct DietMealPortion(DietContributionSet Set, float Satiety,
    FoodNutritionProperties Properties);
