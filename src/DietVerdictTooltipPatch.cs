using System.Text;
using dietsetup.Binding;
using dietsetup.Rules;
using dietsetup.Tags;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace dietsetup;
[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetHeldItemInfo))]
public static class DietVerdictTooltipPatch
{
    [HarmonyPostfix]
    public static void Postfix(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world)
    {
        AppendVerdict(inSlot, dsc, world);
    }

    internal static void AppendVerdict(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world)
    {
        using var snapshotScope = DietRuntimeSnapshot.Read(world.Api);
        if (!DietRuntimeSnapshot.For(world.Api).Config.EnableDietSystem) return;
        if (world is not IClientWorldAccessor clientWorld) return;

        Entity? viewer = clientWorld.Player?.Entity;
        if (viewer == null) return;

        CompiledDiet? diet = DietIdResolver.ResolveDiet(viewer);
        if (diet == null) return;

        ulong tagMask = DietRuntimeSnapshot.For(world.Api).Tags.GetTagMask(world, inSlot, out float spoilLevel, out bool determined);
        if (!determined) return;

        DietResolveResult result = DietResolver.Resolve(diet, tagMask, spoilLevel);
        if (result.Verdict == DietVerdict.Edible) return;

        dsc.AppendLine(Lang.Get($"dietsetup:verdict-{result.Verdict.ToString().ToLowerInvariant()}"));
    }
}
[HarmonyPatch(typeof(BlockPie), nameof(BlockPie.GetHeldItemInfo))]
public static class BlockPieVerdictTooltipPatch
{
    [HarmonyPostfix]
    public static void Postfix(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world)
    {
        DietVerdictTooltipPatch.AppendVerdict(inSlot, dsc, world);
    }
}
