using System;
using dietsetup.Binding;
using dietsetup.Composition;
using dietsetup.Rules;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace dietsetup;

internal static class DietSpoilageResolution
{
    [ThreadStatic] private static ItemStack? cachedStack;
    [ThreadStatic] private static EntityAgent? cachedEntity;
    [ThreadStatic] private static DietRuntimeSnapshot? cachedSnapshot;
    [ThreadStatic] private static CompiledDiet? cachedDiet;
    [ThreadStatic] private static float cachedSpoil;
    [ThreadStatic] private static ItemStack? cachedPie;
    [ThreadStatic] private static DietContributionSet? cachedResult;
    [ThreadStatic] private static ItemStack? pie;
    [ThreadStatic] private static int scopeDepth;
    [ThreadStatic] private static ulong cachedMask;

    internal static void ClearCache()
    {
        cachedStack = null; cachedEntity = null; cachedSnapshot = null; cachedDiet = null; cachedPie = null;
        cachedResult = null;
    }

    internal static IDisposable PieContext(ItemStack? stack)
    {
        var previous = pie;
        pie = stack;
        scopeDepth++;
        return new RestoreContext(() => { pie = previous; scopeDepth--; ClearCache(); });
    }

    /// <summary>Memoises one mouthful outside a consumption, so a preview resolves the same set every
    /// query instead of rebuilding one whose spoilage weight nothing has observed yet.</summary>
    internal static IDisposable ResolveScope()
    {
        scopeDepth++;
        return new RestoreContext(() => { scopeDepth--; ClearCache(); });
    }

    /// <summary><paramref name="vanillaSatLoss"/> is vanilla's own spoilage multiplier, known only inside
    /// the FoodSpoilageSatLossMul postfix. It is recorded on the set rather than baked into it, so the
    /// weighting no longer depends on whether health, satiety or a preview asked first.</summary>
    internal static bool TryResolve(float spoilState, ItemStack? stack, EntityAgent? entity,
        out DietContributionSet set, float? vanillaSatLoss = null)
    {
        set = DietContributionSet.Single(Neutral, 0);
        var snapshot = DietRuntimeSnapshot.For(entity?.Api);
        if (!snapshot.Config.EnableDietSystem || stack?.Collectible == null) return false;
        var diet = DietIdResolver.ResolveDiet(entity, snapshot);
        if (diet == null) return false;
        ulong mask = pie != null
            ? snapshot.Tags.GetPieFillingTagMask(stack.Collectible, pie.Collectible, spoilState)
            : snapshot.Tags.GetTagMaskForSpoilState(stack.Collectible, spoilState);
        if (cachedResult != null && cachedMask == mask && ReferenceEquals(cachedStack, stack) && ReferenceEquals(cachedEntity, entity)
            && ReferenceEquals(cachedSnapshot, snapshot) && ReferenceEquals(cachedDiet, diet)
            && ReferenceEquals(cachedPie, pie) && cachedSpoil == spoilState)
        {
            set = cachedResult;
            if (vanillaSatLoss is { } observed) set.Spoilage.Capture(observed);
            return true;
        }
        set = snapshot.Composition.Build(snapshot.Tags, diet, stack.Collectible, mask, spoilState,
            DietSourceAttributionRegistry.For(stack));
        if (vanillaSatLoss is { } vanilla) set.Spoilage.Capture(vanilla);
        if (scopeDepth == 0 && DietConsumption.Current == null) return true;
        cachedMask = mask;
        cachedStack = stack;
        cachedEntity = entity;
        cachedSnapshot = snapshot;
        cachedDiet = diet;
        cachedPie = pie;
        cachedSpoil = spoilState;
        cachedResult = set;
        return true;
    }

    internal static float ApplySatiety(float vanilla, DietResolveResult result) =>
        result.ReplacesSpoilage ? result.Satiety : vanilla * result.Satiety;

    internal static float ApplyHealth(float vanilla, DietResolveResult result) =>
        result.ReplacesSpoilage ? Math.Min(1f, result.Satiety) : vanilla * Math.Min(1f, result.Satiety);

    internal static readonly DietResolveResult Neutral =
        new(DietVerdict.Edible, 1f, 1f, Array.Empty<CompiledEffect>(), false);

    private sealed class RestoreContext(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
