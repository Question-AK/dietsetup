using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace dietsetup;

// Food health arrives through Entity.ReceiveDamage, which ACA calls before base eating decides whether
// the mouthful happens. Only the Internal source is watched: that is what vanilla eating and ACA use,
// so unrelated damage landing in the same window is never withdrawn with a refused mouthful.
[HarmonyPatch(typeof(EntityBehaviorHealth), nameof(EntityBehaviorHealth.OnEntityReceiveDamage))]
public static class DietConsumptionHealthPatch
{
    [HarmonyPrefix]
    internal static void Prefix(EntityBehaviorHealth __instance, DamageSource damageSource,
        out (DietConsumption? Operation, float Before) __state)
    {
        __state = (null, 0);
        if (damageSource?.Source != EnumDamageSource.Internal) return;
        if (!ReferenceEquals(DietConsumption.Current?.Entity, __instance.entity)) return;
        __state = (DietConsumption.Current, __instance.Health);
    }

    [HarmonyPostfix]
    internal static void Postfix(EntityBehaviorHealth __instance, bool __runOriginal,
        (DietConsumption? Operation, float Before) __state)
    {
        if (__state.Operation != null && __runOriginal)
            __state.Operation.RecordHealth(__instance.Health - __state.Before);
    }
}
