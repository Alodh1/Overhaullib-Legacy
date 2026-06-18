using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace CombatOverhaul.Armor;

public interface IAffectsPlayerStats
{
    public Dictionary<string, float> PlayerStats(ItemSlot slot, EntityPlayer player);

    public bool StatsChanged { get; set; }
}

public sealed class WearableStatsBehavior : EntityBehavior, IDisposable
{
    public WearableStatsBehavior(Entity entity) : base(entity)
    {
        _player = entity as EntityPlayer ?? throw new InvalidDataException("This is player behavior");

        _system = _player.Api.ModLoader.GetModSystem<CombatOverhaulSystem>();
        _system.OnDispose += Dispose;

        if (_player.Api.Side == EnumAppSide.Client)
        {
            if (_existingBehaviors.TryGetValue(_player.PlayerUID, out WearableStatsBehavior? previousBehavior))
            {
                previousBehavior.PartialDispose();
            }

            _existingBehaviors[_player.PlayerUID] = this;
        }
    }

    public override string PropertyName() => "CombatOverhaul:WearableStats";
    public Dictionary<string, float> Stats { get; } = new();

    public override void AfterInitialized(bool onFirstSpawn)
    {
        TryInitialize();
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        DisposeCore(clearExisting: false);
        base.OnEntityDespawn(despawn);
    }

    public override void OnGameTick(float deltaTime)
    {
        TryInitialize();
    }

    public void RefreshStatsNow()
    {
        if (_disposed) return;

        TryInitialize();
        UpdateStatsValuesConditional(true, true, true);
    }

    private bool TryInitialize()
    {
        if (_initialized || _disposed) return _initialized;

        InventoryBase? inventory = GetGearInventory(_player);

        if (inventory == null) return false;

        if (inventory is ArmorInventory armorInventory)
        {
            armorInventory.OnSlotModified += UpdateStatsValuesConditional;
            _subscribedInventory = armorInventory;
            _subscribedArmorInventory = true;
        }
        else
        {
            inventory.SlotModified += UpdateStatsValues;
            _subscribedInventory = inventory;
            _subscribedArmorInventory = false;
        }

        UpdateStatsValues(0);

        _initialized = true;
        return true;
    }

    private readonly EntityPlayer _player;
    private readonly CombatOverhaulSystem _system;
    private const string _statsCategory = "CombatOverhaul:Armor";
    private bool _initialized = false;
    private bool _disposed = false;
    private InventoryBase? _subscribedInventory;
    private bool _subscribedArmorInventory;
    private static readonly Dictionary<string, WearableStatsBehavior> _existingBehaviors = [];

    private static InventoryBase? GetGearInventory(Entity entity)
    {
        return entity.GetBehavior<EntityBehaviorPlayerInventory>()?.Inventory;
    }

    private void UpdateStatsValues(int slotId)
    {
        UpdateStatsValuesConditional(true, true, true);
    }

    private void UpdateStatsValuesConditional(bool itemChanged, bool durabilityChanged, bool isArmorSlot)
    {
        InventoryBase? inventory = GetGearInventory(_player);

        if (inventory == null) return;

        bool anyStatsChangedItem = itemChanged;
        bool anyStatsChangedBehavior = itemChanged;

        if (!itemChanged)
        {
            foreach (ItemSlot slot in inventory
                .Where(slot => slot?.Itemstack?.Item != null)
                .Where(slot => slot.Itemstack.Item.GetRemainingDurability(slot.Itemstack) > 0))
            {
                if (slot?.Itemstack?.Item is not IAffectsPlayerStats item) continue;

                if (item.StatsChanged)
                {
                    anyStatsChangedItem = true;
                    item.StatsChanged = false;
                }
            }

            foreach (ItemSlot slot in inventory
                .Where(slot => slot?.Itemstack?.Item != null)
                .Where(slot => slot.Itemstack.Item.GetRemainingDurability(slot.Itemstack) > 0))
            {
                IAffectsPlayerStats? behavior = slot.Itemstack.Item.CollectibleBehaviors.Where(behavior => behavior is IAffectsPlayerStats).FirstOrDefault(defaultValue: null) as IAffectsPlayerStats;

                if (behavior == null) continue;

                if (behavior.StatsChanged)
                {
                    anyStatsChangedBehavior = true;
                    behavior.StatsChanged = false;
                }
            }
        }

        if (!anyStatsChangedItem && !anyStatsChangedBehavior) return;



        foreach ((string stat, _) in Stats)
        {
            _player.Stats.Remove(stat, _statsCategory);
        }

        Stats.Clear();

        if (anyStatsChangedItem)
        {
            foreach (ItemSlot slot in inventory
                .Where(slot => slot?.Itemstack?.Item != null)
                .Where(slot => slot.Itemstack.Item.GetRemainingDurability(slot.Itemstack) > 0 || slot.Itemstack.Item.GetMaxDurability(slot.Itemstack) == 0))
            {
                if (slot?.Itemstack?.Item is not IAffectsPlayerStats item) continue;

                foreach ((string stat, float value) in item.PlayerStats(slot, _player))
                {
                    AddStatValue(stat, value);
                }
            }
        }

        if (anyStatsChangedBehavior)
        {
            foreach (ItemSlot slot in inventory
                .Where(slot => slot?.Itemstack?.Item != null)
                .Where(slot => slot.Itemstack.Item.GetRemainingDurability(slot.Itemstack) > 0 || slot.Itemstack.Item.GetMaxDurability(slot.Itemstack) == 0))
            {
                IAffectsPlayerStats? behavior = slot.Itemstack.Item.CollectibleBehaviors.Where(behavior => behavior is IAffectsPlayerStats).FirstOrDefault(defaultValue: null) as IAffectsPlayerStats;

                if (behavior == null) continue;

                foreach ((string stat, float value) in behavior.PlayerStats(slot, _player))
                {
                    AddStatValue(stat, value);
                }
            }
        }

        foreach ((string stat, float value) in Stats)
        {
            _player.Stats.Set(stat, _statsCategory, value, false);
        }

        _player.walkSpeed = _player.Stats.GetBlended("walkspeed");


    }
    
    private void AddStatValue(string stat, float value)
    {
        if (stat == "walkspeed" && value < 0)
        {
            value *= _player.Stats.GetBlended("armorWalkSpeedAffectedness");
        }

        if (stat == "hungerrate" && value > 0)
        {
            value *= _player.Stats.GetBlended("armorHungerRateAffectedness");
        }

        if (stat == "manipulationSpeed" && value < 0)
        {
            value *= _player.Stats.GetBlended("armorManipulationSpeedAffectedness");
        }

        if (!Stats.ContainsKey(stat))
        {
            Stats[stat] = value;
        }
        else
        {
            Stats[stat] += value;
        }
    }

    private void PartialDispose()
    {
        DisposeCore(clearExisting: false);
    }

    public void Dispose()
    {
        DisposeCore(clearExisting: true);
    }

    private void DisposeCore(bool clearExisting)
    {
        if (_disposed) return;
        _disposed = true;

        UnsubscribeInventory();
        _system.OnDispose -= Dispose;

        if (clearExisting)
        {
            _existingBehaviors.Clear();
        }
        else if (_existingBehaviors.TryGetValue(_player.PlayerUID, out WearableStatsBehavior? behavior) && behavior == this)
        {
            _existingBehaviors.Remove(_player.PlayerUID);
        }
    }

    private void UnsubscribeInventory()
    {
        InventoryBase? inventory = _subscribedInventory ?? GetGearInventory(_player);
        if (inventory == null) return;

        if (_subscribedArmorInventory && inventory is ArmorInventory armorInventory)
        {
            armorInventory.OnSlotModified -= UpdateStatsValuesConditional;
        }
        else
        {
            inventory.SlotModified -= UpdateStatsValues;
        }

        _subscribedInventory = null;
    }
}
