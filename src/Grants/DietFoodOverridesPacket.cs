using System;
using System.Collections.Generic;
using ProtoBuf;
using Vintagestory.API.Common;

namespace dietsetup.Grants;

/// <summary>Wire shape for the server's resolved grant table (architecture 7.6, dedicated-multiplayer
/// gap). Carries the winning row per collectible, not raw food-overrides.json patterns -- wildcard
/// matching and specificity tie-breaking already ran once, server-side, against the server's own
/// api.World.Collectibles (Standing rule 8: one resolve, one owner). Parallel arrays, not a
/// protobuf-net dictionary, matching DietBindingsPacket's shape.</summary>
[ProtoContract]
public class DietFoodOverridesPacket
{
    [ProtoMember(1)] public string[] ItemCodes = Array.Empty<string>();
    [ProtoMember(2)] public string[] Categories = Array.Empty<string>();
    [ProtoMember(3)] public float[] BaseSatiety = Array.Empty<float>();

    /// <summary>Encoded FoodAccessRule per row. An empty array means a sender that predates material
    /// permissions, which is unrestricted; a short one is a malformed packet, not a permissive one.</summary>
    [ProtoMember(4)] public string[] Access = Array.Empty<string>();

    public static DietFoodOverridesPacket From(IReadOnlyList<(CollectibleObject Collectible, EnumFoodCategory Category, float BaseSatiety, FoodAccessRule Access)> rows)
    {
        var itemCodes = new string[rows.Count];
        var categories = new string[rows.Count];
        var baseSatiety = new float[rows.Count];
        var access = new string[rows.Count];

        for (int i = 0; i < rows.Count; i++)
        {
            itemCodes[i] = rows[i].Collectible.Code.ToString();
            categories[i] = rows[i].Category.ToString();
            baseSatiety[i] = rows[i].BaseSatiety;
            access[i] = rows[i].Access.Encode();
        }

        return new DietFoodOverridesPacket
        {
            ItemCodes = itemCodes, Categories = categories, BaseSatiety = baseSatiety, Access = access
        };
    }
}
