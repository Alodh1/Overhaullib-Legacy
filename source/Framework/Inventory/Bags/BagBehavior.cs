using System.Diagnostics;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace CombatOverhaul.Armor;

public class GoesIntoSlotsInfo : CollectibleBehavior
{
    public GoesIntoSlotsInfo(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        if (inSlot.Itemstack?.Collectible == null) return;

        string? stackDressType = inSlot.Itemstack.Collectible.Attributes["clothescategory"].AsString() ?? inSlot.Itemstack.Collectible.Attributes["attachableToEntity"]["categoryCode"].AsString();
        string[]? stackDressTypes = inSlot.Itemstack.Collectible.Attributes["clothescategories"].AsObject<string[]>() ?? inSlot.Itemstack.Collectible.Attributes["attachableToEntity"]["categoryCodes"].AsObject<string[]>();

        List<string> slotTypes = [];
        if (stackDressTypes != null)
        {
            slotTypes.AddRange(stackDressTypes);
        }
        if (stackDressType != null)
        {
            slotTypes.Add(stackDressType);
        }

        if (!slotTypes.Any()) return;

        string slotTypeNames = slotTypes.Select(slot => Lang.Get($"combatoverhaul:slot-{slot}")).Aggregate((f, s) => $"{f}, {s}");
        string slotTypePrefix = Lang.Get("combatoverhaul:slot-types");

        dsc.AppendLine($"{slotTypePrefix}: {slotTypeNames}");
    }
}

public class GearEquipableBag : CollectibleBehavior, IHeldBag, IAttachedInteractions
{
    public SlotConfig DefaultSlotConfig { get; protected set; } = new([], []);
    public SlotConfig[] SlotConfigs { get; protected set; } = [];
    public int SlotsNumber { get; protected set; } = 0;

    protected ICoreAPI? Api;

    public GearEquipableBag(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        Api = api;

        DefaultSlotConfig.Resolve(api);
        SlotConfigs.Foreach(config => config.Resolve(api));
    }

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        SlotConfigJson? defaultSlotConfigJson = properties.AsObject<SlotConfigJson>();
        SlotConfigJson[]? slotConfigsJson = properties["slots"]?.AsObject<SlotConfigJson[]>();

        if (defaultSlotConfigJson != null)
        {
            DefaultSlotConfig = defaultSlotConfigJson.ToConfig();
        }

        if (slotConfigsJson != null)
        {
            SlotConfigs = slotConfigsJson.Select(config => config.ToConfig()).ToArray();
        }

        SlotsNumber = (DefaultSlotConfig?.SlotsNumber ?? 0) + (SlotConfigs?.Select(config => config.SlotsNumber).Sum() ?? 0);
    }

    public void Clear(ItemStack backPackStack)
    {
        ITreeAttribute? stackBackPackTree = backPackStack.Attributes.GetTreeAttribute("backpack");

        if (stackBackPackTree == null) return;

        TreeAttribute slots = new();

        for (int slotIndex = 0; slotIndex < SlotsNumber; slotIndex++)
        {
            slots["slot-" + slotIndex] = new ItemstackAttribute(null);
        }

        stackBackPackTree["slots"] = slots;
    }

    public ItemStack?[] GetContents(ItemStack bagstack, IWorldAccessor world)
    {
        ITreeAttribute backPackTree = bagstack.Attributes.GetTreeAttribute("backpack");
        if (backPackTree == null) return Array.Empty<ItemStack?>();

        List<ItemStack?> contents = new();
        ITreeAttribute slotsTree = backPackTree.GetTreeAttribute("slots");

        foreach ((_, IAttribute attribute) in slotsTree.SortedCopy())
        {
            ItemStack? contentStack = (ItemStack?)attribute?.GetValue();

            if (contentStack != null)
            {
                contentStack.ResolveBlockOrItem(world);
            }

            contents.Add(contentStack);
        }

        return contents.ToArray();
    }

    public virtual bool IsEmpty(ItemStack bagstack)
    {
        ITreeAttribute backPackTree = bagstack.Attributes.GetTreeAttribute("backpack");
        if (backPackTree == null) return true;
        ITreeAttribute slotsTree = backPackTree.GetTreeAttribute("slots");

        foreach (KeyValuePair<string, IAttribute> val in slotsTree)
        {
            IItemStack stack = (IItemStack)val.Value?.GetValue();
            if (stack != null && stack.StackSize > 0) return false;
        }

        return true;
    }

    public virtual int GetQuantitySlots(ItemStack bagstack) => SlotsNumber;

    public void Store(ItemStack bagstack, ItemSlotBagContent slot)
    {
        ITreeAttribute? stackBackPackTree = bagstack.Attributes.GetTreeAttribute("backpack");
        ITreeAttribute? slotsTree = stackBackPackTree?.GetTreeAttribute("slots");

        if (slotsTree == null)
        {
            _ = GetOrCreateSlots(bagstack, slot.Inventory, slot.BagIndex, Api.World);
            stackBackPackTree = bagstack.Attributes.GetTreeAttribute("backpack");
            slotsTree = stackBackPackTree?.GetTreeAttribute("slots");
            if (slotsTree == null) return;
        }

        slotsTree["slot-" + slot.SlotIndex] = new ItemstackAttribute(slot.Itemstack);
    }

    public virtual string GetSlotBgColor(ItemStack bagstack)
    {
        return bagstack.ItemAttributes["backpack"]["slotBgColor"].AsString(null);
    }

    protected const int DefaultFlags = (int)(EnumItemStorageFlags.General | EnumItemStorageFlags.Agriculture | EnumItemStorageFlags.Alchemy | EnumItemStorageFlags.Jewellery | EnumItemStorageFlags.Metallurgy | EnumItemStorageFlags.Outfit);

    public virtual EnumItemStorageFlags GetStorageFlags(ItemStack bagstack)
    {
        return (EnumItemStorageFlags)DefaultFlags;
    }
    public virtual TagSet GetStorageTags(ItemStack bagStack) => TagSet.Empty;

    public virtual List<ItemSlotBagContent?> GetOrCreateSlots(ItemStack bagstack, InventoryBase parentinv, int bagIndex, IWorldAccessor world)
    {
        List<ItemSlotBagContent?> bagContents = new();

        EnumItemStorageFlags flags = (EnumItemStorageFlags)DefaultFlags;

        ITreeAttribute stackBackPackTree = bagstack.Attributes.GetTreeAttribute("backpack");
        if (stackBackPackTree == null)
        {
            stackBackPackTree = new TreeAttribute();
            ITreeAttribute slotsTree = new TreeAttribute();

            int slotIndex = 0;

            for (; slotIndex < DefaultSlotConfig.SlotsNumber; slotIndex++)
            {
                ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, DefaultSlotConfig.SlotColor)
                {
                    Config = DefaultSlotConfig
                };
                bagContents.Add(slot);
                slotsTree["slot-" + slotIndex] = new ItemstackAttribute(null);
                if (DefaultSlotConfig.SlotsIcon != null)
                {
                    slot.BackgroundIcon = DefaultSlotConfig.SlotsIcon;
                }
            }

            foreach (SlotConfig config in SlotConfigs)
            {
                int lastIndex = slotIndex + config.SlotsNumber;

                for (; slotIndex < lastIndex; slotIndex++)
                {
                    ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, config.SlotColor)
                    {
                        Config = config
                    };
                    bagContents.Add(slot);
                    slotsTree["slot-" + slotIndex] = new ItemstackAttribute(null);
                    if (config.SlotsIcon != null)
                    {
                        slot.BackgroundIcon = config.SlotsIcon;
                    }
                }
            }

            stackBackPackTree["slots"] = slotsTree;
            bagstack.Attributes["backpack"] = stackBackPackTree;
        }
        else
        {
            ITreeAttribute slotsTree = stackBackPackTree.GetTreeAttribute("slots");

            foreach (KeyValuePair<string, IAttribute> val in slotsTree)
            {
                int slotIndex = int.Parse(val.Key.Split("-")[1]);

                SlotConfig config = GetSlotConfig(slotIndex);

                ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, config.SlotColor)
                {
                    Config = config
                };

                if (config.SlotsIcon != null)
                {
                    slot.BackgroundIcon = config.SlotsIcon;
                }

                if (val.Value?.GetValue() != null)
                {
                    ItemstackAttribute attr = (ItemstackAttribute)val.Value;
                    slot.Itemstack = attr.value;
                    slot.Itemstack.ResolveBlockOrItem(world);
                }

                while (bagContents.Count <= slotIndex) bagContents.Add(null);
                bagContents[slotIndex] = slot;
            }
        }

        return bagContents;
    }

    public void OnAttached(ItemSlot itemslot, int slotIndex, Entity toEntity, EntityAgent byEntity)
    {

    }

    public void OnDetached(ItemSlot itemslot, int slotIndex, Entity fromEntity, EntityAgent byEntity)
    {
        getOrCreateContainerWorkspace(slotIndex, fromEntity, null).Close((byEntity as EntityPlayer).Player);
    }


    public AttachedContainerWorkspace getOrCreateContainerWorkspace(int slotIndex, Entity onEntity, Action onRequireSave)
    {
        return ObjectCacheUtil.GetOrCreate(onEntity.Api, "att-cont-workspace-" + slotIndex + "-" + onEntity.EntityId + "-" + collObj.Id, () => new AttachedContainerWorkspace(onEntity, onRequireSave));
    }

    public AttachedContainerWorkspace getContainerWorkspace(int slotIndex, Entity onEntity)
    {
        return ObjectCacheUtil.TryGet<AttachedContainerWorkspace>(onEntity.Api, "att-cont-workspace-" + slotIndex + "-" + onEntity.EntityId + "-" + collObj.Id);
    }


    public virtual void OnInteract(ItemSlot bagSlot, int slotIndex, Entity onEntity, EntityAgent byEntity, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled, Action onRequireSave)
    {
        EntityControls controls = byEntity.MountedOn?.Controls ?? byEntity.Controls;
        if (!controls.Sprint)
        {
            handled = EnumHandling.PreventDefault;
            getOrCreateContainerWorkspace(slotIndex, onEntity, onRequireSave).OnInteract(bagSlot, slotIndex, onEntity, byEntity, hitPosition);
        }
    }

    public void OnReceivedClientPacket(ItemSlot bagSlot, int slotIndex, Entity onEntity, IServerPlayer player, int packetid, byte[] data, ref EnumHandling handled, Action onRequireSave)
    {
        int targetSlotIndex = packetid >> 11;

        if (slotIndex != targetSlotIndex) return;

        int first10Bits = (1 << 11) - 1;
        packetid = packetid & first10Bits;

        getOrCreateContainerWorkspace(slotIndex, onEntity, onRequireSave).OnReceivedClientPacket(player, packetid, data, bagSlot, slotIndex, ref handled);
    }

    public bool OnTryAttach(ItemSlot itemslot, int slotIndex, Entity toEntity)
    {
        return true;
    }

    public bool OnTryDetach(ItemSlot itemslot, int slotIndex, Entity fromEntity)
    {
        return IsEmpty(itemslot.Itemstack);
    }

    public void OnEntityDespawn(ItemSlot itemslot, int slotIndex, Entity onEntity, EntityDespawnData despawn)
    {
        getContainerWorkspace(slotIndex, onEntity)?.OnDespawn(despawn);
    }

    public void OnEntityDeath(ItemSlot itemslot, int slotIndex, Entity onEntity, DamageSource damageSourceForDeath)
    {
        ItemStack?[] contents = GetContents(itemslot.Itemstack, onEntity.World);
        foreach (ItemStack? stack in contents)
        {
            if (stack == null) continue;
            onEntity.World.SpawnItemEntity(stack, onEntity.Pos.XYZ);
        }
    }

    protected virtual SlotConfig GetSlotConfig(int index)
    {
        if (index < DefaultSlotConfig.SlotsNumber)
        {
            return DefaultSlotConfig;
        }

        int previousIndex = DefaultSlotConfig.SlotsNumber;
        for (int configIndex = 0; configIndex < SlotConfigs.Length; configIndex++)
        {
            previousIndex += SlotConfigs[configIndex].SlotsNumber;

            if (index < previousIndex)
            {
                return SlotConfigs[configIndex];
            }
        }

        return DefaultSlotConfig;
    }
}

public class SlotHotkeyConfig
{
    public string HotkeyCode { get; set; } = "";
    public string HotkeyName { get; set; } = "";
    public GlKeys HotkeyKey { get; set; } = GlKeys.R;
    public string RequiredTypeCode { get; set; } = "";
    public string RequiredTypeValue { get; set; } = "";
}

public class ToolBag : GearEquipableBag
{
    public SlotConfig? MainHandSlotConfig { get; protected set; } = null;
    public SlotConfig? OffHandSlotConfig { get; protected set; } = null;

    public string? TakeOutSlotColor { get; protected set; } = null;
    public string HotkeyCode { get; protected set; } = "";
    public string HotkeyName { get; protected set; } = "";
    public GlKeys HotKeyKey { get; protected set; } = GlKeys.R;
    public List<SlotHotkeyConfig> HotkeyConfigs { get; protected set; } = [];
    public int RegularSlotsNumber { get; protected set; } = 0;
    public int ToolSlotNumber { get; protected set; } = 0;
    public int TakeOutSlotNumber { get; protected set; } = 0;
    public string? TakeOutSlotIcon { get; protected set; } = null;

    public ToolBag(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        TakeOutSlotColor = properties["takeOutColor"].AsString(null);

        if (properties.KeyExists("hotkey"))
        {
            SlotHotkeyConfig hotkeyConfig = properties["hotkey"].AsObject<SlotHotkeyConfig>();
            HotkeyCode = hotkeyConfig.HotkeyCode;
            HotkeyName = hotkeyConfig.HotkeyName;
            HotKeyKey = hotkeyConfig.HotkeyKey;
            HotkeyConfigs.Add(hotkeyConfig);
        }
        else
        {
            HotkeyCode = properties["hotkeyCode"].AsString("");
            HotkeyName = properties["hotkeyName"].AsString("");
            HotKeyKey = Enum.Parse<GlKeys>(properties["hotkeyKey"].AsString("R"));
            if (HotkeyCode != "")
            {
                HotkeyConfigs.Add(new SlotHotkeyConfig
                {
                    HotkeyCode = HotkeyCode,
                    HotkeyName = HotkeyName,
                    HotkeyKey = HotKeyKey
                });
            }
        }

        if (properties.KeyExists("hotkeys"))
        {
            SlotHotkeyConfig[] hotkeyConfigs = properties["hotkeys"].AsObject<SlotHotkeyConfig[]>() ?? [];
            HotkeyConfigs.AddRange(hotkeyConfigs.Where(config => config.HotkeyCode != ""));

            SlotHotkeyConfig? firstConfig = HotkeyConfigs.FirstOrDefault();
            if (firstConfig != null)
            {
                HotkeyCode = firstConfig.HotkeyCode;
                HotkeyName = firstConfig.HotkeyName;
                HotKeyKey = firstConfig.HotkeyKey;
            }
        }

        TakeOutSlotIcon = properties["takeOutSlotIcon"].AsString();

        SlotConfigJson? mainHandSlotConfigJson = properties["toolSlot"]?.AsObject<SlotConfigJson>();
        SlotConfigJson? offHandSlotConfigJson = properties["offhandToolSlot"]?.AsObject<SlotConfigJson>();

        if (mainHandSlotConfigJson != null)
        {
            MainHandSlotConfig = mainHandSlotConfigJson.ToConfig();
        }

        if (offHandSlotConfigJson != null)
        {
            OffHandSlotConfig = offHandSlotConfigJson.ToConfig();
        }

        RegularSlotsNumber = SlotsNumber;

        if (mainHandSlotConfigJson != null) ToolSlotNumber += 1;
        if (offHandSlotConfigJson != null) ToolSlotNumber += 1;

        TakeOutSlotNumber = Math.Clamp(properties["takeOutSlotsNumber"].AsInt(ToolSlotNumber), 0, ToolSlotNumber);

        SlotsNumber += ToolSlotNumber + TakeOutSlotNumber;
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        Api = api;

        MainHandSlotConfig?.Resolve(api);
        OffHandSlotConfig?.Resolve(api);

        if (api is not ICoreClientAPI clientApi) return;

        ClientApi = clientApi;

        foreach (SlotHotkeyConfig hotkeyConfig in HotkeyConfigs)
        {
            if (hotkeyConfig.HotkeyCode == "") continue;

            if (!clientApi.Input.HotKeys.TryGetValue(hotkeyConfig.HotkeyCode, out HotKey? hotkey))
            {
                clientApi.Input.RegisterHotKey(hotkeyConfig.HotkeyCode, hotkeyConfig.HotkeyName, hotkeyConfig.HotkeyKey);
                hotkey = clientApi.Input.HotKeys[hotkeyConfig.HotkeyCode];
            }

            PreviousHotkeyHandlers[hotkeyConfig.HotkeyCode] = hotkey.Handler;
            clientApi.Input.SetHotKeyHandler(hotkeyConfig.HotkeyCode, keyCombination => OnHotkeyPressed(keyCombination, hotkeyConfig));
        }
    }

    public override List<ItemSlotBagContent?> GetOrCreateSlots(ItemStack bagstack, InventoryBase parentinv, int bagIndex, IWorldAccessor world)
    {
        List<ItemSlotBagContent?> bagContents = new();

        EnumItemStorageFlags flags = (EnumItemStorageFlags)DefaultFlags;

        ITreeAttribute stackBackPackTree = bagstack.Attributes.GetTreeAttribute("backpack");
        if (stackBackPackTree == null)
        {
            stackBackPackTree = new TreeAttribute();
            ITreeAttribute slotsTree = new TreeAttribute();

            int slotIndex = 0;

            if (MainHandSlotConfig != null)
            {
                ItemSlotBagContentWithWildcardMatch toolSlot = new(parentinv, bagIndex, slotIndex, flags, bagstack, MainHandSlotConfig.SlotColor)
                {
                    Config = MainHandSlotConfig
                };
                toolSlot.MainHand = true;
                bagContents.Add(toolSlot);
                slotsTree["slot-" + slotIndex] = new ItemstackAttribute(null);
                if (MainHandSlotConfig.SlotsIcon != null)
                {
                    toolSlot.BackgroundIcon = MainHandSlotConfig.SlotsIcon;
                }
                slotIndex += 1;
            }

            if (OffHandSlotConfig != null)
            {
                ItemSlotBagContentWithWildcardMatch toolSlot = new(parentinv, bagIndex, slotIndex, flags, bagstack, OffHandSlotConfig.SlotColor)
                {
                    Config = OffHandSlotConfig
                };
                toolSlot.MainHand = false;
                bagContents.Add(toolSlot);
                slotsTree["slot-" + slotIndex] = new ItemstackAttribute(null);
                if (OffHandSlotConfig.SlotsIcon != null)
                {
                    toolSlot.BackgroundIcon = OffHandSlotConfig.SlotsIcon;
                }
                slotIndex += 1;
            }

            for (; slotIndex < DefaultSlotConfig.SlotsNumber + ToolSlotNumber; slotIndex++)
            {
                ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, DefaultSlotConfig.SlotColor)
                {
                    Config = DefaultSlotConfig
                };
                bagContents.Add(slot);
                slotsTree["slot-" + slotIndex] = new ItemstackAttribute(null);
                if (DefaultSlotConfig.SlotsIcon != null)
                {
                    slot.BackgroundIcon = DefaultSlotConfig.SlotsIcon;
                }
            }

            foreach (SlotConfig config in SlotConfigs)
            {
                int lastIndex = slotIndex + config.SlotsNumber;

                for (; slotIndex < lastIndex; slotIndex++)
                {
                    ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, config.SlotColor)
                    {
                        Config = config
                    };
                    bagContents.Add(slot);
                    slotsTree["slot-" + slotIndex] = new ItemstackAttribute(null);
                    if (config.SlotsIcon != null)
                    {
                        slot.BackgroundIcon = config.SlotsIcon;
                    }
                }
            }

            if (HasMainHandTakeOutSlot)
            {
                ItemSlotTakeOutOnly takeOutSLot = new(parentinv, bagIndex, slotIndex, flags, bagstack, TakeOutSlotColor);
                takeOutSLot.MainHand = true;
                bagContents.Add(takeOutSLot);
                slotsTree["slot-" + slotIndex] = new ItemstackAttribute(null);
                if (TakeOutSlotIcon != null)
                {
                    takeOutSLot.BackgroundIcon = TakeOutSlotIcon;
                }
                slotIndex += 1;
            }

            if (HasOffHandTakeOutSlot)
            {
                ItemSlotTakeOutOnly takeOutSLot = new(parentinv, bagIndex, slotIndex, flags, bagstack, TakeOutSlotColor);
                takeOutSLot.MainHand = false;
                bagContents.Add(takeOutSLot);
                slotsTree["slot-" + slotIndex] = new ItemstackAttribute(null);
                if (TakeOutSlotIcon != null)
                {
                    takeOutSLot.BackgroundIcon = TakeOutSlotIcon;
                }
            }

            stackBackPackTree["slots"] = slotsTree;
            bagstack.Attributes["backpack"] = stackBackPackTree;
        }
        else
        {
            ITreeAttribute slotsTree = stackBackPackTree.GetTreeAttribute("slots");

            foreach (KeyValuePair<string, IAttribute> val in slotsTree.SortedCopy())
            {
                int slotIndex = int.Parse(val.Key.Split("-")[1]);

                if (slotIndex == 0)
                {
                    if (MainHandSlotConfig != null)
                    {
                        ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, MainHandSlotConfig.SlotColor)
                        {
                            Config = MainHandSlotConfig
                        };
                        slot.MainHand = true;

                        if (val.Value?.GetValue() != null)
                        {
                            ItemstackAttribute attr = (ItemstackAttribute)val.Value;
                            slot.Itemstack = attr.value;
                            slot.Itemstack.ResolveBlockOrItem(world);
                        }

                        if (MainHandSlotConfig.SlotsIcon != null)
                        {
                            slot.BackgroundIcon = MainHandSlotConfig.SlotsIcon;
                        }

                        while (bagContents.Count <= slotIndex) bagContents.Add(null);
                        bagContents[slotIndex] = slot;
                    }
                    else if (OffHandSlotConfig != null)
                    {
                        ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, OffHandSlotConfig.SlotColor)
                        {
                            Config = OffHandSlotConfig
                        };
                        slot.MainHand = false;

                        if (val.Value?.GetValue() != null)
                        {
                            ItemstackAttribute attr = (ItemstackAttribute)val.Value;
                            slot.Itemstack = attr.value;
                            slot.Itemstack.ResolveBlockOrItem(world);
                        }

                        if (OffHandSlotConfig.SlotsIcon != null)
                        {
                            slot.BackgroundIcon = OffHandSlotConfig.SlotsIcon;
                        }

                        while (bagContents.Count <= slotIndex) bagContents.Add(null);
                        bagContents[slotIndex] = slot;
                    }
                    else
                    {
                        SlotConfig config = GetSlotConfig(slotIndex - ToolSlotNumber);

                        ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, config.SlotColor)
                        {
                            Config = config
                        };

                        if (config.SlotsIcon != null)
                        {
                            slot.BackgroundIcon = config.SlotsIcon;
                        }

                        if (val.Value?.GetValue() != null)
                        {
                            ItemstackAttribute attr = (ItemstackAttribute)val.Value;
                            slot.Itemstack = attr.value;
                            slot.Itemstack.ResolveBlockOrItem(world);
                        }

                        while (bagContents.Count <= slotIndex) bagContents.Add(null);
                        bagContents[slotIndex] = slot;
                    }
                }
                else if (slotIndex == 1 && ToolSlotNumber == 2 && OffHandSlotConfig != null)
                {
                    ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, OffHandSlotConfig.SlotColor)
                    {
                        Config = OffHandSlotConfig
                    };
                    slot.MainHand = false;

                    if (val.Value?.GetValue() != null)
                    {
                        ItemstackAttribute attr = (ItemstackAttribute)val.Value;
                        slot.Itemstack = attr.value;
                        slot.Itemstack.ResolveBlockOrItem(world);
                    }

                    if (OffHandSlotConfig.SlotsIcon != null)
                    {
                        slot.BackgroundIcon = OffHandSlotConfig.SlotsIcon;
                    }

                    while (bagContents.Count <= slotIndex) bagContents.Add(null);
                    bagContents[slotIndex] = slot;
                }
                else if (slotIndex == MainHandTakeOutSlotIndex && HasMainHandTakeOutSlot)
                {
                    ItemSlotTakeOutOnly takeOutSLot = new(parentinv, bagIndex, slotIndex, flags, bagstack, TakeOutSlotColor);
                    takeOutSLot.MainHand = true;

                    if (val.Value?.GetValue() != null)
                    {
                        ItemstackAttribute attr = (ItemstackAttribute)val.Value;
                        takeOutSLot.Itemstack = attr.value;
                        takeOutSLot.Itemstack.ResolveBlockOrItem(world);
                    }

                    if (TakeOutSlotIcon != null)
                    {
                        takeOutSLot.BackgroundIcon = TakeOutSlotIcon;
                    }

                    while (bagContents.Count <= slotIndex) bagContents.Add(null);
                    bagContents[slotIndex] = takeOutSLot;
                }
                else if (slotIndex == OffHandTakeOutSlotIndex && HasOffHandTakeOutSlot)
                {
                    ItemSlotTakeOutOnly takeOutSLot = new(parentinv, bagIndex, slotIndex, flags, bagstack, TakeOutSlotColor);
                    takeOutSLot.MainHand = false;

                    if (val.Value?.GetValue() != null)
                    {
                        ItemstackAttribute attr = (ItemstackAttribute)val.Value;
                        takeOutSLot.Itemstack = attr.value;
                        takeOutSLot.Itemstack.ResolveBlockOrItem(world);
                    }

                    if (TakeOutSlotIcon != null)
                    {
                        takeOutSLot.BackgroundIcon = TakeOutSlotIcon;
                    }

                    while (bagContents.Count <= slotIndex) bagContents.Add(null);
                    bagContents[slotIndex] = takeOutSLot;
                }
                else if (!IsSurplusTakeOutSlotIndex(slotIndex))
                {
                    SlotConfig config = GetSlotConfig(slotIndex - ToolSlotNumber);

                    ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, config.SlotColor)
                    {
                        Config = config
                    };

                    if (config.SlotsIcon != null)
                    {
                        slot.BackgroundIcon = config.SlotsIcon;
                    }

                    if (val.Value?.GetValue() != null)
                    {
                        ItemstackAttribute attr = (ItemstackAttribute)val.Value;
                        slot.Itemstack = attr.value;
                        slot.Itemstack.ResolveBlockOrItem(world);
                    }

                    while (bagContents.Count <= slotIndex) bagContents.Add(null);
                    bagContents[slotIndex] = slot;
                }
            }
        }

        return bagContents;
    }

    protected bool HasMainHandTakeOutSlot => MainHandSlotConfig != null && TakeOutSlotNumber > 0;
    protected bool HasOffHandTakeOutSlot => OffHandSlotConfig != null && TakeOutSlotNumber > (MainHandSlotConfig != null ? 1 : 0);
    protected int MainHandTakeOutSlotIndex => RegularSlotsNumber + ToolSlotNumber;
    protected int OffHandTakeOutSlotIndex => MainHandTakeOutSlotIndex + (HasMainHandTakeOutSlot ? 1 : 0);

    protected bool IsSurplusTakeOutSlotIndex(int slotIndex)
    {
        int takeOutStartIndex = RegularSlotsNumber + ToolSlotNumber;
        return slotIndex >= takeOutStartIndex + TakeOutSlotNumber && slotIndex < takeOutStartIndex + ToolSlotNumber;
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        if (HotkeyName != "")
        {
            dsc.AppendLine($"Uses hotkey: '{HotkeyName}'");
        }
    }

    protected ActionConsumable<KeyCombination>? PreviousHotkeyHandler;
    protected Dictionary<string, ActionConsumable<KeyCombination>?> PreviousHotkeyHandlers { get; } = [];
    protected ICoreClientAPI? ClientApi;
    protected long HotkeyCooldownUntilMs = 0;
    protected const long HotkeyCooldown = 120;

    protected readonly struct HotkeyStoragePriority
    {
        public readonly bool HeldItemCanBeStored;
        public readonly ItemSlotBagContentWithWildcardMatch? TargetSlot;

        public HotkeyStoragePriority(bool heldItemCanBeStored, ItemSlotBagContentWithWildcardMatch? targetSlot)
        {
            HeldItemCanBeStored = heldItemCanBeStored;
            TargetSlot = targetSlot;
        }
    }

    protected virtual bool OnHotkeyPressed(KeyCombination keyCombination) => OnHotkeyPressed(keyCombination, null);

    protected virtual bool OnHotkeyPressed(KeyCombination keyCombination, SlotHotkeyConfig? hotkeyConfig)
    {
        InventoryPlayerBackpacks? inventory = GetBackpackInventory();

        bool handled = false;

        if (inventory != null && Api != null && HotkeyCooldownUntilMs < Api.World.ElapsedMilliseconds)
        {
            ToolBagSystemClient? system = ClientApi?.ModLoader?.GetModSystem<CombatOverhaulSystem>()?.ClientToolBagSystem;

            if (system != null)
            {
                ItemSlotBagContentWithWildcardMatch? selectedSlot = null;
                HotkeyStoragePriority storagePriority = GetSameHotkeyStoragePriority(inventory, keyCombination);

                if (storagePriority.TargetSlot != null)
                {
                    selectedSlot = storagePriority.TargetSlot;
                }
                else if (storagePriority.HeldItemCanBeStored)
                {
                    handled = true;
                }
                else
                {
                    List<ItemSlotBagContentWithWildcardMatch> slots = inventory
                        .OfType<ItemSlotBagContentWithWildcardMatch>()
                        .Where(slot => slot.Config.HandleHotkey)
                        .Where(slot => SlotUsesKeyCombination(slot, keyCombination))
                        .ToList();

                    selectedSlot = slots.FirstOrDefault(slot => !HasHotkeyActionableStack(GetHandSlot(slot)) && HasHotkeyActionableStack(slot))
                        ?? slots.FirstOrDefault(HasHotkeyActionableStack);
                }

                if (selectedSlot != null)
                {
                    system.Send(selectedSlot.ToolBagId, selectedSlot.ToolBagIndex, selectedSlot.MainHand, selectedSlot.SlotIndex);
                    handled = true;
                }

                if (handled)
                {
                    HotkeyCooldownUntilMs = Api.World.ElapsedMilliseconds + HotkeyCooldown;
                }
            }
        }

        if (handled)
        {
            return true;
        }

        ActionConsumable<KeyCombination>? previousHandler = PreviousHotkeyHandler;
        if (hotkeyConfig != null)
        {
            PreviousHotkeyHandlers.TryGetValue(hotkeyConfig.HotkeyCode, out previousHandler);
        }

        bool previousHandled = previousHandler?.Invoke(keyCombination) ?? false;
        return previousHandled;
    }

    protected virtual HotkeyStoragePriority GetSameHotkeyStoragePriority(InventoryPlayerBackpacks inventory, KeyCombination keyCombination)
    {
        bool heldItemCanBeStored = false;

        foreach (ItemSlotBagContentWithWildcardMatch slot in inventory.OfType<ItemSlotBagContentWithWildcardMatch>())
        {
            if (!slot.Config.HandleHotkey || !SlotUsesKeyCombination(slot, keyCombination))
            {
                continue;
            }

            ItemSlot? handSlot = GetHandSlot(slot);
            if (!HasHotkeyActionableStack(handSlot) || handSlot == null || !slot.CanHold(handSlot))
            {
                continue;
            }

            heldItemCanBeStored = true;

            if (!HasHotkeyActionableStack(slot))
            {
                return new HotkeyStoragePriority(true, slot);
            }
        }

        return new HotkeyStoragePriority(heldItemCanBeStored, null);
    }

    protected virtual bool SlotUsesKeyCombination(ItemSlotBagContentWithWildcardMatch slot, KeyCombination keyCombination)
    {
        if (slot.SourceBag?.Collectible?.CollectibleBehaviors == null)
        {
            return false;
        }

        foreach (ToolBag behavior in slot.SourceBag.Collectible.CollectibleBehaviors.OfType<ToolBag>())
        {
            foreach (SlotHotkeyConfig config in behavior.HotkeyConfigs)
            {
                if (behavior.MatchesHotkeyConfig(slot.SourceBag, config) && HotkeyConfigMatchesCurrentMapping(config, keyCombination))
                {
                    return true;
                }
            }
        }

        return false;
    }

    protected virtual bool HotkeyConfigMatchesCurrentMapping(SlotHotkeyConfig hotkeyConfig, KeyCombination keyCombination)
    {
        if (hotkeyConfig.HotkeyCode == "" || ClientApi?.Input.HotKeys.TryGetValue(hotkeyConfig.HotkeyCode, out HotKey? hotkey) != true)
        {
            return false;
        }

        return KeyCombinationsMatch(hotkey.CurrentMapping, keyCombination);
    }

    protected static bool KeyCombinationsMatch(KeyCombination? currentMapping, KeyCombination pressedCombination)
    {
        if (currentMapping == null)
        {
            return false;
        }

        return currentMapping.KeyCode == pressedCombination.KeyCode
            && currentMapping.SecondKeyCode.GetValueOrDefault() == pressedCombination.SecondKeyCode.GetValueOrDefault()
            && currentMapping.Ctrl == pressedCombination.Ctrl
            && currentMapping.Alt == pressedCombination.Alt
            && currentMapping.Shift == pressedCombination.Shift;
    }

    protected ItemSlot? GetHandSlot(ItemSlotBagContentWithWildcardMatch slot)
    {
        return slot.MainHand ? ClientApi?.World?.Player?.Entity?.RightHandItemSlot : ClientApi?.World?.Player?.Entity?.LeftHandItemSlot;
    }

    protected static bool HasHotkeyActionableStack(ItemSlot? slot)
    {
        return slot?.Itemstack != null && slot.Itemstack.StackSize > 0 && slot.Itemstack.Collectible != null;
    }

    protected virtual bool MatchesHotkeyConfig(ItemStack sourceBag, SlotHotkeyConfig hotkeyConfig)
    {
        if (hotkeyConfig.RequiredTypeCode == "" || hotkeyConfig.RequiredTypeValue == "") return true;
        if (sourceBag.Attributes?["types"] is not ITreeAttribute types) return true;

        string? actualValue = types[hotkeyConfig.RequiredTypeCode]?.GetValue()?.ToString();
        return actualValue == null || actualValue == hotkeyConfig.RequiredTypeValue;
    }

    protected InventoryPlayerBackpacks? GetBackpackInventory()
    {
        return ClientApi?.World?.Player?.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName) as InventoryPlayerBackpacks;
    }
}
