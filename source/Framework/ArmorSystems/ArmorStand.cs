using CombatOverhaul.DamageSystems;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CombatOverhaul.Armor;

public class EntityCOArmorStand : EntityHumanoid
{
    private EntityBehaviorCOArmorStandInventory? invbh;
    private float fireDamage;
    private readonly string[] poses = ["idle", "lefthandup", "righthandup", "twohandscross"];

    public override bool IsCreature => false;
    public override bool IsInteractable => true;

    private int CurPose
    {
        get => WatchedAttributes.GetInt("curPose");
        set => WatchedAttributes.SetInt("curPose", value);
    }

    public EntityCOArmorStand() { }

    public override ItemSlot? RightHandItemSlot => invbh?.Inventory[ArmorInventory._totalSlotsNumber];
    public override ItemSlot? LeftHandItemSlot => invbh?.Inventory[ArmorInventory._totalSlotsNumber + 1];

    public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
    {
        base.Initialize(properties, api, InChunkIndex3d);
        invbh = GetBehavior<EntityBehaviorCOArmorStandInventory>();
    }

    public override void OnInteract(EntityAgent byEntity, ItemSlot slot, Vec3d hitPosition, EnumInteractMode mode)
    {
        if (!Alive || mode == EnumInteractMode.Attack)
        {
            return;
        }

        if (World.Side == EnumAppSide.Client)
        {
            return;
        }

        IPlayer? player = (byEntity as EntityPlayer)?.Player;
        if (player != null && !byEntity.World.Claims.TryAccess(player, Pos.AsBlockPos, EnumBlockAccessFlags.Use))
        {
            player.InventoryManager.ActiveHotbarSlot.MarkDirty();
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

        if (mode != EnumInteractMode.Interact)
        {
            base.OnInteract(byEntity, slot, hitPosition, mode);
            return;
        }

        if (handslot.Itemstack?.Collectible is ItemWrench)
        {
            AnimManager.StopAnimation(poses[CurPose]);
            CurPose = (CurPose + 1) % poses.Length;
            AnimManager.StartAnimation(new AnimationMetaData { Animation = poses[CurPose], Code = poses[CurPose] }.Init());
            WatchedAttributes.MarkPathDirty("curPose");
            return;
        }

        int selectionBoxIndex = player?.CurrentEntitySelection?.SelectionBoxIndex ?? -1;

        if (handslot.Empty)
        {
            if (invbh.TryTakeSelected(byEntity, handslot, selectionBoxIndex))
            {
                MarkShapeModified();
                return;
            }

            if (byEntity.Controls.ShiftKey && invbh.IsEmptyIncludingLegacy())
            {
                Item? standItem = byEntity.World.GetItem(Code);
                if (standItem != null)
                {
                    ItemStack stack = new(standItem);
                    if (!byEntity.TryGiveItemStack(stack))
                    {
                        byEntity.World.SpawnItemEntity(stack, ServerPos.XYZ);
                    }
                    byEntity.World.Logger.Audit("{0} Took 1x{1} from Armor Stand at {2}.", byEntity.GetName(), stack.Collectible.Code, ServerPos.AsBlockPos);
                }

                Die();
                return;
            }

            return;
        }

        ItemStack? heldStack = handslot.Itemstack;
        if (heldStack?.Collectible == null || heldStack.StackSize <= 0)
        {
            handslot.Itemstack = null;
            handslot.MarkDirty();
            return;
        }

        if (IsToolOrToolrackItem(heldStack))
        {
            if (invbh.LegacyHandSlotBlocksPlacement(selectionBoxIndex))
            {
                return;
            }

            AssetLocation code = heldStack.Collectible.Code;
            int moved = invbh.TryPutToolIntoCoHandSlots(handslot, selectionBoxIndex);
            if (moved > 0)
            {
                MarkShapeModified();
                byEntity.World.Logger.Audit("{0} Put 1x{1} onto Armor Stand at {2}.", byEntity.GetName(), code, ServerPos.AsBlockPos);
            }
            return;
        }

        if (!IsArmorStandArmor(heldStack))
        {
            return;
        }

        if (invbh.LegacyArmorSlotBlocksPlacement(heldStack, selectionBoxIndex))
        {
            return;
        }

        WeightedSlot sinkslot = invbh.Inventory.GetBestSuitedSlot(handslot);
        if (sinkslot.weight > 0 && sinkslot.slot != null)
        {
            AssetLocation? code = heldStack.Collectible.Code;
            if (handslot.TryPutInto(byEntity.World, sinkslot.slot) > 0)
            {
                MarkShapeModified();
                byEntity.World.Logger.Audit("{0} Put 1x{1} onto Armor Stand at {2}.", byEntity.GetName(), code, ServerPos.AsBlockPos);
            }
        }
    }

    private static bool IsToolOrToolrackItem(ItemStack stack)
    {
        return stack.Collectible.Tool != null || stack.ItemAttributes?["toolrackTransform"].Exists == true;
    }

    private static bool IsArmorStandArmor(ItemStack stack)
    {
        return ItemSlotCharacter.IsDressType(stack, EnumCharacterDressType.ArmorBody)
            || ItemSlotCharacter.IsDressType(stack, EnumCharacterDressType.ArmorHead)
            || ItemSlotCharacter.IsDressType(stack, EnumCharacterDressType.ArmorLegs);
    }

    public override bool ReceiveDamage(DamageSource damageSource, float damage)
    {
        if (damageSource.Source == EnumDamageSource.Internal && damageSource.Type == EnumDamageType.Fire)
        {
            fireDamage += damage;
        }
        if (fireDamage > 4)
        {
            Die();
        }

        return base.ReceiveDamage(damageSource, damage);
    }
}

public class EntityBehaviorCOArmorStandInventory : EntityBehavior
{
    private const string LegacyTreeKey = "wearablesInv";
    private const string CoTreeKey = "coArmorStandInv";
    private const int LegacySlotCount = 5;
    private const int LegacyHeadSlot = 0;
    private const int LegacyBodySlot = 1;
    private const int LegacyLegsSlot = 2;
    private const int LegacyRightHandSlot = 3;
    private const int LegacyLeftHandSlot = 4;

    private readonly EntityAgent eagent;
    private readonly ArmorStandArmorInventory coInventory;
    private readonly ArmorStandLegacyInventory legacyInventory;
    private bool loadingCoInventory;
    private bool loadingLegacyInventory;
    private bool hasPersistentCoTree;
    private bool hasPersistentLegacyTree;

    private static readonly Dictionary<string, StepParentElementTo> HeadStepParent = new()
    {
        [""] = new StepParentElementTo { ElementName = "Head" }
    };

    private static readonly Dictionary<string, StepParentElementTo> BodyStepParent = new()
    {
        [""] = new StepParentElementTo { ElementName = "ASUpperBody" }
    };

    private static readonly Dictionary<string, StepParentElementTo> LegsStepParent = new()
    {
        [""] = new StepParentElementTo { ElementName = "Legs" }
    };

    private static readonly Dictionary<string, StepParentElementTo> RightHandStepParent = new()
    {
        [""] = new StepParentElementTo { ElementName = "RightHandAP" },
        ["OarStorage"] = new StepParentElementTo { ElementName = "LowerArmR_R_Oar" },
        ["LowerArmL"] = new StepParentElementTo { ElementName = "LowerArmR_R" }
    };

    private static readonly Dictionary<string, StepParentElementTo> LeftHandStepParent = new()
    {
        [""] = new StepParentElementTo { ElementName = "LeftHandAP" },
        ["OarStorage"] = new StepParentElementTo { ElementName = "LowerArmL_R_Oar" },
        ["LowerArmR"] = new StepParentElementTo { ElementName = "LowerArmL" }
    };

    public InventoryBase Inventory => coInventory;
    public string InventoryClassName => "inventory";

    public EntityBehaviorCOArmorStandInventory(Entity entity) : base(entity)
    {
        eagent = entity as EntityAgent ?? throw new Exception("Armor stand inventory behavior requires EntityAgent");
        coInventory = new ArmorStandArmorInventory(null, null);
        legacyInventory = new ArmorStandLegacyInventory(null, null);
    }

    public override string PropertyName() => "coarmorstandinventory";

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);

        coInventory.LateInitialize("coarmorstand-" + entity.EntityId, entity.World.Api);
        legacyInventory.LateInitialize("legacyarmorstand-" + entity.EntityId, entity.World.Api);
        coInventory.SlotModified += OnCoInventorySlotModified;
        legacyInventory.SlotModified += OnLegacyInventorySlotModified;

        LoadCoInventory();
        LoadLegacyInventory();
        eagent.WatchedAttributes.RegisterModifiedListener(CoTreeKey, CoWearablesModified);
        eagent.WatchedAttributes.RegisterModifiedListener(LegacyTreeKey, LegacyWearablesModified);
    }

    private void CoWearablesModified()
    {
        LoadCoInventory();
        eagent.MarkShapeModified();
    }

    private void LegacyWearablesModified()
    {
        LoadLegacyInventory();
        eagent.MarkShapeModified();
    }

    private void OnCoInventorySlotModified(int slotId)
    {
        if (loadingCoInventory) return;
        SaveCoInventory();
        eagent.MarkShapeModified();
    }

    private void OnLegacyInventorySlotModified(int slotId)
    {
        if (loadingLegacyInventory) return;
        SaveLegacyInventory();
        eagent.MarkShapeModified();
    }

    private void LoadCoInventory()
    {
        ITreeAttribute? tree = eagent.WatchedAttributes.GetTreeAttribute(CoTreeKey);
        hasPersistentCoTree |= tree != null;
        loadingCoInventory = true;
        try
        {
            coInventory.FromTreeAttributes(tree ?? new TreeAttribute());
        }
        finally
        {
            loadingCoInventory = false;
        }
    }

    private void LoadLegacyInventory()
    {
        ITreeAttribute? tree = eagent.WatchedAttributes.GetTreeAttribute(LegacyTreeKey);
        hasPersistentLegacyTree |= tree != null;
        loadingLegacyInventory = true;
        try
        {
            legacyInventory.FromTreeAttributes(tree ?? new TreeAttribute());
        }
        finally
        {
            loadingLegacyInventory = false;
        }
    }

    private void SaveCoInventory()
    {
        if (entity.World.Side != EnumAppSide.Server || (!hasPersistentCoTree && coInventory.Empty)) return;
        TreeAttribute tree = new();
        coInventory.ToTreeAttributes(tree);
        eagent.WatchedAttributes[CoTreeKey] = tree;
        eagent.WatchedAttributes.MarkPathDirty(CoTreeKey);
        hasPersistentCoTree = true;
    }

    private void SaveLegacyInventory()
    {
        if (entity.World.Side != EnumAppSide.Server || (!hasPersistentLegacyTree && legacyInventory.Empty)) return;
        TreeAttribute tree = new();
        legacyInventory.ToTreeAttributes(tree);
        eagent.WatchedAttributes[LegacyTreeKey] = tree;
        eagent.WatchedAttributes.MarkPathDirty(LegacyTreeKey);
        hasPersistentLegacyTree = true;
    }

    public bool TryTakeSelected(EntityAgent byEntity, ItemSlot handslot, int selectionBoxIndex)
    {
        if (!handslot.Empty) return false;

        foreach (int legacyIndex in OrderedLegacySlots(selectionBoxIndex))
        {
            if (TryMoveWholeStackToEmptyHand(legacyInventory[legacyIndex], handslot, out int moved))
            {
                SaveLegacyInventory();
                eagent.MarkShapeModified();
                byEntity.World.Logger.Audit("{0} Took {1}x{2} from vanilla Armor Stand slot at {3}.", byEntity.GetName(), moved, handslot.Itemstack?.Collectible.Code, entity.ServerPos.AsBlockPos);
                return true;
            }
        }

        foreach (int coIndex in OrderedCoSlots(selectionBoxIndex))
        {
            if (coIndex < 0 || coIndex >= coInventory.Count) continue;
            if (TryMoveWholeStackToEmptyHand(coInventory[coIndex], handslot, out int moved))
            {
                SaveCoInventory();
                eagent.MarkShapeModified();
                byEntity.World.Logger.Audit("{0} Took {1}x{2} from Armor Stand at {3}.", byEntity.GetName(), moved, handslot.Itemstack?.Collectible.Code, entity.ServerPos.AsBlockPos);
                return true;
            }
        }

        return false;
    }

    public bool IsEmptyIncludingLegacy()
    {
        ClearInvalidGhostStacks(coInventory, saveCo: true);
        ClearInvalidGhostStacks(legacyInventory, saveCo: false);
        return coInventory.Empty && legacyInventory.Empty;
    }

    public bool LegacyArmorSlotBlocksPlacement(ItemStack stack, int selectionBoxIndex)
    {
        int legacySlot = GetLegacyArmorSlot(stack);
        if (legacySlot < 0) return false;
        if (selectionBoxIndex >= 0 && selectionBoxIndex <= LegacyLeftHandSlot && selectionBoxIndex != legacySlot) return false;
        return !legacyInventory[legacySlot].Empty;
    }

    public bool LegacyHandSlotBlocksPlacement(int selectionBoxIndex)
    {
        if (selectionBoxIndex == LegacyRightHandSlot) return !legacyInventory[LegacyRightHandSlot].Empty;
        if (selectionBoxIndex == LegacyLeftHandSlot) return !legacyInventory[LegacyLeftHandSlot].Empty;
        return !legacyInventory[LegacyRightHandSlot].Empty && !legacyInventory[LegacyLeftHandSlot].Empty;
    }

    public int TryPutToolIntoCoHandSlots(ItemSlot handslot, int selectionBoxIndex)
    {
        if (selectionBoxIndex == LegacyRightHandSlot)
        {
            return handslot.TryPutInto(entity.World, coInventory[ArmorInventory._totalSlotsNumber]);
        }
        if (selectionBoxIndex == LegacyLeftHandSlot)
        {
            return handslot.TryPutInto(entity.World, coInventory[ArmorInventory._totalSlotsNumber + 1]);
        }

        int moved = handslot.TryPutInto(entity.World, coInventory[ArmorInventory._totalSlotsNumber]);
        if (moved == 0)
        {
            moved = handslot.TryPutInto(entity.World, coInventory[ArmorInventory._totalSlotsNumber + 1]);
        }
        return moved;
    }

    private static bool TryMoveWholeStackToEmptyHand(ItemSlot sourceSlot, ItemSlot handslot, out int moved)
    {
        moved = 0;
        if (!handslot.Empty) return false;

        ItemStack? stack = sourceSlot.Itemstack;
        if (stack?.Collectible == null || stack.StackSize <= 0)
        {
            sourceSlot.Itemstack = null;
            sourceSlot.MarkDirty();
            return false;
        }

        moved = stack.StackSize;
        sourceSlot.Itemstack = null;
        handslot.Itemstack = stack;
        sourceSlot.MarkDirty();
        handslot.MarkDirty();
        return true;
    }

    private void ClearInvalidGhostStacks(InventoryBase inventory, bool saveCo)
    {
        bool changed = false;
        for (int i = 0; i < inventory.Count; i++)
        {
            ItemStack? stack = inventory[i].Itemstack;
            if (stack != null && (stack.Collectible == null || stack.StackSize <= 0))
            {
                inventory[i].Itemstack = null;
                inventory[i].MarkDirty();
                changed = true;
            }
        }

        if (!changed) return;
        if (saveCo) SaveCoInventory(); else SaveLegacyInventory();
    }

    private static IEnumerable<int> OrderedLegacySlots(int selectionBoxIndex)
    {
        if (selectionBoxIndex >= LegacyHeadSlot && selectionBoxIndex <= LegacyLeftHandSlot)
        {
            yield return selectionBoxIndex;
        }

        for (int i = 0; i < LegacySlotCount; i++)
        {
            if (i != selectionBoxIndex) yield return i;
        }
    }

    private static IEnumerable<int> OrderedCoSlots(int selectionBoxIndex)
    {
        if (selectionBoxIndex == LegacyRightHandSlot)
        {
            yield return ArmorInventory._totalSlotsNumber;
        }
        else if (selectionBoxIndex == LegacyLeftHandSlot)
        {
            yield return ArmorInventory._totalSlotsNumber + 1;
        }
        else if (selectionBoxIndex >= LegacyHeadSlot && selectionBoxIndex <= LegacyLegsSlot)
        {
            DamageZone zone = selectionBoxIndex switch
            {
                LegacyHeadSlot => DamageZone.Head | DamageZone.Face | DamageZone.Neck,
                LegacyBodySlot => DamageZone.Torso | DamageZone.Arms | DamageZone.Hands,
                LegacyLegsSlot => DamageZone.Legs | DamageZone.Feet,
                _ => DamageZone.None
            };

            for (int i = ArmorInventory._vanillaSlots; i < ArmorInventory._armorSlotsLastIndex; i++)
            {
                if ((ArmorInventory.ArmorTypeFromIndex(i).Slots & zone) != DamageZone.None)
                {
                    yield return i;
                }
            }
        }

        for (int i = 0; i < ArmorInventory._totalSlotsNumber + 2; i++)
        {
            yield return i;
        }
    }

    private static int GetLegacyArmorSlot(ItemStack stack)
    {
        if (ItemSlotCharacter.IsDressType(stack, EnumCharacterDressType.ArmorHead)) return LegacyHeadSlot;
        if (ItemSlotCharacter.IsDressType(stack, EnumCharacterDressType.ArmorBody)) return LegacyBodySlot;
        if (ItemSlotCharacter.IsDressType(stack, EnumCharacterDressType.ArmorLegs)) return LegacyLegsSlot;
        return -1;
    }

    public override void OnTesselation(ref Shape entityShape, string shapePathForLogging, ref bool shapeIsCloned, ref string[] willDeleteElements)
    {
        if (entityShape == null)
        {
            return;
        }

        for (int i = 0; i < legacyInventory.Count; i++)
        {
            RenderSlot(i, legacyInventory[i], shapePathForLogging, ref entityShape, ref shapeIsCloned, ref willDeleteElements, legacy: true);
        }

        for (int i = 0; i < coInventory.Count; i++)
        {
            RenderSlot(i, coInventory[i], shapePathForLogging, ref entityShape, ref shapeIsCloned, ref willDeleteElements, legacy: false);
        }
    }

    private void RenderSlot(int slotIndex, ItemSlot slot, string shapePathForLogging, ref Shape entityShape, ref bool shapeIsCloned, ref string[] willDeleteElements, bool legacy)
    {
        if (slot.Empty || slot.Itemstack?.Collectible == null)
        {
            return;
        }

        if (!TryGetRenderSlot(slotIndex, slot, legacy, out string slotCode, out Dictionary<string, StepParentElementTo>? stepParent))
        {
            return;
        }

        IAttachableToEntity? attachable = IAttachableToEntity.FromCollectible(slot.Itemstack.Collectible);
        if (attachable == null)
        {
            return;
        }

        if (!shapeIsCloned)
        {
            entityShape = entityShape.Clone();
            shapeIsCloned = true;
        }

        entityShape = EntityBehaviorContainer.addGearToShape(
            entity.Api,
            entity,
            (entity.Api as ICoreClientAPI)?.EntityTextureAtlas,
            entityShape,
            slot.Itemstack,
            attachable,
            slotCode,
            shapePathForLogging,
            ref willDeleteElements,
            entity.Properties.Client.Textures,
            stepParent);
    }

    private static bool TryGetRenderSlot(int slotIndex, ItemSlot slot, bool legacy, out string slotCode, out Dictionary<string, StepParentElementTo>? stepParent)
    {
        slotCode = "";
        stepParent = null;

        if (slot.Itemstack?.Collectible == null)
        {
            return false;
        }

        if (legacy)
        {
            if (slotIndex == LegacyRightHandSlot)
            {
                slotCode = "righthand";
                stepParent = RightHandStepParent;
                return true;
            }
            if (slotIndex == LegacyLeftHandSlot)
            {
                slotCode = "lefthand";
                stepParent = LeftHandStepParent;
                return true;
            }
            return TryGetVanillaArmorStandRenderSlot(slot.Itemstack, out slotCode, out stepParent);
        }

        if (slotIndex == ArmorInventory._totalSlotsNumber)
        {
            slotCode = "righthand";
            stepParent = RightHandStepParent;
            return true;
        }
        if (slotIndex == ArmorInventory._totalSlotsNumber + 1)
        {
            slotCode = "lefthand";
            stepParent = LeftHandStepParent;
            return true;
        }
        if (slotIndex < ArmorInventory._vanillaSlots)
        {
            return false;
        }
        if (TryGetVanillaArmorStandRenderSlot(slot.Itemstack, out slotCode, out stepParent))
        {
            return true;
        }
        if (slotIndex >= ArmorInventory._armorSlotsLastIndex)
        {
            return false;
        }

        ArmorType armorType = ArmorInventory.ArmorTypeFromIndex(slotIndex);
        string layer = armorType.Layers.ToString().ToLowerInvariant();
        if ((armorType.Slots & (DamageZone.Head | DamageZone.Face | DamageZone.Neck)) != DamageZone.None)
        {
            slotCode = "head-" + layer;
            stepParent = HeadStepParent;
            return true;
        }
        if ((armorType.Slots & (DamageZone.Torso | DamageZone.Arms | DamageZone.Hands)) != DamageZone.None)
        {
            slotCode = "body-" + layer;
            stepParent = BodyStepParent;
            return true;
        }
        if ((armorType.Slots & (DamageZone.Legs | DamageZone.Feet)) != DamageZone.None)
        {
            slotCode = "legs-" + layer;
            stepParent = LegsStepParent;
            return true;
        }

        return false;
    }

    private static bool TryGetVanillaArmorStandRenderSlot(ItemStack stack, out string slotCode, out Dictionary<string, StepParentElementTo>? stepParent)
    {
        slotCode = "";
        stepParent = null;

        if (ItemSlotCharacter.IsDressType(stack, EnumCharacterDressType.ArmorHead))
        {
            slotCode = "head";
            stepParent = HeadStepParent;
            return true;
        }
        if (ItemSlotCharacter.IsDressType(stack, EnumCharacterDressType.ArmorBody))
        {
            slotCode = "body";
            stepParent = BodyStepParent;
            return true;
        }
        if (ItemSlotCharacter.IsDressType(stack, EnumCharacterDressType.ArmorLegs))
        {
            slotCode = "legs";
            stepParent = LegsStepParent;
            return true;
        }

        return false;
    }

    public override void FromBytes(bool isSync)
    {
        LoadCoInventory();
        LoadLegacyInventory();
        eagent.MarkShapeModified();
    }

    public override void ToBytes(bool forClient)
    {
        SaveCoInventory();
        SaveLegacyInventory();
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        SaveCoInventory();
        SaveLegacyInventory();
        base.OnEntityDespawn(despawn);
    }

    public override void OnEntityDeath(DamageSource damageSourceForDeath)
    {
        if (entity.World.Side == EnumAppSide.Server)
        {
            DropAllContentsOnce();
        }
        base.OnEntityDeath(damageSourceForDeath);
    }

    private void DropAllContentsOnce()
    {
        DropInventoryContents(legacyInventory);
        DropInventoryContents(coInventory);
        SaveLegacyInventory();
        SaveCoInventory();
    }

    private void DropInventoryContents(InventoryBase inventory)
    {
        for (int i = 0; i < inventory.Count; i++)
        {
            ItemStack? stack = inventory[i].Itemstack;
            if (stack?.Collectible == null || stack.StackSize <= 0)
            {
                inventory[i].Itemstack = null;
                inventory[i].MarkDirty();
                continue;
            }

            inventory[i].Itemstack = null;
            inventory[i].MarkDirty();
            entity.World.SpawnItemEntity(stack, entity.ServerPos.XYZ);
        }
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
    private ItemSlot[] slots;

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

    public override int Count => slots.Length;

    public override ItemSlot this[int slotId]
    {
        get => slots[slotId];
        set => slots[slotId] = value;
    }

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        List<ItemSlot> modifiedSlots = new();
        slots = SlotsFromTreeAttributes(tree, GenEmptySlots(ArmorInventory._totalSlotsNumber + 6), modifiedSlots);
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

        return new ArmorStandArmorSlot(this, ArmorInventory.ArmorTypeFromIndex(slotId));
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
        // Death drops are handled by EntityBehaviorCOArmorStandInventory to include legacy slots exactly once.
    }
}

public class ArmorStandLegacyInventory : InventoryBase
{
    private ItemSlot[] slots;

    public ArmorStandLegacyInventory(string className, string id, ICoreAPI api) : base(className, id, api)
    {
        slots = GenEmptySlots(5);
        baseWeight = 2.5f;
    }

    public ArmorStandLegacyInventory(string inventoryId, ICoreAPI api) : base(inventoryId, api)
    {
        slots = GenEmptySlots(5);
        baseWeight = 2.5f;
    }

    public override int Count => slots.Length;

    public override ItemSlot this[int slotId]
    {
        get => slots[slotId];
        set => slots[slotId] = value;
    }

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        List<ItemSlot> modifiedSlots = new();
        slots = SlotsFromTreeAttributes(tree, GenEmptySlots(5), modifiedSlots);
        for (int i = 0; i < modifiedSlots.Count; i++) DidModifyItemSlot(modifiedSlots[i]);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        SlotsToTreeAttributes(slots, tree);
    }

    protected override ItemSlot NewSlot(int slotId)
    {
        return new ItemSlotSurvival(this);
    }

    public override void OnOwningEntityDeath(Vec3d pos)
    {
    }
}
