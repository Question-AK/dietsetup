using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace dietsetup.Tags;

public sealed class FoodTagRegistry
{
    public const int MaxTags = 64;
    public const string FreshTag = "fresh";
    public const string SpoiledTag = "spoiled";

    private readonly Dictionary<string, int> tagBits = new();
    private readonly Dictionary<string, FoodTagAxis> tagAxis = new();
    private readonly Dictionary<string, List<string>> tagPatterns = new();
    private readonly HashSet<(bool isBlock, int id)> loggedTransitionFailures = new();
    // Item and block IDs occupy separate spaces.
    private ulong[] itemMasks = Array.Empty<ulong>();
    private ulong[] blockMasks = Array.Empty<ulong>();
    private ulong sourceAxisMask;
    private ulong stateAxisMask;
    private static readonly Dictionary<string, EnumFoodCategory> SourceBar = new()
    {
        ["meat"] = EnumFoodCategory.Protein,
        ["organ"] = EnumFoodCategory.Protein,
        ["blood"] = EnumFoodCategory.Protein,
        ["carrion"] = EnumFoodCategory.Protein,
        ["fish"] = EnumFoodCategory.Protein,
        ["insect"] = EnumFoodCategory.Protein,
        ["egg"] = EnumFoodCategory.Protein,
        ["dairy"] = EnumFoodCategory.Dairy,
        ["grain"] = EnumFoodCategory.Grain,
        ["seed"] = EnumFoodCategory.Grain,
        ["root"] = EnumFoodCategory.Vegetable,
        ["leaf"] = EnumFoodCategory.Vegetable,
        ["fruit"] = EnumFoodCategory.Fruit,
        ["nut"] = EnumFoodCategory.Fruit,
        ["sap"] = EnumFoodCategory.Fruit,
        ["resin"] = EnumFoodCategory.Fruit,
        ["bone"] = EnumFoodCategory.Vegetable,
        ["mineral"] = EnumFoodCategory.Vegetable,
    };
    public FoodTagRegistry()
    {
        // Stable reserved bits keep freshness independent of asset load order.
        EnsureBit(FreshTag, FoodTagAxis.State);
        EnsureBit(SpoiledTag, FoodTagAxis.State);
    }

    public IEnumerable<string> AllTagNames => tagBits.Keys;
    public int UntaggedNutritiousCount { get; private set; }
    public static EnumFoodCategory? NutrientBarFor(string sourceTag) =>
        SourceBar.TryGetValue(sourceTag, out EnumFoodCategory bar) ? bar : null;
    public bool TryGetBit(string tag, out int bit) => tagBits.TryGetValue(tag, out bit);
    public void LoadFrom(FoodTagConfigFile file)
    {
        EnsureMutable();
        LoadAxis(file.Source, FoodTagAxis.Source);
        LoadAxis(file.State, FoodTagAxis.State);
        LoadAxis(file.Form, FoodTagAxis.Form);
    }

    private void LoadAxis(Dictionary<string, string[]> tags, FoodTagAxis axis)
    {
        if (tags == null) throw new ArgumentException($"foodtags.{axis}: null object");
        foreach ((string tag, string[] patterns) in tags)
        {
            if (string.IsNullOrWhiteSpace(tag) || patterns == null || Array.Exists(patterns, string.IsNullOrWhiteSpace))
                throw new ArgumentException($"foodtags.{axis}.{tag}: tag and patterns must be non-null and non-empty");
            EnsureBit(tag, axis);
            foreach (string pattern in patterns)
            {
                if (!tagPatterns.TryGetValue(tag, out List<string>? list))
                {
                    tagPatterns[tag] = list = new List<string>();
                }
                list.Add(pattern);
            }
        }
    }
    public List<string> ApplyOverrides(FoodTagConfigFile file)
    {
        EnsureMutable();
        var replaced = new List<string>();
        ApplyOverrideAxis(file.Source, FoodTagAxis.Source, replaced);
        ApplyOverrideAxis(file.State, FoodTagAxis.State, replaced);
        ApplyOverrideAxis(file.Form, FoodTagAxis.Form, replaced);
        return replaced;
    }

    private void ApplyOverrideAxis(Dictionary<string, string[]> tags, FoodTagAxis axis, List<string> replaced)
    {
        if (tags == null) throw new ArgumentException($"foodtags.{axis}: null object");
        foreach ((string tag, string[] patterns) in tags)
        {
            if (string.IsNullOrWhiteSpace(tag) || patterns == null || Array.Exists(patterns, string.IsNullOrWhiteSpace))
                throw new ArgumentException($"foodtags.{axis}.{tag}: tag and patterns must be non-null and non-empty");
            bool existed = tagBits.ContainsKey(tag);
            EnsureBit(tag, axis);
            tagPatterns[tag] = new List<string>(patterns);
            if (existed) replaced.Add(tag);
        }
    }

    private int EnsureBit(string tag, FoodTagAxis axis)
    {
        if (tagBits.TryGetValue(tag, out int existing))
        {
            if (tagAxis[tag] != axis)
            {
                throw new InvalidOperationException(
                    $"[dietsetup] Tag '{tag}' registered under axis '{axis}' but was already registered as '{tagAxis[tag]}'.");
            }
            return existing;
        }

        if (tagBits.Count >= MaxTags)
        {
            throw new InvalidOperationException(
                $"[dietsetup] Cannot register tag '{tag}': the {MaxTags}-tag mask is already full.");
        }

        int bit = tagBits.Count;
        tagBits[tag] = bit;
        tagAxis[tag] = axis;
        if (axis == FoodTagAxis.Source)
        {
            sourceAxisMask |= 1UL << bit;
        }
        else if (axis == FoodTagAxis.State)
        {
            stateAxisMask |= 1UL << bit;
        }
        return bit;
    }
    public void ResolveStaticTags(ICoreAPI api)
    {
        EnsureMutable();
        var patternArrays = new Dictionary<string, string[]>(tagPatterns.Count);
        foreach ((string tag, List<string> patterns) in tagPatterns)
        {
            patternArrays[tag] = patterns.ToArray();
        }
        ulong formOtherMask = 0;
        int wholeBit = -1;
        foreach ((string tag, int bit) in tagBits)
        {
            if (!tagAxis.TryGetValue(tag, out FoodTagAxis axis) || axis != FoodTagAxis.Form) continue;
            if (tag == "whole") { wholeBit = bit; continue; }
            formOtherMask |= 1UL << bit;
        }

        itemMasks = new ulong[api.World.Items.Count];
        blockMasks = new ulong[api.World.Blocks.Count];
        UntaggedNutritiousCount = 0;

        foreach (CollectibleObject collectible in api.World.Collectibles)
        {
            AssetLocation? code = collectible.Code;
            if (code == null) continue;
            string codeStr = code.ToString();

            ulong mask = 0;
            foreach ((string tag, string[] patterns) in patternArrays)
            {
                if (patterns.Length > 0 && WildcardUtil.Match(patterns, codeStr))
                {
                    mask |= 1UL << tagBits[tag];
                }
            }

            bool relevant = (mask & sourceAxisMask) != 0 || collectible.NutritionProps != null;
            if (wholeBit >= 0 && relevant && (mask & formOtherMask) == 0)
            {
                mask |= 1UL << wholeBit;
            }

            if (collectible is Block)
            {
                blockMasks[collectible.Id] = mask;
            }
            else
            {
                itemMasks[collectible.Id] = mask;
            }

            if (collectible.NutritionProps != null && (mask & sourceAxisMask) == 0)
            {
                UntaggedNutritiousCount++;
                api.Logger.Warning("[dietsetup] Collectible '{0}' has nutrition properties but no source tag in the food tag registry.", codeStr);
            }
        }
    }

    public ulong GetStaticMask(CollectibleObject collectible)
    {
        int id = collectible.Id;
        ulong[] table = collectible is Block ? blockMasks : itemMasks;
        return id >= 0 && id < table.Length ? table[id] : 0;
    }

    private bool IsRelevant(ulong staticMask, CollectibleObject collectible) =>
        (staticMask & sourceAxisMask) != 0 || collectible.NutritionProps != null;
    public ulong GetTagMaskForSpoilState(CollectibleObject collectible, float spoilLevel)
    {
        ulong mask = GetStaticMask(collectible);
        if (!IsRelevant(mask, collectible)) return mask;

        mask |= 1UL << tagBits[spoilLevel > 0f ? SpoiledTag : FreshTag];
        return mask;
    }
    public ulong GetPieFillingTagMask(CollectibleObject fillingCollectible, CollectibleObject pieCollectible, float pieSpoilLevel)
    {
        ulong fillingMask = GetStaticMask(fillingCollectible);
        if (!IsRelevant(fillingMask, fillingCollectible)) return fillingMask;

        ulong pieMask = GetStaticMask(pieCollectible);
        ulong mask = (fillingMask & ~stateAxisMask) | (pieMask & stateAxisMask);
        mask |= 1UL << tagBits[pieSpoilLevel > 0f ? SpoiledTag : FreshTag];
        return mask;
    }
    public ulong GetTagMask(IWorldAccessor world, ItemSlot slot, out bool determined) =>
        GetTagMask(world, slot, out _, out determined);
    public ulong GetTagMask(IWorldAccessor world, ItemSlot slot, out float spoilLevel, out bool determined)
    {
        determined = true;
        spoilLevel = 0f;
        ItemStack? stack = slot.Itemstack;
        if (stack?.Collectible == null) return 0;

        ulong mask = GetStaticMask(stack.Collectible);

        if (!IsRelevant(mask, stack.Collectible)) return mask;

        float? transitionLevel;
        try
        {
            transitionLevel = stack.Collectible.UpdateAndGetTransitionState(world, slot, EnumTransitionType.Perish)?.TransitionLevel;
        }
        catch (Exception ex)
        {
            determined = false;
            LogTransitionFailureOnce(world, stack.Collectible, ex);
            return mask;
        }

        spoilLevel = transitionLevel ?? 0f;
        mask |= 1UL << tagBits[spoilLevel > 0f ? SpoiledTag : FreshTag];

        return mask;
    }

    private void LogTransitionFailureOnce(IWorldAccessor world, CollectibleObject collectible, Exception ex)
    {
        var key = (collectible is Block, collectible.Id);
        lock (loggedTransitionFailures)
        {
            if (!loggedTransitionFailures.Add(key)) return;
        }
        world.Logger.Error("[dietsetup] GetTagMask: transition state read failed for '{0}': {1}", collectible.Code, ex);
    }

    public IEnumerable<string> TagNames(ulong mask)
    {
        foreach ((string tag, int bit) in tagBits)
        {
            if ((mask & (1UL << bit)) != 0)
            {
                yield return tag;
            }
        }
    }
    private bool frozen;
    internal void Freeze() => frozen = true;
    private void EnsureMutable()
    {
        if (frozen) throw new InvalidOperationException("Published food tags cannot be modified.");
    }

    internal FoodTagConfigFile Export()
    {
        var file = new FoodTagConfigFile();
        foreach (var (tag, axis) in tagAxis)
        {
            var target = axis == FoodTagAxis.Source ? file.Source : axis == FoodTagAxis.State ? file.State : file.Form;
            target[tag] = tagPatterns.TryGetValue(tag, out var patterns) ? patterns.ToArray() : Array.Empty<string>();
        }
        return file;
    }
}
