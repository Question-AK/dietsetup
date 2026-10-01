using System;
using System.Linq;
using dietsetup.Rules;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace dietsetup;

// Neither vanilla nor PlayerModelLib rescales nutrient levels when MaxSaturation changes, so a race or
// stomach switch would leave levels measured against the old stomach. Kept in server-only Attributes.
internal static class DietNutritionBasis
{
    internal const string Attribute = "dietsetup:nutritionBasis";
    private const int AllCategories = 0b11111;
    private static readonly EnumFoodCategory[] Categories =
        [EnumFoodCategory.Fruit, EnumFoodCategory.Vegetable, EnumFoodCategory.Protein, EnumFoodCategory.Grain, EnumFoodCategory.Dairy];

    internal static bool Supports(CompiledDiet? diet, EnumFoodCategory category) =>
        diet == null || diet.Categories.TryGetValue(category, out var compiled) && compiled.Capacity > 0f;

    internal static float Fraction(float level, float maxSaturation) => Bound(level / maxSaturation, 1f);

    // Written as a comparison so a NaN level reads as empty rather than poisoning the bonus.
    private static float Bound(float value, float max) => value > 0f ? Math.Min(value, max) : 0f;

    /// <summary>Keeps each level's share of the stomach for categories this diet and the recorded one both
    /// weigh. Anything else restarts empty, so switching never fills a bar nothing was eaten for.</summary>
    internal static void Reconcile(EntityBehaviorHunger hunger, CompiledDiet? diet)
    {
        if (hunger.entity.Api?.Side != EnumAppSide.Server) return;
        // Through the getter, so PlayerModelLib applies a pending maxSaturationFactor change before the comparison.
        float max = hunger.MaxSaturation;
        if (!(max > 0f) || !float.IsFinite(max)) return;
        int supported = Mask(diet);
        ITreeAttribute? basis = hunger.entity.Attributes.GetTreeAttribute(Attribute);
        float oldMax = basis?.GetFloat("maxSaturation") ?? 0f;
        int oldSupported = basis?.GetInt("supported") ?? 0;
        if (oldMax == max && oldSupported == supported) return;

        // Without a usable record (saves from before this build) the old stomach is unknown; bound only.
        bool known = oldMax > 0f && float.IsFinite(oldMax);
        for (int i = 0; i < Categories.Length; i++)
        {
            float level = DietDiagnostics.Level(hunger, Categories[i]);
            float next = !known ? Bound(level, max)
                : (oldSupported & supported & 1 << i) != 0 ? Bound(level * (max / oldMax), max)
                : 0f;
            if (next != level) DietConsumption.SetLevel(hunger, Categories[i], next);
        }
        Record(hunger.entity, max, supported);
    }

    /// <summary>Removes satiety above a shrunken stomach; a larger stomach adds none. Not called from
    /// Initialize, whose MaxSaturation can predate the race's trait stats.</summary>
    internal static void BoundSatiety(EntityBehaviorHunger hunger)
    {
        if (hunger.entity.Api?.Side != EnumAppSide.Server) return;
        float max = hunger.MaxSaturation;
        if (max > 0f && float.IsFinite(max) && hunger.Saturation > max) hunger.Saturation = max;
    }

    /// <summary>The diet system is off, so vanilla owns the levels; only follow the stomach so a later
    /// re-enable does not rescale levels vanilla earned against it.</summary>
    internal static void Follow(EntityBehaviorHunger hunger)
    {
        if (hunger.entity.Api?.Side != EnumAppSide.Server) return;
        float max = hunger.MaxSaturation;
        if (max > 0f && float.IsFinite(max)) Record(hunger.entity, max, AllCategories);
    }

    internal static string Describe(Entity entity)
    {
        ITreeAttribute? basis = entity.Attributes.GetTreeAttribute(Attribute);
        if (basis == null) return "basis=(not recorded yet)";
        int supported = basis.GetInt("supported");
        var names = Categories.Where((_, i) => (supported & 1 << i) != 0);
        return $"basis maxSat={basis.GetFloat("maxSaturation"):F1} supported={string.Join(",", names)}";
    }

    private static int Mask(CompiledDiet? diet)
    {
        int mask = 0;
        for (int i = 0; i < Categories.Length; i++)
            if (Supports(diet, Categories[i])) mask |= 1 << i;
        return mask;
    }

    private static void Record(Entity entity, float max, int supported)
    {
        ITreeAttribute? basis = entity.Attributes.GetTreeAttribute(Attribute);
        if (basis != null && basis.GetFloat("maxSaturation") == max && basis.GetInt("supported") == supported) return;
        basis = new TreeAttribute();
        basis.SetFloat("maxSaturation", max);
        basis.SetInt("supported", supported);
        entity.Attributes.SetAttribute(Attribute, basis);
    }
}
