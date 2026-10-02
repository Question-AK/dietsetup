using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Vintagestory.API.Common;

namespace dietsetup.Rules;

public sealed class CompiledDiet
{
    public string Id { get; init; } = "";
    public string SourceDomain { get; init; } = "";
    public NutritionModel NutritionModel { get; init; } = NutritionModel.Legacy;
    public OverflowNutrition OverflowNutrition { get; init; } = OverflowNutrition.WholeItem;

    public ImmutableDictionary<EnumFoodCategory, CompiledCategory> Categories { get; init; } = ImmutableDictionary<EnumFoodCategory, CompiledCategory>.Empty;

    public float FallbackSatietyMult { get; init; } = 1f;
    public float FallbackNutritionMult { get; init; } = 1f;
    public ImmutableArray<CompiledRule> Rules { get; init; } = ImmutableArray<CompiledRule>.Empty;
}
public enum NutritionModel
{
    /// <summary>Gain scaled by 1/capacity on a bar the size of the stomach; vanilla decay.</summary>
    Legacy,
    /// <summary>Gain normalised to racial demand and divided by the requirement; decay on the Human curve.</summary>
    DemandNormalised,
}

public enum OverflowNutrition
{
    /// <summary>A mouthful that starts below full credits its whole nutrition, as in vanilla.</summary>
    WholeItem,
    /// <summary>A mouthful credits the share of its effective satiety that fitted the stomach.</summary>
    Proportional,
}

public readonly struct CompiledCategory
{
    public readonly float Capacity;
    /// <summary>The diet's own share of the gain scale: 1/capacity under Legacy, 1/requirement under
    /// DemandNormalised, 0 for an unsupported category under either.</summary>
    public readonly float NutritionGainScale;
    public readonly float HealthWeight;
    public readonly float NutritionRequirement;

    public CompiledCategory(float capacity, float nutritionGainScale, float healthWeight, float nutritionRequirement = 1f)
    {
        Capacity = capacity;
        NutritionGainScale = nutritionGainScale;
        HealthWeight = healthWeight;
        NutritionRequirement = nutritionRequirement;
    }
}
