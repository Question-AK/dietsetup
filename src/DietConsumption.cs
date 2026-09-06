using System;
using System.Collections.Generic;
using dietsetup.Rules;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace dietsetup;

internal sealed class DietConsumption : IDisposable
{
    [ThreadStatic] private static DietConsumption? current;
    internal static DietConsumption? Current => current;
    private readonly DietConsumption? previous;
    internal EntityAgent Entity { get; }
    internal DietRuntimeSnapshot Snapshot { get; }
    internal Queue<DietResolveResult> Pending { get; } = new();
    internal Queue<DietIngredientTrace> TraceQueue { get; } = new();
    internal DietIngredientTrace? ActiveTrace;
    internal readonly List<DietIngredientTrace> Traces = new();
    private readonly List<DietResolveResult> credited = new();
    internal ItemStack? Stack;
    internal int InitialCount;
    internal int RemovedLiquid;
    internal bool CapturingMeal;
    internal bool CapturingLiquid;

    private DietConsumption(EntityAgent entity)
    {
        Entity = entity;
        Snapshot = DietRuntimeSnapshot.For(entity.Api);
        previous = current;
        current = this;
    }

    internal static DietConsumption? Begin(EntityAgent entity)
    {
        if (entity.World.Side != EnumAppSide.Server || !DietRuntimeSnapshot.For(entity.Api).Config.EnableDietSystem)
            return null;
        return new DietConsumption(entity);
    }

    internal bool TryCredit(EntityAgent entity, out float multiplier)
    {
        multiplier = 1f;
        ActiveTrace = null;
        if (!ReferenceEquals(Entity, entity) || !Pending.TryDequeue(out var result)) return false;
        ActiveTrace = TraceQueue.TryDequeue(out var row) ? row : null;
        if (ActiveTrace != null) Traces.Add(ActiveTrace);
        multiplier = result.Nutrition;
        credited.Add(result);
        return true;
    }

    internal void Confirm(bool consumed)
    {
        if (Snapshot.Config.RecordLastConsumption)
            Entity.Api.ModLoader.GetModSystem<DietSetupModSystem>().RecordConsumption(Entity.EntityId,
                $"consumed={consumed}\n" + string.Join("\n", Traces.ConvertAll(row => row.Format())));
        if (!consumed) return;
        foreach (var result in credited) DietEffectRunner.Fire(Entity.Api, Entity, result);
    }

    public void Dispose()
    {
        DietSpoilageResolution.ClearCache();
        Pending.Clear();
        credited.Clear();
        TraceQueue.Clear();
        Traces.Clear();
        ActiveTrace = null;
        if (ReferenceEquals(current, this)) current = previous;
    }
}
