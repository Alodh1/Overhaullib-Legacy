using CombatOverhaul.DamageSystems;
using CombatOverhaul.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace CombatOverhaul.Armor;

/// <summary>
/// 1.22+/1.23-safe replacement for the old ItemWearable-derived CombatOverhaul:WearableArmor item class.
/// Put this behavior on a normal Item together with the vanilla Wearable behavior.
/// </summary>
public class WearableArmorBehavior : CollectibleBehavior, IWearableStatsSupplier
{
    protected ICoreAPI? api;

    public WearableArmorBehavior(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        this.api = api;
    }

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling, ref EnumHandling handling)
    {
        // Shift-right-click should keep vanilla Wearable behavior semantics.
        if (byEntity.Controls.ShiftKey)
        {
            handling = EnumHandling.PassThrough;
            return;
        }

        if (slot.Itemstack?.Item == null) return;

        IPlayer? player = (byEntity as EntityPlayer)?.Player;
        if (player == null) return;

        ArmorInventory? inventory = GetGearInventory(byEntity) as ArmorInventory;
        if (inventory == null) return;

        ArmorBehavior? behavior = collObj.GetCollectibleBehavior<ArmorBehavior>(true);
        if (behavior == null) return;

        string code = slot.Itemstack.Item.Code;
        ArmorType armorType = behavior.ArmorType;

        try
        {
            IEnumerable<int> slots = inventory.GetSlotBlockingSlotsIndices(armorType);

            foreach (int index in slots)
            {
                ItemStack stack = inventory[index].TakeOutWhole();
                if (!player.InventoryManager.TryGiveItemstack(stack))
                {
                    byEntity.Api.World.SpawnItemEntity(stack, byEntity.ServerPos.AsBlockPos);
                }
                inventory[index].MarkDirty();
            }

            int slotIndex = inventory.GetFittingSlotIndex(armorType);
            inventory[slotIndex].TryFlipWith(slot);

            inventory[slotIndex].MarkDirty();
            slot.MarkDirty();

            handHandling = EnumHandHandling.PreventDefault;
            handling = EnumHandling.PreventSubsequent;
        }
        catch (Exception exception)
        {
            LoggerUtil.Error(api, this, $"Error on equipping '{code}' that occupies {armorType}:\n{exception}");
        }
    }

    public override void OnCreatedByCrafting(ItemSlot[] allInputSlots, ItemSlot outputSlot, IRecipeBase byRecipe, ref EnumHandling bhHandling)
    {
        bool isRepairRecipe = IsRepairRecipe(byRecipe);
        int newDurability = 0;

        if (outputSlot is not DummySlot)
        {
            EnsureConditionExists(outputSlot);
            outputSlot.Itemstack.Attributes.SetFloat("condition", 1);

            if (isRepairRecipe)
            {
                CalculateRepairValueProperly(allInputSlots, outputSlot, out float repairValue, out int _);

                int curDur = outputSlot.Itemstack.Collectible.GetRemainingDurability(outputSlot.Itemstack);
                int maxDur = outputSlot.Itemstack.Collectible.GetMaxDurability(outputSlot.Itemstack);

                newDurability = Math.Min(maxDur, (int)(curDur + maxDur * repairValue));
            }
        }

        // Repair recipes are fully handled here; letting vanilla Wearable run after this can crash on CO armor JSON.
        bhHandling = isRepairRecipe ? EnumHandling.PreventSubsequent : EnumHandling.PassThrough;

        // Prevent derp in the handbook
        if (outputSlot is DummySlot) return;

        if (isRepairRecipe)
        {
            outputSlot.Itemstack.Attributes.SetInt("durability", newDurability);
        }
    }

    public override bool ConsumeCraftingIngredients(ItemSlot[] slots, ItemSlot outputSlot, IRecipeBase matchingRecipe, ref EnumHandling handling)
    {
        // Consume as much materials in the input grid as needed
        if (IsRepairRecipe(matchingRecipe))
        {
            CalculateRepairValueProperly(slots, outputSlot, out float _, out int matCostPerMatType);

            foreach (ItemSlot islot in slots)
            {
                if (islot.Empty) continue;

                if (IsArmorSlot(islot))
                {
                    islot.Itemstack = null;
                    continue;
                }

                islot.TakeOut(matCostPerMatType);
            }

            handling = EnumHandling.PreventSubsequent;
            return true;
        }

        handling = EnumHandling.PassThrough;
        return false;
    }

    private static bool IsRepairRecipe(IRecipeBase? recipe)
    {
        return recipe?.Name?.Path?.Contains("repair", StringComparison.OrdinalIgnoreCase) == true;
    }

    public bool IsArmorType(ItemSlot slot)
    {
        return GetVanillaWearableBehavior()?.IsArmorType(slot) ?? collObj.GetCollectibleBehavior<ArmorBehavior>(true) != null;
    }

    public EnumCharacterDressType GetDressType(ItemSlot slot)
    {
        return GetVanillaWearableBehavior()?.GetDressType(slot) ?? EnumCharacterDressType.Unknown;
    }

    public StatModifiers? GetStatModifiers(ItemSlot slot)
    {
        return GetVanillaWearableBehavior()?.GetStatModifiers(slot);
    }

    public ProtectionModifiers? GetProtectionModifiers(ItemSlot slot)
    {
        return GetVanillaWearableBehavior()?.GetProtectionModifiers(slot);
    }

    public AssetLocation[] GetFootStepSounds(ItemSlot slot)
    {
        AssetLocation[]? sounds = GetVanillaWearableBehavior()?.GetFootStepSounds(slot);
        if (sounds == null || sounds.Length == 0) return Array.Empty<AssetLocation>();

        return IsSelectedFootStepArmor(slot) ? sounds : Array.Empty<AssetLocation>();
    }

    public float GetMaxWarmth(ItemSlot slot)
    {
        return GetVanillaWearableBehavior()?.GetMaxWarmth(slot) ?? 0f;
    }

    private CollectibleBehaviorWearable? GetVanillaWearableBehavior()
    {
        return collObj.GetBehavior<CollectibleBehaviorWearable>();
    }

    private static bool IsSelectedFootStepArmor(ItemSlot slot)
    {
        if (slot.Inventory == null || slot.Itemstack == null) return true;

        (int soundPriority, int zonePriority, int slotId) current = GetFootStepArmorPriority(slot);
        if (current.soundPriority <= 0) return true;

        foreach (ItemSlot candidate in slot.Inventory)
        {
            if (ReferenceEquals(candidate, slot) || candidate.Empty) continue;

            (int soundPriority, int zonePriority, int slotId) other = GetFootStepArmorPriority(candidate);
            if (other.soundPriority <= 0) continue;

            if (other.soundPriority > current.soundPriority) return false;
            if (other.soundPriority < current.soundPriority) continue;

            if (other.zonePriority > current.zonePriority) return false;
            if (other.zonePriority < current.zonePriority) continue;

            if (other.slotId >= 0 && (current.slotId < 0 || other.slotId < current.slotId)) return false;
        }

        return true;
    }

    private static (int soundPriority, int zonePriority, int slotId) GetFootStepArmorPriority(ItemSlot slot)
    {
        ItemStack? stack = slot.Itemstack;
        CollectibleObject? collectible = stack?.Collectible;
        if (collectible == null) return (0, 0, -1);

        if (collectible.GetCollectibleBehavior<ArmorBehavior>(true) == null) return (0, 0, -1);

        AssetLocation[]? sounds = collectible.GetBehavior<CollectibleBehaviorWearable>()?.GetFootStepSounds(slot);
        if (sounds == null || sounds.Length == 0) return (0, 0, -1);

        int slotId = slot.Inventory?.GetSlotId(slot) ?? -1;
        return (GetSoundPriority(collectible), GetZonePriority(collectible), slotId);
    }

    private static int GetSoundPriority(CollectibleObject collectible)
    {
        string code = collectible.Code?.Path?.ToLowerInvariant() ?? "";
        string sound = collectible.Attributes?["footStepSound"].AsString("")?.ToLowerInvariant() ?? "";
        string value = $"{code} {sound}";

        if (value.Contains("plate")) return 60;
        if (value.Contains("scale")) return 50;
        if (value.Contains("chain")) return 45;
        if (value.Contains("brigandine")) return 40;
        if (value.Contains("lamellar")) return 30;
        if (value.Contains("leather") || value.Contains("jerkin") || value.Contains("hide")) return 20;

        return 10;
    }

    private static int GetZonePriority(CollectibleObject collectible)
    {
        ArmorType armorType = collectible.GetCollectibleBehavior<ArmorBehavior>(true)?.ArmorType ?? ArmorType.Empty;

        if ((armorType.Slots & DamageZone.Torso) != 0) return 700;
        if ((armorType.Slots & DamageZone.Legs) != 0) return 600;
        if ((armorType.Slots & DamageZone.Feet) != 0) return 500;
        if ((armorType.Slots & DamageZone.Arms) != 0) return 400;
        if ((armorType.Slots & DamageZone.Hands) != 0) return 300;
        if ((armorType.Slots & DamageZone.Head) != 0) return 200;
        if ((armorType.Slots & DamageZone.Neck) != 0) return 100;
        if ((armorType.Slots & DamageZone.Face) != 0) return 90;

        return 0;
    }

    protected static InventoryBase? GetGearInventory(Entity entity)
    {
        return entity.GetBehavior<EntityBehaviorPlayerInventory>().Inventory;
    }

    protected virtual void EnsureConditionExists(ItemSlot slot)
    {
        // Prevent derp in the handbook
        if (slot is DummySlot) return;

        if (!slot.Itemstack.Attributes.HasAttribute("condition") && api?.Side == EnumAppSide.Server)
        {
            if (slot.Itemstack.ItemAttributes?["warmth"].Exists == true && slot.Itemstack.ItemAttributes?["warmth"].AsFloat() != 0)
            {
                if (slot is ItemSlotTrade)
                {
                    slot.Itemstack.Attributes.SetFloat("condition", (float)api.World.Rand.NextDouble() * 0.25f + 0.75f);
                }
                else
                {
                    slot.Itemstack.Attributes.SetFloat("condition", (float)api.World.Rand.NextDouble() * 0.4f);
                }

                slot.MarkDirty();
            }
        }
    }

    protected virtual void CalculateRepairValueProperly(ItemSlot[] inSlots, ItemSlot outputSlot, out float repairValue, out int matCostPerMatType)
    {
        repairValue = 0;
        matCostPerMatType = 0;

        ItemSlot? armorSlot = inSlots.FirstOrDefault(IsArmorSlot);
        if (armorSlot?.Itemstack == null || outputSlot.Itemstack == null) return;

        int origMatCount = GetOrigMatCount(inSlots, outputSlot);
        if (origMatCount == 0)
        {
            origMatCount = collObj.Attributes?["materialCount"].AsInt(1) ?? 1;
        }

        int curDur = outputSlot.Itemstack.Collectible.GetRemainingDurability(armorSlot.Itemstack);
        int maxDur = outputSlot.Itemstack.Collectible.GetMaxDurability(outputSlot.Itemstack);

        // How much 1x mat repairs in %
        float repairValuePerItem = 2f / origMatCount;
        // How much the mat repairs in durability
        float repairDurabilityPerItem = repairValuePerItem * maxDur;
        // Divide missing durability by repair per item = items needed for full repair
        int fullRepairMatCount = (int)Math.Max(1, Math.Round((maxDur - curDur) / repairDurabilityPerItem));
        // Limit repair value to smallest stack size of all repair mats
        int minMatStackSize = GetInputRepairCount(inSlots);
        // Divide the cost amongst all mats
        int matTypeCount = GetRepairMatTypeCount(inSlots);

        int availableRepairMatCount = Math.Min(fullRepairMatCount, minMatStackSize * matTypeCount);
        matCostPerMatType = Math.Min(fullRepairMatCount, minMatStackSize);

        // Repairing costs half as many materials as newly creating it
        repairValue = (float)availableRepairMatCount / origMatCount * 2;
    }

    protected virtual int GetRepairMatTypeCount(ItemSlot[] slots)
    {
        List<ItemStack> stackTypes = new();
        foreach (ItemSlot slot in slots)
        {
            if (slot.Empty) continue;
            bool found = false;
            if (IsArmorSlot(slot)) continue;

            foreach (ItemStack stack in stackTypes)
            {
                if (slot.Itemstack.Satisfies(stack))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                stackTypes.Add(slot.Itemstack);
            }
        }

        return stackTypes.Count;
    }

    protected virtual int GetOrigMatCount(ItemSlot[] inSlots, ItemSlot outputSlot)
    {
        int count = 0;
        foreach (ItemSlot slot in inSlots)
        {
            if (slot.Empty || IsArmorSlot(slot)) continue;
            count += Math.Max(1, slot.StackSize);
        }
        return count;
    }

    protected virtual int GetInputRepairCount(ItemSlot[] inSlots)
    {
        int min = int.MaxValue;
        foreach (ItemSlot slot in inSlots)
        {
            if (slot.Empty || IsArmorSlot(slot)) continue;
            min = Math.Min(min, slot.StackSize);
        }
        return min == int.MaxValue ? 0 : min;
    }

    protected static bool IsArmorSlot(ItemSlot slot)
    {
        CollectibleObject? collectible = slot.Itemstack?.Collectible;
        if (collectible == null) return false;

#pragma warning disable CS0618
        if (collectible is ItemWearable) return true;
#pragma warning restore CS0618

        return collectible.GetCollectibleBehavior<ArmorBehavior>(true) != null;
    }
}
