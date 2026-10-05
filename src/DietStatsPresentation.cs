using System;
using System.Reflection;
using dietsetup.Binding;
using dietsetup.Rules;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace dietsetup;

[HarmonyPatch(typeof(CharacterExtraDialogs), nameof(CharacterExtraDialogs.ComposeStatsGui))]
internal static class DietStatsComposePatch
{
    [HarmonyPrefix]
    private static void Prefix(CharacterExtraDialogs __instance, out DietStatsLayout? __state)
    {
        __state = DietStatsPresentation.Current;
        DietStatsPresentation.Current = DietStatsPresentation.For(__instance);
        DietStatsPresentation.Remember(DietStatsPresentation.Current);
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, DietStatsLayout? __state)
    {
        DietStatsPresentation.Current = __state;
        return __exception;
    }
}

[HarmonyPatch]
internal static class DietStatsLabelPatch
{
    private static MethodBase TargetMethod()
    {
        foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Vintagestory.API.Client.GuiComposerHelpers)))
            if (method.Name == "AddStaticText" && method.GetParameters().Length == 5
                && method.GetParameters()[3].ParameterType == typeof(ElementBounds)) return method;
        throw new MissingMethodException("GuiComposerHelpers.AddStaticText");
    }

    [HarmonyPrefix]
    private static bool Prefix(GuiComposer composer, ElementBounds bounds, ref GuiComposer __result)
    {
        if (DietStatsPresentation.Current?.SkipLabel(bounds) != true) return true;
        __result = composer;
        return false;
    }
}

[HarmonyPatch]
internal static class DietStatsBarComposePatch
{
    private static MethodBase TargetMethod()
    {
        foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Vintagestory.API.Client.GuiComposerHelpers)))
            if (method.Name == "AddStatbar" && method.GetParameters().Length == 4
                && method.GetParameters()[1].ParameterType == typeof(ElementBounds)) return method;
        throw new MissingMethodException("GuiComposerHelpers.AddStatbar");
    }

    [HarmonyPrefix]
    private static bool Prefix(GuiComposer composer, ElementBounds bounds, string key, ref GuiComposer __result)
    {
        if (DietStatsPresentation.Current?.SkipBar(bounds, key) != true) return true;
        __result = composer;
        return false;
    }
}

[HarmonyPatch(typeof(ElementBounds), nameof(ElementBounds.FixedUnder))]
internal static class DietStatsPhysicalLayoutPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref ElementBounds __result) => DietStatsPresentation.Current?.CompactPhysical(ref __result);
}

[HarmonyPatch(typeof(CharacterExtraDialogs), "UpdateStatBars")]
internal static class DietStatsBarUpdatePatch
{
    [HarmonyPrefix]
    private static bool Prefix(CharacterExtraDialogs __instance) => !DietStatsPresentation.RebuildIfChanged(__instance)
        && DietStatsPresentation.UpdateBars(__instance);
}

[HarmonyPatch(typeof(CharacterExtraDialogs), "UpdateStats")]
internal static class DietStatsUpdatePatch
{
    [HarmonyPrefix]
    private static bool Prefix(CharacterExtraDialogs __instance) => !DietStatsPresentation.RebuildIfChanged(__instance);
}

internal sealed class DietStatsLayout
{
    private static readonly EnumFoodCategory[] Categories =
        [EnumFoodCategory.Fruit, EnumFoodCategory.Vegetable, EnumFoodCategory.Grain, EnumFoodCategory.Protein, EnumFoodCategory.Dairy];

    private readonly bool[] visible;
    private int labelIndex;
    private int visibleLabels;
    private double? labelBaseY;
    private double? barBaseY;
    private bool compactedPhysical;

    internal string Key { get; }

    private DietStatsLayout(EntityPlayer player, CompiledDiet diet)
    {
        visible = new bool[Categories.Length];
        int mask = 0;
        for (int i = 0; i < Categories.Length; i++)
        {
            visible[i] = diet.Categories.TryGetValue(Categories[i], out CompiledCategory category) && category.Capacity > 0f;
            if (visible[i]) mask |= 1 << i;
        }
        Key = $"{player.EntityId}:{mask}";
    }

    internal static DietStatsLayout? Create(EntityPlayer? player)
    {
        if (player?.Api == null) return null;
        DietRuntimeSnapshot snapshot = DietRuntimeSnapshot.For(player.Api);
        if (!snapshot.Config.EnableDietSystem || snapshot.Revision == 0) return null;
        CompiledDiet? diet = DietIdResolver.ResolveDiet(player, snapshot);
        if (diet == null) return null;
        var layout = new DietStatsLayout(player, diet);
        return layout.visible[0] && layout.visible[1] && layout.visible[2] && layout.visible[3] && layout.visible[4]
            ? null : layout;
    }

    internal bool SkipLabel(ElementBounds bounds)
    {
        if (IsNutritionTitle(bounds)) return !HasVisible();
        if (!IsNutritionRow(bounds) || labelIndex >= Categories.Length) return false;

        int index = labelIndex++;
        labelBaseY ??= bounds.fixedY;
        if (!visible[index]) return true;

        bounds.fixedY = labelBaseY.Value + visibleLabels++ * 20d;
        return false;
    }

    internal bool SkipBar(ElementBounds bounds, string key)
    {
        int index = Array.IndexOf(new[] { "fruitBar", "vegetableBar", "grainBar", "proteinBar", "dairyBar" }, key);
        if (index < 0) return false;
        barBaseY ??= bounds.fixedY;
        if (!visible[index]) return true;

        int previousVisible = 0;
        for (int i = 0; i < index; i++) if (visible[i]) previousVisible++;
        bounds.fixedY = barBaseY.Value + previousVisible * 20d;
        return false;
    }

    internal void CompactPhysical(ref ElementBounds bounds)
    {
        if (compactedPhysical || labelIndex != Categories.Length || labelBaseY == null) return;
        compactedPhysical = true;
        bounds.fixedY = visibleLabels > 0
            ? labelBaseY.Value + (visibleLabels - 1) * 20d + 15d
            : labelBaseY.Value - 5d;
    }

    internal bool IsVisible(string key) => key switch
    {
        "fruitBar" => visible[0],
        "vegetableBar" => visible[1],
        "grainBar" => visible[2],
        "proteinBar" => visible[3],
        "dairyBar" => visible[4],
        _ => false
    };

    private bool HasVisible() => visible[0] || visible[1] || visible[2] || visible[3] || visible[4];

    private static bool IsNutritionTitle(ElementBounds bounds) =>
        bounds.fixedX == 0d && bounds.fixedWidth == 200d && bounds.fixedY == 25d;

    private static bool IsNutritionRow(ElementBounds bounds) =>
        bounds.fixedX == 0d && bounds.fixedWidth == 90d && bounds.fixedHeight == 20d
        && bounds.fixedY >= 45d && bounds.fixedY <= 125d;
}

internal static class DietStatsPresentation
{
    [ThreadStatic] private static bool rebuilding;
    [ThreadStatic] internal static DietStatsLayout? Current;
    private static string? composedKey;

    internal static DietStatsLayout? For(CharacterExtraDialogs dialogs) =>
        DietStatsLayout.Create(Player(dialogs));

    internal static void Remember(DietStatsLayout? layout) => composedKey = layout?.Key;

    internal static void SnapshotChanged(ICoreClientAPI api)
    {
        CharacterExtraDialogs? dialogs = api.ModLoader.GetModSystem<CharacterExtraDialogs>();
        if (dialogs != null) RebuildIfChanged(dialogs);
    }

    internal static bool RebuildIfChanged(CharacterExtraDialogs dialogs)
    {
        if (rebuilding) return false;
        DietStatsLayout? layout = For(dialogs);
        if (layout?.Key == composedKey) return false;
        if (!IsOpen(dialogs)) return false;

        rebuilding = true;
        try { dialogs.ComposeStatsGui(); }
        finally { rebuilding = false; }
        return true;
    }

    internal static bool UpdateBars(CharacterExtraDialogs dialogs)
    {
        DietStatsLayout? layout = For(dialogs);
        if (layout == null) return true;

        GuiComposer? composer = Composer(dialogs);
        EntityPlayer? player = Player(dialogs);
        if (composer == null || player == null || !IsOpen(dialogs)) return false;
        var hunger = player.WatchedAttributes.GetTreeAttribute("hunger");
        if (hunger == null) return false;

        float saturation = hunger.GetFloat("currentsaturation", 0f);
        float maxSaturation = hunger.GetFloat("maxsaturation", 0f);
        if (composer.GetElement("satiety") is GuiElementDynamicText satietyText)
            satietyText.SetNewText((int)Math.Round(saturation) + " / " + (int)Math.Round(maxSaturation), false, false, false);

        UpdateBar(composer, layout, "fruitBar", hunger.GetFloat("fruitLevel", 0f), maxSaturation);
        UpdateBar(composer, layout, "vegetableBar", hunger.GetFloat("vegetableLevel", 0f), maxSaturation);
        UpdateBar(composer, layout, "grainBar", hunger.GetFloat("grainLevel", 0f), maxSaturation);
        UpdateBar(composer, layout, "proteinBar", hunger.GetFloat("proteinLevel", 0f), maxSaturation);
        UpdateBar(composer, layout, "dairyBar", hunger.GetFloat("dairyLevel", 0f), maxSaturation);
        return false;
    }

    private static void UpdateBar(GuiComposer composer, DietStatsLayout layout, string key, float value, float max)
    {
        if (!layout.IsVisible(key) || composer.GetElement(key) is not GuiElementStatbar bar) return;
        bar.SetLineInterval(max / 10f);
        bar.SetValues(value, 0f, max);
    }

    private static EntityPlayer? Player(CharacterExtraDialogs dialogs) =>
        (AccessTools.Field(typeof(CharacterExtraDialogs), "capi")?.GetValue(dialogs) as ICoreClientAPI)?.World.Player?.Entity;

    private static GuiComposer? Composer(CharacterExtraDialogs dialogs) =>
        (AccessTools.Field(typeof(CharacterExtraDialogs), "dlg")?.GetValue(dialogs) as GuiDialogCharacterBase)?.Composers["playerstats"];

    private static bool IsOpen(CharacterExtraDialogs dialogs) =>
        (AccessTools.Field(typeof(CharacterExtraDialogs), "dlg")?.GetValue(dialogs) as GuiDialog)?.IsOpened() == true;
}
