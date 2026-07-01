using CombatOverhaul.Implementations;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace CombatOverhaul.Integration;

public interface IMouseWheelInput
{
    bool OnMouseWheel(ItemSlot slot, IClientPlayer byPlayer, float delta);
}

internal static class MouseWheelPatch
{
    public static void Patch(string harmonyId, ICoreClientAPI api)
    {
        _clientApi = api;
        api.Event.MouseWheelMove += OnMouseWheelEvent;
        Harmony harmony = new(harmonyId);

        harmony.Patch(
            typeof(HudHotbar).GetMethod("OnMouseWheel", AccessTools.all),
            prefix: new HarmonyMethod(AccessTools.Method(typeof(MouseWheelPatch), nameof(OnMouseWheel)))
            );
    }

    public static void Unpatch(string harmonyId)
    {
        Harmony harmony = new(harmonyId);

        harmony.Unpatch(typeof(HudHotbar).GetMethod("OnMouseWheel", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        if (_clientApi != null)
        {
            _clientApi.Event.MouseWheelMove -= OnMouseWheelEvent;
            _clientApi = null;
        }
    }

    private static ICoreClientAPI? _clientApi;

    public static float GetDelta(MouseWheelEventArgs args)
    {
        return args.deltaPrecise != 0 ? args.deltaPrecise : args.delta;
    }

    private static void OnMouseWheelEvent(MouseWheelEventArgs args)
    {
        OnMouseWheel(args);
    }

    private static bool OnMouseWheel(MouseWheelEventArgs args)
    {
        if (_clientApi == null) return true;
        if (args.IsHandled) return true;

        float delta = GetDelta(args);
        if (delta == 0) return true;

        ItemSlot slot = _clientApi.World.Player.InventoryManager.ActiveHotbarSlot;

        IMouseWheelInput? item = slot.Itemstack?.Collectible?.GetCollectibleInterface<IMouseWheelInput>();

        if (item != null)
        {
            IClientPlayer player = _clientApi.World.Player;
            bool handled = item.OnMouseWheel(slot, player, delta);
            if (handled)
            {
                args.SetHandled();
            }

            return !handled;
        }

        return true;
    }
}
