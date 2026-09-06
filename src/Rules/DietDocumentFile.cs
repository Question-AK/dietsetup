using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace dietsetup.Rules;
public class DietDocumentFile : DietAuthoringFile
{
    public int? SchemaVersion { get; set; }
    public string? Id { get; set; }
    public string? Extends { get; set; }
    public Dictionary<string, DietCategoryFile> Categories { get; set; } = new();
    public DietFallbackFile? Fallback { get; set; }
    public DietRuleFileEntry[] Rules { get; set; } = Array.Empty<DietRuleFileEntry>();
}

public class DietCategoryFile : DietAuthoringFile
{
    public float? Capacity { get; set; }
    public float? DrainRate { get; set; }
    public float? SatietyMult { get; set; }
    public float? NutritionMult { get; set; }
}

public class DietFallbackFile : DietAuthoringFile
{
    public float? SatietyMult { get; set; }
    public float? NutritionMult { get; set; }
}

public class DietRuleFileEntry : DietAuthoringFile
{
    public string? Trigger { get; set; }
    public string[]? Requires { get; set; }
    public string[]? Excludes { get; set; }
    public int? Priority { get; set; }
    public string? Verdict { get; set; }
    public float? SatietyMult { get; set; }
    public float? NutritionMult { get; set; }
    public CurveAnchorFile[]? SatietyCurve { get; set; }
    public CurveAnchorFile[]? NutritionCurve { get; set; }

    public DietEffectFile[]? Effects { get; set; }
    public float? Capacity { get; set; }
    public bool? ShadowedIntentionally { get; set; }
}

public class CurveAnchorFile : DietAuthoringFile
{
    public float Spoil { get; set; }
    public float Value { get; set; }
}
public class DietEffectFile : DietAuthoringFile
{
    public string Type { get; set; } = "";
    public string? Mode { get; set; }
    public float? Amount { get; set; }
    public string? Verdict { get; set; }
    public string? Key { get; set; }
    public float? DurationSec { get; set; }
    public int? Ticks { get; set; }
}

public abstract class DietAuthoringFile
{
    [JsonExtensionData] public Dictionary<string, JToken>? UnknownFields { get; set; }
}
