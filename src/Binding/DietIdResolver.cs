using dietsetup.Rules;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace dietsetup.Binding;

public static class DietIdResolver
{
    public const string OverrideAttribute = "dietsetup:dietOverride";
    public const string DefaultDietId = "base";
    public const string ClearKeyword = "clear";
    internal static CompiledDiet? ResolveDiet(Entity? forEntity, DietRuntimeSnapshot? snapshot = null)
    {
        snapshot ??= DietRuntimeSnapshot.For(forEntity?.Api);
        string dietId = ResolveCore(forEntity, out _, out _, snapshot);
        return snapshot.GetDiet(dietId) ?? snapshot.GetDiet(DefaultDietId);
    }

    public enum ResolvePath { ExplicitOverride, RaceTrait, Default }

    public static string Resolve(Entity? forEntity) => ResolveCore(forEntity, out _, out _);
    public static string ResolveDetailed(Entity? forEntity, out ResolvePath path, out string? matchedTrait) =>
        ResolveCore(forEntity, out path, out matchedTrait);

    private static string ResolveCore(Entity? forEntity, out ResolvePath path, out string? matchedTrait, DietRuntimeSnapshot? snapshot = null)
    {
        path = ResolvePath.Default;
        matchedTrait = null;
        if (forEntity == null) return DefaultDietId;

        string? overrideId = forEntity.WatchedAttributes?.GetString(OverrideAttribute);
        if (!string.IsNullOrEmpty(overrideId))
        {
            path = ResolvePath.ExplicitOverride;
            return overrideId;
        }
        BindingsFile bindings = (snapshot ?? DietRuntimeSnapshot.For(forEntity.Api)).Bindings;

        if (forEntity is EntityPlayer entityPlayer && entityPlayer.Player is IPlayer iplayer)
        {
            foreach ((string traitCode, string dietId) in bindings.Bindings)
            {
                if (HasTrait(iplayer, traitCode))
                {
                    path = ResolvePath.RaceTrait;
                    matchedTrait = traitCode;
                    return dietId;
                }
            }
        }

        return bindings.Default ?? DefaultDietId;
    }
    private static bool HasTrait(IPlayer iplayer, string traitCode)
    {
        if (iplayer.Entity?.Api == null) return false;

        string charClass = iplayer.Entity.WatchedAttributes.GetString("characterClass");
        // CharacterSystem.HasTrait can dereference an unassigned character class during join.
        if (string.IsNullOrEmpty(charClass)) return false;

        CharacterSystem? charSys = iplayer.Entity.Api.ModLoader.GetModSystem<CharacterSystem>();
        return charSys != null && charSys.HasTrait(iplayer, traitCode);
    }
}
