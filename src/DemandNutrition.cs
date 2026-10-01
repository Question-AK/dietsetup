using System;
using dietsetup.Rules;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace dietsetup;

// Equations: notes/race-mechanics/session-handoffs/2026-10-02-nutrition-model-revision-claude.md.
internal static class DemandNutrition
{
    // A Human stomach: every demand-normalised bar gains and decays as if it were this size.
    internal const float ReferenceStomach = 1500f;
    // PlayerModelLib multiplies every ReduceSaturation by this stat; Race Framework's racial hunger is set here,
    // never on vanilla hungerrate, which also carries cold and other mods' effects that must stay.
    internal const string RacialDemandStat = "saturationLossFactor";
    internal static bool PlayerModelLibLoaded;

    /// <summary>The factor applied to the vanilla nutrition credit. Legacy and diet-less credits are unchanged.</summary>
    internal static float GainScale(CompiledDiet? diet, EnumFoodCategory category, EntityBehaviorHunger? hunger)
    {
        if (diet == null || !diet.Categories.TryGetValue(category, out var compiled)) return 1f;
        if (diet.NutritionModel != NutritionModel.DemandNormalised || hunger == null) return compiled.NutritionGainScale;
        float max = hunger.MaxSaturation;
        // Vanilla adds the scaled credit before clamping to MaxSaturation, so a negative scale would drain the bar.
        if (!(max > 0f) || !float.IsFinite(max)) return 0f;
        return compiled.NutritionGainScale * max / (ReferenceStomach * Demand(hunger.entity));
    }

    /// <summary>The racial factor PlayerModelLib folded into this entity's decay, or 1 where none was.</summary>
    internal static float Demand(Entity entity)
    {
        if (!PlayerModelLibLoaded) return 1f;
        float factor = entity.Stats.GetBlended(RacialDemandStat);
        // A factor of 0 stops decay and pins the stomach full; dividing gain by it would fill a bar in one bite.
        return factor > 0f && float.IsFinite(factor) ? factor : 1f;
    }

    /// <summary>Reference decrement over vanilla's, for one bar at level <paramref name="level"/> of a
    /// <paramref name="max"/> stomach. Both share the tick's activity, dairy and environment factors.</summary>
    internal static float DecayRatio(float level, float max, float demand) =>
        Math.Max(0.5f * max / ReferenceStomach, 0.001f * level) / (demand * Math.Max(0.5f, 0.001f * level));
}
