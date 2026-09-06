using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using dietsetup.Binding;
using dietsetup.Grants;
using dietsetup.Rules;
using dietsetup.Tags;
using Newtonsoft.Json;
using ProtoBuf;

namespace dietsetup;

[ProtoContract]
public sealed class DietConfigurationPacket
{
    [ProtoMember(1)] public long Revision;
    [ProtoMember(2)] public string Hash = "";
    [ProtoMember(3)] public string Payload = "";

    internal static DietConfigurationPacket From(DietRuntimeSnapshot snapshot) => new()
    {
        Revision = snapshot.Revision, Hash = snapshot.Hash, Payload = snapshot.Payload
    };

    internal static string ComputeHash(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    internal EffectiveDietConfiguration Read()
    {
        if (Revision <= 0 || !string.Equals(Hash, ComputeHash(Payload), StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid diet configuration revision or hash.");
        return JsonConvert.DeserializeObject<EffectiveDietConfiguration>(Payload)
            ?? throw new InvalidOperationException("Empty diet configuration payload.");
    }
}

internal sealed class EffectiveDietConfiguration
{
    public DietSetupConfig Config { get; set; } = new();
    public FoodTagConfigFile Tags { get; set; } = new();
    public Dictionary<string, DietDocumentFile> Diets { get; set; } = new();
    public Dictionary<string, string> Domains { get; set; } = new();
    public BindingsFile Bindings { get; set; } = new();
    public DietFoodOverridesPacket Grants { get; set; } = new();
}
