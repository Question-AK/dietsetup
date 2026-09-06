using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using dietsetup.Binding;
using Newtonsoft.Json;
using dietsetup.Rules;
using dietsetup.Tags;
using Vintagestory.API.Common;

namespace dietsetup;

internal sealed class DietRuntimeSnapshot
{
    internal static readonly DietRuntimeSnapshot Empty = new(new DietSetupConfig(), new FoodTagRegistry(),
        new Dictionary<string, CompiledDiet>(), new BindingsFile(), 0, "", "");

    internal DietSetupConfig Config { get; }
    internal FoodTagRegistry Tags { get; }
    internal IReadOnlyDictionary<string, CompiledDiet> Diets { get; }
    internal BindingsFile Bindings { get; }
    internal long Revision { get; }
    internal string Hash { get; }
    internal string Payload { get; }

    internal DietRuntimeSnapshot(DietSetupConfig config, FoodTagRegistry tags,
        Dictionary<string, CompiledDiet> diets, BindingsFile bindings, long revision, string hash, string payload)
    {
        config.Validate();
        tags.Freeze();
        Config = JsonConvert.DeserializeObject<DietSetupConfig>(JsonConvert.SerializeObject(config))!;
        Tags = tags;
        Diets = new ReadOnlyDictionary<string, CompiledDiet>(new Dictionary<string, CompiledDiet>(diets));
        Bindings = JsonConvert.DeserializeObject<BindingsFile>(JsonConvert.SerializeObject(bindings))!;
        Revision = revision;
        Hash = hash;
        Payload = payload;
    }

    internal CompiledDiet? GetDiet(string id) => Diets.TryGetValue(id, out var diet) ? diet : null;

    [ThreadStatic] private static ReadScope? activeRead;

    internal static IDisposable Read(ICoreAPI api) => new ReadScope(api, For(api));

    private sealed class ReadScope : IDisposable
    {
        internal readonly ICoreAPI Api;
        internal readonly DietRuntimeSnapshot Snapshot;
        private readonly ReadScope? previous;
        internal ReadScope(ICoreAPI api, DietRuntimeSnapshot snapshot)
        {
            Api = api;
            Snapshot = snapshot;
            previous = activeRead;
            activeRead = this;
        }
        public void Dispose() => activeRead = previous;
    }

    internal static DietRuntimeSnapshot For(ICoreAPI? api)
    {
        if (activeRead is { } read && (api == null || ReferenceEquals(read.Api, api))) return read.Snapshot;
        // An eat keeps one revision even if a reload publishes while vanilla is crediting ingredients.
        if (DietConsumption.Current is { } operation && ReferenceEquals(operation.Entity.Api, api))
            return operation.Snapshot;
        return api?.ModLoader.GetModSystem<DietSetupModSystem>()?.Snapshot ?? Empty;
    }
}
