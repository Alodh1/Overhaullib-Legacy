using Cairo;
using CombatOverhaul.Animations;
using CombatOverhaul.Armor;
using CombatOverhaul.Colliders;
using CombatOverhaul.DamageSystems;
using CombatOverhaul.Implementations;
using CombatOverhaul.Inputs;
using CombatOverhaul.Integration;
using CombatOverhaul.Integration.Transpilers;
using CombatOverhaul.MeleeSystems;
using CombatOverhaul.RangedSystems;
using CombatOverhaul.RangedSystems.Aiming;
using CombatOverhaul.Utils;
using CombatOverhaul.Vanity;
using Newtonsoft.Json.Linq;
using OpenTK.Mathematics;
using ProtoBuf;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace CombatOverhaul;

public sealed class ArmorConfig
{
    public int MaxAttackTier { get; set; } = 9;
    public int MaxArmorTier { get; set; } = 24;
    public float[][] DamageReduction { get; set; } =
    [
        [0.50f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f],
        [0.25f, 0.50f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f],
        [0.20f, 0.25f, 0.50f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f],
        [0.15f, 0.20f, 0.25f, 0.50f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f],
        [0.12f, 0.15f, 0.20f, 0.25f, 0.50f, 1.00f, 1.00f, 1.00f, 1.00f],
        [0.10f, 0.12f, 0.15f, 0.20f, 0.25f, 0.50f, 1.00f, 1.00f, 1.00f],
        [0.08f, 0.10f, 0.12f, 0.15f, 0.20f, 0.30f, 0.60f, 1.00f, 1.00f],
        [0.06f, 0.08f, 0.10f, 0.12f, 0.15f, 0.25f, 0.50f, 0.75f, 1.00f],
        [0.04f, 0.06f, 0.08f, 0.10f, 0.12f, 0.20f, 0.40f, 0.60f, 0.90f],
        [0.02f, 0.04f, 0.06f, 0.08f, 0.10f, 0.15f, 0.30f, 0.50f, 0.80f],
        [0.01f, 0.02f, 0.04f, 0.06f, 0.08f, 0.10f, 0.20f, 0.40f, 0.70f],
        [0.01f, 0.01f, 0.02f, 0.04f, 0.06f, 0.08f, 0.16f, 0.30f, 0.60f],
        [0.01f, 0.01f, 0.01f, 0.02f, 0.04f, 0.06f, 0.12f, 0.25f, 0.50f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.02f, 0.04f, 0.08f, 0.12f, 0.25f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.02f, 0.04f, 0.08f, 0.12f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.02f, 0.04f, 0.08f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.02f, 0.04f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.02f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f],
        [0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f]
    ];
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class TogglePacket
{
    public string HotKeyCode { get; set; } = "";
}

internal class LogStuff : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        string mods = api.ModLoader.Mods.Select(mod => $"'{mod.Info.ModID}'\t'{mod.Info.Version}'\t'{mod.Info.Name}'\t'{Aggregate(mod.Info.Authors, mod)}'\t'{mod.FileName}'").Aggregate((f, s) => $"{f}\n{s}");
        api.Logger.Event("Loaded mods:\n" + mods);
    }

    private static string Aggregate(IEnumerable<string> list, Mod mod)
    {
        if (!list.Any())
        {
            mod.Logger.Warning($"Mod '{mod.Info.Name} ({mod.FileName})' has no authors specified in mod info.");
            return "-";
        }

        return list.Aggregate((f, s) => $"{f}, {s}");
    }
}

public partial class CombatOverhaulSystem : ModSystem
{
    public event Action? OnDispose;
    public event Action<Settings>? SettingsLoaded;
    public event Action<Settings>? SettingsChanged;

    public Settings Settings { get; set; } = new();
    public bool Disposed { get; private set; } = false;
    public static event Action<ICoreAPI>? OnSettingsChange;

    public const string VanityInventoryCode = "combatoverhaul:vanity";

    public override void StartPre(ICoreAPI api)
    {
        (api as ServerCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.characterInvClassName, typeof(ArmorInventory));
        (api as ServerCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.backpackInvClassName, typeof(InventoryPlayerBackPacksCombatOverhaul));
        (api as ClientCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.characterInvClassName, typeof(ArmorInventory));
        (api as ClientCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.backpackInvClassName, typeof(InventoryPlayerBackPacksCombatOverhaul));

        api.RegisterBlockClass("CombatOverhaul:GenericDisplayBlock", typeof(Utils.GenericDisplayBlock));
        api.RegisterBlockClass("CombatOverhaul:HandbookAntlerMount", typeof(HandbookAntlerMount));
        api.RegisterBlockEntityClass("CombatOverhaul:GenericDisplayBlockEntity", typeof(Utils.GenericDisplayBlockEntity));

        RegisterClassMappings(api);
    }

    public override void Start(ICoreAPI api)
    {
        GrindingWheelCompat.SetApi(api);
        SplitMaterialWeaponUtil.SetApi(api);
        HarmonyPatchesManager.Patch(api);

        if (api.Side == EnumAppSide.Client)
        {
            HarmonyPatches.ClientSettings = Settings;
            AnimationPatches.ClientSettings = Settings;
        }
        else
        {
            HarmonyPatches.ServerSettings = Settings;
            AnimationPatches.ServerSettings = Settings;
        }

        AiTaskRegistry.Register<AiTaskCOTurretMode>("CombatOverhaul:TurretMode");
        AiTaskRegistry.Register<StaggerAiTask>("CombatOverhaul:Stagger");

        StartConfigLibSubscription(api);
        ApplyConfigFileSettings(api);
        ApplyRuntimeSettings(Settings);
        HarmonyPatchesManager.ConfigureDiagnostics(api, Settings);

        if (api.ModLoader.IsModEnabled("combatoverhaul") || api.ModLoader.IsModEnabled("combatoverhaulfork"))
        {
            EidolonSlam_KnockbackMultiplierPatch.KnockbackMultiplier = 0.2f;
        }
    }

    private static void RegisterClassMappings(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass("CombatOverhaul:FirstPersonAnimations", typeof(FirstPersonAnimationsBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:ThirdPersonAnimations", typeof(ThirdPersonAnimationsBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:EntityColliders", typeof(CollidersEntityBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:EntityDamageModel", typeof(EntityDamageModelBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:PlayerDamageModel", typeof(PlayerDamageModelBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:ActionsManager", typeof(ActionsManagerPlayerBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:AimingAccuracy", typeof(AimingAccuracyBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:WearableStats", typeof(WearableStatsBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:InInventory", typeof(InInventoryPlayerBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:ProjectilePhysics", typeof(ProjectilePhysicsBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:CurvedFlight", typeof(CurvedFlightBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:OvalFlight", typeof(OvalFlightBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:AutoAnimation", typeof(AutoAnimationBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:Stagger", typeof(StaggerBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:PositionBeforeFalling", typeof(PositionBeforeFallingBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:ArmorStandInventory", typeof(EntityBehaviorCOArmorStandInventory));

        api.RegisterCollectibleBehaviorClass("CombatOverhaul:Animatable", typeof(Animatable));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:AnimatableAttachable", typeof(AnimatableAttachable));

        // Legacy aliases for old mods/assets still using AnimationsLib behavior IDs.
        api.RegisterCollectibleBehaviorClass("AnimationsLib:Animatable", typeof(Animatable));
        api.RegisterCollectibleBehaviorClass("AnimationsLib:AnimatableAttachable", typeof(AnimatableAttachable));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:Projectile", typeof(ProjectileBehavior));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:Armor", typeof(ArmorBehavior));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:WearableArmor", typeof(WearableArmorBehavior));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:WearableWithStats", typeof(WearableWithStatsBehavior));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:GearEquipableBag", typeof(GearEquipableBag));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:ToolBag", typeof(ToolBag));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:TextureFromAttributes", typeof(TextureFromAttributes));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:TexturesFromAttributes", typeof(TexturesFromAttributes));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:AdditionalSlots", typeof(AdditionalSlotsBehavior));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:GoesIntoSlotsInfo", typeof(GoesIntoSlotsInfo));
        api.RegisterCollectibleBehaviorClass("CombatOverhaul:MeleeWeaponBehavior", typeof(MeleeWeaponBehavior));

        api.RegisterItemClass("CombatOverhaul:Bow", typeof(BowItem));
        api.RegisterItemClass("CombatOverhaul:Sling", typeof(SlingItem));
        api.RegisterItemClass("CombatOverhaul:MeleeWeapon", typeof(MeleeWeapon));
        api.RegisterItemClass("CombatOverhaul:StanceBasedMeleeWeapon", typeof(StanceBasedMeleeWeapon));
        api.RegisterItemClass("CombatOverhaul:TextureAttributedItem", typeof(TextureAttributedItem));
        api.RegisterItemClass("CombatOverhaul:VanillaShield", typeof(VanillaShield));
        api.RegisterItemClass("CombatOverhaul:WearableArmor", typeof(ItemWearableArmor));
        api.RegisterItemClass("CombatOverhaul:WearableFueledLightSource", typeof(WearableFueledLightSource));

        api.RegisterEntity("CombatOverhaul:Projectile", typeof(ProjectileEntity));
        api.RegisterEntity("CombatOverhaul:ArmorStand", typeof(EntityCOArmorStand));
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        _serverApi = api;
        ServerProjectileSystem = new(api);
        ServerRangedWeaponSystem = new(api);
        ServerSoundsSynchronizer = new(api);
        ServerMeleeSystem = new(api);
        ServerImpaleSystem = new(api);
        ServerBlockSystem = new(api);
        ServerStatsSystem = new(api);
        ServerAttachmentSystem = new(api);
        ServerToolBagSystem = new(api);
        ServerVanitySystem = new(api);

        _serverToggleChannel = api.Network.RegisterChannel("combatOverhaulToggleItem")
            .RegisterMessageType<TogglePacket>()
            .SetMessageHandler<TogglePacket>(ToggleWearableItem);

        _serverGameplaySettingsChannel = api.Network.RegisterChannel(GameplaySettingsChannelId)
            .RegisterMessageType<ServerGameplaySettingsPacket>();
        _playerNowPlayingSettingsHandler = SendGameplaySettings;
        api.Event.PlayerNowPlaying += _playerNowPlayingSettingsHandler;

        if (ShieldAutoPatcher.IsCombatOverhaulEnabled(api))
        {
            ShieldAutoPatcher.Patch(api);
            _shieldAutoPatchServerListener = api.Event.RegisterGameTickListener(PatchShieldsOnServerTick, 50, 50);
        }
    }
    public override void StartClientSide(ICoreClientAPI api)
    {
        _clientApi = api;

        ClientProjectileSystem = new(api, api.ModLoader.GetModSystem<EntityPartitioning>());
        ActionListener = new(api);
        DirectionCursorRenderer = new(api, Settings);
        ReticleRenderer = new(api);
        DirectionController = new(api, DirectionCursorRenderer, Settings);
        ClientRangedWeaponSystem = new(api);
        ClientSoundsSynchronizer = new(api);
        AimingSystem = new(api, ReticleRenderer);
        ClientMeleeSystem = new(api);
        ClientImpaleSystem = new(api);
        ClientBlockSystem = new(api);
        ClientStatsSystem = new(api);
        ClientAttachmentSystem = new(api);
        ClientToolBagSystem = new(api);
        ClientToolBagSelectionSystem = new(api, ClientToolBagSystem);
        ClientVanitySystem = new(api);

        api.Event.RegisterRenderer(ReticleRenderer, EnumRenderStage.Ortho);
        api.Event.RegisterRenderer(DirectionCursorRenderer, EnumRenderStage.Ortho);

        _clientToggleChannel = api.Network.RegisterChannel("combatOverhaulToggleItem")
            .RegisterMessageType<TogglePacket>();

        _clientGameplaySettingsChannel = api.Network.RegisterChannel(GameplaySettingsChannelId)
            .RegisterMessageType<ServerGameplaySettingsPacket>()
            .SetMessageHandler<ServerGameplaySettingsPacket>(HandleServerGameplaySettings);

#if DEBUG
        if (!api.IsSinglePlayer)
        {
            api.Event.EnqueueMainThreadTask(() => OnSettingsChange?.Invoke(api), "game");
        }
#else
        if (!api.IsSinglePlayer)
        {
            api.Event.EnqueueMainThreadTask(() => OnSettingsChange?.Invoke(api), "game");
        }
#endif

        api.Input.RegisterHotKey("toggleWearableLight", "Toggle wearable light source", GlKeys.L);
        api.Input.SetHotKeyHandler("toggleWearableLight", _ => ToggleWearableItem(api.World.Player, "toggleWearableLight"));

        api.Input.RegisterHotKey("toggleTpAnimations", "Toggle CO third person animations", GlKeys.PageDown, ctrlPressed: true);
        api.Input.RegisterHotKey("toggleAllAnimations", "Toggle all CO animations", GlKeys.PageUp, ctrlPressed: true);

        // api.Input.AddHotkeyListener only receives vanilla hotkeys here.

        api.Input.SetHotKeyHandler("toggleAllAnimations", _ =>
        {
            Settings.DisableAllAnimations = !Settings.DisableAllAnimations;
            if (Settings.DisableAllAnimations)
            {
                LoggerUtil.Notify(api, this, $"Animations disabled");
                api.TriggerIngameError(this, "animationsDisabled", "Overhaul lib animations are DISABLED");
            }
            else
            {
                LoggerUtil.Notify(api, this, $"Animations enabled");
                api.TriggerIngameError(this, "animationsDisabled", "Overhaul lib animations are ENABLED");
            }
            return true;
        });
        api.Input.SetHotKeyHandler("toggleTpAnimations", _ =>
        {
            Settings.DisableThirdPersonAnimations = !Settings.DisableThirdPersonAnimations;
            if (Settings.DisableThirdPersonAnimations)
            {
                LoggerUtil.Notify(api, this, $"Third person animations disabled");
                api.TriggerIngameError(this, "animationsDisabled", "Third person Overhaul lib animations are DISABLED");
            }
            else
            {
                Settings.DisableAllAnimations = false;
                LoggerUtil.Notify(api, this, $"All animations enabled");
                api.TriggerIngameError(this, "animationsDisabled", "All Overhaul lib animations are ENABLED");
            }
            return true;
        });

        api.Event.PlayerEntitySpawn += EnsureOwnPlayerAnimationBehaviors;
        api.Event.LevelFinalize += EnsureOwnPlayerAnimationBehaviors;
        if (ShieldAutoPatcher.IsCombatOverhaulEnabled(api))
        {
            api.Event.LevelFinalize += PatchShieldsOnClientLevelFinalize;
            ShieldAutoPatcher.Patch(api);
        }
        _ensureAnimationBehaviorsListener = api.Event.RegisterGameTickListener(_ => EnsureOwnPlayerAnimationBehaviors(), 1000, 1000);
    }
    public override void AssetsLoaded(ICoreAPI api)
    {
        StartConfigLibSubscription(api);
        ApplyConfigFileSettings(api);
        ApplyRuntimeSettings(Settings);
        HarmonyPatchesManager.ConfigureDiagnostics(api, Settings);
        QuenchablePatchGate.DisableCustomQuenchRecipeAssetsIfDisabled(api);

        if (api is not ICoreClientAPI clientApi) return;

        foreach (ArmorLayers layer in Enum.GetValues<ArmorLayers>())
        {
            foreach (DamageZone zone in Enum.GetValues<DamageZone>())
            {
                string iconPath = $"combatoverhaul:textures/gui/icons/armor-{layer}-{zone}.svg";
                string iconCode = $"combatoverhaul-armor-{layer}-{zone}";

                if (!clientApi.Assets.Exists(new AssetLocation(iconPath))) continue;

                RegisterCustomIcon(clientApi, iconCode, iconPath);
            }
        }

        List<IAsset> icons = clientApi.Assets.GetManyInCategory("textures", _iconsFolder, loadAsset: false);
        foreach (IAsset icon in icons)
        {
            string iconPath = icon.Location.ToString();
            string iconCode = icon.Location.Domain + ":" + icon.Location.Path[_iconsPath.Length..^4].ToLowerInvariant();

            if (!iconPath.ToLowerInvariant().EndsWith(".svg"))
            {
                LoggerUtil.Verbose(clientApi, this, $"Icon should have '.svg' format, skipping. Path: {iconPath}");
                return;
            }

            RegisterCustomIcon(clientApi, iconCode, iconPath);
        }
    }
    public override void AssetsFinalize(ICoreAPI api)
    {
        QuenchablePatchGate.RemoveCustomQuenchRecipesIfDisabled(api);
        ArmorQuenchComponents.Apply(api);

        IAsset armorConfigAsset = api.Assets.Get("combatoverhaul:config/armor-config.json");
        JsonObject armorConfig = JsonObject.FromJson(armorConfigAsset.ToText());
        ArmorConfig armorConfigObj = armorConfig.AsObject<ArmorConfig>();

        DamageResistData.MaxAttackTier = armorConfigObj.MaxAttackTier;
        DamageResistData.MaxArmorTier = armorConfigObj.MaxArmorTier;
        DamageResistData.DamageReduction = armorConfigObj.DamageReduction;

        if (api is ICoreClientAPI clientApi)
        {
            DetermineSlotsStatus(clientApi);
        }

        EnsureTongsTransformsForForgableItems(api);
        GrindingWheelCompat.EnsureWeaponBuffableBehavior(api);
    }

    private static readonly JObject DefaultOnTongTransform = JObject.Parse("""
    {
      "translation": { "x": -0.9, "y": -0.74, "z": -0.39 },
      "rotation": { "x": -102, "y": -109, "z": 16 }
    }
    """);

    private static readonly JObject DefaultOnMetalTongTransform = JObject.Parse("""
    {
      "translation": { "x": -1.2, "y": -0.74, "z": -0.59 },
      "rotation": { "x": -90, "y": -102, "z": -92 },
      "origin": { "x": 0.5, "y": 0.6, "z": 0.5 },
      "scale": 0.93
    }
    """);

    private static void EnsureTongsTransformsForForgableItems(ICoreAPI api)
    {
        int patched = 0;

        foreach (Item item in api.World.Items)
        {
            if (item?.Code?.Domain is not ("armory" or "combatoverhaul")) continue;
            if (item.Attributes?["forgable"]?.AsBool(false) != true) continue;

            JObject attrs = (item.Attributes?.Token as JObject)?.DeepClone() as JObject ?? new JObject();
            bool changed = false;

            if (attrs["onTongTransform"] == null)
            {
                attrs["onTongTransform"] = DefaultOnTongTransform.DeepClone();
                changed = true;
            }

            if (attrs["onMetalTongTransform"] == null)
            {
                attrs["onMetalTongTransform"] = DefaultOnMetalTongTransform.DeepClone();
                changed = true;
            }

            if (!changed) continue;

            item.Attributes = new JsonObject(attrs);
            patched++;
        }

        if (patched > 0)
        {
            api.Logger.Notification($"[OverhaullibLegacyCompat] Applied tongs transforms to {patched} forgable CO/Armory items.");
        }
    }
    public override void Dispose()
    {
        if (Disposed) return;

        HarmonyPatchesManager.Unpatch();
        GrindingWheelCompat.Dispose();
        StopConfigLibSubscriptionRetry();
        UnsubscribeFromConfigChange();
        _configLibSubscriptionApi = null;
        if (_serverApi != null && _playerNowPlayingSettingsHandler != null)
        {
            _serverApi.Event.PlayerNowPlaying -= _playerNowPlayingSettingsHandler;
        }
        if (_serverApi != null && _shieldAutoPatchServerListener != 0)
        {
            _serverApi.Event.UnregisterGameTickListener(_shieldAutoPatchServerListener);
            _shieldAutoPatchServerListener = 0;
        }
        _serverApi = null;
        _playerNowPlayingSettingsHandler = null;

        ClientThreadCleanup.DisposeRenderer(_clientApi, ReticleRenderer, EnumRenderStage.Ortho, "combat-overhaul-reticle-dispose");
        ClientThreadCleanup.DisposeRenderer(_clientApi, DirectionCursorRenderer, EnumRenderStage.Ortho, "combat-overhaul-direction-cursor-dispose");
        if (_clientApi != null)
        {
            _clientApi.Event.PlayerEntitySpawn -= EnsureOwnPlayerAnimationBehaviors;
            _clientApi.Event.LevelFinalize -= EnsureOwnPlayerAnimationBehaviors;
            _clientApi.Event.LevelFinalize -= PatchShieldsOnClientLevelFinalize;
            if (_ensureAnimationBehaviorsListener != 0)
            {
                _clientApi.Event.UnregisterGameTickListener(_ensureAnimationBehaviorsListener);
                _ensureAnimationBehaviorsListener = 0;
            }
        }

        ActionListener?.Dispose();
        AimingSystem?.Dispose();

        OnDispose?.Invoke();
        OnDispose = null;

        _clientApi?.World.UnregisterGameTickListener(_cacheMissesReportedListener);

        Disposed = true;
        ServerImpaleSystem?.Dispose();
        ServerVanitySystem?.Dispose();
    }

    public bool ToggleWearableItem(IPlayer player, string hotkeyCode)
    {
        IInventory? gearInventory = player.Entity.GetBehavior<EntityBehaviorPlayerInventory>()?.Inventory;

        if (gearInventory == null) return false;

        bool toggled = false;
        foreach (ItemSlot slot in gearInventory)
        {
            if (slot?.Itemstack?.Collectible?.GetCollectibleInterface<ITogglableItem>() is ITogglableItem togglableItem && togglableItem.HotKeyCode == hotkeyCode)
            {
                togglableItem.Toggle(player, slot);
                toggled = true;
            }
        }

        if (player is IClientPlayer)
        {
            _clientToggleChannel?.SendPacket(new TogglePacket() { HotKeyCode = hotkeyCode });
        }

        return toggled;
    }
    public void ToggleWearableItem(IServerPlayer player, TogglePacket packet) => ToggleWearableItem(player, packet.HotKeyCode);

    private void SendGameplaySettings(IServerPlayer player)
    {
        _serverGameplaySettingsChannel?.SendPacket(ServerGameplaySettingsPacket.From(Settings), player);
    }

    private void BroadcastGameplaySettings()
    {
        _serverGameplaySettingsChannel?.BroadcastPacket(ServerGameplaySettingsPacket.From(Settings));
    }

    private void HandleServerGameplaySettings(ServerGameplaySettingsPacket packet)
    {
        _lastServerGameplaySettings = packet;
        packet.ApplyTo(Settings);
        ApplyRuntimeSettings(Settings);
        SettingsChanged?.Invoke(Settings);
    }

    private void ReapplyServerGameplaySettingsIfNeeded(ICoreAPI? api)
    {
        if (api?.Side != EnumAppSide.Client || _lastServerGameplaySettings == null) return;

        _lastServerGameplaySettings.ApplyTo(Settings);
    }

    public ProjectileSystemClient? ClientProjectileSystem { get; private set; }
    public ProjectileSystemServer? ServerProjectileSystem { get; private set; }
    public ActionListener? ActionListener { get; private set; }
    public DirectionCursorRenderer? DirectionCursorRenderer { get; private set; }
    public ReticleRenderer? ReticleRenderer { get; private set; }
    public ClientAimingSystem? AimingSystem { get; private set; }
    public DirectionController? DirectionController { get; private set; }
    public RangedWeaponSystemClient? ClientRangedWeaponSystem { get; private set; }
    public RangedWeaponSystemServer? ServerRangedWeaponSystem { get; private set; }
    public SoundsSynchronizerClient? ClientSoundsSynchronizer { get; private set; }
    public SoundsSynchronizerServer? ServerSoundsSynchronizer { get; private set; }
    public MeleeSystemClient? ClientMeleeSystem { get; private set; }
    public MeleeSystemServer? ServerMeleeSystem { get; private set; }
    public ImpaleSystemClient? ClientImpaleSystem { get; private set; }
    public ImpaleSystemServer? ServerImpaleSystem { get; private set; }
    public MeleeBlockSystemClient? ClientBlockSystem { get; private set; }
    public MeleeBlockSystemServer? ServerBlockSystem { get; private set; }
    public StatsSystemClient? ClientStatsSystem { get; private set; }
    public StatsSystemServer? ServerStatsSystem { get; private set; }
    public AttachableSystemClient? ClientAttachmentSystem { get; private set; }
    public AttachableSystemServer? ServerAttachmentSystem { get; private set; }
    public ToolBagSystemClient? ClientToolBagSystem { get; private set; }
    public ToolBagSystemServer? ServerToolBagSystem { get; private set; }
    public ToolBagSelectionSystemClient? ClientToolBagSelectionSystem { get; private set; }
    public VanitySystemClient? ClientVanitySystem { get; private set; }
    public VanitySystemServer? ServerVanitySystem { get; private set; }

    private ICoreClientAPI? _clientApi;
    private readonly Vector4 _iconScale = new(-0.1f, -0.1f, 1.2f, 1.2f);
    private IClientNetworkChannel? _clientToggleChannel;
    private IServerNetworkChannel? _serverToggleChannel;
    private IClientNetworkChannel? _clientGameplaySettingsChannel;
    private IServerNetworkChannel? _serverGameplaySettingsChannel;
    private ICoreServerAPI? _serverApi;
    private PlayerDelegate? _playerNowPlayingSettingsHandler;
    private ServerGameplaySettingsPacket? _lastServerGameplaySettings;
    private const string GameplaySettingsChannelId = "CombatOverhaul:server-gameplay-settings";
    private const string _iconsFolder = "sloticons";
    private const string _iconsPath = $"textures/{_iconsFolder}/";
    private long _cacheMissesReportedListener = 0;
    private long _ensureAnimationBehaviorsListener = 0;
    private long _shieldAutoPatchServerListener = 0;
    private bool _reportedAnimationBehaviorFallback = false;
    private bool _reportedAnimationBehaviorFallbackError = false;
    private readonly HashSet<AssetLocation> _reportedMissingSvgIcons = [];
    private object? _configLibSystem;
    private EventInfo? _configSettingChangedEvent;
    private EventInfo? _configLoadedEvent;
    private MethodInfo? _configGetConfigMethod;
    private Delegate? _configSettingChangedHandler;
    private Delegate? _configLoadedHandler;
    private ICoreAPI? _configLibSubscriptionApi;
    private long _configLibSubscribeRetryListener;
    private bool _configLibSubscribed;
    private bool _reportedConfigLibIntegrationError;
    private string? _lastAppliedConfigFileSettingsSummary;
    private const int ConfigLibSubscribeRetryIntervalMs = 500;

    private void RegisterCustomIcon(ICoreClientAPI api, string key, string path)
    {
        AssetLocation location = new(path);
        IAsset? svgAsset = TryGetLoadedSvgAsset(api, location);

        api.Gui.Icons.CustomIcons[key] = delegate (Context ctx, int x, int y, float w, float h, double[] rgba)
        {
            svgAsset = EnsureLoadedSvgAsset(api, location, svgAsset);
            if (svgAsset == null) return;

            int value = ColorUtil.ColorFromRgba(75, 75, 75, 255);

            if (rgba.Length == 4)
            {
                value = ColorUtil.ColorFromRgba(rgba);
            }

            if (rgba.Length >= 4 && rgba[0] == 0 && rgba[1] == 0 && rgba[2] == 0 && rgba[3] == 0.2) // To override vanilla clothes and armor icon color
            {
                value = ColorUtil.ColorFromRgba(75, 75, 75, 190);
            }

            Surface target = ctx.GetTarget();

            int xNew = x + (int)(w * _iconScale.X);
            int yNew = y + (int)(h * _iconScale.Y);
            int wNew = (int)(w * _iconScale.W);
            int hNew = (int)(h * _iconScale.Z);

            api.Gui.DrawSvg(svgAsset, (ImageSurface)(object)((target is ImageSurface) ? target : null), xNew, yNew, wNew, hNew, value);
        };
    }

    private IAsset? EnsureLoadedSvgAsset(ICoreClientAPI api, AssetLocation location, IAsset? asset)
    {
        if (asset?.IsLoaded() == true && asset.Data != null) return asset;

        asset = TryGetLoadedSvgAsset(api, location);
        if (asset != null) return asset;

        if (_reportedMissingSvgIcons.Add(location))
        {
            LoggerUtil.Warn(api, this, $"Failed to draw custom GUI icon '{location}': SVG asset is missing or not loaded");
        }

        return null;
    }

    private static IAsset? TryGetLoadedSvgAsset(ICoreClientAPI api, AssetLocation location)
    {
        IAsset? asset = api.Assets.TryGet(location, loadAsset: true);
        return asset?.IsLoaded() == true && asset.Data != null ? asset : null;
    }

    private void StartConfigLibSubscription(ICoreAPI api)
    {
        if (_configLibSubscribed || _reportedConfigLibIntegrationError) return;
        if (!api.ModLoader.IsModEnabled("configlib")) return;

        _configLibSubscriptionApi = api;

        if (SubscribeToConfigChange(api)) return;
        if (_reportedConfigLibIntegrationError || _configLibSubscribeRetryListener != 0) return;

        _configLibSubscribeRetryListener = api.Event.RegisterGameTickListener(_ =>
        {
            if (_reportedConfigLibIntegrationError)
            {
                StopConfigLibSubscriptionRetry();
                return;
            }

            if (!SubscribeToConfigChange(api)) return;

            StopConfigLibSubscriptionRetry();
        }, ConfigLibSubscribeRetryIntervalMs, ConfigLibSubscribeRetryIntervalMs);
    }

    private void StopConfigLibSubscriptionRetry()
    {
        if (_configLibSubscribeRetryListener == 0) return;

        _configLibSubscriptionApi?.Event.UnregisterGameTickListener(_configLibSubscribeRetryListener);
        _configLibSubscribeRetryListener = 0;
    }

    private bool SubscribeToConfigChange(ICoreAPI api)
    {
        if (_configLibSubscribed) return true;

        try
        {
            Type? configLibSystemType = FindLoadedType("ConfigLib.ConfigLibModSystem");
            if (configLibSystemType == null) return false;

            object? system = GetModSystem(api, configLibSystemType);
            if (system == null) return false;

            _configLibSystem = system;
            _configGetConfigMethod = configLibSystemType.GetMethod("GetConfig", BindingFlags.Public | BindingFlags.Instance, null, [typeof(string)], null);
            _configSettingChangedEvent = configLibSystemType.GetEvent("SettingChanged", BindingFlags.Public | BindingFlags.Instance);
            _configLoadedEvent = configLibSystemType.GetEvent("ConfigsLoaded", BindingFlags.Public | BindingFlags.Instance);

            if (_configSettingChangedEvent?.EventHandlerType != null)
            {
                _configSettingChangedHandler = CreateConfigSettingChangedHandler(_configSettingChangedEvent.EventHandlerType);
                if (_configSettingChangedHandler != null)
                {
                    _configSettingChangedEvent.AddEventHandler(system, _configSettingChangedHandler);
                }
            }

            if (_configLoadedEvent?.EventHandlerType != null)
            {
                _configLoadedHandler = CreateConfigLoadedHandler(_configLoadedEvent.EventHandlerType);
                if (_configLoadedHandler != null)
                {
                    _configLoadedEvent.AddEventHandler(system, _configLoadedHandler);
                }
            }

            _configLibSubscribed = true;
            OnConfigLoaded();
            return true;
        }
        catch (Exception exception)
        {
            ReportConfigLibIntegrationError(api, exception);
            UnsubscribeFromConfigChange();
            return false;
        }
    }

    private void OnConfigSettingChanged(string domain, object setting)
    {
        if (domain != "combatoverhaul" && domain != "combatoverhaulfork" && domain != "bullseyecontinued" && domain != "overhaullib") return;

        bool applied = TryInvokeConfigAssignment(setting, "AssignSettingValue");
        if (!applied)
        {
            applied = ApplyConfigFileSettings(_configLibSubscriptionApi);
        }

        if (applied)
        {
            SaveArmorQuenchConfig(_configLibSubscriptionApi);
            ReapplyServerGameplaySettingsIfNeeded(_configLibSubscriptionApi);
            ApplyRuntimeSettings(Settings);
            if (_configLibSubscriptionApi != null)
            {
                HarmonyPatchesManager.ConfigureDiagnostics(_configLibSubscriptionApi, Settings);
            }
            if (_configLibSubscriptionApi?.Side == EnumAppSide.Server)
            {
                BroadcastGameplaySettings();
            }
            SettingsChanged?.Invoke(Settings);
        }
    }

    private void OnConfigLoaded()
    {
        AssignConfigSettings("combatoverhaul");
        AssignConfigSettings("combatoverhaulfork");
        AssignConfigSettings("bullseyecontinued");
        AssignConfigSettings("overhaullib");
        ApplyConfigFileSettings(_configLibSubscriptionApi);
        ReapplyServerGameplaySettingsIfNeeded(_configLibSubscriptionApi);
        ApplyRuntimeSettings(Settings);
        if (_configLibSubscriptionApi != null)
        {
            HarmonyPatchesManager.ConfigureDiagnostics(_configLibSubscriptionApi, Settings);
        }
        if (_configLibSubscriptionApi?.Side == EnumAppSide.Server)
        {
            BroadcastGameplaySettings();
        }
        SettingsLoaded?.Invoke(Settings);
    }

    private void UnsubscribeFromConfigChange()
    {
        if (_configLibSystem != null)
        {
            if (_configSettingChangedHandler != null)
            {
                _configSettingChangedEvent?.RemoveEventHandler(_configLibSystem, _configSettingChangedHandler);
            }

            if (_configLoadedHandler != null)
            {
                _configLoadedEvent?.RemoveEventHandler(_configLibSystem, _configLoadedHandler);
            }
        }

        _configLibSystem = null;
        _configSettingChangedEvent = null;
        _configLoadedEvent = null;
        _configGetConfigMethod = null;
        _configSettingChangedHandler = null;
        _configLoadedHandler = null;
        _configLibSubscribed = false;
    }

    private void AssignConfigSettings(string domain)
    {
        if (_configLibSystem == null || _configGetConfigMethod == null) return;

        object? config = _configGetConfigMethod.Invoke(_configLibSystem, [domain]);
        if (config == null) return;

        TryInvokeConfigAssignment(config, "AssignSettingsValues");
    }

    private bool ApplyConfigFileSettings(ICoreAPI? api)
    {
        bool applied = ApplyYamlConfigFileSettings(api);
        return ApplyArmorQuenchConfig(api) || applied;
    }

    private const string ArmorQuenchConfigFile = "overhaulliblegacy-armorquenching.json";
    private bool _armorQuenchConfigLoaded;
    private static readonly PropertyInfo[] ArmorQuenchProperties = typeof(Settings).GetProperties()
        .Where(property => property.Name.StartsWith("ArmorQuench", StringComparison.Ordinal)).ToArray();

    private bool ApplyArmorQuenchConfig(ICoreAPI? api)
    {
        if (api?.Side != EnumAppSide.Server) return false;
        _armorQuenchConfigLoaded = false;
        try
        {
            JObject? config = api.LoadModConfig<JObject>(ArmorQuenchConfigFile);
            var values = new Dictionary<PropertyInfo, object?>();
            foreach (PropertyInfo property in ArmorQuenchProperties)
            {
                JToken? token = config?.GetValue(property.Name, StringComparison.OrdinalIgnoreCase);
                if (token == null) continue;
                object? value = token.ToObject(property.PropertyType);
                if (value == null || value is float number && !float.IsFinite(number))
                    throw new FormatException($"Invalid value for {property.Name}.");
                values[property] = value;
            }
            foreach (var (property, value) in values) property.SetValue(Settings, value);
            _armorQuenchConfigLoaded = true;
            SaveArmorQuenchConfig(api);
            return true;
        }
        catch (Exception exception)
        {
            LoggerUtil.Warn(api, this, $"Could not load {ArmorQuenchConfigFile}; the file was left unchanged: {exception.Message}");
            return false;
        }
    }

    private void SaveArmorQuenchConfig(ICoreAPI? api)
    {
        if (api?.Side != EnumAppSide.Server || !_armorQuenchConfigLoaded) return;
        try
        {
            api.StoreModConfig(ArmorQuenchProperties.ToDictionary(property => property.Name,
                property => property.GetValue(Settings)), ArmorQuenchConfigFile);
        }
        catch (Exception exception)
        {
            LoggerUtil.Warn(api, this, $"Could not save {ArmorQuenchConfigFile}: {exception.Message}");
        }
    }

    private bool ApplyYamlConfigFileSettings(ICoreAPI? api)
    {
        string configPath = System.IO.Path.Combine(GamePaths.ModConfig, "combatoverhaul.yaml");
        if (!System.IO.File.Exists(configPath)) return false;

        List<string> appliedSettings = [];

        try
        {
            foreach (string rawLine in System.IO.File.ReadLines(configPath))
            {
                string line = StripYamlComment(rawLine).Trim();
                if (line.Length == 0) continue;

                int separator = line.IndexOf(':');
                if (separator <= 0) continue;

                string key = line[..separator].Trim();
                string value = line[(separator + 1)..].Trim();
                if (key.Length == 0 || value.Length == 0) continue;

                if (TryAssignSettingsPropertyFromString(key, value, out string? appliedSetting) && appliedSetting != null)
                {
                    appliedSettings.Add(appliedSetting);
                }
            }
        }
        catch (Exception exception)
        {
            LoggerUtil.Warn(api ?? _clientApi, typeof(CombatOverhaulSystem), $"Could not apply Combat Overhaul runtime config file '{configPath}':\n{exception}");
            return false;
        }

        if (appliedSettings.Count == 0) return false;

        string summary = string.Join(", ", appliedSettings.OrderBy(setting => setting));
        if (!string.Equals(summary, _lastAppliedConfigFileSettingsSummary, StringComparison.Ordinal))
        {
            _lastAppliedConfigFileSettingsSummary = summary;
        }

        return true;
    }

    private bool TryAssignSettingsPropertyFromString(string key, string value, out string? appliedSetting)
    {
        appliedSetting = null;
        string normalizedKey = NormalizeConfigName(key);
        PropertyInfo? property = typeof(Settings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(property => NormalizeConfigName(property.Name) == normalizedKey);

        if (property?.CanWrite != true) return false;
        if (!TryConvertConfigValue(value, property.PropertyType, out object? converted)) return false;

        property.SetValue(Settings, converted);
        appliedSetting = $"{key}={FormatConfigValue(converted)}";
        return true;
    }

    private static bool TryConvertConfigValue(string value, Type targetType, out object? converted)
    {
        converted = null;
        value = UnquoteConfigScalar(value);

        if (targetType == typeof(bool))
        {
            if (!bool.TryParse(value, out bool boolValue)) return false;
            converted = boolValue;
            return true;
        }

        if (targetType == typeof(int))
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue)) return false;
            converted = intValue;
            return true;
        }

        if (targetType == typeof(float))
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue)) return false;
            converted = floatValue;
            return true;
        }

        if (targetType == typeof(string))
        {
            converted = value;
            return true;
        }

        return false;
    }

    private static string FormatConfigValue(object? value) => value switch
    {
        bool boolValue => boolValue.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
        float floatValue => floatValue.ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value?.ToString() ?? ""
    };

    private static string UnquoteConfigScalar(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    private static string StripYamlComment(string line)
    {
        bool inSingleQuote = false;
        bool inDoubleQuote = false;

        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (character == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (character == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (character == '#' && !inSingleQuote && !inDoubleQuote)
            {
                return line[..index];
            }
        }

        return line;
    }

    private static string NormalizeConfigName(string name)
    {
        Span<char> buffer = stackalloc char[name.Length];
        int length = 0;

        foreach (char character in name)
        {
            if (!char.IsLetterOrDigit(character)) continue;
            buffer[length++] = char.ToLowerInvariant(character);
        }

        return new string(buffer[..length]);
    }

    private bool TryInvokeConfigAssignment(object target, string methodName)
    {
        try
        {
            if (TryInvokeInstanceConfigAssignment(target, methodName)) return true;
            if (TryInvokeExtensionConfigAssignment(target, methodName)) return true;
        }
        catch (Exception exception)
        {
            ReportConfigLibIntegrationError(null, exception);
        }

        return false;
    }

    private bool TryInvokeInstanceConfigAssignment(object target, string methodName)
    {
        MethodInfo? method = target.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method => IsConfigAssignmentMethod(method, methodName, extensionMethod: false, target.GetType()));

        if (method == null) return false;

        method.Invoke(target, [Settings]);
        return true;
    }

    private bool TryInvokeExtensionConfigAssignment(object target, string methodName)
    {
        Type targetType = target.GetType();
        Type settingsType = Settings.GetType();
        Assembly configLibAssembly = targetType.Assembly;

        foreach (Type type in GetLoadableTypes(configLibAssembly))
        {
            if (!type.IsAbstract || !type.IsSealed) continue;

            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), inherit: false)) continue;
                if (!IsConfigAssignmentMethod(method, methodName, extensionMethod: true, targetType)) continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (!parameters[1].ParameterType.IsAssignableFrom(settingsType)) continue;

                method.Invoke(null, [target, Settings]);
                return true;
            }
        }

        return false;
    }

    private static bool IsConfigAssignmentMethod(MethodInfo method, string methodName, bool extensionMethod, Type targetType)
    {
        if (method.Name != methodName) return false;

        ParameterInfo[] parameters = method.GetParameters();
        if (extensionMethod)
        {
            return parameters.Length == 2 && parameters[0].ParameterType.IsAssignableFrom(targetType);
        }

        return parameters.Length == 1;
    }

    private Delegate? CreateConfigSettingChangedHandler(Type eventHandlerType)
    {
        MethodInfo? invokeMethod = eventHandlerType.GetMethod("Invoke");
        ParameterInfo[]? parametersInfo = invokeMethod?.GetParameters();
        if (parametersInfo?.Length != 3) return null;

        ParameterExpression[] parameters = parametersInfo
            .Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name))
            .ToArray();
        MethodInfo targetMethod = GetType().GetMethod(nameof(OnConfigSettingChanged), BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(nameof(OnConfigSettingChanged));
        MethodCallExpression body = Expression.Call(
            Expression.Constant(this),
            targetMethod,
            Expression.Convert(parameters[0], typeof(string)),
            Expression.Convert(parameters[2], typeof(object)));

        return Expression.Lambda(eventHandlerType, body, parameters).Compile();
    }

    private Delegate? CreateConfigLoadedHandler(Type eventHandlerType)
    {
        MethodInfo? invokeMethod = eventHandlerType.GetMethod("Invoke");
        ParameterInfo[]? parametersInfo = invokeMethod?.GetParameters();
        if (parametersInfo?.Length != 0) return null;

        MethodInfo targetMethod = GetType().GetMethod(nameof(OnConfigLoaded), BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(nameof(OnConfigLoaded));
        return Delegate.CreateDelegate(eventHandlerType, this, targetMethod, throwOnBindFailure: false);
    }

    private static object? GetModSystem(ICoreAPI api, Type modSystemType)
    {
        MethodInfo? getModSystem = api.ModLoader.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "GetModSystem" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0);

        return getModSystem?.MakeGenericMethod(modSystemType).Invoke(api.ModLoader, null);
    }

    private static Type? FindLoadedType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(fullName, throwOnError: false);
            if (type != null) return type;
        }

        return null;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type != null).Cast<Type>();
        }
    }

    private void ReportConfigLibIntegrationError(ICoreAPI? api, Exception exception)
    {
        if (_reportedConfigLibIntegrationError) return;

        _reportedConfigLibIntegrationError = true;
        LoggerUtil.Warn(api ?? _clientApi, typeof(CombatOverhaulSystem), $"ConfigLib optional integration failed and was disabled:\n{exception}");
    }

    private static void ApplyRuntimeSettings(Settings settings)
    {
        DamageResistData.EntityProtectionFactor = settings.EntityProtectionMultiplier;
        QuenchableStatUtil.WeaponDamageMultiplier = Math.Max(0f, settings.WeaponQuenchDamageMultiplier);
        QuenchableStatUtil.ArmorQuenchPlateOnly = settings.ArmorQuenchPlateOnly;
        QuenchableStatUtil.ArmorQuenchFlatReduction = float.IsFinite(settings.ArmorQuenchFlatReduction) ? Math.Clamp(settings.ArmorQuenchFlatReduction, 0f, 100f) : 0.2f;
        QuenchableStatUtil.ArmorQuenchDurabilityBonus = float.IsFinite(settings.ArmorQuenchDurabilityBonus) ? Math.Clamp(settings.ArmorQuenchDurabilityBonus, 0f, 10f) : 0.1f;
        QuenchableStatUtil.ArmorQuenchPenaltyReduction = float.IsFinite(settings.ArmorQuenchPenaltyReduction) ? Math.Clamp(settings.ArmorQuenchPenaltyReduction, 0f, 1f) : 0.1f;
        QuenchableStatUtil.ArmorQuenchMaxPenaltyReduction = float.IsFinite(settings.ArmorQuenchMaxPenaltyReduction) ? Math.Clamp(settings.ArmorQuenchMaxPenaltyReduction, 0f, 1f) : 0.5f;
        QuenchableStatUtil.ArmorQuenchBaseShatterChance = float.IsFinite(settings.ArmorQuenchBaseShatterChance) ? Math.Clamp(settings.ArmorQuenchBaseShatterChance, 0f, 1f) : 0.05f;
        QuenchableStatUtil.ArmorQuenchShatterChancePerQuench = float.IsFinite(settings.ArmorQuenchShatterChancePerQuench) ? Math.Clamp(settings.ArmorQuenchShatterChancePerQuench, 0f, 1f) : 0.05f;
        QuenchableStatUtil.ArmorQuenchTemperShatterMultiplier = float.IsFinite(settings.ArmorQuenchTemperShatterMultiplier) ? Math.Clamp(settings.ArmorQuenchTemperShatterMultiplier, 0f, 1f) : 0.8f;
        QuenchableStatUtil.ArmorQuenchTemperPowerMultiplier = float.IsFinite(settings.ArmorQuenchTemperPowerMultiplier) ? Math.Clamp(settings.ArmorQuenchTemperPowerMultiplier, 0f, 1f) : 0.92f;

    }

    private void EnsureOwnPlayerAnimationBehaviors(IClientPlayer player)
    {
        if (player.Entity?.EntityId == _clientApi?.World?.Player?.Entity?.EntityId)
        {
            EnsureOwnPlayerAnimationBehaviors();
        }
    }

    private void EnsureOwnPlayerAnimationBehaviors()
    {
        try
        {
            EntityPlayer? playerEntity = _clientApi?.World?.Player?.Entity;
            if (playerEntity == null) return;

            // Common steady-state case: all three behaviors already exist. Bail before allocating
            // the throwaway JsonObject below; this watchdog runs once per second for the whole session.
            if (playerEntity.GetBehavior<FirstPersonAnimationsBehavior>() != null
                && playerEntity.GetBehavior<ThirdPersonAnimationsBehavior>() != null
                && playerEntity.GetBehavior<WearableStatsBehavior>() != null)
            {
                return;
            }

            bool added = false;
            JsonObject emptyAttributes = new(new JObject());

            if (playerEntity.GetBehavior<FirstPersonAnimationsBehavior>() == null)
            {
                FirstPersonAnimationsBehavior firstPerson = new(playerEntity);
                playerEntity.AddBehavior(firstPerson);
                firstPerson.Initialize(playerEntity.Properties, emptyAttributes);
                firstPerson.AfterInitialized(false);
                added = true;
            }

            if (playerEntity.GetBehavior<ThirdPersonAnimationsBehavior>() == null)
            {
                ThirdPersonAnimationsBehavior thirdPerson = new(playerEntity);
                playerEntity.AddBehavior(thirdPerson);
                thirdPerson.Initialize(playerEntity.Properties, emptyAttributes);
                thirdPerson.AfterInitialized(false);
                added = true;
            }

            if (playerEntity.GetBehavior<WearableStatsBehavior>() == null)
            {
                WearableStatsBehavior wearableStats = new(playerEntity);
                playerEntity.AddBehavior(wearableStats);
                wearableStats.Initialize(playerEntity.Properties, emptyAttributes);
                wearableStats.AfterInitialized(false);
                wearableStats.RefreshStatsNow();
                added = true;
            }

            if (added && !_reportedAnimationBehaviorFallback)
            {
                _reportedAnimationBehaviorFallback = true;
                LoggerUtil.Warn(_clientApi, this, "Attached missing OverhaulLib player client behaviors at runtime.");
            }
        }
        catch (Exception exception)
        {
            if (!_reportedAnimationBehaviorFallbackError)
            {
                _reportedAnimationBehaviorFallbackError = true;
                LoggerUtil.Warn(_clientApi, this, $"Could not attach OverhaulLib player animation behaviors at runtime: {exception}");
            }
        }
    }

    private void PatchShieldsOnClientLevelFinalize()
    {
        if (_clientApi != null)
        {
            ShieldAutoPatcher.Patch(_clientApi);
        }
    }

    private void PatchShieldsOnServerTick(float dt)
    {
        if (_serverApi == null) return;

        if (_shieldAutoPatchServerListener != 0)
        {
            _serverApi.Event.UnregisterGameTickListener(_shieldAutoPatchServerListener);
            _shieldAutoPatchServerListener = 0;
        }

        ShieldAutoPatcher.Patch(_serverApi);
    }

    private void DetermineSlotsStatus(ICoreClientAPI api)
    {
        foreach (Item? item in api.World.Items)
        {
            string? stackDressType = item?.Attributes?["clothescategory"].AsString() ?? item?.Attributes?["attachableToEntity"]["categoryCode"].AsString();
            string[]? stackDressTypes = item?.Attributes?["clothescategories"].AsObject<string[]>() ?? item?.Attributes?["attachableToEntity"]["categoryCodes"].AsObject<string[]>();

            if (stackDressType != null)
            {
                SetSlotsStatus(stackDressType);
            }

            if (stackDressTypes != null)
            {
                foreach (string gearType in stackDressTypes)
                {
                    SetSlotsStatus(gearType);
                }
            }
        }
    }

    private static void SetSlotsStatus(string gearType)
    {
        switch (gearType)
        {
            case "miscgear": CharacterTabPatch.SlotsStatus.Misc = true; break;
            case "headgear": CharacterTabPatch.SlotsStatus.Headgear = true; break;
            case "frontgear": CharacterTabPatch.SlotsStatus.FrontGear = true; break;
            case "backgear": CharacterTabPatch.SlotsStatus.BackGear = true; break;
            case "rightshouldergear": CharacterTabPatch.SlotsStatus.RightShoulderGear = true; break;
            case "leftshouldergear": CharacterTabPatch.SlotsStatus.LeftShoulderGear = true; break;
            case "waistgear": CharacterTabPatch.SlotsStatus.WaistHear = true; break;
            case "addBeltLeft": CharacterTabPatch.SlotsStatus.Belt = true; break;
            case "addBeltRight": CharacterTabPatch.SlotsStatus.Belt = true; break;
            case "addBeltBack": CharacterTabPatch.SlotsStatus.Belt = true; break;
            case "addBeltFront": CharacterTabPatch.SlotsStatus.Belt = true; break;
            case "addBackpack1": CharacterTabPatch.SlotsStatus.Backpack = true; break;
            case "addBackpack2": CharacterTabPatch.SlotsStatus.Backpack = true; break;
            case "addBackpack3": CharacterTabPatch.SlotsStatus.Backpack = true; break;
            case "addBackpack4": CharacterTabPatch.SlotsStatus.Backpack = true; break;
        }
    }
}

public partial class CombatOverhaulAnimationsSystem : ModSystem
{
    public AnimationsManager? PlayerAnimationsManager { get; private set; }
    public DebugWindowManager? DebugManager { get; private set; }
    public ParticleEffectsManager? ParticleEffectsManager { get; private set; }
    public VanillaAnimationsSystemClient? ClientVanillaAnimations { get; private set; }
    public VanillaAnimationsSystemServer? ServerVanillaAnimations { get; private set; }
    public AnimationSystemClient? ClientTpAnimationSystem { get; private set; }
    public AnimationSystemServer? ServerTpAnimationSystem { get; private set; }

    public IShaderProgram? AnimatedItemShaderProgram => _shaderProgram;
    public IShaderProgram? AnimatedItemShaderProgramFirstPerson => _shaderProgramFirstPerson;

    public override void Start(ICoreAPI api)
    {
        _api = api;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Event.ReloadShader += LoadAnimatedItemShaders;
        _ = LoadAnimatedItemShaders();
        ParticleEffectsManager = new(api);
        PlayerAnimationsManager = new(api, ParticleEffectsManager);
        DebugManager = new(api, ParticleEffectsManager, PlayerAnimationsManager);
        ClientVanillaAnimations = new(api);
        ClientTpAnimationSystem = new(api);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        ParticleEffectsManager = new(api);
        ServerVanillaAnimations = new(api);
        ServerTpAnimationSystem = new(api);
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        PlayerAnimationsManager?.Load();
        if (api is ICoreClientAPI) DebugManager?.Load(api as ICoreClientAPI);
    }

    public override void Dispose()
    {
        if (_api is ICoreClientAPI clientApi)
        {
            clientApi.Event.ReloadShader -= LoadAnimatedItemShaders;
        }

        DisposeShaders();
        DebugManager?.Dispose();
        DebugManager = null;
    }


    private ShaderProgram? _shaderProgram;
    private ShaderProgram? _shaderProgramFirstPerson;
    private ICoreAPI? _api;

    private bool LoadAnimatedItemShaders()
    {
        if (_api is not ICoreClientAPI clientApi) return false;

        DisposeShaders();

        _shaderProgram = clientApi.Shader.NewShaderProgram() as ShaderProgram;
        _shaderProgramFirstPerson = clientApi.Shader.NewShaderProgram() as ShaderProgram;

        if (_shaderProgram == null || _shaderProgramFirstPerson == null) return false;

        _shaderProgram.AssetDomain = Mod.Info.ModID;
        clientApi.Shader.RegisterFileShaderProgram("customstandard", AnimatedItemShaderProgram);
        _shaderProgram.Compile();

        _shaderProgramFirstPerson.AssetDomain = Mod.Info.ModID;
        clientApi.Shader.RegisterFileShaderProgram("customstandardfirstperson", AnimatedItemShaderProgramFirstPerson);
        _shaderProgramFirstPerson.Compile();

        return true;
    }

    private void DisposeShaders()
    {
        _shaderProgram?.Dispose();
        _shaderProgram = null;

        _shaderProgramFirstPerson?.Dispose();
        _shaderProgramFirstPerson = null;
    }
}
