using CombatOverhaul.DamageSystems;
using CombatOverhaul.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CombatOverhaul.Armor;

public class EntityCOArmorStand : EntityHumanoid
{
    EntityBehaviorCOArmorStandInventory? invbh;
    float fireDamage;

    public override bool IsCreature => false;
    public override bool IsInteractable => true;

    int CurPose
    {
        get { return WatchedAttributes.GetInt("curPose"); }
        set { WatchedAttributes.SetInt("curPose", value); }
    }

    public EntityCOArmorStand() { }

    public override ItemSlot? RightHandItemSlot => invbh?.Inventory[ArmorInventory._totalSlotsNumber];
    public override ItemSlot? LeftHandItemSlot => invbh?.Inventory[ArmorInventory._totalSlotsNumber + 1];

    public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
    {
        base.Initialize(properties, api, InChunkIndex3d);
        invbh = GetBehavior<EntityBehaviorCOArmorStandInventory>();
    }

    string[] poses = new string[] { "idle", "lefthandup", "righthandup", "twohandscross" };

    public override void OnInteract(EntityAgent byEntity, ItemSlot slot, Vec3d hitPosition, EnumInteractMode mode)
    {
        if (!Alive || mode == 0)
        {
            return;
        }

        // Critical multiplayer dupe fix:
        // Never mutate the armor stand inventory, player inventory, spawn items, or despawn the stand on the client.
        // The server performs the real transfer and syncs the watched attributes/inventories back to clients.
        if (World.Side == EnumAppSide.Client)
        {
            return;
        }

        IPlayer? plr = (byEntity as EntityPlayer)?.Player;
        if (plr != null && !byEntity.World.Claims.TryAccess(plr, Pos.AsBlockPos, EnumBlockAccessFlags.Use))
        {
            plr.InventoryManager.ActiveHotbarSlot.MarkDirty();
            WatchedAttributes.MarkAllDirty();
            return;
        }

        if (invbh?.Inventory == null)
        {
            base.OnInteract(byEntity, slot, hitPosition, mode);
            return;
        }

        ItemSlot? handslot = byEntity.RightHandItemSlot;
        if (handslot == null)
        {
            base.OnInteract(byEntity, slot, hitPosition, mode);
            return;
        }

        if (mode == EnumInteractMode.Interact && handslot.Itemstack?.Collectible is ItemWrench)
        {
            AnimManager.StopAnimation(poses[CurPose]);
            CurPose = (CurPose + 1) % poses.Length;
            AnimManager.StartAnimation(new AnimationMetaData() { Animation = poses[CurPose], Code = poses[CurPose] }.Init());
            WatchedAttributes.MarkPathDirty("curPose");
            return;
        }

        if (mode == EnumInteractMode.Interact)
        {
            if (handslot.Empty)
            {
                // Take the first available item from the stand into the player's hand.
                for (int i = 0; i < invbh.Inventory.Count; i++)
                {
                    ItemSlot gslot = invbh.Inventory[i];
                    if (gslot.Empty) continue;
                    if (gslot.Itemstack?.Collectible?.Code == null)
                    {
                        gslot.Itemstack = null;
                        gslot.MarkDirty();
                        continue;
                    }

                    if (gslot.TryPutInto(byEntity.World, handslot) > 0)
                    {
                        byEntity.World.Logger.Audit("{0} Took 1x{1} from Armor Stand at {2}.",
                            byEntity.GetName(),
                            handslot.Itemstack?.Collectible.Code,
                            ServerPos.AsBlockPos
                        );
                        return;
                    }
                }
            }
            else
            {
                // Tool/toolrack item placement. Use handslot, not the method parameter slot.
                if (handslot.Itemstack?.Collectible?.Tool != null || handslot.Itemstack?.ItemAttributes?["toolrackTransform"].Exists == true)
                {
                    var collectibleCode = handslot.Itemstack.Collectible.Code;
                    int moved = 0;

                    if (RightHandItemSlot != null)
                    {
                        moved = handslot.TryPutInto(byEntity.World, RightHandItemSlot);
                    }
                    if (moved == 0 && LeftHandItemSlot != null)
                    {
                        moved = handslot.TryPutInto(byEntity.World, LeftHandItemSlot);
                    }

                    if (moved > 0)
                    {
                        byEntity.World.Logger.Audit("{0} Put 1x{1} onto Armor Stand at {2}.",
                            byEntity.GetName(),
                            collectibleCode,
                            ServerPos.AsBlockPos
                        );
                        return;
                    }
                }

                // Armor placement. Use handslot, not the method parameter slot.
                if (!ItemSlotCharacter.IsDressType(handslot.Itemstack, EnumCharacterDressType.ArmorBody)
                    && !ItemSlotCharacter.IsDressType(handslot.Itemstack, EnumCharacterDressType.ArmorHead)
                    && !ItemSlotCharacter.IsDressType(handslot.Itemstack, EnumCharacterDressType.ArmorLegs))
                {
                    return;
                }

                WeightedSlot sinkslot = invbh.Inventory.GetBestSuitedSlot(handslot);
                if (sinkslot.weight > 0 && sinkslot.slot != null)
                {
                    var collectibleCode = handslot.Itemstack?.Collectible.Code;
                    if (handslot.TryPutInto(byEntity.World, sinkslot.slot) > 0)
                    {
                        byEntity.World.Logger.Audit("{0} Put 1x{1} onto Armor Stand at {2}.",
                            byEntity.GetName(),
                            collectibleCode,
                            ServerPos.AsBlockPos
                        );
                        return;
                    }
                }
            }

            bool empty = true;
            for (int i = 0; i < invbh.Inventory.Count; i++)
            {
                empty &= invbh.Inventory[i].Empty;
            }

            if (empty && byEntity.Controls.ShiftKey)
            {
                Item? standItem = byEntity.World.GetItem(Code);
                if (standItem != null)
                {
                    ItemStack stack = new ItemStack(standItem);
                    if (!byEntity.TryGiveItemStack(stack))
                    {
                        byEntity.World.SpawnItemEntity(stack, ServerPos.XYZ);
                    }
                    byEntity.World.Logger.Audit("{0} Took 1x{1} from Armor Stand at {2}.",
                        byEntity.GetName(),
                        stack.Collectible.Code,
                        ServerPos.AsBlockPos
                    );
                }

                // Inventory is known empty here. Despawning after giving the stand item is now server-only,
                // preventing client/server double pickup duplication.
                Die();
                return;
            }
        }

        base.OnInteract(byEntity, slot, hitPosition, mode);
    }

    public override bool ReceiveDamage(DamageSource damageSource, float damage)
    {
        if (damageSource.Source == EnumDamageSource.Internal && damageSource.Type == EnumDamageType.Fire) fireDamage += damage;
        if (fireDamage > 4) Die();

        return base.ReceiveDamage(damageSource, damage);
    }
}

public class EntityBehaviorCOArmorStandInventory : EntityBehavior
{
    private const string WearablesTreeKey = "wearablesInv";

    private readonly EntityAgent eagent;
    private readonly ArmorStandArmorInventory inv;

    public InventoryBase Inventory => inv;
    public string InventoryClassName => "inventory";

    public EntityBehaviorCOArmorStandInventory(Entity entity) : base(entity)
    {
        eagent = entity as EntityAgent ?? throw new Exception("Armor stand inventory behavior requires EntityAgent");
        inv = new ArmorStandArmorInventory(null, null);
    }

    public override string PropertyName() => "coarmorstandinventory";

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);

        inv.LateInitialize("gearinv-" + entity.EntityId, entity.World.Api);
        inv.SlotModified += OnInventorySlotModified;

        LoadInv();
        eagent.WatchedAttributes.RegisterModifiedListener(WearablesTreeKey, WearablesModified);
    }

    private void WearablesModified()
    {
        LoadInv();
        eagent.MarkShapeModified();
    }

    private void OnInventorySlotModified(int slotId)
    {
        SaveInv();
        eagent.MarkShapeModified();
    }

    private void LoadInv()
    {
        ITreeAttribute? tree = eagent.WatchedAttributes.GetTreeAttribute(WearablesTreeKey);
        if (tree == null)
        {
            tree = new TreeAttribute();
            eagent.WatchedAttributes[WearablesTreeKey] = tree;
        }

        inv.FromTreeAttributes(tree);
    }

    private void SaveInv()
    {
        TreeAttribute tree = new TreeAttribute();
        inv.ToTreeAttributes(tree);
        eagent.WatchedAttributes[WearablesTreeKey] = tree;
        eagent.WatchedAttributes.MarkPathDirty(WearablesTreeKey);
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        SaveInv();
        base.OnEntityDespawn(despawn);
    }

    public override void OnEntityDeath(DamageSource damageSourceForDeath)
    {
        SaveInv();
        base.OnEntityDeath(damageSourceForDeath);
    }
}

public class ArmorStandArmorSlot : ItemSlot
{
    public ArmorType ArmorType { get; }
    public ArmorType StoredArmoredType => GetStoredArmorType();

    public ArmorStandArmorSlot(InventoryBase inventory, ArmorType armorType) : base(inventory)
    {
        ArmorType = armorType;
        _inventory = inventory as ArmorStandArmorInventory ?? throw new Exception();
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (DrawUnavailable || !base.CanHold(sourceSlot) || sourceSlot.Itemstack == null || !IsArmor(sourceSlot.Itemstack.Collectible, out IArmor? armor)) return false;

        if (armor == null || !_inventory.CanHoldArmorPiece(armor)) return false;

        return armor.ArmorType.Intersect(ArmorType);
    }

    private readonly ArmorStandArmorInventory _inventory;

    private ArmorType GetStoredArmorType()
    {
        if (Itemstack?.Item != null && IsArmor(Itemstack.Collectible, out IArmor? armor) && armor != null)
        {
            return armor.ArmorType;
        }
        else
        {
            return ArmorType.Empty;
        }
    }

    private static bool IsArmor(CollectibleObject item, out IArmor? armor)
    {
        if (item is IArmor armorItem)
        {
            armor = armorItem;
            return true;
        }

        CollectibleBehavior? behavior = item.CollectibleBehaviors.FirstOrDefault(x => x is IArmor);

        if (behavior is not IArmor armorBehavior)
        {
            armor = null;
            return false;
        }

        armor = armorBehavior;
        return true;
    }
}

public class ArmorStandArmorInventory : InventoryBase
{
    ItemSlot[] slots;

    public ArmorStandArmorInventory(string className, string id, ICoreAPI api) : base(className, id, api)
    {
        slots = GenEmptySlots(ArmorInventory._totalSlotsNumber + 6);
        baseWeight = 2.5f;
    }

    public ArmorStandArmorInventory(string inventoryId, ICoreAPI api) : base(inventoryId, api)
    {
        slots = GenEmptySlots(ArmorInventory._totalSlotsNumber + 6);
        baseWeight = 2.5f;
    }

    public override int Count
    {
        get { return slots.Length; }
    }

    public override ItemSlot this[int slotId] { get { return slots[slotId]; } set { slots[slotId] = value; } }

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        List<ItemSlot> modifiedSlots = new List<ItemSlot>();
        slots = SlotsFromTreeAttributes(tree, slots, modifiedSlots);
        for (int i = 0; i < modifiedSlots.Count; i++) DidModifyItemSlot(modifiedSlots[i]);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        SlotsToTreeAttributes(slots, tree);
    }

    public bool IsSlotAvailable(ArmorType armorType) => !slots.Where(entry => !entry.Empty).OfType<ArmorStandArmorSlot>().Any(entry => entry.StoredArmoredType.Intersect(armorType));
    public bool CanHoldArmorPiece(ArmorType armorType)
    {
        return !slots.Where(entry => !entry.Empty).OfType<ArmorStandArmorSlot>().Any(entry => entry.StoredArmoredType.Intersect(armorType));
    }
    public bool CanHoldArmorPiece(IArmor armor) => CanHoldArmorPiece(armor.ArmorType);

    protected override ItemSlot NewSlot(int slotId)
    {
        if (slotId == ArmorInventory._totalSlotsNumber || slotId == ArmorInventory._totalSlotsNumber + 1) return new ItemSlotSurvival(this);
        if (slotId > ArmorInventory._totalSlotsNumber + 1)
        {
            return new ItemSlotBackpack(this);
        }

        ArmorStandArmorSlot slot = new(this, ArmorInventory.ArmorTypeFromIndex(slotId));

        return slot;
    }

    public override void DiscardAll()
    {
        base.DiscardAll();
        for (int i = 0; i < Count; i++)
        {
            DidModifyItemSlot(this[i]);
        }
    }

    public override void OnOwningEntityDeath(Vec3d pos)
    {
        // Don't drop contents on death. Contents are stored/synced through wearablesInv.
    }
}
