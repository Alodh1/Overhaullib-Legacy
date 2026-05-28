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

        // Use the actual slot passed by the engine for this interaction.
        // EntityAgent.RightHandItemSlot can point at the entity-hand/equipment slot instead
        // of the player's active hotbar slot in some contexts, which means removal appears
        // to do nothing even though the armor stand slot was checked correctly.
        ItemSlot handslot = slot;

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
            if (invbh.TryTakeSelected(byEntity, handslot, selectionBoxIndex, hitPosition))
            {
                MarkShapeModified();
                return;
            }

            if (byEntity.Controls.ShiftKey && invbh.Inventory.Empty)
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
            AssetLocation code = heldStack.Collectible.Code;
            int moved = invbh.TryPutToolIntoHandSlots(handslot, selectionBoxIndex, hitPosition);
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

        AssetLocation? armorCode = heldStack.Collectible.Code;
        if (invbh.TryPutArmorIntoArmorSlots(handslot, selectionBoxIndex, hitPosition) > 0)
        {
            MarkShapeModified();
            byEntity.World.Logger.Audit("{0} Put 1x{1} onto Armor Stand at {2}.", byEntity.GetName(), armorCode, ServerPos.AsBlockPos);
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
            || ItemSlotCharacter.IsDressType(stack, EnumCharacterDressType.ArmorLegs)
            || ArmorStandArmorSlot.TryGetArmor(stack, out _);
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
    private const string TreeKey = "coArmorStandInv";
    private readonly EntityAgent eagent;
    private readonly ArmorStandArmorInventory inventory;
    private bool loadingInventory;
    private bool hasPersistentTree;

    private const int VanillaHeadSelection = 0;
    private const int VanillaBodySelection = 1;
    private const int VanillaLegsSelection = 2;
    private const int VanillaRightHandSelection = 3;
    private const int VanillaLeftHandSelection = 4;
    private const string MigratedVanillaInventoryFlag = "coArmorStandMigratedVanillaInv";
    private const int VanillaArmorStandSlotCount = 5;

    private static bool IsToolOrToolrackItem(ItemStack stack)
    {
        return stack.Collectible.Tool != null || stack.ItemAttributes?["toolrackTransform"].Exists == true;
    }

    private static bool IsArmorStandArmor(ItemStack stack)
    {
        return ItemSlotCharacter.IsDressType(stack, EnumCharacterDressType.ArmorBody)
            || ItemSlotCharacter.IsDressType(stack, EnumCharacterDressType.ArmorHead)
            || ItemSlotCharacter.IsDressType(stack, EnumCharacterDressType.ArmorLegs)
            || ArmorStandArmorSlot.TryGetArmor(stack, out _);
    }

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

    public InventoryBase Inventory => inventory;
    public string InventoryClassName => "inventory";

    public EntityBehaviorCOArmorStandInventory(Entity entity) : base(entity)
    {
        eagent = entity as EntityAgent ?? throw new Exception("Armor stand inventory behavior requires EntityAgent");
        inventory = new ArmorStandArmorInventory(null, null);
    }

    public override string PropertyName() => "inventory";

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);

        inventory.LateInitialize("armorstand-" + entity.EntityId, entity.World.Api);
        inventory.SlotModified += OnInventorySlotModified;

        LoadInventory();
        eagent.WatchedAttributes.RegisterModifiedListener(TreeKey, WearablesModified);
        eagent.WatchedAttributes.RegisterModifiedListener(LegacyTreeKey, LegacyWearablesModified);
    }

    private void WearablesModified()
    {
        LoadInventory();
        eagent.MarkShapeModified();
    }

    private void LegacyWearablesModified()
    {
        // Only reload the old vanilla tree if no CO tree exists yet.
        // Once CO has saved its own inventory, wearablesInv is legacy/remove-only source data
        // and should not be allowed to overwrite the live CO armor stand inventory.
        if (eagent.WatchedAttributes.GetTreeAttribute(TreeKey) != null) return;

        LoadInventory();
        eagent.MarkShapeModified();
    }

    private void OnInventorySlotModified(int slotId)
    {
        if (loadingInventory) return;
        SaveInventory();
        eagent.MarkShapeModified();
    }

    private void LoadInventory()
    {
        ITreeAttribute? coTree = eagent.WatchedAttributes.GetTreeAttribute(TreeKey);
        ITreeAttribute? legacyTree = eagent.WatchedAttributes.GetTreeAttribute(LegacyTreeKey);
        ITreeAttribute? tree = coTree ?? legacyTree;

        hasPersistentTree |= coTree != null;

        // Do not use wearablesInv as the live CO save location.  The vanilla armor stand
        // system owns that key and can rewrite it on unload/reload.  That was the real
        // reason placed CO armor rendered in-session but disappeared after chunk reload.
        //
        // CO now persists to coArmorStandInv.  Old ruin/mannequin contents are still read
        // once from wearablesInv if no CO tree exists yet, then immediately copied into
        // coArmorStandInv so later chunk saves preserve them too.
        bool loadedFromLegacyTree = coTree == null && legacyTree != null;
        bool shouldPersistLoadedLegacyTree = entity.World.Side == EnumAppSide.Server
            && loadedFromLegacyTree
            && LooksLikeVanillaArmorStandInventory(legacyTree);

        loadingInventory = true;
        try
        {
            inventory.FromTreeAttributes(tree ?? new TreeAttribute());
        }
        finally
        {
            loadingInventory = false;
        }

        if (shouldPersistLoadedLegacyTree && !inventory.Empty)
        {
            SaveInventory(force: true);
        }
    }

    private bool MigrateVanillaArmorStandContentsIfNeeded(ITreeAttribute? sourceTree)
    {
        if (sourceTree == null || eagent.WatchedAttributes.GetBool(MigratedVanillaInventoryFlag)) return false;
        if (!LooksLikeVanillaArmorStandInventory(sourceTree)) return false;

        bool changed = false;
        bool hadLegacyContent = false;
        bool remainingLegacyContent = false;

        int maxLegacySlot = Math.Min(VanillaArmorStandSlotCount, inventory.Count);
        for (int slotId = 0; slotId < maxLegacySlot; slotId++)
        {
            ItemSlot sourceSlot = inventory[slotId];
            ItemStack? stack = sourceSlot.Itemstack;
            if (stack?.Collectible == null || stack.StackSize <= 0)
            {
                if (stack != null)
                {
                    sourceSlot.Itemstack = null;
                    sourceSlot.MarkDirty();
                    changed = true;
                }
                continue;
            }

            hadLegacyContent = true;
            if (TryMigrateVanillaArmorStandSlot(slotId, sourceSlot))
            {
                changed = true;
            }

            if (!sourceSlot.Empty)
            {
                remainingLegacyContent = true;
            }
        }

        if (!hadLegacyContent || !remainingLegacyContent)
        {
            eagent.WatchedAttributes.SetBool(MigratedVanillaInventoryFlag, true);
            eagent.WatchedAttributes.MarkPathDirty(MigratedVanillaInventoryFlag);
            changed = true;
        }

        return changed;
    }

    private static bool LooksLikeVanillaArmorStandInventory(ITreeAttribute sourceTree)
    {
        int qslots = sourceTree.GetInt("qslots", 0);
        if (qslots > 0 && qslots <= VanillaArmorStandSlotCount) return true;

        ITreeAttribute? slotsTree = sourceTree.GetTreeAttribute("slots");
        if (slotsTree == null) return false;

        bool hasLegacySlot = false;
        for (int slotId = 0; slotId < VanillaArmorStandSlotCount; slotId++)
        {
            if (slotsTree.GetItemstack(slotId.ToString()) != null)
            {
                hasLegacySlot = true;
                break;
            }
        }

        if (!hasLegacySlot) return false;

        int scanCount = ArmorInventory._totalSlotsNumber + 6;
        for (int slotId = VanillaArmorStandSlotCount; slotId < scanCount; slotId++)
        {
            if (slotsTree.GetItemstack(slotId.ToString()) != null) return false;
        }

        return true;
    }

    private bool TryMigrateVanillaArmorStandSlot(int legacySlotId, ItemSlot sourceSlot)
    {
        ItemStack? stack = sourceSlot.Itemstack;
        if (stack?.Collectible == null || stack.StackSize <= 0) return false;

        if (ArmorStandArmorSlot.TryGetArmor(stack, out IArmor? armor) && armor != null)
        {
            return TryMigrateVanillaArmorIntoArmorSlot(legacySlotId, sourceSlot, armor);
        }

        if (IsToolOrToolrackItem(stack))
        {
            return TryMigrateVanillaItemIntoHandSlot(legacySlotId, sourceSlot);
        }

        return TryMigrateVanillaItemIntoStorageSlot(sourceSlot);
    }

    private bool TryMigrateVanillaArmorIntoArmorSlot(int legacySlotId, ItemSlot sourceSlot, IArmor armor)
    {
        ItemStack? stack = sourceSlot.Itemstack;
        if (stack?.Collectible == null || stack.StackSize <= 0) return false;

        foreach (int index in OrderedArmorInsertSlots(legacySlotId, armor.ArmorType))
        {
            if (index < 0 || index >= inventory.Count) continue;
            if (inventory[index] is not ArmorStandArmorSlot targetSlot) continue;
            if (!targetSlot.Empty) continue;
            if (!targetSlot.ArmorType.Intersect(armor.ArmorType)) continue;
            if (!inventory.CanAcceptArmorStack(stack, targetSlot, sourceSlot)) continue;

            MoveWholeStack(sourceSlot, targetSlot);
            return true;
        }

        return false;
    }

    private bool TryMigrateVanillaItemIntoHandSlot(int legacySlotId, ItemSlot sourceSlot)
    {
        if (legacySlotId == VanillaLeftHandSelection)
        {
            if (TryMoveWholeStack(sourceSlot, inventory[ArmorInventory._totalSlotsNumber + 1])) return true;
            return TryMoveWholeStack(sourceSlot, inventory[ArmorInventory._totalSlotsNumber]);
        }

        if (legacySlotId == VanillaRightHandSelection)
        {
            if (TryMoveWholeStack(sourceSlot, inventory[ArmorInventory._totalSlotsNumber])) return true;
            return TryMoveWholeStack(sourceSlot, inventory[ArmorInventory._totalSlotsNumber + 1]);
        }

        if (TryMoveWholeStack(sourceSlot, inventory[ArmorInventory._totalSlotsNumber])) return true;
        return TryMoveWholeStack(sourceSlot, inventory[ArmorInventory._totalSlotsNumber + 1]);
    }

    private bool TryMigrateVanillaItemIntoStorageSlot(ItemSlot sourceSlot)
    {
        for (int index = ArmorInventory._totalSlotsNumber + 2; index < inventory.Count; index++)
        {
            if (TryMoveWholeStack(sourceSlot, inventory[index])) return true;
        }

        return false;
    }

    private static bool TryMoveWholeStack(ItemSlot sourceSlot, ItemSlot targetSlot)
    {
        if (!targetSlot.Empty) return false;
        ItemStack? stack = sourceSlot.Itemstack;
        if (stack?.Collectible == null || stack.StackSize <= 0) return false;

        MoveWholeStack(sourceSlot, targetSlot);
        return true;
    }

    private static void MoveWholeStack(ItemSlot sourceSlot, ItemSlot targetSlot)
    {
        ItemStack? stack = sourceSlot.Itemstack;
        sourceSlot.Itemstack = null;
        targetSlot.Itemstack = stack;
        sourceSlot.MarkDirty();
        targetSlot.MarkDirty();
    }

    public bool HandleInteract(EntityAgent byEntity, ItemSlot slot, Vec3d hitPosition, EnumInteractMode mode)
    {
        if (!eagent.Alive || mode == EnumInteractMode.Attack)
        {
            return true;
        }

        if (entity.World.Side == EnumAppSide.Client)
        {
            return true;
        }

        IPlayer? player = (byEntity as EntityPlayer)?.Player;
        if (player != null && !byEntity.World.Claims.TryAccess(player, entity.Pos.AsBlockPos, EnumBlockAccessFlags.Use))
        {
            player.InventoryManager.ActiveHotbarSlot.MarkDirty();
            entity.WatchedAttributes.MarkAllDirty();
            return true;
        }

        ItemSlot handslot = slot;

        if (mode != EnumInteractMode.Interact)
        {
            return false;
        }

        if (handslot.Itemstack?.Collectible is ItemWrench)
        {
            CyclePose();
            return true;
        }

        int selectionBoxIndex = player?.CurrentEntitySelection?.SelectionBoxIndex ?? -1;

        if (handslot.Empty)
        {
            if (TryTakeSelected(byEntity, handslot, selectionBoxIndex, hitPosition))
            {
                eagent.MarkShapeModified();
                return true;
            }

            if (byEntity.Controls.ShiftKey && inventory.Empty)
            {
                Item? standItem = byEntity.World.GetItem(entity.Code);
                if (standItem != null)
                {
                    ItemStack stack = new(standItem);
                    if (!byEntity.TryGiveItemStack(stack))
                    {
                        byEntity.World.SpawnItemEntity(stack, entity.ServerPos.XYZ);
                    }
                    byEntity.World.Logger.Audit("{0} Took 1x{1} from Armor Stand at {2}.", byEntity.GetName(), stack.Collectible.Code, entity.ServerPos.AsBlockPos);
                }

                eagent.Die();
                return true;
            }

            return true;
        }

        ItemStack? heldStack = handslot.Itemstack;
        if (heldStack?.Collectible == null || heldStack.StackSize <= 0)
        {
            handslot.Itemstack = null;
            handslot.MarkDirty();
            return true;
        }

        if (IsToolOrToolrackItem(heldStack))
        {
            AssetLocation code = heldStack.Collectible.Code;
            int moved = TryPutToolIntoHandSlots(handslot, selectionBoxIndex, hitPosition);
            if (moved > 0)
            {
                eagent.MarkShapeModified();
                byEntity.World.Logger.Audit("{0} Put 1x{1} onto Armor Stand at {2}.", byEntity.GetName(), code, entity.ServerPos.AsBlockPos);
            }
            return true;
        }

        if (!IsArmorStandArmor(heldStack))
        {
            return true;
        }

        AssetLocation? armorCode = heldStack.Collectible.Code;
        if (TryPutArmorIntoArmorSlots(handslot, selectionBoxIndex, hitPosition) > 0)
        {
            eagent.MarkShapeModified();
            byEntity.World.Logger.Audit("{0} Put 1x{1} onto Armor Stand at {2}.", byEntity.GetName(), armorCode, entity.ServerPos.AsBlockPos);
        }

        return true;
    }

    private void CyclePose()
    {
        string[] poses = ["idle", "lefthandup", "righthandup", "twohandscross"];
        int curPose = entity.WatchedAttributes.GetInt("curPose");
        eagent.AnimManager.StopAnimation(poses[curPose]);
        curPose = (curPose + 1) % poses.Length;
        eagent.WatchedAttributes.SetInt("curPose", curPose);
        eagent.AnimManager.StartAnimation(new AnimationMetaData { Animation = poses[curPose], Code = poses[curPose] }.Init());
        eagent.WatchedAttributes.MarkPathDirty("curPose");
    }

    private void SaveInventory(bool force = false)
    {
        if (entity.World.Side != EnumAppSide.Server || (!force && !hasPersistentTree && inventory.Empty)) return;

        TreeAttribute tree = new();
        inventory.ToTreeAttributes(tree);

        // Save CO contents under a CO-owned key.  Do not overwrite wearablesInv here;
        // that key is the old vanilla/ruin inventory source and is kept only as
        // remove-only legacy input.
        eagent.WatchedAttributes[TreeKey] = tree;
        eagent.WatchedAttributes.MarkPathDirty(TreeKey);
        hasPersistentTree = true;
    }

    public bool TryTakeSelected(EntityAgent byEntity, ItemSlot handslot, int selectionBoxIndex, Vec3d hitPosition)
    {
        if (!handslot.Empty) return false;

        // Ruin/mannequin armor stands store their generated contents in the old vanilla slots
        // 0..4 of wearablesInv.  Keep those slots remove-only and try them before the CO
        // layered armor slots so players can actually loot generated stands.
        foreach (int legacyIndex in OrderedVanillaArmorStandSlots(selectionBoxIndex, hitPosition))
        {
            if (TryTakeFromSlotOrRawTree(byEntity, handslot, legacyIndex, out int moved))
            {
                SaveInventory();
                eagent.MarkShapeModified();
                byEntity.World.Logger.Audit("{0} Took {1}x{2} from vanilla Armor Stand slot at {3}.", byEntity.GetName(), moved, handslot.Itemstack?.Collectible.Code, entity.ServerPos.AsBlockPos);
                return true;
            }
        }

        foreach (int index in OrderedCoSlots(selectionBoxIndex, hitPosition))
        {
            if (index < 0 || index >= inventory.Count) continue;
            if (index < VanillaArmorStandSlotCount) continue;

            if (TryMoveWholeStackToEmptyHand(inventory[index], handslot, out int moved))
            {
                SaveInventory();
                eagent.MarkShapeModified();
                byEntity.World.Logger.Audit("{0} Took {1}x{2} from Armor Stand at {3}.", byEntity.GetName(), moved, handslot.Itemstack?.Collectible.Code, entity.ServerPos.AsBlockPos);
                return true;
            }
        }

        return false;
    }

    private bool TryTakeFromSlotOrRawTree(EntityAgent byEntity, ItemSlot handslot, int slotIndex, out int moved)
    {
        moved = 0;
        if (slotIndex < 0 || slotIndex >= VanillaArmorStandSlotCount || !handslot.Empty) return false;

        if (slotIndex < inventory.Count && TryMoveWholeStackToEmptyHand(inventory[slotIndex], handslot, out moved))
        {
            return true;
        }

        // Fallback for generated ruin stands if another behavior/older save kept the item only
        // in the raw watched attribute tree.  This makes the old vanilla slots removable
        // without re-enabling the vanilla container behavior.
        ITreeAttribute? rawTree = eagent.WatchedAttributes.GetTreeAttribute(TreeKey);
        string rawTreeKey = TreeKey;
        ITreeAttribute? slotsTree = rawTree?.GetTreeAttribute("slots");
        if (slotsTree == null)
        {
            rawTree = eagent.WatchedAttributes.GetTreeAttribute(LegacyTreeKey);
            rawTreeKey = LegacyTreeKey;
            slotsTree = rawTree?.GetTreeAttribute("slots");
        }
        if (slotsTree == null) return false;

        string key = slotIndex.ToString();
        ItemStack? stack = slotsTree.GetItemstack(key);
        if (stack == null) return false;

        if (entity.World != null)
        {
            stack.ResolveBlockOrItem(entity.World);
        }

        if (stack.Collectible == null || stack.StackSize <= 0)
        {
            slotsTree.RemoveAttribute(key);
            eagent.WatchedAttributes.MarkPathDirty(rawTreeKey);
            return false;
        }

        moved = stack.StackSize;
        slotsTree.RemoveAttribute(key);

        if (slotIndex < inventory.Count)
        {
            inventory[slotIndex].Itemstack = null;
        }

        handslot.Itemstack = stack;
        handslot.MarkDirty();
        eagent.WatchedAttributes.MarkPathDirty(rawTreeKey);
        if (rawTreeKey == TreeKey) hasPersistentTree = true;
        return true;
    }

    public int TryPutToolIntoHandSlots(ItemSlot handslot, int selectionBoxIndex, Vec3d hitPosition)
    {
        int targetedSlot = VanillaSlotFromInteraction(selectionBoxIndex, hitPosition);
        int effectiveSelection = targetedSlot >= VanillaHeadSelection && targetedSlot <= VanillaLeftHandSelection
            ? targetedSlot
            : selectionBoxIndex;

        int moved;
        if (effectiveSelection == VanillaRightHandSelection)
        {
            moved = handslot.TryPutInto(entity.World, inventory[ArmorInventory._totalSlotsNumber]);
            if (moved > 0) SaveInventory(force: true);
            return moved;
        }
        if (effectiveSelection == VanillaLeftHandSelection)
        {
            moved = handslot.TryPutInto(entity.World, inventory[ArmorInventory._totalSlotsNumber + 1]);
            if (moved > 0) SaveInventory(force: true);
            return moved;
        }

        moved = handslot.TryPutInto(entity.World, inventory[ArmorInventory._totalSlotsNumber]);
        if (moved == 0)
        {
            moved = handslot.TryPutInto(entity.World, inventory[ArmorInventory._totalSlotsNumber + 1]);
        }
        if (moved > 0) SaveInventory(force: true);
        return moved;
    }

    public int TryPutArmorIntoArmorSlots(ItemSlot handslot, int selectionBoxIndex, Vec3d hitPosition)
    {
        ItemStack? sourceStack = handslot.Itemstack;
        if (sourceStack?.Collectible == null || !ArmorStandArmorSlot.TryGetArmor(sourceStack, out IArmor? armor) || armor == null)
        {
            return 0;
        }

        if (!inventory.CanAcceptArmorStack(sourceStack))
        {
            return 0;
        }

        foreach (int index in OrderedArmorInsertSlots(selectionBoxIndex, hitPosition, armor.ArmorType))
        {
            if (index < 0 || index >= inventory.Count) continue;
            if (inventory[index] is not ArmorStandArmorSlot targetSlot) continue;
            if (!targetSlot.Empty) continue;
            if (!targetSlot.ArmorType.Intersect(armor.ArmorType)) continue;
            if (!targetSlot.CanHold(handslot)) continue;

            int moved = handslot.TryPutInto(entity.World, targetSlot);
            if (moved > 0) SaveInventory(force: true);
            return moved;
        }

        return 0;
    }

    private IEnumerable<int> OrderedArmorInsertSlots(int selectionBoxIndex, ArmorType sourceArmorType)
    {
        return OrderedArmorInsertSlots(selectionBoxIndex, new Vec3d(0, 0, 0), sourceArmorType);
    }

    private IEnumerable<int> OrderedArmorInsertSlots(int selectionBoxIndex, Vec3d hitPosition, ArmorType sourceArmorType)
    {
        HashSet<int> yielded = new();
        int targetedSlot = VanillaSlotFromInteraction(selectionBoxIndex, hitPosition);
        int effectiveSelection = targetedSlot >= VanillaHeadSelection && targetedSlot <= VanillaLeftHandSelection
            ? targetedSlot
            : selectionBoxIndex;

        DamageZone preferredZone = effectiveSelection switch
        {
            VanillaHeadSelection => DamageZone.Head | DamageZone.Face | DamageZone.Neck,
            VanillaBodySelection => DamageZone.Torso | DamageZone.Arms | DamageZone.Hands,
            VanillaLegsSelection => DamageZone.Legs | DamageZone.Feet,
            _ => DamageZone.None
        };

        if (preferredZone != DamageZone.None)
        {
            for (int i = ArmorInventory._vanillaSlots; i < ArmorInventory._armorSlotsLastIndex; i++)
            {
                ArmorType slotType = ArmorInventory.ArmorTypeFromIndex(i);
                if ((slotType.Slots & preferredZone) == DamageZone.None) continue;
                if (!slotType.Intersect(sourceArmorType)) continue;
                if (yielded.Add(i)) yield return i;
            }
        }

        for (int i = ArmorInventory._vanillaSlots; i < ArmorInventory._armorSlotsLastIndex; i++)
        {
            ArmorType slotType = ArmorInventory.ArmorTypeFromIndex(i);
            if (!slotType.Intersect(sourceArmorType)) continue;
            if (yielded.Add(i)) yield return i;
        }
    }

    private bool TryMoveWholeStackToEmptyHand(ItemSlot sourceSlot, ItemSlot handslot, out int moved)
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

        // Do not use the vanilla armor-stand detach path here.  The vanilla stand
        // only exposes old wearablesInv slots through a specific "ctrl-right click
        // to detach" attachment target. CO replaces that targeting, so transfer
        // directly from the stored slot into the actual player interaction slot.
        // TryPutInto goes through normal slot/inventory dirtying instead of silently
        // assigning to the wrong right-hand/equipment slot.
        moved = sourceSlot.TryPutInto(entity.World, handslot);
        if (moved <= 0)
        {
            return false;
        }

        sourceSlot.MarkDirty();
        handslot.MarkDirty();
        return true;
    }

    private int VanillaSlotFromInteraction(int selectionBoxIndex, Vec3d hitPosition)
    {
        if (selectionBoxIndex >= VanillaHeadSelection && selectionBoxIndex <= VanillaLeftHandSelection)
        {
            return selectionBoxIndex;
        }

        // Fallback target for CO armor stands.  Vanilla armor stands require the
        // player to look at a specific attachment target before the old slot can be
        // detached.  CO's entity/selection setup can hide that target, so estimate
        // the intended vanilla slot from the actual hit position instead.
        if (hitPosition == null) return -1;

        double localY = LocalHitY(hitPosition);
        if (double.IsNaN(localY)) return -1;

        if (localY >= 1.45) return VanillaHeadSelection;
        if (localY >= 0.75) return VanillaBodySelection;
        return VanillaLegsSelection;
    }

    private double LocalHitY(Vec3d hitPosition)
    {
        double y = hitPosition.Y;

        // Some entity interactions pass world hit coordinates; some pass local hit
        // coordinates.  Prefer the interpretation that lands inside an armor-stand
        // sized vertical range.
        double localFromEntity = y - entity.Pos.Y;
        if (localFromEntity >= -0.25 && localFromEntity <= 2.75) return localFromEntity;

        double localFromServerPos = y - entity.ServerPos.Y;
        if (localFromServerPos >= -0.25 && localFromServerPos <= 2.75) return localFromServerPos;

        if (y >= -0.25 && y <= 2.75) return y;

        return localFromEntity;
    }

    private IEnumerable<int> OrderedVanillaArmorStandSlots(int selectionBoxIndex, Vec3d hitPosition)
    {
        int targetedSlot = VanillaSlotFromInteraction(selectionBoxIndex, hitPosition);
        if (targetedSlot >= VanillaHeadSelection && targetedSlot <= VanillaLeftHandSelection)
        {
            yield return targetedSlot;
        }

        if (selectionBoxIndex >= VanillaHeadSelection && selectionBoxIndex <= VanillaLeftHandSelection && selectionBoxIndex != targetedSlot)
        {
            yield return selectionBoxIndex;
        }

        for (int i = 0; i < VanillaArmorStandSlotCount; i++)
        {
            if (i != targetedSlot && i != selectionBoxIndex) yield return i;
        }
    }

    private IEnumerable<int> OrderedCoSlots(int selectionBoxIndex, Vec3d hitPosition)
    {
        int targetedSlot = VanillaSlotFromInteraction(selectionBoxIndex, hitPosition);
        int effectiveSelection = targetedSlot >= VanillaHeadSelection && targetedSlot <= VanillaLeftHandSelection
            ? targetedSlot
            : selectionBoxIndex;

        if (effectiveSelection == VanillaRightHandSelection)
        {
            yield return ArmorInventory._totalSlotsNumber;
        }
        else if (effectiveSelection == VanillaLeftHandSelection)
        {
            yield return ArmorInventory._totalSlotsNumber + 1;
        }
        else if (effectiveSelection >= VanillaHeadSelection && effectiveSelection <= VanillaLegsSelection)
        {
            DamageZone zone = effectiveSelection switch
            {
                VanillaHeadSelection => DamageZone.Head | DamageZone.Face | DamageZone.Neck,
                VanillaBodySelection => DamageZone.Torso | DamageZone.Arms | DamageZone.Hands,
                VanillaLegsSelection => DamageZone.Legs | DamageZone.Feet,
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

        for (int i = VanillaArmorStandSlotCount; i < inventorySlotSearchCount; i++)
        {
            yield return i;
        }
    }

    private static int inventorySlotSearchCount => ArmorInventory._totalSlotsNumber + 2;

    public override void OnTesselation(ref Shape entityShape, string shapePathForLogging, ref bool shapeIsCloned, ref string[] willDeleteElements)
    {
        if (entityShape == null)
        {
            return;
        }

        for (int i = 0; i < inventory.Count; i++)
        {
            RenderSlot(i, inventory[i], shapePathForLogging, ref entityShape, ref shapeIsCloned, ref willDeleteElements);
        }
    }

    private void RenderSlot(int slotIndex, ItemSlot slot, string shapePathForLogging, ref Shape entityShape, ref bool shapeIsCloned, ref string[] willDeleteElements)
    {
        if (slot.Empty || slot.Itemstack?.Collectible == null)
        {
            return;
        }

        if (!TryGetRenderSlot(slotIndex, slot.Itemstack, out string slotCode, out Dictionary<string, StepParentElementTo>? stepParent))
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

    private static bool TryGetRenderSlot(int slotIndex, ItemStack stack, out string slotCode, out Dictionary<string, StepParentElementTo>? stepParent)
    {
        slotCode = "";
        stepParent = null;

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

        return TryGetArmorStandRenderSlot(stack, out slotCode, out stepParent);
    }

    private static bool TryGetArmorStandRenderSlot(ItemStack stack, out string slotCode, out Dictionary<string, StepParentElementTo>? stepParent)
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

        if (!ArmorStandArmorSlot.TryGetArmor(stack, out IArmor? armor) || armor == null)
        {
            return false;
        }

        ArmorType type = armor.ArmorType;
        if ((type.Slots & (DamageZone.Head | DamageZone.Face | DamageZone.Neck)) != DamageZone.None)
        {
            slotCode = "head";
            stepParent = HeadStepParent;
            return true;
        }
        if ((type.Slots & (DamageZone.Torso | DamageZone.Arms | DamageZone.Hands)) != DamageZone.None)
        {
            slotCode = "body";
            stepParent = BodyStepParent;
            return true;
        }
        if ((type.Slots & (DamageZone.Legs | DamageZone.Feet)) != DamageZone.None)
        {
            slotCode = "legs";
            stepParent = LegsStepParent;
            return true;
        }

        return false;
    }

    public override void FromBytes(bool isSync)
    {
        LoadInventory();
        eagent.MarkShapeModified();
    }

    public override void ToBytes(bool forClient)
    {
        SaveInventory();
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        SaveInventory();
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

        SaveInventory();
    }
}

public class ArmorStandArmorSlot : ItemSlot
{
    public ArmorType ArmorType { get; }
    public ArmorType StoredArmoredType => GetStoredArmorType();
    public override int MaxSlotStackSize => 1;

    public ArmorStandArmorSlot(InventoryBase inventory, ArmorType armorType) : base(inventory)
    {
        ArmorType = armorType;
        _inventory = inventory as ArmorStandArmorInventory ?? throw new Exception();
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (DrawUnavailable || !base.CanHold(sourceSlot) || sourceSlot.Itemstack == null || !TryGetArmor(sourceSlot.Itemstack, out IArmor? armor)) return false;

        if (armor == null || !_inventory.CanHoldArmorPiece(armor, sourceSlot.Itemstack, this)) return false;

        return armor.ArmorType.Intersect(ArmorType);
    }

    private readonly ArmorStandArmorInventory _inventory;

    private ArmorType GetStoredArmorType()
    {
        if (Itemstack?.Collectible != null && TryGetArmor(Itemstack, out IArmor? armor) && armor != null)
        {
            return armor.ArmorType;
        }
        else
        {
            return ArmorType.Empty;
        }
    }

    public static bool TryGetArmor(ItemStack stack, out IArmor? armor)
    {
        if (stack.Collectible == null)
        {
            armor = null;
            return false;
        }

        if (stack.Collectible is IArmor armorItem)
        {
            armor = armorItem;
            return true;
        }

        CollectibleBehavior? behavior = stack.Collectible.CollectibleBehaviors.FirstOrDefault(x => x is IArmor);

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
        // Use the engine inventory serializer/deserializer here.  The manual
        // slots-tree reader could render newly inserted armor for the current
        // session, but it did not round-trip the same inventory format as
        // InventoryBase.SlotsToTreeAttributes(), so placed armor could vanish
        // after chunk unload/reload.
        List<ItemSlot> modifiedSlots = new();
        ItemSlot[] loadedSlots = SlotsFromTreeAttributes(tree, GenEmptySlots(ArmorInventory._totalSlotsNumber + 6), modifiedSlots);

        if (loadedSlots.Length == ArmorInventory._totalSlotsNumber + 6)
        {
            slots = loadedSlots;
        }
        else
        {
            slots = GenEmptySlots(ArmorInventory._totalSlotsNumber + 6);
            int copyCount = Math.Min(loadedSlots.Length, slots.Length);
            for (int i = 0; i < copyCount; i++)
            {
                slots[i].Itemstack = loadedSlots[i].Itemstack;
            }
        }

        // Do not call DidModifyItemSlot() while loading entity attributes.
        // During chunk/entity initialization patched inventory code can null-ref
        // there, deleting generated armor stands from the chunk.  Slot changes
        // after loading are still saved explicitly by the behavior.
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        SlotsToTreeAttributes(slots, tree);
    }

    public bool IsSlotAvailable(ArmorType armorType) => CanHoldArmorPiece(armorType);

    public bool CanHoldArmorPiece(ArmorType armorType) => CanHoldArmorPiece(armorType, null);

    public bool CanHoldArmorPiece(ArmorType armorType, ItemSlot? targetSlot) => CanHoldArmorPiece(armorType, targetSlot, null);

    public bool CanHoldArmorPiece(ArmorType armorType, ItemSlot? targetSlot, ItemSlot? ignoredSourceSlot)
    {
        foreach (ItemSlot slot in slots)
        {
            if (ReferenceEquals(slot, targetSlot) || ReferenceEquals(slot, ignoredSourceSlot) || slot.Empty) continue;
            ItemStack? storedStack = slot.Itemstack;
            if (storedStack?.Collectible == null) continue;
            if (!ArmorStandArmorSlot.TryGetArmor(storedStack, out IArmor? storedArmor) || storedArmor == null) continue;
            if (storedArmor.ArmorType.Intersect(armorType)) return false;
        }

        return true;
    }

    public bool CanHoldArmorPiece(IArmor armor) => CanHoldArmorPiece(armor.ArmorType);

    public bool CanHoldArmorPiece(IArmor armor, ItemStack sourceStack, ItemSlot targetSlot)
    {
        return CanAcceptArmorStack(sourceStack, targetSlot);
    }

    public bool CanAcceptArmorStack(ItemStack sourceStack, ItemSlot? targetSlot = null) => CanAcceptArmorStack(sourceStack, targetSlot, null);

    public bool CanAcceptArmorStack(ItemStack sourceStack, ItemSlot? targetSlot, ItemSlot? ignoredSourceSlot)
    {
        if (sourceStack.Collectible == null || !ArmorStandArmorSlot.TryGetArmor(sourceStack, out IArmor? sourceArmor) || sourceArmor == null)
        {
            return false;
        }

        if (ContainsSameArmorPiece(sourceStack, targetSlot, ignoredSourceSlot)) return false;
        return CanHoldArmorPiece(sourceArmor.ArmorType, targetSlot, ignoredSourceSlot);
    }

    public bool ContainsSameArmorPiece(ItemStack sourceStack, ItemSlot? targetSlot = null) => ContainsSameArmorPiece(sourceStack, targetSlot, null);

    public bool ContainsSameArmorPiece(ItemStack sourceStack, ItemSlot? targetSlot, ItemSlot? ignoredSourceSlot)
    {
        AssetLocation? sourceCode = sourceStack.Collectible?.Code;
        if (sourceCode == null) return false;

        foreach (ItemSlot slot in slots)
        {
            if (ReferenceEquals(slot, targetSlot) || ReferenceEquals(slot, ignoredSourceSlot) || slot.Empty) continue;
            if (slot.Itemstack?.Collectible?.Code?.Equals(sourceCode) == true) return true;
        }

        return false;
    }

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
        // Death drops are handled by EntityBehaviorCOArmorStandInventory to avoid duplicate drops.
    }
}
