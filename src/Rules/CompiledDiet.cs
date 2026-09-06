using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Vintagestory.API.Common;

namespace dietsetup.Rules;

public sealed class CompiledDiet
{
    public string Id { get; init; } = "";
    public string SourceDomain { get; init; } = "";

    public ImmutableDictionary<EnumFoodCategory, CompiledCategory> Categories { get; init; } = ImmutableDictionary<EnumFoodCategory, CompiledCategory>.Empty;

    public float FallbackSatietyMult { get; init; } = 1f;
    public float FallbackNutritionMult { get; init; } = 1f;
    public ImmutableArray<CompiledRule> Rules { get; init; } = ImmutableArray<CompiledRule>.Empty;
}
public readonly struct CompiledCategory
{
    public readonly float Capacity;
    public readonly float NutritionGainScale;
    public readonly float HealthWeight;

    public CompiledCategory(float capacity, float nutritionGainScale, float healthWeight)
    {
        Capacity = capacity;
        NutritionGainScale = nutritionGainScale;
        HealthWeight = healthWeight;
    }
}
