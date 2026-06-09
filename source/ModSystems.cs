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
using ConfigLib;
using Newtonsoft.Json.Linq;
using OpenTK.Mathematics;
using ProtoBuf;
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

        ExtendedElementPose.NameHashCache = new(api, "element pose name hash cache", 500000, 11 * 60 * 1000, threadSafe: true);
    }

    public override void Start(ICoreAPI api)
    {
        GrindingWheelCompat.SetApi(api);
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
        api.RegisterItemClass("CombatOverhaul:VanillaShield", typeof(VanillaShield));
        api.RegisterItemClass("CombatOverhaul:WearableArmor", typeof(ItemWearableArmor));
        api.RegisterItemClass("CombatOverhaul:WearableFueledLightSource", typeof(WearableFueledLightSource));
        api.RegisterBlockClass("CombatOverhaul:GenericDisplayBlock", typeof(Utils.GenericDisplayBlock));
        api.RegisterBlockEntityClass("CombatOverhaul:GenericDisplayBlockEntity", typeof(Utils.GenericDisplayBlockEntity));

        api.RegisterEntity("CombatOverhaul:Projectile", typeof(ProjectileEntity));
        api.RegisterEntity("CombatOverhaul:ArmorStand", typeof(EntityCOArmorStand));


        AiTaskRegistry.Register<AiTaskCOTurretMode>("CombatOverhaul:TurretMode");
        AiTaskRegistry.Register<StaggerAiTask>("CombatOverhaul:Stagger");

        InInventoryPlayerBehavior._reportedEntities.Clear();

        if (api.ModLoader.IsModEnabled("configlib"))
        {
            SubscribeToConfigChange(api);
        }

        if (api.ModLoader.IsModEnabled("combatoverhaul") || api.ModLoader.IsModEnabled("combatoverhaulfork"))
        {
            EidolonSlam_KnockbackMultiplierPatch.KnockbackMultiplier = 0.2f;
        }
    }
    public override void StartServerSide(ICoreServerAPI api)
    {
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
        _ensureAnimationBehaviorsListener = api.Event.RegisterGameTickListener(_ => EnsureOwnPlayerAnimationBehaviors(), 1000, 1000);
    }
    public override void AssetsLoaded(ICoreAPI api)
    {
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
        // Disabled for now: runtime shield autopatching is causing startup instability
        // with mixed mod stacks on 1.22.1. We'll reintroduce with a safer pipeline.
        // ShieldAutoPatcher.Patch(api);

        QuenchablePatchGate.RemoveCustomQuenchRecipesIfDisabled(api);

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

        _clientApi?.Event.UnregisterRenderer(ReticleRenderer, EnumRenderStage.Ortho);
        _clientApi?.Event.UnregisterRenderer(DirectionCursorRenderer, EnumRenderStage.Ortho);
        if (_clientApi != null)
        {
            _clientApi.Event.PlayerEntitySpawn -= EnsureOwnPlayerAnimationBehaviors;
            _clientApi.Event.LevelFinalize -= EnsureOwnPlayerAnimationBehaviors;
            if (_ensureAnimationBehaviorsListener != 0)
            {
                _clientApi.Event.UnregisterGameTickListener(_ensureAnimationBehaviorsListener);
                _ensureAnimationBehaviorsListener = 0;
            }
        }

        OnDispose?.Invoke();

        _clientApi?.World.UnregisterGameTickListener(_cacheMissesReportedListener);

        Disposed = true;

        ExtendedElementPose.NameHashCache?.Dispose();

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
    private const string _iconsFolder = "sloticons";
    private const string _iconsPath = $"textures/{_iconsFolder}/";
    private long _cacheMissesReportedListener = 0;
    private long _ensureAnimationBehaviorsListener = 0;
    private bool _reportedAnimationBehaviorFallback = false;
    private bool _reportedAnimationBehaviorFallbackError = false;

    private void RegisterCustomIcon(ICoreClientAPI api, string key, string path)
    {
        api.Gui.Icons.CustomIcons[key] = delegate (Context ctx, int x, int y, float w, float h, double[] rgba)
        {
            int value = ColorUtil.ColorFromRgba(75, 75, 75, 255);

            if (rgba.Length == 4)
            {
                value = ColorUtil.ColorFromRgba(rgba);
            }

            if (rgba[0] == 0 && rgba[1] == 0 && rgba[2] == 0 && rgba[3] == 0.2) // To override vanilla clothes and armor icon color
            {
                value = ColorUtil.ColorFromRgba(75, 75, 75, 190);
            }

            AssetLocation location = new(path);
            IAsset svgAsset = api.Assets.TryGet(location);
            Surface target = ctx.GetTarget();

            int xNew = x + (int)(w * _iconScale.X);
            int yNew = y + (int)(h * _iconScale.Y);
            int wNew = (int)(w * _iconScale.W);
            int hNew = (int)(h * _iconScale.Z);

            api.Gui.DrawSvg(svgAsset, (ImageSurface)(object)((target is ImageSurface) ? target : null), xNew, yNew, wNew, hNew, value);
        };
    }

    private void SubscribeToConfigChange(ICoreAPI api)
    {
        ConfigLibModSystem system = api.ModLoader.GetModSystem<ConfigLibModSystem>();

        system.SettingChanged += (domain, config, setting) =>
        {
            if (domain != "combatoverhaul" && domain != "combatoverhaulfork" && domain != "bullseyecontinued" && domain != "overhaullib") return;

            setting.AssignSettingValue(Settings);
            ApplyRuntimeSettings(Settings);
            SettingsChanged?.Invoke(Settings);
        };

        system.ConfigsLoaded += () =>
        {
            system.GetConfig("combatoverhaul")?.AssignSettingsValues(Settings);
            system.GetConfig("combatoverhaulfork")?.AssignSettingsValues(Settings);
            system.GetConfig("bullseyecontinued")?.AssignSettingsValues(Settings);
            system.GetConfig("overhaullib")?.AssignSettingsValues(Settings);
            ApplyRuntimeSettings(Settings);
            SettingsLoaded?.Invoke(Settings);
        };
    }

    private static void ApplyRuntimeSettings(Settings settings)
    {
        DamageResistData.EntityProtectionFactor = settings.EntityProtectionMultiplier;
        QuenchableStatUtil.WeaponDamageBonusPerQuench = Math.Max(0f, settings.WeaponQuenchDamageBonusPerQuench);
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

            if (added && !_reportedAnimationBehaviorFallback)
            {
                _reportedAnimationBehaviorFallback = true;
                LoggerUtil.Warn(_clientApi, this, "Attached missing OverhaulLib player animation behaviors at runtime.");
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
    }


    private ShaderProgram? _shaderProgram;
    private ShaderProgram? _shaderProgramFirstPerson;
    private ICoreAPI? _api;

    private bool LoadAnimatedItemShaders()
    {
        if (_api is not ICoreClientAPI clientApi) return false;

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
}
