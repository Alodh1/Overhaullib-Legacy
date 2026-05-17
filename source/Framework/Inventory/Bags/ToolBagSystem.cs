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

    private ItemSlotBagContentWithWildcardMatch? GetAlternativeToolHolderSlot(IInventory inventory, ItemSlot activeSlot)
    {
        if (activeSlot.Empty) return null;

        return inventory
            .OfType<ItemSlotBagContentWithWildcardMatch>()
            .Where(slot => slot.Config.HandleHotkey || slot.Config.DisplayInToolDialog)
            .FirstOrDefault(slot => slot.Empty && slot.CanTakeFrom(activeSlot));
    }

    private bool ProcessSlots(IServerPlayer player, IInventory inventory, string bagId, int bagIndex, int slotIndex, bool mainHand)
    {
        ItemSlotBagContentWithWildcardMatch? toolSlot = GetToolSlot(inventory, bagId, bagIndex, mainHand, slotIndex);
        if (toolSlot == null) return false;

        ItemSlot activeSlot = GetActiveSlot(player, mainHand);

        if (activeSlot.Empty && toolSlot.Empty) return false;

        if (toolSlot.Empty)
        {
            return PutBack(player, inventory, bagId, bagIndex, mainHand, slotIndex, activeSlot);
        }

        return TakeOut(player, inventory, bagId, bagIndex, mainHand, slotIndex, activeSlot);
    }

    private bool TakeOut(IServerPlayer player, IInventory inventory, string bagId, int bagIndex, bool mainHand, int slotIndex, ItemSlot activeSlot)
    {
        ItemSlotTakeOutOnly? takeOutSlot = GetTakeOutSlot(inventory, bagId, bagIndex, mainHand);
        ItemSlotBagContentWithWildcardMatch? anotherToolSlot = GetAlternativeToolHolderSlot(inventory, activeSlot);

        if (anotherToolSlot != null)
        {
            int movedQuantity = 0;
            if (activeSlot.Itemstack?.StackSize > 0)
            {
                movedQuantity = activeSlot.TryPutInto(_world, anotherToolSlot, activeSlot.Itemstack.StackSize);
                MarkSlotsDirty(activeSlot, anotherToolSlot);
            }

            if (activeSlot.Empty)
            {
                ItemSlotBagContentWithWildcardMatch? toolSlot = GetToolSlot(inventory, bagId, bagIndex, mainHand, slotIndex);
                if (toolSlot == null || toolSlot.Empty) return false;

                movedQuantity = toolSlot.TryPutInto(_world, activeSlot, toolSlot.Itemstack?.StackSize ?? 1);
                MarkSlotsDirty(toolSlot, activeSlot);

                return movedQuantity > 0;
            }
        }

        if (takeOutSlot != null)
        {
            try
            {
                int movedQuantity = 0;
                if (activeSlot.Itemstack?.StackSize > 0)
                {
                    takeOutSlot.CanHoldNow = true;
                    movedQuantity = activeSlot.TryPutInto(_world, takeOutSlot, activeSlot.Itemstack.StackSize);
                    MarkSlotsDirty(activeSlot, takeOutSlot);
                }

                if (!activeSlot.Empty) return false;

                ItemSlotBagContentWithWildcardMatch? toolSlot = GetToolSlot(inventory, bagId, bagIndex, mainHand, slotIndex);
                if (toolSlot == null || toolSlot.Empty) return false;

                movedQuantity = toolSlot.TryPutInto(_world, activeSlot, toolSlot.Itemstack?.StackSize ?? 1);
                MarkSlotsDirty(toolSlot, activeSlot, takeOutSlot);

                return movedQuantity > 0;
            }
            finally
            {
                // Always reset this, even if a move fails or returns early.
                takeOutSlot.CanHoldNow = false;
                MarkSlotsDirty(activeSlot, takeOutSlot);
            }
        }

        int fallbackMovedQuantity = 0;
        if (activeSlot.Itemstack?.StackSize > 0)
        {
            bool gaveAway = player.Entity.TryGiveItemStack(activeSlot.Itemstack);
            MarkSlotsDirty(activeSlot);

            if (!gaveAway)
            {
                return false;
            }
        }

        if (!activeSlot.Empty) return false;

        ItemSlotBagContentWithWildcardMatch? fallbackToolSlot = GetToolSlot(inventory, bagId, bagIndex, mainHand, slotIndex);
        if (fallbackToolSlot == null || fallbackToolSlot.Empty) return false;

        fallbackMovedQuantity = fallbackToolSlot.TryPutInto(_world, activeSlot, fallbackToolSlot.Itemstack?.StackSize ?? 1);
        MarkSlotsDirty(fallbackToolSlot, activeSlot);

        return fallbackMovedQuantity > 0;
    }

    private bool PutBack(IServerPlayer player, IInventory inventory, string bagId, int bagIndex, bool mainHand, int slotIndex, ItemSlot activeSlot)
    {
        ItemSlotBagContentWithWildcardMatch? toolSlot = GetToolSlot(inventory, bagId, bagIndex, mainHand, slotIndex);
        if (toolSlot == null || !toolSlot.Empty || activeSlot.Empty) return false;

        int movedQuantity = activeSlot.TryPutInto(_world, toolSlot, activeSlot.Itemstack?.StackSize ?? 1);
        MarkSlotsDirty(activeSlot, toolSlot);

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
}
