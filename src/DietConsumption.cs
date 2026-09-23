using System;
using System.Collections.Generic;
using dietsetup.Composition;
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
    internal Queue<DietContributionSet> Pending { get; } = new();
    internal Queue<DietIngredientTrace> TraceQueue { get; } = new();
    internal DietIngredientTrace? ActiveTrace;
    internal readonly List<DietIngredientTrace> Traces = new();
    private readonly List<DietContributionSet> credited = new();
    internal ItemStack? Stack;
    internal int InitialCount;
    internal int RemovedLiquid;
    internal bool CapturingMeal;
    /// <summary>Shared by every contribution of one physical mouthful, including ones enqueued by a
    /// nested delivery site, so a winning rule's effects fire once for it.</summary>
    internal object? CreditGroup;
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

    /// <summary>An already-open scope for the same entity, so a delivery site nested inside another one
    /// (ACA credits its expanded rows before the base item) shares one snapshot, queue and transaction.</summary>
    internal static DietConsumption? Join(EntityAgent entity) =>
        current is { } scope && ReferenceEquals(scope.Entity, entity) ? scope : null;

    internal bool TryCredit(EntityAgent entity, out float multiplier)
    {
        multiplier = 1f;
        ActiveTrace = null;
        if (!ReferenceEquals(Entity, entity) || !Pending.TryDequeue(out var set)) return false;
        ActiveTrace = TraceQueue.TryDequeue(out var row) ? row : null;
        if (ActiveTrace != null) Traces.Add(ActiveTrace);
        multiplier = set.Nutrition();
        credited.Add(set);
        return true;
    }

    internal void Confirm(bool consumed)
    {
        if (Snapshot.Config.RecordLastConsumption)
            Entity.Api.ModLoader.GetModSystem<DietSetupModSystem>().RecordConsumption(Entity.EntityId,
                $"consumed={consumed}\n" + string.Join("\n", Traces.ConvertAll(row => row.Format())));
        if (!consumed) return;
        // One physical mouthful fires a winning rule's effects once however many portions it was split into.
        var fired = new HashSet<(object, string)>();
        foreach (var set in credited)
            foreach (var component in set.Components)
                if (fired.Add((set.Group, component.Result.WinningRule)))
                    DietEffectRunner.Fire(Entity.Api, Entity, component.Result);
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
