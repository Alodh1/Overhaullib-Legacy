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
public class WearableArmorBehavior : CollectibleBehavior
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
