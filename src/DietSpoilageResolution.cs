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

    /// <summary><paramref name="vanillaSatLoss"/> is vanilla's own spoilage multiplier, known only inside
    /// the FoodSpoilageSatLossMul postfix. Later callers in the same mouthful hit the cache, so the
    /// contribution weights are computed once from the real value.</summary>
    internal static bool TryResolve(float spoilState, ItemStack? stack, EntityAgent? entity,
        out DietContributionSet set, float vanillaSatLoss = 1f)
    {
        set = DietContributionSet.Single(Neutral, 0, vanillaSatLoss);
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
            return true;
        }
        set = snapshot.Composition.Build(snapshot.Tags, diet, stack.Collectible, mask, spoilState, vanillaSatLoss,
            DietAcaIntegration.HasRealContributions(stack));
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
