using System;
using System.Collections.Generic;
using dietsetup.Composition;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace dietsetup;

/// <summary>What a delivery site established about the mouthful. <c>Unknown</c> is not <c>Refused</c>:
/// a site that never captured the stack (no compiled diet, undetermined tag mask) cannot say a
/// credited mouthful was refused, and must not withdraw one.</summary>
internal enum DietConsumptionOutcome { Unknown, Consumed, Refused }

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
    private readonly List<Provisional> submitted = new();
    private float submittedHealth;
    private DietConsumptionOutcome outcome;
    internal ItemStack? Stack;
    internal int InitialCount;
    internal int RemovedLiquid;
    internal bool CapturingMeal;
    /// <summary>Shared by every contribution of one physical mouthful, including ones enqueued by a
    /// nested delivery site, so a winning rule's effects fire once for it.</summary>
    internal object? CreditGroup;
    internal bool CapturingLiquid;
    // One fullness decision per mouthful; a nested site for the same entity belongs to the outer mouthful.
    private readonly DietConsumption bite;
    private bool? startedFull;
    internal bool? StartedFull { get => bite.startedFull; set => bite.startedFull = value; }
    private ProportionalCredit? overflow;
    private readonly List<(EnumFoodCategory Category, float Satiety, float Gain)> overflowShare = new();

    private DietConsumption(EntityAgent entity)
    {
        Entity = entity;
        Snapshot = DietRuntimeSnapshot.For(entity.Api);
        previous = current;
        bite = previous is { } outer && ReferenceEquals(outer.Entity, entity) ? outer.bite : this;
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

    /// <summary>A delivery site can submit nourishment before the mouthful is settled: ACA credits its
    /// expanded rows, and heals, before base eating decides anything. Recording each submission lets an
    /// unconsumed transaction take back exactly what it saw credited, and nothing else.</summary>
    internal void RecordSubmission(EnumFoodCategory category, float level, float saturation, float lossDelay) =>
        submitted.Add(new Provisional(category, level, saturation, lossDelay));

    internal void RecordHealth(float delta) => submittedHealth += delta;

    /// <summary>Opened at the mouthful's first credit, after excess satiety is trimmed, so the space is the stomach
    /// the whole mouthful competes for.</summary>
    internal void OpenProportionalCredit(float space) => bite.overflow ??= new ProportionalCredit(Math.Max(0f, space));

    internal void NoteLevelBeforeCredit(EnumFoodCategory category, float level) => bite.overflow?.NoteBefore(category, level);

    /// <summary>The credit's final satiety and uncapped bar gain, as vanilla applied them after every prefix.</summary>
    internal void RecordProportionalCredit(EnumFoodCategory category, float satiety, float gain)
    {
        if (bite.overflow == null) return;
        bite.overflow.Add(category, satiety, gain);
        overflowShare.Add((category, satiety, gain));
    }

    internal void Confirm(DietConsumptionOutcome outcome)
    {
        this.outcome = outcome;
        bool consumed = outcome == DietConsumptionOutcome.Consumed;
        // Only the scope that owns the mouthful settles it, after every nested site has credited.
        string settlement = outcome != DietConsumptionOutcome.Refused && ReferenceEquals(bite, this) ? SettleProportionalCredit() : "";
        if (Snapshot.Config.RecordLastConsumption)
            Entity.Api.ModLoader.GetModSystem<DietSetupModSystem>().RecordConsumption(Entity.EntityId,
                $"consumed={consumed}{settlement}\n" + string.Join("\n", Traces.ConvertAll(row => row.Format())));
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
        if (outcome == DietConsumptionOutcome.Refused) Withdraw();
        DietSpoilageResolution.ClearCache();
        Pending.Clear();
        credited.Clear();
        TraceQueue.Clear();
        Traces.Clear();
        ActiveTrace = null;
        if (ReferenceEquals(current, this)) current = previous;
    }

    /// <summary>Scales every bar this mouthful fed by the share of its effective satiety that fitted. Applied to the
    /// uncapped gains, so neither the order of the credits nor a bar reaching its cap part-way changes the result.</summary>
    private string SettleProportionalCredit()
    {
        if (overflow is not { } credit) return "";
        // A mouthful that fits, or one with no satiety, keeps fraction 1 and divides nothing.
        if (!(credit.Satiety > credit.Space)) return $" overflow=proportional space={credit.Space:F2} satiety={credit.Satiety:F2} fraction=1";
        float fraction = credit.Space / credit.Satiety;
        if (Entity.GetBehavior<EntityBehaviorHunger>() is not { } hunger) return "";
        float max = hunger.MaxSaturation;
        bool changed = false;
        foreach (var (category, before, gain) in credit.Bars())
        {
            float level = DietDiagnostics.Level(hunger, category);
            float target = Math.Max(0f, Math.Min(max, before + gain * fraction));
            if (!(target < level)) continue;
            float kept = level > before ? (target - before) / (level - before) : 0f;
            foreach (var row in Traces)
                if (row.Category == category && row.ActualNutrition is float actual) row.ActualNutrition = actual * kept;
            SetLevel(hunger, category, target);
            changed = true;
        }
        if (changed) hunger.UpdateNutrientHealthBoost();
        if (StartedFull != true) DietEatFeedback.NoteOverflow(Entity, fraction);
        return $" overflow=proportional space={credit.Space:F2} satiety={credit.Satiety:F2} fraction={fraction:F4}";
    }

    private void Withdraw()
    {
        // A refused nested site's credits were never part of the mouthful its owner settles.
        foreach (var (category, satiety, gain) in overflowShare) bite.overflow?.Add(category, -satiety, -gain);
        overflowShare.Clear();
        if (submittedHealth != 0f && Entity.GetBehavior<EntityBehaviorHealth>() is { } health)
            health.Health -= submittedHealth;
        submittedHealth = 0f;
        if (submitted.Count == 0) return;
        var hunger = Entity.GetBehavior<EntityBehaviorHunger>();
        if (hunger == null) { submitted.Clear(); return; }
        // Reverse order, because the loss delay each submission overwrote is a running maximum.
        for (int i = submitted.Count - 1; i >= 0; i--)
        {
            var row = submitted[i];
            hunger.Saturation -= row.Saturation;
            SetLevel(hunger, row.Category, DietDiagnostics.Level(hunger, row.Category) - row.Level);
            SetLossDelay(hunger, row.Category, row.LossDelay);
        }
        submitted.Clear();
        hunger.UpdateNutrientHealthBoost();
    }

    internal static void SetLevel(EntityBehaviorHunger hunger, EnumFoodCategory category, float value)
    {
        switch (category)
        {
            case EnumFoodCategory.Fruit: hunger.FruitLevel = value; break;
            case EnumFoodCategory.Vegetable: hunger.VegetableLevel = value; break;
            case EnumFoodCategory.Protein: hunger.ProteinLevel = value; break;
            case EnumFoodCategory.Grain: hunger.GrainLevel = value; break;
            case EnumFoodCategory.Dairy: hunger.DairyLevel = value; break;
        }
    }

    private static void SetLossDelay(EntityBehaviorHunger hunger, EnumFoodCategory category, float value)
    {
        switch (category)
        {
            case EnumFoodCategory.Fruit: hunger.SaturationLossDelayFruit = value; break;
            case EnumFoodCategory.Vegetable: hunger.SaturationLossDelayVegetable = value; break;
            case EnumFoodCategory.Protein: hunger.SaturationLossDelayProtein = value; break;
            case EnumFoodCategory.Grain: hunger.SaturationLossDelayGrain = value; break;
            case EnumFoodCategory.Dairy: hunger.SaturationLossDelayDairy = value; break;
        }
    }

    private readonly record struct Provisional(EnumFoodCategory Category, float Level, float Saturation, float LossDelay);

    private sealed class ProportionalCredit
    {
        internal readonly float Space;
        internal float Satiety;
        private readonly Dictionary<EnumFoodCategory, (float Before, float Gain)> bars = new();

        internal ProportionalCredit(float space) => Space = space;

        internal void NoteBefore(EnumFoodCategory category, float level)
        {
            if (!bars.ContainsKey(category)) bars[category] = (level, 0f);
        }

        internal void Add(EnumFoodCategory category, float satiety, float gain)
        {
            Satiety += satiety;
            if (bars.TryGetValue(category, out var bar)) bars[category] = (bar.Before, bar.Gain + gain);
        }

        internal IEnumerable<(EnumFoodCategory Category, float Before, float Gain)> Bars()
        {
            foreach (var (category, bar) in bars) yield return (category, bar.Before, bar.Gain);
        }
    }
}
