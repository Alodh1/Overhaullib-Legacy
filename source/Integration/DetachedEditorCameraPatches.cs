#if DEBUG
using CombatOverhaul.Animations;
using HarmonyLib;
using System.Reflection;
using Vintagestory.Client.NoObf;

namespace CombatOverhaul.Integration;

internal static class DetachedEditorCameraPatches
{
    public static void Patch(string harmonyId)
    {
        Harmony harmony = new(harmonyId);
        harmony.Patch(
            AccessTools.Method(typeof(PlayerCamera), nameof(PlayerCamera.OnBeforeRenderFrame3D)),
            postfix: new HarmonyMethod(typeof(DetachedEditorCameraPatches), nameof(OnBeforeRenderFrame3DPostfix)));

        harmony.Patch(
            typeof(ClientMain).GetMethod(nameof(ClientMain.UpdateCameraYawPitch), BindingFlags.Instance | BindingFlags.Public),
            prefix: new HarmonyMethod(typeof(DetachedEditorCameraPatches), nameof(UpdateCameraYawPitchPrefix))
            {
                priority = Priority.First
            });
    }

    public static void Unpatch(string harmonyId)
    {
        Harmony harmony = new(harmonyId);
        harmony.Unpatch(AccessTools.Method(typeof(PlayerCamera), nameof(PlayerCamera.OnBeforeRenderFrame3D)), HarmonyPatchType.Postfix, harmonyId);
        harmony.Unpatch(typeof(ClientMain).GetMethod(nameof(ClientMain.UpdateCameraYawPitch), BindingFlags.Instance | BindingFlags.Public), HarmonyPatchType.Prefix, harmonyId);
    }

    private static void OnBeforeRenderFrame3DPostfix(PlayerCamera __instance)
    {
        DetachedEditorCamera.ApplyActiveCameraOverride(__instance);
    }

    private static bool UpdateCameraYawPitchPrefix(ClientMain __instance)
    {
        return !DetachedEditorCamera.SuppressVanillaCameraUpdate(__instance);
    }
}
#endif
