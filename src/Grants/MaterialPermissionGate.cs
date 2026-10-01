using System;
using System.Collections.Generic;
using dietsetup.Binding;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace dietsetup.Grants;

/// <summary>The consumption half of authoring contract 3: whether this entity may physically eat a
/// material Diet Setup made edible. Permission is not nourishment -- a permitted diet can still
/// resolve the material as <c>Inedible</c> and eat it for nothing.</summary>
internal static class MaterialPermissionGate
{
    // A bowl holding an expanded food holding a liquid is the deepest authored case, and the limit
    // also terminates a walk over containment data that somehow points back at itself.
    private const int MaxContainmentDepth = 3;

    /// <summary>Server-authoritative refusal for one consumption site, called before anything is
    /// removed or credited. Returns false on the client and on any configuration with no restriction,
    /// so ordinary eating never pays for this.</summary>
    internal static bool Refuses(EntityAgent? entity, ItemStack? stack, ItemStack?[]? contents = null)
    {
        ICoreAPI? api = entity?.Api;
        if (api == null || entity!.World?.Side != EnumAppSide.Server) return false;
        if (!FoodOverrideRegistry.HasAnyRestriction(api)) return false;

        DietRuntimeSnapshot snapshot = DietRuntimeSnapshot.For(api);
        if (!snapshot.Config.EnableDietSystem) return false;

        string? dietId = PermissionDiet(entity, snapshot);
        CollectibleObject? denied = Denied(api, stack, dietId, 0);
        if (denied == null && contents != null)
        {
            bool meal = stack?.Collectible is BlockMeal;
            foreach (ItemStack? content in contents)
            {
                denied = Denied(api, content, dietId, 1, meal);
                if (denied != null) break;
            }
        }

        if (denied == null) return false;
        Report(entity, snapshot, denied, dietId);
        return true;
    }

    /// <summary>The same decision with no side effects, for the held-item tooltip. It reads the
    /// client's own synchronized table, so a preview cannot disagree with what the server enforces.</summary>
    internal static bool Denies(ICoreAPI? api, Entity? viewer, ItemStack? stack)
    {
        if (api == null || !FoodOverrideRegistry.HasAnyRestriction(api)) return false;
        DietRuntimeSnapshot snapshot = DietRuntimeSnapshot.For(api);
        return snapshot.Config.EnableDietSystem
            && Denied(api, stack, PermissionDiet(viewer, snapshot), 0) != null;
    }

    /// <summary>An entity whose diet id never compiled is on no list: a selected-but-refused diet must
    /// not inherit the base diet's permissions.</summary>
    private static string? PermissionDiet(Entity? entity, DietRuntimeSnapshot snapshot)
    {
        if (entity == null) return null;
        string id = DietIdResolver.Resolve(entity);
        return snapshot.GetDiet(id) != null ? id : null;
    }

    /// <summary>A grant only sets standalone nutrition, so an ingredient the actual nutrition path feeds from
    /// native in-meal props is not eating the grant. That exempts the ingredient alone, never what it contains.</summary>
    private static CollectibleObject? Denied(ICoreAPI api, ItemStack? stack, string? dietId, int depth, bool mealIngredient = false)
    {
        if (stack?.Collectible == null || depth > MaxContainmentDepth) return null;
        if (!(mealIngredient && MealReadsInMealNutrition(stack)) && Refused(api, stack.Collectible, dietId))
            return stack.Collectible;

        // Membership is the only question these codes can answer: no quantity, and never an ordinary
        // nutritional restriction. A code that resolves to nothing is unknown containment, not a denial.
        foreach (string code in DietAcaIntegration.MadeWithCodes(stack))
        {
            CollectibleObject? ingredient = Resolve(api, code);
            if (ingredient != null && !HasInMealAttribute(ingredient) && Refused(api, ingredient, dietId)) return ingredient;
        }

        if (stack.Collectible is not BlockContainer container) return null;

        ItemStack[]? contents;
        try { contents = container.GetNonEmptyContents(api.World, stack); }
        catch (Exception) { return null; }
        if (contents == null) return null;

        bool meal = container is BlockMeal;
        foreach (ItemStack content in contents)
        {
            CollectibleObject? denied = Denied(api, content, dietId, depth + 1, meal);
            if (denied != null) return denied;
        }
        return null;
    }

    // BlockMeal.GetIngredientStackNutritionProperties reads these two before any standalone props.
    private static bool MealReadsInMealNutrition(ItemStack stack) =>
        BlockLiquidContainerBase.GetContainableProps(stack)?.NutritionPropsPerLitreWhenInMeal != null
        || HasInMealAttribute(stack.Collectible);

    // ACA bakes a madeWith ingredient from this attribute before its NutritionProps (GetNutrientsFromIngredient).
    private static bool HasInMealAttribute(CollectibleObject collectible) =>
        collectible.Attributes?["nutritionPropsWhenInMeal"].Exists == true;

    private static bool Refused(ICoreAPI api, CollectibleObject collectible, string? dietId)
    {
        FoodAccessRule rule = FoodOverrideRegistry.AccessFor(api, collectible);
        return rule.Restricts && !rule.Permits(dietId);
    }

    private static CollectibleObject? Resolve(ICoreAPI api, string? code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        AssetLocation location;
        try { location = new AssetLocation(code); }
        catch (Exception) { return null; }
        return (CollectibleObject?)api.World.GetItem(location) ?? api.World.GetBlock(location);
    }

    private static void Report(EntityAgent entity, DietRuntimeSnapshot snapshot, CollectibleObject denied, string? dietId)
    {
        if ((entity as EntityPlayer)?.Player is IServerPlayer player)
            player.SendIngameError("dietsetup-material-denied", Message(denied));

        if (snapshot.Config.RecordLastConsumption)
            entity.Api.ModLoader.GetModSystem<DietSetupModSystem>()?.RecordConsumption(entity.EntityId,
                $"consumed=False refused=material-permission material={denied.Code} diet={dietId ?? "(unresolved)"}");
    }

    /// <summary>Lang throws rather than missing when no translation set is loaded, and a refusal must
    /// survive that: the material's own code still names it.</summary>
    private static string Message(CollectibleObject denied)
    {
        string code = denied.Code?.ToString() ?? "?";
        try
        {
            string name = denied.Code == null ? code : Lang.GetMatching(denied.Code.Domain
                + (denied is Block ? ":block-" : ":item-") + denied.Code.Path);
            return Lang.Get("dietsetup:material-denied", name);
        }
        catch (Exception) { return "dietsetup:material-denied " + code; }
    }
}
