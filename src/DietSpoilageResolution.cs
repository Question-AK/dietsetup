using System;
using dietsetup.Binding;
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
    [ThreadStatic] private static DietResolveResult cachedResult;
    [ThreadStatic] private static ItemStack? pie;
    [ThreadStatic] private static int scopeDepth;
    [ThreadStatic] private static ulong cachedMask;

    internal static void ClearCache()
    {
        cachedStack = null; cachedEntity = null; cachedSnapshot = null; cachedDiet = null; cachedPie = null;
    }

    internal static IDisposable PieContext(ItemStack? stack)
    {
        var previous = pie;
        pie = stack;
        scopeDepth++;
        return new RestoreContext(() => { pie = previous; scopeDepth--; ClearCache(); });
    }

    internal static bool TryResolve(float spoilState, ItemStack? stack, EntityAgent? entity, out DietResolveResult result)
    {
        result = new DietResolveResult(DietVerdict.Edible, 1f, 1f, Array.Empty<CompiledEffect>(), false);
        var snapshot = DietRuntimeSnapshot.For(entity?.Api);
        if (!snapshot.Config.EnableDietSystem || stack?.Collectible == null) return false;
        var diet = DietIdResolver.ResolveDiet(entity, snapshot);
        if (diet == null) return false;
        ulong mask = pie != null
            ? snapshot.Tags.GetPieFillingTagMask(stack.Collectible, pie.Collectible, spoilState)
            : snapshot.Tags.GetTagMaskForSpoilState(stack.Collectible, spoilState);
        if (cachedMask == mask && ReferenceEquals(cachedStack, stack) && ReferenceEquals(cachedEntity, entity)
            && ReferenceEquals(cachedSnapshot, snapshot) && ReferenceEquals(cachedDiet, diet)
            && ReferenceEquals(cachedPie, pie) && cachedSpoil == spoilState)
        {
            result = cachedResult;
            return true;
        }
        result = DietResolver.Resolve(diet, mask, spoilState);
        if (scopeDepth == 0 && DietConsumption.Current == null) return true;
        cachedMask = mask;
        cachedStack = stack;
        cachedEntity = entity;
        cachedSnapshot = snapshot;
        cachedDiet = diet;
        cachedPie = pie;
        cachedSpoil = spoilState;
        cachedResult = result;
        return true;
    }

    internal static float ApplySatiety(float vanilla, DietResolveResult result) =>
        result.ReplacesSpoilage ? result.Satiety : vanilla * result.Satiety;

    internal static float ApplyHealth(float vanilla, DietResolveResult result) =>
        result.ReplacesSpoilage ? Math.Min(1f, result.Satiety) : vanilla * Math.Min(1f, result.Satiety);

    private sealed class RestoreContext(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
