using CombatOverhaul.Utils;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace CombatOverhaul.Integration;

internal static class NightVisionSlotPatches
{
    public static void Patch(string harmonyId, ICoreClientAPI api)
    {
        Harmony harmony = new(harmonyId);
        bool patchedVanilla = PatchMethod(harmony, ResolveVanillaRenderMethod());
        bool patchedBetterJonas = false;

        if (api.ModLoader.IsModEnabled("betterjonasdevicesfixedagain") || api.ModLoader.IsModEnabled("betterjonasdevicesfixed"))
        {
            patchedBetterJonas = PatchMethod(harmony, ResolveBetterJonasApplyEffectMethod());
        }

    }

    public static void Unpatch(string harmonyId)
    {
        Harmony harmony = new(harmonyId);
        UnpatchMethod(harmony, ResolveVanillaRenderMethod(), harmonyId);
        UnpatchMethod(harmony, ResolveBetterJonasApplyEffectMethod(), harmonyId);
    }

    private static bool PatchMethod(Harmony harmony, MethodInfo? method)
    {
        if (method == null) return false;

        harmony.Patch(
            method,
            transpiler: new HarmonyMethod(typeof(NightVisionSlotPatches), nameof(InventorySlotIndexTranspiler)));
        return true;
    }

    private static void UnpatchMethod(Harmony harmony, MethodInfo? method, string harmonyId)
    {
        if (method == null) return;
        harmony.Unpatch(method, HarmonyPatchType.Transpiler, harmonyId);
    }

    private static MethodInfo? ResolveVanillaRenderMethod()
    {
        return AccessTools.Method(typeof(ModSystemNightVision), nameof(ModSystemNightVision.OnRenderFrame));
    }

    private static MethodInfo? ResolveBetterJonasApplyEffectMethod()
    {
        Type? rendererType = AccessTools.TypeByName("BetterJonasDevices.NightVisionRenderer");
        return rendererType == null ? null : AccessTools.Method(rendererType, "ApplyEffect");
    }

    private static IEnumerable<CodeInstruction> InventorySlotIndexTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> codes = new(instructions);
        MethodInfo? inventoryGetter = AccessTools.PropertyGetter(typeof(IInventory), "Item");
        MethodInfo slotIndexMethod = AccessTools.Method(typeof(NightVisionDeviceUtil), nameof(NightVisionDeviceUtil.GetNightVisionSlotIndexOrDefault))
            ?? throw new MissingMethodException(nameof(NightVisionDeviceUtil.GetNightVisionSlotIndexOrDefault));

        for (int index = 0; index < codes.Count; index++)
        {
            CodeInstruction instruction = codes[index];

            if (LoadsVanillaNightVisionSlot(instruction)
                && index + 1 < codes.Count
                && inventoryGetter != null
                && codes[index + 1].Calls(inventoryGetter))
            {
                yield return new CodeInstruction(OpCodes.Dup);
                yield return new CodeInstruction(OpCodes.Call, slotIndexMethod);
                continue;
            }

            yield return instruction;
        }
    }

    private static bool LoadsVanillaNightVisionSlot(CodeInstruction instruction)
    {
        return instruction.opcode == OpCodes.Ldc_I4_S && Convert.ToInt32(instruction.operand) == NightVisionDeviceUtil.VanillaNightVisionSlot
            || instruction.opcode == OpCodes.Ldc_I4 && Convert.ToInt32(instruction.operand) == NightVisionDeviceUtil.VanillaNightVisionSlot;
    }
}
