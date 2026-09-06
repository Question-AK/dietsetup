using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace dietsetup;
internal static class RotIntakeAccrual
{
    private const string Tag = "rot";
    private const double DefaultHalfLifeHours = 48.0;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ICoreAPI, object> loggedCaptureExceptions = new();

    internal static void LogCaptureFailure(EntityAgent byEntity, Exception ex)
    {
        if (byEntity?.Api == null) return;
        lock (loggedCaptureExceptions)
        {
            if (loggedCaptureExceptions.TryGetValue(byEntity.Api, out _)) return;
            loggedCaptureExceptions.Add(byEntity.Api, new object());
        }
        byEntity?.Api?.Logger?.Warning("[dietsetup] Rot intake capture failed; this bite grants no intake, later bites will retry: {0}", ex);
    }
    public static void AccrueRotIntake(EntityPlayer player, float transitionLevel)
    {
        if (transitionLevel <= 0f) return;

        DietSetupConfig cfg = DietRuntimeSnapshot.For(player.Api).Config;
        if (!cfg.EnableDietSystem || !cfg.EnableRotIntakeTracking) return;

        ITreeAttribute wa = player.WatchedAttributes;
        double nowHours = player.World.Calendar.TotalHours;
        string valueKey = DietSetupModSystem.AttrIntake(Tag);
        string updatedKey = DietSetupModSystem.AttrIntakeUpdatedHours(Tag);
        double lastHours = wa.GetDouble(updatedKey, nowHours);
        double raw = wa.GetDouble(valueKey, 0.0);

        double halfLife = cfg.IntakeHalfLifeHours.TryGetValue(Tag, out double h) ? h : DefaultHalfLifeHours;
        double decayed = raw * Math.Pow(0.5, Math.Max(0.0, nowHours - lastHours) / halfLife);
        double next = Math.Min(cfg.RotIntakeCap, decayed + cfg.RotIntakePerBite * transitionLevel);

        wa.SetDouble(valueKey, next);
        wa.SetDouble(updatedKey, nowHours);
    }
}
[HarmonyPatch(typeof(CollectibleObject), "tryEatStop")]
public static class RotIntakeStandaloneEatPatch
{
    [HarmonyPrefix]
    public static void Prefix(CollectibleObject __instance, float secondsUsed, ItemSlot slot, EntityAgent byEntity,
        out (ItemStack Stack, CollectibleObject Collectible, int InitialCount, float TransitionLevel)? __state)
    {
        __state = null;
        try
        {
            if (byEntity is not EntityPlayer || byEntity.World is not IServerWorldAccessor || secondsUsed < 0.95f) return;
            if (!DietRuntimeSnapshot.For(byEntity.Api).Config.EnableDietSystem || !DietRuntimeSnapshot.For(byEntity.Api).Config.EnableRotIntakeTracking) return;

            if (slot == null) return;
            ItemStack? stack = slot.Itemstack;
            if (stack == null || stack.StackSize <= 0 || !ReferenceEquals(stack.Collectible, __instance)) return;
            int initialCount = stack.StackSize;
            TransitionState? state = __instance.UpdateAndGetTransitionState(byEntity.World, slot, EnumTransitionType.Perish);
            if (state == null || !float.IsFinite(state.TransitionLevel)) return;
            if (!ReferenceEquals(slot.Itemstack, stack) || !ReferenceEquals(stack.Collectible, __instance)
                || stack.StackSize != initialCount) return;

            __state = (stack, __instance, initialCount, state.TransitionLevel);
        }
        catch (Exception ex)
        {
            RotIntakeAccrual.LogCaptureFailure(byEntity, ex);
        }
    }

    [HarmonyPostfix]
    public static void Postfix(float secondsUsed, EntityAgent byEntity, bool __runOriginal,
        (ItemStack Stack, CollectibleObject Collectible, int InitialCount, float TransitionLevel)? __state)
    {
        if (!__runOriginal || secondsUsed < 0.95f || __state is not { } evidence) return;
        if (byEntity is not EntityPlayer player || byEntity.World is not IServerWorldAccessor) return;
        if (!ReferenceEquals(evidence.Stack.Collectible, evidence.Collectible)
            || evidence.Stack.StackSize != evidence.InitialCount - 1) return;

        RotIntakeAccrual.AccrueRotIntake(player, evidence.TransitionLevel);
    }
}
[HarmonyPatch(typeof(BlockMeal), nameof(BlockMeal.Consume))]
public static class RotIntakeMealEatPatch
{
    [HarmonyPrefix]
    public static void Prefix(BlockMeal __instance, IPlayer eatingPlayer, ItemSlot inSlot,
        out (ItemStack Stack, CollectibleObject Collectible, float TransitionLevel)? __state)
    {
        var byEntity = eatingPlayer.Entity;
        var slot = inSlot;
        __state = null;
        try
        {
            if (byEntity is not EntityPlayer || byEntity.World is not IServerWorldAccessor) return;
            if (!DietRuntimeSnapshot.For(byEntity.Api).Config.EnableDietSystem || !DietRuntimeSnapshot.For(byEntity.Api).Config.EnableRotIntakeTracking) return;

            if (slot == null) return;
            ItemStack? stack = slot.Itemstack;
            if (stack == null || stack.StackSize <= 0 || !ReferenceEquals(stack.Collectible, __instance)) return;
            int initialCount = stack.StackSize;
            float transitionLevel;
            if (__instance is BlockPie)
            {
                TransitionState? pieState = __instance.UpdateAndGetTransitionState(byEntity.World, slot, EnumTransitionType.Perish);
                if (pieState == null || !float.IsFinite(pieState.TransitionLevel)) return;
                transitionLevel = pieState.TransitionLevel;
            }
            else
            {
                ItemStack[] contents = __instance.GetNonEmptyContents(byEntity.World, stack);
                if (contents.Length == 0) return;

                float total = 0f;
                int counted = 0;
                foreach (ItemStack contentStack in contents)
                {
                    if (contentStack?.Collectible == null) continue;
                    var dummySlot = new DummySlot(contentStack);
                    TransitionState? state = contentStack.Collectible.UpdateAndGetTransitionState(byEntity.World, dummySlot, EnumTransitionType.Perish);
                    if (state == null) continue;
                    if (!float.IsFinite(state.TransitionLevel)) return;
                    total += state.TransitionLevel;
                    counted++;
                }
                if (counted == 0) return;
                transitionLevel = total / counted;
            }

            if (!float.IsFinite(transitionLevel) || !ReferenceEquals(slot.Itemstack, stack)
                || !ReferenceEquals(stack.Collectible, __instance) || stack.StackSize != initialCount) return;
            __state = (stack, __instance, transitionLevel);
        }
        catch (Exception ex)
        {
            RotIntakeAccrual.LogCaptureFailure(byEntity, ex);
        }
    }

    [HarmonyPostfix]
    public static void Postfix(IPlayer eatingPlayer, float remainingServings, float __result, bool __runOriginal,
        (ItemStack Stack, CollectibleObject Collectible, float TransitionLevel)? __state)
    {
        var byEntity = eatingPlayer.Entity;
        if (!__runOriginal || __result >= remainingServings || __state is not { } evidence) return;
        if (byEntity is not EntityPlayer player || byEntity.World is not IServerWorldAccessor) return;
        if (!ReferenceEquals(evidence.Stack.Collectible, evidence.Collectible)) return;

        RotIntakeAccrual.AccrueRotIntake(player, evidence.TransitionLevel);
    }
}
