using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using dietsetup.Tags;
using Vintagestory.API.Common;

namespace dietsetup.Rules;

public readonly record struct DietValidationMessage(int Rule, string Text);
public static class DietCompiler
{
    private static readonly EnumFoodCategory[] AllCategories =
    {
        EnumFoodCategory.Fruit, EnumFoodCategory.Vegetable, EnumFoodCategory.Grain,
        EnumFoodCategory.Protein, EnumFoodCategory.Dairy
    };

    public static CompiledDiet? Compile(FoodTagRegistry tags, string id, DietDocumentFile doc, string domain, float capacityFloor, List<DietValidationMessage> fatal, List<DietValidationMessage> warnings)
    {
        foreach (string error in ValidateDocument(doc)) fatal.Add(new DietValidationMessage(0, error));
        if (!float.IsFinite(capacityFloor) || capacityFloor <= 0 || !float.IsFinite(1f / capacityFloor))
            fatal.Add(new DietValidationMessage(0, "capacityFloor must be finite and positive with finite reciprocal"));
        if (fatal.Count > 0) return null;
        if (doc.SchemaVersion != 1)
        {
            fatal.Add(new DietValidationMessage(1, $"schemaVersion missing or unknown (got {(doc.SchemaVersion?.ToString() ?? "(missing)")})"));
        }

        Dictionary<EnumFoodCategory, CompiledCategory> categories = CompileCategories(id, doc.Categories, capacityFloor, fatal, warnings);

        if (!float.IsFinite(categories.Values.Sum(c => c.Capacity))) fatal.Add(new(0, "total capacity exceeds finite health arithmetic"));
        if (categories.Values.All(c => c.Capacity == 0f))
        {
            fatal.Add(new DietValidationMessage(8, "all five capacities are 0, this diet can never gain health"));
        }

        float fallbackSatiety = doc.Fallback?.SatietyMult ?? 1f;
        float fallbackNutrition = doc.Fallback?.NutritionMult ?? 1f;

        var rules = new List<CompiledRule>(doc.Rules.Length);
        for (int i = 0; i < doc.Rules.Length; i++)
        {
            CompiledRule? rule = CompileRule(tags, id, doc.Rules[i], i, fatal, warnings);
            if (rule != null) rules.Add(rule.Value);
        }

        CompiledRule[] sorted = SortByWinOrder(rules);
        CheckShadowedRules(id, sorted, warnings);
        CheckUncoveredCategories(tags, id, categories, sorted, fallbackNutrition, warnings);

        if (fatal.Count > 0) return null;

        return new CompiledDiet
        {
            Id = id,
            SourceDomain = domain,
            Categories = categories.ToImmutableDictionary(),
            FallbackSatietyMult = fallbackSatiety,
            FallbackNutritionMult = fallbackNutrition,
            Rules = sorted.ToImmutableArray(),
        };
    }

    private static Dictionary<EnumFoodCategory, CompiledCategory> CompileCategories(string id, Dictionary<string, DietCategoryFile> categoryFiles, float capacityFloor, List<DietValidationMessage> fatal, List<DietValidationMessage> warnings)
    {
        var result = new Dictionary<EnumFoodCategory, CompiledCategory>();

        foreach ((string name, DietCategoryFile catFile) in categoryFiles)
        {
            if (!Enum.TryParse(name, true, out EnumFoodCategory cat) || !AllCategories.Contains(cat))
            {
                fatal.Add(new DietValidationMessage(0, $"categories block names unknown category '{name}'"));
                continue;
            }

            if (catFile.SatietyMult.HasValue || catFile.NutritionMult.HasValue)
            {
                fatal.Add(new DietValidationMessage(6, $"category '{cat}' sets a rule-scoped multiplier (satietyMult/nutritionMult belong on rules, not categories)"));
            }

            result[cat] = DeriveCategory(id, cat, catFile.Capacity ?? 1f, capacityFloor, warnings);
        }

        foreach (EnumFoodCategory cat in AllCategories)
        {
            if (!result.ContainsKey(cat))
            {
                result[cat] = DeriveCategory(id, cat, 1f, capacityFloor, warnings);
            }
        }

        return result;
    }
    private static CompiledCategory DeriveCategory(string id, EnumFoodCategory cat, float rawCapacity, float capacityFloor, List<DietValidationMessage> warnings)
    {
        float capacity = rawCapacity;
        if (rawCapacity > 0f && rawCapacity < capacityFloor)
        {
            capacity = capacityFloor;
            warnings.Add(new DietValidationMessage(11, $"category '{cat}' capacity {rawCapacity:F3} clamped to floor {capacityFloor:F3}"));
        }

        float gainScale = capacity > 0f ? 1f / capacity : 0f;
        return new CompiledCategory(capacity, gainScale, capacity);
    }

    private static CompiledRule? CompileRule(FoodTagRegistry tags, string id, DietRuleFileEntry rf, int declarationIndex, List<DietValidationMessage> fatal, List<DietValidationMessage> warnings)
    {
        string[] requires = rf.Requires ?? Array.Empty<string>();
        string[] excludes = rf.Excludes ?? Array.Empty<string>();
        string label = requires.Length == 0 ? "(no requires)" : string.Join("+", requires);

        if (rf.Capacity.HasValue)
        {
            fatal.Add(new DietValidationMessage(5, $"rule '{label}': sets 'capacity', a category-scoped field"));
        }

        bool requiresOk = TryCompileMask(tags, label, requires, fatal, out ulong requiresMask);
        bool excludesOk = TryCompileMask(tags, label, excludes, fatal, out ulong excludesMask);
        if (!requiresOk || !excludesOk) return null;

        DietVerdict verdict = ParseVerdict(label, rf.Verdict, fatal);

        bool satietyIsCurve = rf.SatietyCurve is { Length: > 0 };
        bool nutritionIsCurve = rf.NutritionCurve is { Length: > 0 };
        if (satietyIsCurve && rf.SatietyMult.HasValue)
        {
            fatal.Add(new DietValidationMessage(16, $"rule '{label}': sets both 'satietyMult' and 'satietyCurve' -- author one, not both"));
        }
        if (nutritionIsCurve && rf.NutritionMult.HasValue)
        {
            fatal.Add(new DietValidationMessage(16, $"rule '{label}': sets both 'nutritionMult' and 'nutritionCurve' -- author one, not both"));
        }

        float satietyMult = rf.SatietyMult ?? 1f;
        float nutritionMult = rf.NutritionMult ?? 1f;
        var writtenFields = new HashSet<string>();
        if (rf.SatietyMult.HasValue || satietyIsCurve) writtenFields.Add("Satiety");
        if (rf.NutritionMult.HasValue || nutritionIsCurve) writtenFields.Add("Nutrition");
        if (rf.Verdict != null) writtenFields.Add("Verdict");

        CompiledEffect[] effects = CompileEffects(label, rf.Effects, writtenFields, fatal, warnings,
            ref satietyMult, ref nutritionMult, ref verdict);

        CompiledValue satiety = satietyIsCurve ? CompiledValue.FromCurve(SortAnchors(rf.SatietyCurve!)) : CompiledValue.Flat(satietyMult);
        CompiledValue nutrition = nutritionIsCurve ? CompiledValue.FromCurve(SortAnchors(rf.NutritionCurve!)) : CompiledValue.Flat(nutritionMult);

        return new CompiledRule(
            requiresMask, excludesMask, BitOperations.PopCount(requiresMask), rf.Priority ?? 0,
            verdict, satiety, nutrition, effects, label, rf.ShadowedIntentionally ?? false,
            satietyIsCurve || nutritionIsCurve || requires.Any(t => t is "fresh" or "spoiled" or "rotten"));
    }
    private static CurveAnchor[] SortAnchors(CurveAnchorFile[] anchorsFile)
    {
        var anchors = new CurveAnchor[anchorsFile.Length];
        for (int i = 0; i < anchorsFile.Length; i++)
        {
            anchors[i] = new CurveAnchor(anchorsFile[i].Spoil, anchorsFile[i].Value);
        }
        Array.Sort(anchors, (a, b) => a.Spoil.CompareTo(b.Spoil));
        return anchors;
    }

    private static bool TryCompileMask(FoodTagRegistry tags, string ruleLabel, string[] names, List<DietValidationMessage> fatal, out ulong mask)
    {
        mask = 0;
        bool ok = true;
        foreach (string tag in names)
        {
            if (!tags.TryGetBit(tag, out int bit))
            {
                fatal.Add(new DietValidationMessage(4, $"rule '{ruleLabel}': references unknown tag '{tag}'"));
                ok = false;
                continue;
            }
            mask |= 1UL << bit;
        }
        return ok;
    }

    private static DietVerdict ParseVerdict(string ruleLabel, string? verdict, List<DietValidationMessage> fatal)
    {
        string v = verdict ?? "edible";
        if (Enum.TryParse(v, true, out DietVerdict parsed) && Enum.IsDefined(parsed)) return parsed;
        fatal.Add(new DietValidationMessage(0, $"rule '{ruleLabel}': unknown verdict '{v}'"));
        return DietVerdict.Edible;
    }
    private static CompiledEffect[] CompileEffects(string ruleLabel, DietEffectFile[]? effectsFile, HashSet<string> writtenFields, List<DietValidationMessage> fatal, List<DietValidationMessage> warnings, ref float satietyMult, ref float nutritionMult, ref DietVerdict verdict)
    {
        if (effectsFile == null || effectsFile.Length == 0) return Array.Empty<CompiledEffect>();

        var list = new List<CompiledEffect>(effectsFile.Length);

        foreach (DietEffectFile ef in effectsFile)
        {
            if (!Enum.TryParse(ef.Type, true, out DietEffectType type) || !Enum.IsDefined(type))
            {
                fatal.Add(new DietValidationMessage(7, $"rule '{ruleLabel}': unknown effect type '{ef.Type}'"));
                continue;
            }

            string? field = type switch
            {
                DietEffectType.SatietyMult => "Satiety",
                DietEffectType.NutritionMult => "Nutrition",
                DietEffectType.Verdict => "Verdict",
                _ => null
            };
            if (field != null && !writtenFields.Add(field))
            {
                fatal.Add(new DietValidationMessage(9, $"rule '{ruleLabel}': two effects write the field '{field}'"));
                continue;
            }

            DietVerdict? effectVerdict = null;
            if (type == DietEffectType.Verdict)
            {
                if (!Enum.TryParse(ef.Verdict ?? "", true, out DietVerdict parsedVerdict) || !Enum.IsDefined(parsedVerdict))
                {
                    fatal.Add(new DietValidationMessage(0, $"rule '{ruleLabel}': verdict effect has unknown verdict '{ef.Verdict}'"));
                    continue;
                }
                effectVerdict = parsedVerdict;
                verdict = parsedVerdict;
            }
            else if (type == DietEffectType.SatietyMult)
            {
                satietyMult = ef.Amount ?? 1f;
            }
            else if (type == DietEffectType.NutritionMult)
            {
                nutritionMult = ef.Amount ?? 1f;
            }

            IDietConsequenceEffect? customEffect = null;
            if (type == DietEffectType.Custom)
            {
                string key = ef.Key ?? "";
                if (!DietEffects.TryGet(key, out customEffect))
                {
                    warnings.Add(new DietValidationMessage(13, $"rule '{ruleLabel}': effect type 'custom' key '{key}' has no registered handler yet"));
                }
            }

            DietDamageMode? damageMode = null;
            float durationSec = 0f;
            int ticks = 1;
            if (type == DietEffectType.Damage)
            {
                if (!Enum.TryParse(ef.Mode, true, out DietDamageMode parsedMode) || !Enum.IsDefined(parsedMode))
                {
                    fatal.Add(new DietValidationMessage(0, $"rule '{ruleLabel}': damage effect has missing or unknown mode '{ef.Mode}' (must be 'instant' or 'overTime')"));
                    continue;
                }
                damageMode = parsedMode;
                if (parsedMode == DietDamageMode.OverTime)
                {
                    durationSec = ef.DurationSec ?? 3f;
                    ticks = Math.Max(1, ef.Ticks ?? 3);
                }
            }
            float amount = type is DietEffectType.SatietyMult or DietEffectType.NutritionMult ? ef.Amount ?? 1f : ef.Amount ?? 0f;
            list.Add(new CompiledEffect(type, amount, ef.Mode, effectVerdict, ef.Key, customEffect, damageMode, durationSec, ticks));
        }
        return list.ToArray();
    }
    private static CompiledRule[] SortByWinOrder(List<CompiledRule> rules)
    {
        var indexed = rules.Select((r, idx) => (Rule: r, Index: idx)).ToList();
        indexed.Sort((x, y) =>
        {
            int byPriority = y.Rule.Priority.CompareTo(x.Rule.Priority);
            if (byPriority != 0) return byPriority;
            int bySpecificity = y.Rule.Specificity.CompareTo(x.Rule.Specificity);
            if (bySpecificity != 0) return bySpecificity;
            return x.Index.CompareTo(y.Index);
        });
        return indexed.Select(t => t.Rule).ToArray();
    }
    private static void CheckShadowedRules(string id, CompiledRule[] sorted, List<DietValidationMessage> warnings)
    {
        bool[] actuallyShadowed = new bool[sorted.Length];
        for (int i = 0; i < sorted.Length; i++)
        {
            for (int j = i + 1; j < sorted.Length; j++)
            {
                CompiledRule a = sorted[i];
                CompiledRule b = sorted[j];
                bool requiresSubset = (a.RequiresMask & ~b.RequiresMask) == 0;
                bool excludesSubset = (a.ExcludesMask & ~b.ExcludesMask) == 0;
                if (requiresSubset && excludesSubset)
                {
                    actuallyShadowed[j] = true;
                    if (!b.ShadowedIntentionally)
                    {
                        warnings.Add(new DietValidationMessage(12, $"rule '{b.DebugLabel}' is unreachable, fully shadowed by higher-priority rule '{a.DebugLabel}'"));
                    }
                }
            }
        }

        for (int i = 0; i < sorted.Length; i++)
        {
            if (sorted[i].ShadowedIntentionally && !actuallyShadowed[i])
            {
                warnings.Add(new DietValidationMessage(12, $"rule '{sorted[i].DebugLabel}' is marked shadowedIntentionally but is not shadowed -- the flag is stale"));
            }
        }
    }
    private static void CheckUncoveredCategories(FoodTagRegistry tags, string id, Dictionary<EnumFoodCategory, CompiledCategory> categories, CompiledRule[] rules, float fallbackNutrition, List<DietValidationMessage> warnings)
    {
        if (fallbackNutrition > 0f) return;

        foreach ((EnumFoodCategory cat, CompiledCategory compiled) in categories)
        {
            if (compiled.Capacity <= 0f) continue;
            if (rules.Any(r => r.NutritionMult.CanBePositive && RuleCoversCategory(tags, r, cat))) continue;

            warnings.Add(new DietValidationMessage(10, $"category '{cat}' has capacity {compiled.Capacity:F2} but no rule (and no fallback) can produce nutrition for it"));
        }
    }

    private static bool RuleCoversCategory(FoodTagRegistry tags, CompiledRule rule, EnumFoodCategory cat)
    {
        bool referencesAnySourceTag = false;
        foreach (string tag in tags.TagNames(rule.RequiresMask))
        {
            EnumFoodCategory? bar = FoodTagRegistry.NutrientBarFor(tag);
            if (bar == null) continue;
            referencesAnySourceTag = true;
            if (bar == cat) return true;
        }
        return !referencesAnySourceTag;
    }
    public static List<string> ValidateDocument(DietDocumentFile? doc)
    {
        var errors = new List<string>();
        void Object(DietAuthoringFile? value, string path)
        {
            if (value == null) { errors.Add($"{path}: null object"); return; }
            if (value.UnknownFields != null)
                foreach (string key in value.UnknownFields.Keys) errors.Add($"{path}.{key}: unsupported authoring field");
        }
        void Number(float? value, string path, bool nonnegative = false)
        {
            if (value.HasValue && (!float.IsFinite(value.Value) || (nonnegative && value < 0)))
                errors.Add($"{path}: expected finite{(nonnegative ? " non-negative" : "")} number");
        }
        void Curve(CurveAnchorFile[]? values, string path)
        {
            if (values == null) return;
            if (values.Length == 0) errors.Add($"{path}: curve must have anchors");
            var positions = new HashSet<float>();
            for (int i = 0; i < values.Length; i++)
            {
                var anchor = values[i]; Object(anchor, $"{path}[{i}]");
                if (anchor == null) continue;
                Number(anchor.Spoil, $"{path}[{i}].spoil"); Number(anchor.Value, $"{path}[{i}].value");
                if (anchor.Spoil < 0 || anchor.Spoil > 1) errors.Add($"{path}[{i}].spoil: outside 0..1");
                if (!positions.Add(anchor.Spoil)) errors.Add($"{path}: duplicate spoil position {anchor.Spoil}");
            }
        }
        Object(doc, "diet");
        if (doc == null) return errors;
        if (doc.Categories == null) errors.Add("categories: null object");
        else foreach (var (name, category) in doc.Categories)
        {
            Object(category, $"categories.{name}"); if (category == null) continue;
            Number(category.Capacity, $"categories.{name}.capacity", true);
            if (category.DrainRate.HasValue) errors.Add($"categories.{name}.drainRate: reserved, not supported");
        }
        if (doc.Fallback != null)
        {
            Object(doc.Fallback, "fallback");
            Number(doc.Fallback.SatietyMult, "fallback.satietyMult"); Number(doc.Fallback.NutritionMult, "fallback.nutritionMult");
        }
        if (doc.Rules == null) { errors.Add("rules: null array"); return errors; }
        for (int i = 0; i < doc.Rules.Length; i++)
        {
            string path = $"rules[{i}]"; var rule = doc.Rules[i];
            Object(rule, path); if (rule == null) continue;
            if (rule.Trigger != null && rule.Trigger != "onEat") errors.Add($"{path}.trigger: only onEat is supported");
            if (rule.Requires?.Any(string.IsNullOrWhiteSpace) == true || rule.Excludes?.Any(string.IsNullOrWhiteSpace) == true)
                errors.Add($"{path}: requires/excludes cannot contain null or empty tag names");
            Number(rule.SatietyMult, path + ".satietyMult"); Number(rule.NutritionMult, path + ".nutritionMult");
            Curve(rule.SatietyCurve, path + ".satietyCurve"); Curve(rule.NutritionCurve, path + ".nutritionCurve");
            if (rule.Effects == null) continue;
            for (int j = 0; j < rule.Effects.Length; j++)
            {
                var effect = rule.Effects[j]; string ep = $"{path}.effects[{j}]";
                Object(effect, ep); if (effect == null) continue;
                Number(effect.Amount, ep + ".amount"); Number(effect.DurationSec, ep + ".durationSec");
                if (effect.DurationSec <= 0 || effect.Ticks <= 0) errors.Add($"{ep}: durationSec and ticks must be positive when supplied");
            }
        }
        return errors;
    }
}
