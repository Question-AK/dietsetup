using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace dietsetup;

/// <summary>
/// Public API: dietsetup writes a decaying per-player "intake" counter for how much of a food
/// tag a player has recently eaten, exposed as WatchedAttributes keys any mod can read by
/// string, no assembly reference required --
///   "dietsetup:intake:&lt;tag&gt;"              double, 0..DietSetupConfig.RotIntakeCap, unitless
///   "dietsetup:intake:&lt;tag&gt;:updatedHours"  double, world.Calendar.TotalHours at last write
/// Only "rot" is written in v1 (Phase G3, for rfmechanics' goblin rot aura). Renaming either key
/// shape breaks that consumer -- see README.md.
///
/// Shared accrual formula for the two patches below. Decay-then-add on the in-game calendar
/// clock. Details: notes/dietsetup-patch-internals.md#rot-intake-accrual--rotintakeaccrualpatchcs.
/// </summary>
internal static class RotIntakeAccrual
{
    private const string Tag = "rot";
    private const double DefaultHalfLifeHours = 48.0;
    private static bool loggedCaptureException;

    internal static void LogCaptureFailure(EntityAgent byEntity, Exception ex)
    {
        if (loggedCaptureException) return;
        loggedCaptureException = true;
        byEntity?.Api?.Logger?.Warning("[dietsetup] Rot intake capture failed; this bite grants no intake, later bites will retry: {0}", ex);
    }

    /// <summary>transitionLevel &lt;= 0 (fresh food) contributes nothing and skips the write
    /// entirely -- eating fresh food should not decay the accumulator faster than time alone
    /// already does.</summary>
    public static void AccrueRotIntake(EntityPlayer player, float transitionLevel)
    {
        if (transitionLevel <= 0f) return;

        DietSetupConfig cfg = DietSetupModSystem.Config;
        if (!cfg.EnableRotIntakeTracking) return;

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

/// <summary>Standalone eating -- captures vanilla's spoilage input before consumption and
/// confirms the original stack lost exactly one item, including the final item in a slot.
/// No nutrition query or consumed-slot read: this evidence is local to one invocation.</summary>
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
            if (!DietSetupModSystem.Config.EnableRotIntakeTracking) return;

            if (slot == null) return;
            ItemStack? stack = slot.Itemstack;
            if (stack == null || stack.StackSize <= 0 || !ReferenceEquals(stack.Collectible, __instance)) return;
            int initialCount = stack.StackSize;
            TransitionState? state = __instance.UpdateAndGetTransitionState(byEntity.World, slot, EnumTransitionType.Perish);
            if (state == null || !float.IsFinite(state.TransitionLevel)) return;
            // A transition read may itself replace a spoiled stack. That is not eating it.
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

/// <summary>
/// Meal eating -- captures intake before tryFinishEatMeal can replace the food with an empty
/// container (BlockMeal never calls tryEatStop). Averages TransitionLevel across the pot's contents since cooking pools freshness
/// before it's stamped on stacks -- known, accepted limitation. Details:
/// notes/dietsetup-patch-internals.md#rot-intake-meal--rotintakeaccrualpatchcs-rotintakemealeatpatch.
/// BlockPie is the one exception: its fillings are held permanently fresh by UnspoilContents, so it
/// accrues from the pie's own Perish level instead of averaging the permanently fresh fillings.
/// </summary>
[HarmonyPatch(typeof(BlockMeal), "tryFinishEatMeal")]
public static class RotIntakeMealEatPatch
{
    [HarmonyPrefix]
    public static void Prefix(BlockMeal __instance, float secondsUsed, ItemSlot slot, EntityAgent byEntity,
        out (ItemStack Stack, CollectibleObject Collectible, float TransitionLevel)? __state)
    {
        __state = null;
        try
        {
            if (byEntity is not EntityPlayer || byEntity.World is not IServerWorldAccessor || secondsUsed < 1.45) return;
            if (!DietSetupModSystem.Config.EnableRotIntakeTracking) return;

            if (slot == null) return;
            ItemStack? stack = slot.Itemstack;
            if (stack == null || stack.StackSize <= 0 || !ReferenceEquals(stack.Collectible, __instance)) return;
            int initialCount = stack.StackSize;
            float transitionLevel;

            // Read the pie's own Perish clock, not its permanently unspoiled fillings.
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
    public static void Postfix(EntityAgent byEntity, bool __result, bool __runOriginal,
        (ItemStack Stack, CollectibleObject Collectible, float TransitionLevel)? __state)
    {
        if (!__runOriginal || !__result || __state is not { } evidence) return;
        if (byEntity is not EntityPlayer player || byEntity.World is not IServerWorldAccessor) return;
        if (!ReferenceEquals(evidence.Stack.Collectible, evidence.Collectible)) return;

        RotIntakeAccrual.AccrueRotIntake(player, evidence.TransitionLevel);
    }
}
