using CombatOverhaul.Utils;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace CombatOverhaul.Armor;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class ToolBagPacket
{
    public string ToolBagId { get; set; } = "";
    public int ToolBagIndex { get; set; } = 0;
    public bool MainHand { get; set; } = true;
    public int SlotIndex { get; set; } = 0;
}

public class ToolBagSystemClient
{
    public ToolBagSystemClient(ICoreClientAPI api)
    {
        _clientChannel = api.Network.RegisterChannel(_networkChannelId)
            .RegisterMessageType<ToolBagPacket>();
    }

    public void Send(string toolBagId, int toolBagIndex, bool mainHand, int slotIndex)
    {
        _clientChannel.SendPacket(new ToolBagPacket
        {
            ToolBagId = toolBagId,
            ToolBagIndex = toolBagIndex,
            MainHand = mainHand,
            SlotIndex = slotIndex
        });
    }

    private const string _networkChannelId = "CombatOverhaul:stats";
    private readonly IClientNetworkChannel _clientChannel;
}

public class ToolBagSystemServer
{
    public ToolBagSystemServer(ICoreServerAPI api)
    {
        _api = api;
        api.Network.RegisterChannel(_networkChannelId)
            .RegisterMessageType<ToolBagPacket>()
            .SetMessageHandler<ToolBagPacket>(HandlePacket);
    }

    private const string _networkChannelId = "CombatOverhaul:stats";
    private readonly ICoreServerAPI _api;
    private const long _toolSwapCooldown = 500;
    private readonly Dictionary<long, long> _mainHandCooldownUntilMs = [];
    private readonly Dictionary<long, long> _offHandCooldownUntilMs = [];
    private readonly Dictionary<DisplacementKey, DisplacedStackLocation> _displacedStacks = [];

    private IWorldAccessor _world => _api.World;

    private void HandlePacket(IServerPlayer player, ToolBagPacket packet)
    {
        IInventory? inventory = GetBackpackInventory(player);
        if (inventory == null) return;

        long currentTime = _world.ElapsedMilliseconds;
        long entityId = player.Entity?.EntityId ?? 0;
        _mainHandCooldownUntilMs.TryGetValue(entityId, out long mainHandCooldownUntilMs);
        _offHandCooldownUntilMs.TryGetValue(entityId, out long offHandCooldownUntilMs);

        try
        {
            if (mainHandCooldownUntilMs < currentTime && ProcessSlots(player, inventory, packet.ToolBagId, packet.ToolBagIndex, packet.SlotIndex, mainHand: true))
            {
                _mainHandCooldownUntilMs[entityId] = currentTime + _toolSwapCooldown;
            }

            if (offHandCooldownUntilMs < currentTime && ProcessSlots(player, inventory, packet.ToolBagId, packet.ToolBagIndex, packet.SlotIndex, mainHand: false))
            {
                _offHandCooldownUntilMs[entityId] = currentTime + _toolSwapCooldown;
            }
        }
        catch (Exception exception)
        {
            LoggerUtil.Error(player.Entity?.Api, this, $"Error when trying to use tool bag/sheath '{packet.ToolBagId}': {exception}");
        }
    }

    private ItemSlotBagContentWithWildcardMatch? GetToolSlot(IInventory inventory, string bagId, int bagIndex, bool mainHand, int slotIndex)
    {
        return inventory
            .OfType<ItemSlotBagContentWithWildcardMatch>()
            .Where(slot => slot.Config.HandleHotkey || slot.Config.DisplayInToolDialog)
            .FirstOrDefault(slot => slot.ToolBagId == bagId &&
                                    slot.MainHand == mainHand &&
                                    slot.ToolBagIndex == bagIndex &&
                                    slot.SlotIndex == slotIndex);
    }

    private ItemSlotTakeOutOnly? GetTakeOutSlot(IInventory inventory, string bagId, int bagIndex, bool mainHand)
    {
        return inventory
            .OfType<ItemSlotTakeOutOnly>()
            .FirstOrDefault(slot => slot.ToolBagId == bagId &&
                                    slot.MainHand == mainHand &&
                                    slot.ToolBagIndex == bagIndex);
    }

    private ItemSlot GetActiveSlot(IServerPlayer player, bool mainHand)
    {
        return mainHand ? player.Entity.ActiveHandItemSlot : player.Entity.LeftHandItemSlot;
    }

    private bool ProcessSlots(IServerPlayer player, IInventory inventory, string bagId, int bagIndex, int slotIndex, bool mainHand)
    {
        ItemSlotBagContentWithWildcardMatch? toolSlot = GetToolSlot(inventory, bagId, bagIndex, mainHand, slotIndex);
        if (toolSlot == null) return false;

        ItemSlot activeSlot = GetActiveSlot(player, mainHand);
        DisplacementKey displacementKey = new(player.Entity?.EntityId ?? 0, bagId, bagIndex, mainHand, slotIndex);

        if (activeSlot.Empty && toolSlot.Empty) return false;

        if (toolSlot.Empty)
        {
            return PutBack(player, inventory, bagId, bagIndex, mainHand, slotIndex, activeSlot, displacementKey);
        }

        return TakeOut(player, inventory, bagId, bagIndex, mainHand, slotIndex, activeSlot, displacementKey);
    }

    private bool TakeOut(IServerPlayer player, IInventory inventory, string bagId, int bagIndex, bool mainHand, int slotIndex, ItemSlot activeSlot, DisplacementKey displacementKey)
    {
        ItemSlotTakeOutOnly? takeOutSlot = GetTakeOutSlot(inventory, bagId, bagIndex, mainHand);
        ItemSlotBagContentWithWildcardMatch? toolSlot = GetToolSlot(inventory, bagId, bagIndex, mainHand, slotIndex);
        if (toolSlot == null || toolSlot.Empty) return false;

        _displacedStacks.Remove(displacementKey);
        DisplacedStackLocation? displacedStack = null;

        if (!activeSlot.Empty)
        {
            if (!TryMoveActiveStackToRegularInventory(player, activeSlot, toolSlot, out displacedStack))
            {
                if (!TryMoveActiveStackToTakeOutSlot(takeOutSlot, activeSlot, out displacedStack))
                {
                    return false;
                }
            }
        }

        if (!activeSlot.Empty) return false;

        int movedQuantity = toolSlot.TryPutInto(_world, activeSlot, toolSlot.Itemstack?.StackSize ?? 1);
        MarkSlotsDirty(toolSlot, activeSlot, takeOutSlot);

        if (movedQuantity <= 0)
        {
            if (displacedStack != null)
            {
                TryRestoreDisplacedStack(displacedStack, activeSlot);
            }

            return false;
        }

        if (displacedStack != null)
        {
            _displacedStacks[displacementKey] = displacedStack;
        }

        return true;
    }

    private bool TryMoveActiveStackToRegularInventory(IServerPlayer player, ItemSlot activeSlot, ItemSlot excludedSlot, out DisplacedStackLocation? displacedStack)
    {
        displacedStack = null;
        if (activeSlot.Empty || activeSlot.Itemstack?.StackSize <= 0) return true;

        foreach (ItemSlot targetSlot in GetRegularTargetSlots(player, activeSlot, excludedSlot))
        {
            int stackSize = activeSlot.Itemstack?.StackSize ?? 0;
            if (stackSize <= 0) return true;
            if (stackSize > targetSlot.MaxSlotStackSize) continue;

            int movedQuantity = activeSlot.TryPutInto(_world, targetSlot, stackSize);
            if (movedQuantity > 0)
            {
                MarkSlotsDirty(activeSlot, targetSlot);
            }

            if (activeSlot.Empty && targetSlot.Itemstack != null)
            {
                displacedStack = new(targetSlot, targetSlot.Itemstack);
                return true;
            }

            if (movedQuantity > 0)
            {
                targetSlot.TryPutInto(_world, activeSlot, movedQuantity);
                MarkSlotsDirty(activeSlot, targetSlot);
            }
        }

        return activeSlot.Empty;
    }

    private IEnumerable<ItemSlot> GetRegularTargetSlots(IServerPlayer player, ItemSlot activeSlot, ItemSlot excludedSlot)
    {
        HashSet<ItemSlot> yieldedSlots = [];

        foreach (IInventory? inventory in GetRegularTargetInventories(player))
        {
            if (inventory == null) continue;

            foreach (ItemSlot? slot in inventory)
            {
                if (slot == null) continue;
                if (slot is ItemSlotTakeOutOnly) continue;
                if (!slot.Empty) continue;
                if (ReferenceEquals(slot, activeSlot) || ReferenceEquals(slot, excludedSlot)) continue;
                if (!yieldedSlots.Add(slot)) continue;
                if (!slot.CanTakeFrom(activeSlot)) continue;

                yield return slot;
            }
        }
    }

    private static IEnumerable<IInventory?> GetRegularTargetInventories(IServerPlayer player)
    {
        yield return player.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        yield return player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
    }

    private bool TryMoveActiveStackToTakeOutSlot(ItemSlotTakeOutOnly? takeOutSlot, ItemSlot activeSlot, out DisplacedStackLocation? displacedStack)
    {
        displacedStack = null;
        if (activeSlot.Empty || activeSlot.Itemstack?.StackSize <= 0) return true;
        if (takeOutSlot == null) return false;
        if (!takeOutSlot.Empty) return false;
        if (activeSlot.Itemstack.StackSize > takeOutSlot.MaxSlotStackSize) return false;

        try
        {
            takeOutSlot.CanHoldNow = true;
            int movedQuantity = activeSlot.TryPutInto(_world, takeOutSlot, activeSlot.Itemstack.StackSize);
            if (movedQuantity > 0)
            {
                MarkSlotsDirty(activeSlot, takeOutSlot);
            }

            if (activeSlot.Empty && takeOutSlot.Itemstack != null)
            {
                displacedStack = new(takeOutSlot, takeOutSlot.Itemstack);
                return true;
            }

            if (movedQuantity > 0)
            {
                takeOutSlot.TryPutInto(_world, activeSlot, movedQuantity);
                MarkSlotsDirty(activeSlot, takeOutSlot);
            }

            return false;
        }
        finally
        {
            takeOutSlot.CanHoldNow = false;
            MarkSlotsDirty(activeSlot, takeOutSlot);
        }
    }

    private bool PutBack(IServerPlayer player, IInventory inventory, string bagId, int bagIndex, bool mainHand, int slotIndex, ItemSlot activeSlot, DisplacementKey displacementKey)
    {
        ItemSlotBagContentWithWildcardMatch? toolSlot = GetToolSlot(inventory, bagId, bagIndex, mainHand, slotIndex);
        if (toolSlot == null || !toolSlot.Empty || activeSlot.Empty) return false;

        int movedQuantity = activeSlot.TryPutInto(_world, toolSlot, activeSlot.Itemstack?.StackSize ?? 1);
        MarkSlotsDirty(activeSlot, toolSlot);

        if (movedQuantity <= 0)
        {
            return false;
        }

        if (activeSlot.Empty && _displacedStacks.Remove(displacementKey, out DisplacedStackLocation? displacedStack))
        {
            TryRestoreDisplacedStack(displacedStack, activeSlot);
        }

        return true;
    }

    private bool TryRestoreDisplacedStack(DisplacedStackLocation displacedStack, ItemSlot activeSlot)
    {
        if (!activeSlot.Empty) return false;
        if (displacedStack.Slot.Itemstack == null) return false;
        if (!ReferenceEquals(displacedStack.Slot.Itemstack, displacedStack.Stack)) return false;

        int movedQuantity = displacedStack.Slot.TryPutInto(_world, activeSlot, displacedStack.Slot.Itemstack.StackSize);
        MarkSlotsDirty(displacedStack.Slot, activeSlot);

        return movedQuantity > 0;
    }

    private static void MarkSlotsDirty(params ItemSlot?[] slots)
    {
        foreach (ItemSlot? slot in slots)
        {
            slot?.MarkDirty();
        }
    }

    private static IInventory? GetBackpackInventory(IPlayer player)
    {
        return player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
    }

    private readonly record struct DisplacementKey(long EntityId, string BagId, int BagIndex, bool MainHand, int SlotIndex);

    private sealed record DisplacedStackLocation(ItemSlot Slot, ItemStack Stack);
}
