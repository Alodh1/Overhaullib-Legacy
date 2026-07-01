using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using CombatOverhaul.Utils;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace CombatOverhaul;

public interface IFueledItem
{
    double GetFuelHours(IPlayer player, ItemSlot slot);
    void AddFuelHours(IPlayer player, ItemSlot slot, double hours);
    bool ConsumeFuelWhenSleeping(IPlayer player, ItemSlot slot);
}

public interface ITogglableItem
{
    string HotKeyCode { get; }

    bool TurnedOn(IPlayer player, ItemSlot slot);
    void TurnOn(IPlayer player, ItemSlot slot);
    void TurnOff(IPlayer player, ItemSlot slot);
    void Toggle(IPlayer player, ItemSlot slot);
}

public sealed class FueledItemSystem : ModSystem, IRenderer
{
    public double RenderOrder => 0.01;
    public int RenderRange => 1;

    public override void Start(ICoreAPI api)
    {
        CombatOverhaulSystem system = api.ModLoader.GetModSystem<CombatOverhaulSystem>();
        _settings = system.Settings;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        _clientApi = api;
        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "nightvisionCO");
        api.Event.LevelFinalize += OnLevelFinalize;
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
        _serverApi = api;
        _serverTickListener = api.Event.RegisterGameTickListener(OnServerTick, 1000, 200);
    }

    public override void Dispose()
    {
        if (_clientApi != null)
        {
            _clientApi.Event.UnregisterRenderer(this, EnumRenderStage.Before);
            _clientApi.Event.LevelFinalize -= OnLevelFinalize;
            _clientApi.Render.ShaderUniforms.NightVisionStrength = 0;
        }

        if (_serverApi != null && _serverTickListener != 0)
        {
            _serverApi.Event.UnregisterGameTickListener(_serverTickListener);
            _serverTickListener = 0;
        }

        UnsubscribeClientInventory();
        _nightVisionSlot = null;
        _clientApi = null;
        _serverApi = null;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (_clientApi == null) return;
        IInventory? inventory = GetClientCharacterInventory();
        if (inventory == null)
        {
            SetNightVisionStrength(0);
            return;
        }

        if (!ReferenceEquals(inventory, _subscribedClientInventory))
        {
            SetClientInventory(inventory);
        }

        long now = _clientApi.World.ElapsedMilliseconds;
        if (_nightVisionSlotDirty || now >= _nextNightVisionRefreshMs)
        {
            RefreshNightVisionState();
            _nextNightVisionRefreshMs = now + NightVisionRefreshIntervalMs;
        }

        SetNightVisionStrength(_cachedNightVisionStrength);
    }

    private double _lastCheckTotalHours;
    private ICoreClientAPI? _clientApi;
    private ICoreServerAPI? _serverApi;
    private IInventory? _subscribedClientInventory;
    private ItemSlot? _nightVisionSlot;
    private long _serverTickListener;
    private Settings _settings = new();
    private bool _reportedSleepingStateError;
    private bool _reportedInventoryLookupError;
    private bool _nightVisionSlotDirty = true;
    private long _nextNightVisionRefreshMs;
    private float _cachedNightVisionStrength;
    private const int NightVisionRefreshIntervalMs = 250;

    private void OnServerTick(float dt)
    {
        if (_serverApi == null) return;

        double totalHours = _serverApi.World.Calendar.TotalHours;
        double hoursPassed = totalHours - _lastCheckTotalHours;

        if (hoursPassed < _settings.FueledItemUpdateInGameHours) return;

        foreach (IPlayer? player in _serverApi.World.AllOnlinePlayers)
        {
            if (player.Entity == null) continue;

            IInventory? inventory = player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);
            if (inventory == null) continue;

            int nightVisionSlotIndex = NightVisionDeviceUtil.GetBestNightVisionSlotIndex(inventory);
            if (nightVisionSlotIndex == NightVisionDeviceUtil.VanillaNightVisionSlot) continue;

            ItemSlot? slot = nightVisionSlotIndex >= 0 ? inventory[nightVisionSlotIndex] : null;
            if (slot?.Itemstack?.Collectible is ItemNightvisiondevice device)
            {
                device.AddFuelHours(slot.Itemstack, -hoursPassed);
                slot.MarkDirty();
            }
        }

        foreach (IPlayer? player in _serverApi.World.AllOnlinePlayers)
        {
            if (player.Entity == null) continue;

            IInventory? inventory = player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);
            if (inventory == null) continue;

            foreach (ItemSlot slot in inventory)
            {
                IFueledItem? item = slot?.Itemstack?.Collectible?.GetCollectibleInterface<IFueledItem>();
                if (slot == null || item == null) continue;

                if (IsSleeping(player.Entity) && !item.ConsumeFuelWhenSleeping(player, slot)) continue;

                item.AddFuelHours(player, slot, -hoursPassed);
                slot.MarkDirty();
            }
        }

        _lastCheckTotalHours = totalHours;
    }

    private bool IsSleeping(EntityPlayer player)
    {
        try
        {
            return player.GetBehavior<EntityBehaviorTiredness>()?.IsSleeping == true;
        }
        catch (Exception exception)
        {
            if (!_reportedSleepingStateError)
            {
                _reportedSleepingStateError = true;
                LoggerUtil.Warn(_serverApi ?? player.Api, this, $"Error while checking sleep state for '{player.Player?.PlayerName}':\n{exception}");
            }

            return false;
        }
    }

    private void OnLevelFinalize()
    {
        try
        {
            SetClientInventory(GetClientCharacterInventory());
        }
        catch (Exception exception)
        {
            if (!_reportedInventoryLookupError)
            {
                _reportedInventoryLookupError = true;
                LoggerUtil.Warn(_clientApi, this, $"Error while resolving player inventory behavior for fueled item rendering:\n{exception}");
            }
        }
    }

    private IInventory? GetClientCharacterInventory()
    {
        return _clientApi?.World?.Player?.InventoryManager?.GetOwnInventory(GlobalConstants.characterInvClassName);
    }

    private void SetClientInventory(IInventory? inventory)
    {
        UnsubscribeClientInventory();

        _subscribedClientInventory = inventory;
        if (_subscribedClientInventory != null)
        {
            _subscribedClientInventory.SlotModified += OnClientInventorySlotModified;
        }

        _nightVisionSlotDirty = true;
        _nextNightVisionRefreshMs = 0;
    }

    private void UnsubscribeClientInventory()
    {
        if (_subscribedClientInventory != null)
        {
            _subscribedClientInventory.SlotModified -= OnClientInventorySlotModified;
            _subscribedClientInventory = null;
        }
    }

    private void OnClientInventorySlotModified(int slotId)
    {
        _nightVisionSlotDirty = true;
    }

    private void RefreshNightVisionState()
    {
        _nightVisionSlotDirty = false;
        _nightVisionSlot = FindNightVisionSlot();

        double fuelLeft = (_nightVisionSlot?.Itemstack?.Collectible as ItemNightvisiondevice)?.GetFuelHours(_nightVisionSlot.Itemstack) ?? 0;
        _cachedNightVisionStrength = fuelLeft > 0 ? (float)GameMath.Clamp(fuelLeft * 20, 0, 0.8) : 0;
    }

    private ItemSlot? FindNightVisionSlot()
    {
        IInventory? inventory = _subscribedClientInventory ?? GetClientCharacterInventory();
        if (inventory == null) return null;

        return NightVisionDeviceUtil.GetBestNightVisionSlot(inventory);
    }

    private void SetNightVisionStrength(float strength)
    {
        if (_clientApi == null || Math.Abs(_clientApi.Render.ShaderUniforms.NightVisionStrength - strength) < 0.0001f) return;

        _clientApi.Render.ShaderUniforms.NightVisionStrength = strength;
    }
}
