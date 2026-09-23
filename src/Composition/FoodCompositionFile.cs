using System.Collections.Generic;

namespace dietsetup.Composition;

/// <summary>config/food-composition.json across any domain, plus the ModConfig override.
/// A share divides delivered nourishment, never ingredient mass, and never a nutrient category.</summary>
public class FoodCompositionFile
{
    public int? SchemaVersion { get; set; }
    public string? AppliesWhen { get; set; }
    public List<FoodCompositionEntryFile> Entries { get; set; } = new();
}

public class FoodCompositionEntryFile
{
    public string? Pattern { get; set; }
    public string? Basis { get; set; }
    public Dictionary<string, float> Shares { get; set; } = new();
    public float? UnresolvedRemainder { get; set; }
    public string? Note { get; set; }
}
