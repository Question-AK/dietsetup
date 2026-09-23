using System.Collections.Generic;

namespace dietsetup.Grants;

/// <summary>Raw shape of ModConfig/dietsetup/food-overrides.json (architecture 7.6) -- admin-authored
/// edibility grants for collectibles vanilla shipped with no nutritionProps. ModConfig only, never an
/// asset: a compat pack cannot ship grants in v1.</summary>
public class FoodOverrideDocumentFile
{
    public int? SchemaVersion { get; set; }
    public List<FoodOverrideEntryFile> Grants { get; set; } = new();
}

/// <summary>One grant row. Pattern, category and baseSatiety are always required -- the mod is inventing
/// a number for an item vanilla gave none, so the author states it; no default repeats the "balanced"
/// 0.4 defect (7.6).</summary>
public class FoodOverrideEntryFile
{
    public string? Pattern { get; set; }
    public string? Category { get; set; }
    public float? BaseSatiety { get; set; }
    public FoodAccessFile? Access { get; set; }
}

/// <summary>schemaVersion 2 only. Absent means unrestricted, so every v1 file keeps its exact meaning;
/// present under schemaVersion 1 refuses the file rather than silently ignoring a permission.</summary>
public class FoodAccessFile
{
    public string? Mode { get; set; }
    public List<string>? Diets { get; set; }
}
