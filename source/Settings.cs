namespace CombatOverhaul;

public sealed class Settings
{
    public float DirectionsCursorTransparency { get; set; } = 0.5f;
    public float DirectionsCursorScale { get; set; } = 0.5f;

    public string BowsAimingCursorType { get; set; } = "Fixed";
    public float BowsAimingHorizontalLimit { get; set; } = 0.125f;
    public float BowsAimingVerticalLimit { get; set; } = 0.35f;
    public bool BowTwoHanded { get; set; } = true;
    public float BowAimingHorisontalLimit { get => BowsAimingHorizontalLimit; set => BowsAimingHorizontalLimit = value; }
    public float BowAimingVerticalLimit { get => BowsAimingVerticalLimit; set => BowsAimingVerticalLimit = value; }

    public string ThrownWeaponsCursorType { get; set; } = "Fixed";
    public float ThrownWeaponsAimingHorizontalLimit { get; set; } = 0.125f;
    public float ThrownWeaponsAimingVerticalLimit { get; set; } = 0.25f;

    public string SlingsAimingCursorType { get; set; } = "Fixed";
    public float SlingsAimingHorizontalLimit { get; set; } = 0.125f;
    public float SlingsAimingVerticalLimit { get; set; } = 0.35f;

    public bool PrintRangeHits { get; set; } = false;
    public bool PrintMeleeHits { get; set; } = false;
    public bool PrintPlayerHits { get; set; } = false;

    public float DirectionsSensitivity { get; set; } = 1f;
    public bool DirectionsInvert { get; set; } = false;
    public bool FlipDirectionAfterAttack { get; set; } = true;

    public bool HandsYawSmoothing { get; set; } = false;

    public bool VanillaActionsWhileBlocking { get; set; } = true;

    public bool VanillaArmorGridRecipes { get; set; } = true;

    public bool ToolsmithIntegrationEnabled { get; set; } = true;

    public float CollisionRadius { get; set; } = 16f;

    public float DefaultColliderPenetrationResistance { get; set; } = 5f;

    public bool DirectionsMovementControls { get; set; } = false;
    public bool DirectionsHotkeysControls { get; set; } = false;

    public bool DisableAllAnimations { get; set; } = false;
    public bool DisableThirdPersonAnimations { get; set; } = false;

    public bool MeleeWeaponStopOnTerrainHit { get; set; } = true;

    public bool MeleeWeaponIgnoreTerrainBehind { get; set; } = false;

    public float MeleeWeaponAttackSpeedMultiplier { get; set; } = 1;

    public int GlobalAttackCooldownMs { get; set; } = 1000;

    public bool SecondChanceAvailable { get; set; } = true;

    public bool SecondChanceParticles { get; set; } = true;

    public bool DebugHitParticles { get; set; } = false;

    public bool DebugWeaponTrailParticles { get; set; } = false;

    public float DebugWeaponTrailParticlesSize { get; set; } = 0.5f;

    public bool DebugProjectilesTrailsParticles { get; set; } = false;

    public bool DebugBlockAnglesParticles { get; set; } = false;

    public float EntityProtectionMultiplier { get; set; } = 0.5f;

    public float WeaponQuenchDamageMultiplier { get; set; } = 1.0f;
    public bool ArmorQuenchPlateOnly { get; set; } = false;
    public float ArmorQuenchFlatReduction { get; set; } = 0.2f;
    public float ArmorQuenchDurabilityBonus { get; set; } = 0.1f;
    public float ArmorQuenchPenaltyReduction { get; set; } = 0.1f;
    public float ArmorQuenchMaxPenaltyReduction { get; set; } = 0.5f;
    public float ArmorQuenchBaseShatterChance { get; set; } = 0.05f;
    public float ArmorQuenchShatterChancePerQuench { get; set; } = 0.05f;
    public float ArmorQuenchTemperShatterMultiplier { get; set; } = 0.8f;
    public float ArmorQuenchTemperPowerMultiplier { get; set; } = 0.92f;


    public bool RangedWeaponsDamageSupport { get; set; } = true;

    public bool SwitchFromImmersiveFirstPerson { get; set; } = true;

    public float FueledItemUpdateInGameHours { get; set; } = 0.1f;

    public bool ShortEntityInfo { get; set; } = true;

    public bool SlowDiagnosticsEnabled { get; set; } = false;
    public int SlowDiagnosticsNetworkThresholdMs { get; set; } = 50;
    public int SlowDiagnosticsRangedStatusThresholdMs { get; set; } = 10;
    public int SlowDiagnosticsLogCooldownMs { get; set; } = 5000;
}

[ProtoBuf.ProtoContract(ImplicitFields = ProtoBuf.ImplicitFields.AllPublic)]
public sealed class ServerGameplaySettingsPacket
{
    public float BowsAimingHorizontalLimit { get; set; }
    public float BowsAimingVerticalLimit { get; set; }
    public bool BowTwoHanded { get; set; }
    public float ThrownWeaponsAimingHorizontalLimit { get; set; }
    public float ThrownWeaponsAimingVerticalLimit { get; set; }
    public float SlingsAimingHorizontalLimit { get; set; }
    public float SlingsAimingVerticalLimit { get; set; }
    public bool VanillaActionsWhileBlocking { get; set; }
    public float CollisionRadius { get; set; }
    public float DefaultColliderPenetrationResistance { get; set; }
    public bool MeleeWeaponStopOnTerrainHit { get; set; }
    public bool MeleeWeaponIgnoreTerrainBehind { get; set; }
    public float MeleeWeaponAttackSpeedMultiplier { get; set; }
    public int GlobalAttackCooldownMs { get; set; }
    public bool SecondChanceAvailable { get; set; }
    public float EntityProtectionMultiplier { get; set; }
    public float WeaponQuenchDamageMultiplier { get; set; }
    public bool ArmorQuenchPlateOnly { get; set; }
    public float ArmorQuenchFlatReduction { get; set; }
    public float ArmorQuenchDurabilityBonus { get; set; }
    public float ArmorQuenchPenaltyReduction { get; set; }
    public float ArmorQuenchMaxPenaltyReduction { get; set; }
    public float ArmorQuenchBaseShatterChance { get; set; }
    public float ArmorQuenchShatterChancePerQuench { get; set; }
    public float ArmorQuenchTemperShatterMultiplier { get; set; }
    public float ArmorQuenchTemperPowerMultiplier { get; set; }

    public bool RangedWeaponsDamageSupport { get; set; }
    public float FueledItemUpdateInGameHours { get; set; }

    public static ServerGameplaySettingsPacket From(Settings settings) => new()
    {
        BowsAimingHorizontalLimit = settings.BowsAimingHorizontalLimit,
        BowsAimingVerticalLimit = settings.BowsAimingVerticalLimit,
        BowTwoHanded = settings.BowTwoHanded,
        ThrownWeaponsAimingHorizontalLimit = settings.ThrownWeaponsAimingHorizontalLimit,
        ThrownWeaponsAimingVerticalLimit = settings.ThrownWeaponsAimingVerticalLimit,
        SlingsAimingHorizontalLimit = settings.SlingsAimingHorizontalLimit,
        SlingsAimingVerticalLimit = settings.SlingsAimingVerticalLimit,
        VanillaActionsWhileBlocking = settings.VanillaActionsWhileBlocking,
        CollisionRadius = settings.CollisionRadius,
        DefaultColliderPenetrationResistance = settings.DefaultColliderPenetrationResistance,
        MeleeWeaponStopOnTerrainHit = settings.MeleeWeaponStopOnTerrainHit,
        MeleeWeaponIgnoreTerrainBehind = settings.MeleeWeaponIgnoreTerrainBehind,
        MeleeWeaponAttackSpeedMultiplier = settings.MeleeWeaponAttackSpeedMultiplier,
        GlobalAttackCooldownMs = settings.GlobalAttackCooldownMs,
        SecondChanceAvailable = settings.SecondChanceAvailable,
        EntityProtectionMultiplier = settings.EntityProtectionMultiplier,
        WeaponQuenchDamageMultiplier = settings.WeaponQuenchDamageMultiplier,
        ArmorQuenchPlateOnly = settings.ArmorQuenchPlateOnly,
        ArmorQuenchFlatReduction = settings.ArmorQuenchFlatReduction,
        ArmorQuenchDurabilityBonus = settings.ArmorQuenchDurabilityBonus,
        ArmorQuenchPenaltyReduction = settings.ArmorQuenchPenaltyReduction,
        ArmorQuenchMaxPenaltyReduction = settings.ArmorQuenchMaxPenaltyReduction,
        ArmorQuenchBaseShatterChance = settings.ArmorQuenchBaseShatterChance,
        ArmorQuenchShatterChancePerQuench = settings.ArmorQuenchShatterChancePerQuench,
        ArmorQuenchTemperShatterMultiplier = settings.ArmorQuenchTemperShatterMultiplier,
        ArmorQuenchTemperPowerMultiplier = settings.ArmorQuenchTemperPowerMultiplier,

        RangedWeaponsDamageSupport = settings.RangedWeaponsDamageSupport,
        FueledItemUpdateInGameHours = settings.FueledItemUpdateInGameHours
    };

    public void ApplyTo(Settings settings)
    {
        settings.BowsAimingHorizontalLimit = BowsAimingHorizontalLimit;
        settings.BowsAimingVerticalLimit = BowsAimingVerticalLimit;
        settings.BowTwoHanded = BowTwoHanded;
        settings.ThrownWeaponsAimingHorizontalLimit = ThrownWeaponsAimingHorizontalLimit;
        settings.ThrownWeaponsAimingVerticalLimit = ThrownWeaponsAimingVerticalLimit;
        settings.SlingsAimingHorizontalLimit = SlingsAimingHorizontalLimit;
        settings.SlingsAimingVerticalLimit = SlingsAimingVerticalLimit;
        settings.VanillaActionsWhileBlocking = VanillaActionsWhileBlocking;
        settings.CollisionRadius = CollisionRadius;
        settings.DefaultColliderPenetrationResistance = DefaultColliderPenetrationResistance;
        settings.MeleeWeaponStopOnTerrainHit = MeleeWeaponStopOnTerrainHit;
        settings.MeleeWeaponIgnoreTerrainBehind = MeleeWeaponIgnoreTerrainBehind;
        settings.MeleeWeaponAttackSpeedMultiplier = MeleeWeaponAttackSpeedMultiplier;
        settings.GlobalAttackCooldownMs = GlobalAttackCooldownMs;
        settings.SecondChanceAvailable = SecondChanceAvailable;
        settings.EntityProtectionMultiplier = EntityProtectionMultiplier;
        settings.WeaponQuenchDamageMultiplier = WeaponQuenchDamageMultiplier;
        settings.ArmorQuenchPlateOnly = ArmorQuenchPlateOnly;
        settings.ArmorQuenchFlatReduction = ArmorQuenchFlatReduction;
        settings.ArmorQuenchDurabilityBonus = ArmorQuenchDurabilityBonus;
        settings.ArmorQuenchPenaltyReduction = ArmorQuenchPenaltyReduction;
        settings.ArmorQuenchMaxPenaltyReduction = ArmorQuenchMaxPenaltyReduction;
        settings.ArmorQuenchBaseShatterChance = ArmorQuenchBaseShatterChance;
        settings.ArmorQuenchShatterChancePerQuench = ArmorQuenchShatterChancePerQuench;
        settings.ArmorQuenchTemperShatterMultiplier = ArmorQuenchTemperShatterMultiplier;
        settings.ArmorQuenchTemperPowerMultiplier = ArmorQuenchTemperPowerMultiplier;

        settings.RangedWeaponsDamageSupport = RangedWeaponsDamageSupport;
        settings.FueledItemUpdateInGameHours = FueledItemUpdateInGameHours;
    }
}
