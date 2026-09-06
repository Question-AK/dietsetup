using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.GameContent;

namespace dietsetup;

// Keep vanilla's branch, replacing its condition so live config changes also restore the full-stomach guard.
[HarmonyPatch(typeof(EntityBehaviorHunger), nameof(EntityBehaviorHunger.OnEntityReceiveSaturation))]
public static class DietNutritionGuardTranspiler
{
    internal static bool KeepGuard(bool full, EntityBehaviorHunger hunger) =>
        full && !DietRuntimeSnapshot.For(hunger.entity.Api).Config.EnableDietSystem;

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = new List<CodeInstruction>(instructions);
        int count = 0;
        for (int i = 0; i < code.Count - 1; i++)
        {
            if (code[i].opcode != OpCodes.Ldloc_1 || (code[i + 1].opcode != OpCodes.Brtrue && code[i + 1].opcode != OpCodes.Brtrue_S)) continue;
            code.InsertRange(i + 1, new[] { new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DietNutritionGuardTranspiler), nameof(KeepGuard))) });
            i += 2;
            count++;
        }
        // Five category guards and local 1 are verified against 1.22.6; reject a different engine layout.
        if (count != 5) throw new InvalidOperationException($"Expected 5 full-stomach guards, found {count}.");
        return code;
    }
}
