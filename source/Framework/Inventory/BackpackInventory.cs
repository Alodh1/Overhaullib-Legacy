using CombatOverhaul.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace CombatOverhaul.Armor;

public class InventoryPlayerBackPacksCombatOverhaul : InventoryPlayerBackpacks
{
    public BagInventory BagInventory => bagInv;
    public ItemSlot[] BackpackSlots => bagSlots;

    public InventoryPlayerBackPacksCombatOverhaul(string className, string playerUID, ICoreAPI api) : base(className, playerUID, api)
    {
        _api = api;
    }

    public InventoryPlayerBackPacksCombatOverhaul(string inventoryId, ICoreAPI api) : base(inventoryId, api)
    {
        _api = api;
    }

    public void ReloadBagInventory()
    {
        ReloadResolvedBagInventory();
    }

    public override void AfterBlocksLoaded(IWorldAccessor world)
    {
        base.AfterBlocksLoaded(world);
        ReloadResolvedBagInventory();
    }

    public override void OnItemSlotModified(ItemSlot slot)
    {
        

        // Player modified must have some backpack contents
        // lets store that change in the backpack stack
        if (slot is ItemSlotBagContent bagContentSlot)
        {
            if (CanSaveSlotIntoResolvedBag(bagContentSlot))
            {
                base.OnItemSlotModified(slot);
            }
        }
        else
        {
            ReloadResolvedBagInventory();

            if (Api.Side == EnumAppSide.Server)
            {
                (Api.World.PlayerByUid(playerUID) as IServerPlayer)?.BroadcastPlayerData();
            }
        }

        
    }

    public override object ActivateSlot(int slotId, ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        // Player is about to add a new backpack
        bool tryAddBag = slotId < bagSlots.Length && bagSlots[slotId].Itemstack == null;

        object packet = base.ActivateSlot(slotId, sourceSlot, ref op);

        if (tryAddBag) ReloadResolvedBagInventory();
        return packet;
    }


    public override void DiscardAll()
    {
        for (int i = 0; i < bagSlots.Length; i++)
        {
            if (bagSlots[i].Itemstack != null)
            {
                dirtySlots.Add(i);
            }
            bagSlots[i].Itemstack = null;
        }

        ReloadResolvedBagInventory();
    }

    public override void DropAll(Vec3d pos, int maxStackSize = 0)
    {
        JsonObject? attr = Player?.Entity?.Properties.Attributes;
        int timer = attr == null ? GlobalConstants.TimeToDespawnPlayerInventoryDrops : attr["droppedItemsOnDeathTimer"].AsInt(GlobalConstants.TimeToDespawnPlayerInventoryDrops);

        for (int i = 0; i < bagSlots.Length; i++)
        {
            ItemSlot slot = bagSlots[i];
            if (slot.Itemstack != null)
            {
                EnumHandling handling = EnumHandling.PassThrough;
                slot.Itemstack.Collectible.OnHeldDropped(Api.World, Player, slot, slot.StackSize, ref handling);
                if (handling != EnumHandling.PassThrough) continue;

                dirtySlots.Add(i);
                spawnItemEntity(slot.Itemstack, pos, timer);
                slot.Itemstack = null;
            }
        }

        ReloadResolvedBagInventory();
    }

    private ICoreAPI _api;

    private void ReloadResolvedBagInventory()
    {
        bagInv.ReloadBagInventory(this, GetResolvedBagInventorySlots(bagSlots));
    }

    private ItemSlot[] GetResolvedBagInventorySlots(ItemSlot[] backpackSlots)
    {
        ItemSlot[] gearSlots = GetGearInventory(Owner)?.ToArray() ?? Array.Empty<ItemSlot>();

        return gearSlots.Concat(backpackSlots).Select(GetResolvedReloadSlot).ToArray();
    }

    private ItemSlot GetResolvedReloadSlot(ItemSlot? slot)
    {
        ItemStack? stack = slot?.Itemstack;
        if (stack == null) return slot ?? new DummySlot();

        ResolveStack(stack);

        if (stack.Collectible == null)
        {
            LogUnresolvedStack(slot, stack, "reload");
            return new DummySlot();
        }

        return slot ?? new DummySlot();
    }

    private bool CanSaveSlotIntoResolvedBag(ItemSlotBagContent slot)
    {
        ItemSlot? bagSlot = slot.BagIndex >= 0 && slot.BagIndex < bagInv.BagSlots.Length ? bagInv.BagSlots[slot.BagIndex] : null;
        ItemStack? stack = bagSlot?.Itemstack;
        if (stack == null) return false;

        ResolveStack(stack);

        IHeldBag? bag = stack.Collectible?.GetCollectibleInterface<IHeldBag>();
        if (bag == null)
        {
            LogUnresolvedStack(bagSlot, stack, "save");
            return false;
        }

        return true;
    }

    private void ResolveStack(ItemStack stack)
    {
        if (stack.Collectible == null && _api.World != null)
        {
            stack.ResolveBlockOrItem(_api.World);
        }
    }

    private void LogUnresolvedStack(ItemSlot? slot, ItemStack stack, string action)
    {
        string inventoryId = slot?.Inventory?.InventoryID ?? "<unknown>";
        int slotId = GetSlotIndex(slot);
        LoggerUtil.Warn(_api, this, $"Skipping unresolved stack while trying to {action} bag inventory: inventory={inventoryId}, slot={slotId}, itemId={stack.Id}, stackSize={stack.StackSize}");
    }

    private static int GetSlotIndex(ItemSlot? slot)
    {
        if (slot?.Inventory == null) return -1;

        for (int index = 0; index < slot.Inventory.Count; index++)
        {
            if (ReferenceEquals(slot.Inventory[index], slot))
            {
                return index;
            }
        }

        return -1;
    }

    private static InventoryBase? GetGearInventory(Entity entity)
    {
        return entity?.GetBehavior<EntityBehaviorPlayerInventory>()?.Inventory;
    }
}
