using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace CombatOverhaul;

internal static class NightVisionDeviceUtil
{
    public const int VanillaNightVisionSlot = 12;

    public static int GetBestNightVisionSlotIndex(IInventory inventory)
    {
        if (inventory.Count == 0) return -1;

        if (IsFueledNightVisionSlot(GetSlot(inventory, VanillaNightVisionSlot)))
        {
            return VanillaNightVisionSlot;
        }

        for (int index = 0; index < inventory.Count; index++)
        {
            if (index == VanillaNightVisionSlot) continue;
            if (IsFueledNightVisionSlot(inventory[index])) return index;
        }

        if (IsNightVisionSlot(GetSlot(inventory, VanillaNightVisionSlot)))
        {
            return VanillaNightVisionSlot;
        }

        for (int index = 0; index < inventory.Count; index++)
        {
            if (index == VanillaNightVisionSlot) continue;
            if (IsNightVisionSlot(inventory[index])) return index;
        }

        return -1;
    }

    public static int GetNightVisionSlotIndexOrDefault(IInventory inventory)
    {
        int index = GetBestNightVisionSlotIndex(inventory);
        if (index >= 0) return index;

        return inventory.Count > VanillaNightVisionSlot
            ? VanillaNightVisionSlot
            : Math.Max(0, inventory.Count - 1);
    }

    public static ItemSlot? GetBestNightVisionSlot(IInventory inventory)
    {
        int index = GetBestNightVisionSlotIndex(inventory);
        return GetSlot(inventory, index);
    }

    public static bool IsNightVisionSlot(ItemSlot? slot)
    {
        return slot?.Itemstack?.Collectible is ItemNightvisiondevice;
    }

    public static bool IsFueledNightVisionSlot(ItemSlot? slot)
    {
        ItemStack? stack = slot?.Itemstack;
        return stack?.Collectible is ItemNightvisiondevice device && device.GetFuelHours(stack) > 0;
    }

    private static ItemSlot? GetSlot(IInventory inventory, int index)
    {
        return index >= 0 && index < inventory.Count ? inventory[index] : null;
    }
}
