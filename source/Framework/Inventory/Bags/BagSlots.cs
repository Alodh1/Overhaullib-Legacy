using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace CombatOverhaul.Armor;

public class ItemSlotBagContentWithWildcardMatch : ItemSlotBagContent, IHasSlotBackpackCategory
{
    public ItemStack SourceBag { get; set; }
    public SlotConfig Config { get; set; } = new([], []);
    public string BackpackCategoryCode => Config.BackpackCategoryCode;
    public float OrderPriority => Config.OrderPriority;
    public string ToolBagId { get; set; }
    public int ToolBagIndex { get; set; }
    public bool MainHand { get; set; } = true;

    public ItemSlotBagContentWithWildcardMatch(InventoryBase inventory, int BagIndex, int slotIndex, EnumItemStorageFlags storageType, ItemStack sourceBag, string? color = null) : base(inventory, BagIndex, slotIndex, storageType)
    {
        HexBackgroundColor = color;
        SourceBag = sourceBag;
        ToolBagId = sourceBag.Item?.Code?.ToString() ?? "";
        ToolBagIndex = BagIndex;
    }

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
    {
        if (!CanHold(sourceSlot)) return false;

        return base.CanTakeFrom(sourceSlot, priority);
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (sourceSlot?.Itemstack?.Collectible?.Code != null)
        {
            bool matchWithoutDomain = WildcardUtil.Match(Config.CanHoldWildcards, sourceSlot.Itemstack.Collectible.Code.Path);
            bool matchWithDomain = WildcardUtil.Match(Config.CanHoldWildcards, sourceSlot.Itemstack.Collectible.Code.ToString());

            bool matchWithTags = false;
            if (sourceSlot.Itemstack?.Item != null && Config.CanHoldItemTags.Length != 0)
            {
                matchWithTags = ItemTagRule.ContainsAllFromAtLeastOne(sourceSlot.Itemstack.Item.Tags, Config.CanHoldItemTags);
            }
            if (sourceSlot.Itemstack?.Block != null && Config.CanHoldBlockTags.Length != 0 && !matchWithTags)
            {
                matchWithTags = BlockTagRule.ContainsAllFromAtLeastOne(sourceSlot.Itemstack.Block.Tags, Config.CanHoldBlockTags);
            }

            bool matchWithAttributes = Config.MatchesItemAttributes(sourceSlot.Itemstack);

            return matchWithoutDomain || matchWithDomain || matchWithTags || matchWithAttributes;
        }

        return false;
    }
}

public class ItemSlotTakeOutOnly : ItemSlotBagContent, IHasSlotBackpackCategory
{
    public string ToolBagId { get; set; }
    public int ToolBagIndex { get; set; }
    public bool CanHoldNow { get; set; } = false;
    public bool MainHand { get; set; } = true;
    public string BackpackCategoryCode { get; set; } = "takeout";
    public float OrderPriority { get; set; } = 0.1f;

    public ItemSlotTakeOutOnly(InventoryBase inventory, int BagIndex, int SlotIndex, EnumItemStorageFlags storageType, ItemStack sourceBag, string? color = null) : base(inventory, BagIndex, SlotIndex, storageType)
    {
        HexBackgroundColor = color;

        ToolBagId = sourceBag.Item?.Code?.ToString() ?? "";
        ToolBagIndex = BagIndex;
    }

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) => CanHoldNow;

    public override bool CanHold(ItemSlot sourceSlot) => CanHoldNow;
}
