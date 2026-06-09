using CombatOverhaul.Animations;
using CombatOverhaul.Colliders;
using CombatOverhaul.DamageSystems;
using CombatOverhaul.Inputs;
using CombatOverhaul.MeleeSystems;
using CombatOverhaul.RangedSystems;
using CombatOverhaul.RangedSystems.Aiming;
using CombatOverhaul.Utils;
using OpenTK.Mathematics;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace CombatOverhaul.Implementations;

public enum MeleeWeaponState
{
    Idle,
    WindingUp,
    Attacking,
    Parrying,
    Blocking,
    BlockBashWindingUp,
    BlockBashAttacking,
    BlockBashCooldown,
    Cooldown,
    StartingAim,
    Aiming,
    Throwing
}

public enum MeleeWeaponStance
{
    MainHand,
    OffHand,
    TwoHanded,
    MainHandDualWield,
    OffHandDualWield
}

public class MeleeWeaponClient : IClientWeaponLogic, IHasDynamicMoveAnimations, IOnGameTick, IRestrictAction
{
    public MeleeWeaponClient(ICoreClientAPI api, Item item, MeleeWeaponStats stats)
    {
        Item = item;
        Api = api;

        CombatOverhaulSystem system = api.ModLoader.GetModSystem<CombatOverhaulSystem>();
        MeleeAttackSystem = system.ClientMeleeSystem ?? throw new Exception();
        ImpaleSystem = system.ClientImpaleSystem;
        MeleeBlockSystem = system.ClientBlockSystem ?? throw new Exception();
        SoundsSystem = system.ClientSoundsSynchronizer ?? throw new Exception();
        RangedWeaponSystem = system.ClientRangedWeaponSystem ?? throw new Exception();
        AimingSystem = system.AimingSystem ?? throw new Exception();
        Settings = system.Settings;

        Stats = stats;
        AimingStats = Stats.ThrowAttack?.Aiming.ToStats();
        if (AimingStats != null)
        {
            DefaultVerticalLimit = AimingStats.VerticalLimit;
            DefaultHorizontalLimit = AimingStats.HorizontalLimit;
        }

        InitializeConfiguredAttacks(api, item.Code.ToString());
    }

    private void InitializeConfiguredAttacks(ICoreClientAPI api, string itemCode)
    {
        InitializeStanceAttacks(api, itemCode, Stats.OneHandedStance, "onehanded", ref OneHandedAttack, ref OneHandedRiposte, out DirectionalOneHandedAttacks);
        InitializeStanceAttacks(api, itemCode, Stats.TwoHandedStance, "twohanded", ref TwoHandedAttack, ref TwoHandedRiposte, out DirectionalTwoHandedAttacks);
        InitializeStanceAttacks(api, itemCode, Stats.OffHandStance, "offhand", ref OffHandAttack, ref OffHandRiposte, out DirectionalOffHandAttacks);

        InitializeDualWieldAttacks(api, Stats.MainHandDualWieldStances, MainHandDualWieldAttacks, DirectionalMainHandDualWieldAttacks);
        InitializeDualWieldAttacks(api, Stats.OffHandDualWieldStances, OffHandDualWieldAttacks, DirectionalOffHandDualWieldAttacks);

        InitializeHandleAttack(api, itemCode, Stats.OneHandedStance, "onehanded-handle-", ref OneHandedHandleAttack);
        InitializeHandleAttack(api, itemCode, Stats.TwoHandedStance, "twohanded-handle-", ref TwoHandedHandleAttack);
        InitializeHandleAttack(api, itemCode, Stats.OffHandStance, "offhand-handle-", ref OffHandHandleAttack);
        InitializeDualWieldHandles(api, Stats.MainHandDualWieldStances, MainHandDualWieldHandleAttacks);
        InitializeDualWieldHandles(api, Stats.OffHandDualWieldStances, OffHandDualWieldHandleAttacks);

        InitializeBlockBashes(api, itemCode, Stats.OneHandedStance, "onehanded-blockbash-", ref OneHandedBlockBash, out DirectionalOneHandedBlockBashes);
        InitializeBlockBashes(api, itemCode, Stats.TwoHandedStance, "twohanded-blockbash-", ref TwoHandedBlockBash, out DirectionalTwoHandedBlockBashes);
        InitializeBlockBashes(api, itemCode, Stats.OffHandStance, "offhand-blockbash-", ref OffHandBlockBash, out DirectionalOffHandBlockBashes);
    }

    private void InitializeStanceAttacks(ICoreClientAPI api, string itemCode, StanceStats? stance, string colliderPrefix, ref MeleeAttack? attack, ref MeleeAttack? riposte, out Dictionary<AttackDirection, MeleeAttack>? directionalAttacks)
    {
        directionalAttacks = null;

        if (stance?.Attack != null)
        {
            attack = new(api, stance.Attack);
            RegisterCollider(itemCode, $"{colliderPrefix}-", attack);
        }

        if (stance?.Riposte != null)
        {
            riposte = new(api, stance.Riposte);
            RegisterCollider(itemCode, $"{colliderPrefix}-riposte-", riposte);
        }
        else if (stance?.DirectionalAttacks != null)
        {
            directionalAttacks = CreateDirectionalAttacks(api, itemCode, $"{colliderPrefix}-", stance.DirectionalAttacks, registerColliders: true);
        }
    }

    private void InitializeBlockBashes(ICoreClientAPI api, string itemCode, StanceStats? stance, string colliderPrefix, ref MeleeAttack? blockBash, out Dictionary<AttackDirection, MeleeAttack>? directionalBlockBashes)
    {
        directionalBlockBashes = null;

        if (stance?.BlockBash != null)
        {
            blockBash = new(api, stance.BlockBash);
            RegisterCollider(itemCode, colliderPrefix, blockBash);
        }
        else if (stance?.DirectionalBlockBashes != null)
        {
            directionalBlockBashes = CreateDirectionalAttacks(api, itemCode, colliderPrefix, stance.DirectionalBlockBashes, registerColliders: true);
        }
    }

    private void InitializeHandleAttack(ICoreClientAPI api, string itemCode, StanceStats? stance, string colliderPrefix, ref MeleeAttack? handleAttack)
    {
        if (stance?.HandleAttack == null) return;

        handleAttack = new(api, stance.HandleAttack);
        RegisterCollider(itemCode, colliderPrefix, handleAttack);
    }

    private void InitializeDualWieldAttacks(ICoreClientAPI api, Dictionary<string, StanceStats> stances, Dictionary<string, MeleeAttack> attacks, Dictionary<string, Dictionary<AttackDirection, MeleeAttack>> directionalAttacks)
    {
        foreach ((string wildcard, StanceStats stance) in stances)
        {
            if (stance.Attack != null)
            {
                attacks.Add(wildcard, new(api, stance.Attack));
            }

            if (stance.DirectionalAttacks != null)
            {
                directionalAttacks[wildcard] = CreateDirectionalAttacks(api, "", "", stance.DirectionalAttacks, registerColliders: false);
            }
        }
    }

    private void InitializeDualWieldHandles(ICoreClientAPI api, Dictionary<string, StanceStats> stances, Dictionary<string, MeleeAttack> handleAttacks)
    {
        foreach ((string wildcard, StanceStats stance) in stances)
        {
            if (stance.HandleAttack != null)
            {
                handleAttacks.Add(wildcard, new(api, stance.HandleAttack));
            }
        }
    }

    private Dictionary<AttackDirection, MeleeAttack> CreateDirectionalAttacks(ICoreClientAPI api, string itemCode, string colliderPrefix, Dictionary<string, MeleeAttackStats> attacks, bool registerColliders)
    {
        Dictionary<AttackDirection, MeleeAttack> result = new();
        foreach ((string direction, MeleeAttackStats attackStats) in attacks)
        {
            AttackDirection attackDirection = Enum.Parse<AttackDirection>(direction);
            MeleeAttack attack = new(api, attackStats);
            result.Add(attackDirection, attack);

            if (registerColliders)
            {
                RegisterCollider(itemCode, $"{colliderPrefix}{direction}-", attack);
            }
        }

        return result;
    }

    public bool Active { get; set; } = true;

    public int ItemId => Item.Id;

    public static long GlobalCooldownUntilMs { get; set; } = 0;

    public virtual DirectionsConfiguration DirectionsType { get; protected set; } = DirectionsConfiguration.None;

    public AnimationRequestByCode? GetIdleAnimation(EntityPlayer player, ItemSlot slot, bool mainHand)
    {
        EnsureStance(player, mainHand);
        return GetStance<MeleeWeaponStance>(mainHand) switch
        {
            MeleeWeaponStance.MainHand => Stats?.OneHandedStance?.IdleAnimation == null ? null : new(Stats.OneHandedStance.IdleAnimation, 1, 1, MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.OffHand => Stats?.OffHandStance?.IdleAnimation == null ? null : new(Stats.OffHandStance.IdleAnimation, 1, 1, MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.TwoHanded => Stats?.TwoHandedStance?.IdleAnimation == null ? null : new(Stats.TwoHandedStance.IdleAnimation, 1, 1, MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            _ => null
        };
    }
    public AnimationRequestByCode? GetReadyAnimation(EntityPlayer player, ItemSlot slot, bool mainHand)
    {
        EnsureStance(player, mainHand);
        return GetStance<MeleeWeaponStance>(mainHand) switch
        {
            MeleeWeaponStance.MainHand => Stats?.OneHandedStance?.ReadyAnimation == null ? null : new(Stats.OneHandedStance.ReadyAnimation, 1, 1, MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.OffHand => Stats?.OffHandStance?.ReadyAnimation == null ? null : new(Stats.OffHandStance.ReadyAnimation, 1, 1, MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.TwoHanded => Stats?.TwoHandedStance?.ReadyAnimation == null ? null : new(Stats.TwoHandedStance.ReadyAnimation, 1, 1, MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            _ => null,
        };
    }
    public AnimationRequestByCode? GetWalkAnimation(EntityPlayer player, ItemSlot slot, bool mainHand)
    {
        EnsureStance(player, mainHand);
        return GetStance<MeleeWeaponStance>(mainHand) switch
        {
            MeleeWeaponStance.MainHand => Stats?.OneHandedStance?.WalkAnimation == null ? null : new(Stats.OneHandedStance.WalkAnimation, 1, 1, MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.OffHand => Stats?.OffHandStance?.WalkAnimation == null ? null : new(Stats.OffHandStance.WalkAnimation, 1, 1, MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.TwoHanded => Stats?.TwoHandedStance?.WalkAnimation == null ? null : new(Stats.TwoHandedStance.WalkAnimation, 1, 1, MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            _ => null
        };
    }
    public AnimationRequestByCode? GetRunAnimation(EntityPlayer player, ItemSlot slot, bool mainHand)
    {
        EnsureStance(player, mainHand);
        return GetStance<MeleeWeaponStance>(mainHand) switch
        {
            MeleeWeaponStance.MainHand => Stats?.OneHandedStance?.RunAnimation == null ? null : new(Stats.OneHandedStance.RunAnimation, 1, 1, MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.OffHand => Stats?.OffHandStance?.RunAnimation == null ? null : new(Stats.OffHandStance.RunAnimation, 1, 1, MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.TwoHanded => Stats?.TwoHandedStance?.RunAnimation == null ? null : new(Stats.TwoHandedStance.RunAnimation, 1, 1, MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            _ => null
        };
    }
    public AnimationRequestByCode? GetSwimAnimation(EntityPlayer player, ItemSlot slot, bool mainHand)
    {
        EnsureStance(player, mainHand);
        return GetStance<MeleeWeaponStance>(mainHand) switch
        {
            MeleeWeaponStance.MainHand => Stats?.OneHandedStance?.SwimAnimation == null ? null : new(Stats.OneHandedStance.SwimAnimation, 1, 1, MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.OffHand => Stats?.OffHandStance?.SwimAnimation == null ? null : new(Stats.OffHandStance.SwimAnimation, 1, 1, MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.TwoHanded => Stats?.TwoHandedStance?.SwimAnimation == null ? null : new(Stats.TwoHandedStance.SwimAnimation, 1, 1, MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            _ => null
        };
    }
    public AnimationRequestByCode? GetSwimIdleAnimation(EntityPlayer player, ItemSlot slot, bool mainHand)
    {
        EnsureStance(player, mainHand);
        return GetStance<MeleeWeaponStance>(mainHand) switch
        {
            MeleeWeaponStance.MainHand => Stats?.OneHandedStance?.SwimIdleAnimation == null ? null : new(Stats.OneHandedStance.SwimIdleAnimation, 1, 1, MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.OffHand => Stats?.OffHandStance?.SwimIdleAnimation == null ? null : new(Stats.OffHandStance.SwimIdleAnimation, 1, 1, MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.TwoHanded => Stats?.TwoHandedStance?.SwimIdleAnimation == null ? null : new(Stats.TwoHandedStance.SwimIdleAnimation, 1, 1, MeleeWeaponClient.AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            _ => null
        };
    }

    public virtual void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {
        EnsureStance(player, mainHand);
        SetState(MeleeWeaponState.Idle, mainHand);
        SetSpeedPenalty(mainHand, player);
    }
    public virtual void OnDeselected(EntityPlayer player, bool mainHand, ref int state)
    {
        if (CheckState(mainHand,
            MeleeWeaponState.Attacking,
            MeleeWeaponState.Cooldown,
            MeleeWeaponState.Blocking,
            MeleeWeaponState.Parrying,
            MeleeWeaponState.BlockBashAttacking,
            MeleeWeaponState.BlockBashWindingUp,
            MeleeWeaponState.BlockBashCooldown))
        {
            SetGlobalCooldown(Api, Settings.GlobalAttackCooldownMs);
        }

        if (CheckState(mainHand,
            MeleeWeaponState.WindingUp,
            MeleeWeaponState.Attacking,
            MeleeWeaponState.Cooldown,
            MeleeWeaponState.BlockBashAttacking,
            MeleeWeaponState.BlockBashWindingUp,
            MeleeWeaponState.BlockBashCooldown))
        {
            MeleeAttackSystem.UpdateAttackStatus(player, MeleeAttackStatus.End, mainHand);
        }

        if (CheckState(mainHand,
            MeleeWeaponState.Aiming,
            MeleeWeaponState.StartingAim))
        {
            RangedWeaponSystem.SendStatusChange(player, RangedWeaponStatus.EndAiming, mainHand);
        }

        MeleeBlockSystem.StopBlock(mainHand);
        StopAttackCooldown(mainHand);
        StopBlockCooldown(mainHand);
        GripController?.StopAnimation(mainHand);
        AnimationBehavior?.StopSpeedModifier();
        PlayerActionsBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory);
        AimingAnimationController?.Stop(mainHand);
        AimingSystem.StopAiming();
        AnimationBehavior?.StopAllVanillaAnimations(mainHand);
    }
    public virtual void OnRegistered(ActionsManagerPlayerBehavior behavior, ICoreClientAPI api)
    {
        PlayerActionsBehavior = behavior;
        AnimationBehavior = behavior.entity.GetBehavior<FirstPersonAnimationsBehavior>();
        TpAnimationBehavior = behavior.entity.GetBehavior<ThirdPersonAnimationsBehavior>();
        GripController = new(AnimationBehavior);

        if (AimingStats != null) AimingAnimationController = new(AimingSystem, AnimationBehavior, AimingStats);
    }

    public virtual void RenderDebugCollider(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        if (DebugWindowManager._currentCollider != null)
        {
            DebugWindowManager._currentCollider.Value.Transform(byPlayer.Entity.Pos, byPlayer.Entity, inSlot, Api, right: true)?.Render(Api, byPlayer.Entity, ColorUtil.ColorFromRgba(255, 125, 125, 255));
            return;
        }

        MeleeAttack? attack = GetStanceAttack(byPlayer.Entity, true, CurrentMainHandDirection);
        if (attack != null)
        {
            foreach (MeleeDamageType damageType in attack.DamageTypes)
            {
                damageType.RelativeCollider.Transform(byPlayer.Entity.Pos, byPlayer.Entity, inSlot, Api, right: true)?.Render(Api, byPlayer.Entity);
            }
        }

        MeleeAttack? handle = GetStanceHandleAttack(byPlayer.Entity, mainHand: true);
        if (handle != null)
        {
            foreach (MeleeDamageType damageType in handle.DamageTypes)
            {
                damageType.RelativeCollider.Transform(byPlayer.Entity.Pos, byPlayer.Entity, inSlot, Api, right: true)?.Render(Api, byPlayer.Entity, ColorUtil.ColorFromRgba(150, 150, 150, 255));
            }
        }
    }

    public virtual bool OnMouseWheel(ItemSlot slot, IClientPlayer byPlayer, float delta)
    {
        if (PlayerActionsBehavior?.ActionListener.IsActive(EnumEntityAction.RightMouseDown) == false) return false;

        bool mainHand = byPlayer.Entity.RightHandItemSlot == slot;
        StanceStats? stance = GetStanceStats(byPlayer.Entity, mainHand);
        float canChangeGrip = stance?.GripLengthFactor ?? 0;

        if (canChangeGrip != 0 && stance != null)
        {
            GripController?.ChangeGrip(delta, mainHand, canChangeGrip, stance.GripMinLength, stance.GripMaxLength);
            return true;
        }
        else
        {
            GripController?.ResetGrip(mainHand);
            return false;
        }
    }

    public bool CanAttack(EntityPlayer player, bool mainHand = true) => GetStanceStats(player, mainHand)?.CanAttack ?? false;
    public bool CanBlock(EntityPlayer player, bool mainHand = true) => GetStanceStats(player, mainHand)?.CanBlock ?? false;
    public bool CanParry(EntityPlayer player, bool mainHand = true) => GetStanceStats(player, mainHand)?.CanParry ?? false;
    public bool CanThrow(EntityPlayer player, bool mainHand = true) => GetStanceStats(player, mainHand)?.CanThrow ?? false;
    public bool CanBash(EntityPlayer player, bool mainHand = true) => GetStanceStats(player, mainHand)?.CanBash ?? false;

    public virtual void OnGameTick(ItemSlot slot, EntityPlayer player, ref int state, bool mainHand)
    {
        if (!CanAttack(player, mainHand) && !CanBash(player, mainHand)) return;
        EnsureStance(player, mainHand);
        if (!CheckState(mainHand, MeleeWeaponState.BlockBashAttacking, MeleeWeaponState.Attacking)) return;

        MeleeAttack? attack = GetStanceAttack(player, mainHand, mainHand ? CurrentMainHandDirection : CurrentOffHandDirection, mainHand ? CurrentMainHandAttackIsRiposte : CurrentOffHandAttackIsRiposte);
        MeleeAttack? bash = GetStanceBlockBash(player, mainHand, mainHand ? CurrentMainHandDirection : CurrentOffHandDirection);
        StanceStats? stats = GetStanceStats(player, mainHand);
        MeleeAttack? handle = GetStanceHandleAttack(player, mainHand);

        if (stats == null) return;

        switch (GetState<MeleeWeaponState>(mainHand))
        {
            case MeleeWeaponState.BlockBashAttacking:
                {
                    if (bash != null) TryBash(bash, handle, stats, slot, player, mainHand);
                }
                break;
            case MeleeWeaponState.Attacking:
                {
                    if (attack != null)
                    {
                        TryAttack(attack, handle, stats, slot, player, mainHand, out bool hitTerrain, Settings.MeleeWeaponIgnoreTerrainBehind);

                        if (hitTerrain && Settings.MeleeWeaponStopOnTerrainHit)
                        {
                            Api.World.AddCameraShake(Stats.ScreenShakeStrength);
                            if (stats.AttackHitSound != null)
                            {
                                SoundsSystem.Play(stats.AttackHitSound);
                            }
                            SetState(MeleeWeaponState.Idle, mainHand);
                            AnimationBehavior?.PlayReadyAnimation(mainHand);
                            TpAnimationBehavior?.PlayReadyAnimation(mainHand);
                            StartAttackCooldown(mainHand, TimeSpan.FromSeconds(0.5));
                            MeleeAttackSystem.UpdateAttackStatus(player, MeleeAttackStatus.End, mainHand);
                        }
                    }
                }
                break;
            default:
                break;
        }
    }

    public void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (Stats == null)
        {
            return;
        }
        
        string[] stats = Stats.ProficiencyStats;
        if (Stats.ProficiencyStat != "")
        {
            stats = stats.Prepend(Stats.ProficiencyStat).ToArray();
        }

        if (stats.Length > 0)
        {
            string statsList = stats.Select(statName => Lang.Get($"combatoverhaul:proficiency-{statName}")).Aggregate((f, s) => $"{f}, {s}");
            string description = Lang.Get("combatoverhaul:iteminfo-proficiency", statsList);
            dsc.AppendLine(description);
        }
        

        if (Stats.OneHandedStance?.Attack != null && Stats.OneHandedStance.Attack.DamageTypes.Length > 0)
        {
            string description = GetAttackStatsDescription(inSlot, Stats.OneHandedStance.Attack.DamageTypes.Select(element => element.Damage), "combatoverhaul:iteminfo-melee-weapon-onehanded");
            dsc.AppendLine(description);
        }
        else if (Stats.OneHandedStance?.DirectionalAttacks != null && Stats.OneHandedStance.DirectionalAttacks.Count > 0)
        {
            foreach (string description in GetDirectionalAttackStatsDescriptions(inSlot, Stats.OneHandedStance.DirectionalAttacks, "combatoverhaul:iteminfo-melee-weapon-onehanded"))
            {
                dsc.AppendLine(description);
            }
        }

        if (Stats.TwoHandedStance?.Attack != null && Stats.TwoHandedStance.Attack.DamageTypes.Length > 0)
        {
            string description = GetAttackStatsDescription(inSlot, Stats.TwoHandedStance.Attack.DamageTypes.Select(element => element.Damage), "combatoverhaul:iteminfo-melee-weapon-twohanded");
            dsc.AppendLine(description);
        }
        else if (Stats.TwoHandedStance?.DirectionalAttacks != null && Stats.TwoHandedStance.DirectionalAttacks.Count > 0)
        {
            foreach (string description in GetDirectionalAttackStatsDescriptions(inSlot, Stats.TwoHandedStance.DirectionalAttacks, "combatoverhaul:iteminfo-melee-weapon-twohanded"))
            {
                dsc.AppendLine(description);
            }
        }

        if (Stats.OffHandStance?.Attack != null && Stats.OffHandStance.Attack.DamageTypes.Length > 0)
        {
            string description = GetAttackStatsDescription(inSlot, Stats.OffHandStance.Attack.DamageTypes.Select(element => element.Damage), "combatoverhaul:iteminfo-melee-weapon-offhanded");
            dsc.AppendLine(description);
        }
        else if (Stats.OffHandStance?.DirectionalAttacks != null && Stats.OffHandStance.DirectionalAttacks.Count > 0)
        {
            foreach (string description in GetDirectionalAttackStatsDescriptions(inSlot, Stats.OffHandStance.DirectionalAttacks, "combatoverhaul:iteminfo-melee-weapon-offhanded"))
            {
                dsc.AppendLine(description);
            }
        }

        if (Stats.OneHandedStance?.BlockBash != null && Stats.OneHandedStance.BlockBash.DamageTypes.Length > 0)
        {
            string description = GetAttackStatsDescription(inSlot, Stats.OneHandedStance.BlockBash.DamageTypes.Select(element => element.Damage), "combatoverhaul:iteminfo-melee-weapon-onehanded-bash");
            dsc.AppendLine(description);
        }
        else if (Stats.OneHandedStance?.DirectionalBlockBashes != null && Stats.OneHandedStance.DirectionalBlockBashes.Count > 0)
        {
            IEnumerable<DamageDataJson> damageTypes = Stats.OneHandedStance.DirectionalBlockBashes.Values.SelectMany(element => element.DamageTypes).Select(element => element.Damage);
            string description = GetAttackStatsDescription(inSlot, damageTypes, "combatoverhaul:iteminfo-melee-weapon-onehanded-bash");
            dsc.AppendLine(description);
        }

        if (Stats.TwoHandedStance?.BlockBash != null && Stats.TwoHandedStance.BlockBash.DamageTypes.Length > 0)
        {
            string description = GetAttackStatsDescription(inSlot, Stats.TwoHandedStance.BlockBash.DamageTypes.Select(element => element.Damage), "combatoverhaul:iteminfo-melee-weapon-twohanded-bash");
            dsc.AppendLine(description);
        }
        else if (Stats.TwoHandedStance?.DirectionalBlockBashes != null && Stats.TwoHandedStance.DirectionalBlockBashes.Count > 0)
        {
            IEnumerable<DamageDataJson> damageTypes = Stats.TwoHandedStance.DirectionalBlockBashes.Values.SelectMany(element => element.DamageTypes).Select(element => element.Damage);
            string description = GetAttackStatsDescription(inSlot, damageTypes, "combatoverhaul:iteminfo-melee-weapon-twohanded-bash");
            dsc.AppendLine(description);
        }

        if (Stats.OffHandStance?.BlockBash != null && Stats.OffHandStance.BlockBash.DamageTypes.Length > 0)
        {
            string description = GetAttackStatsDescription(inSlot, Stats.OffHandStance.BlockBash.DamageTypes.Select(element => element.Damage), "combatoverhaul:iteminfo-melee-weapon-offhanded-bash");
            dsc.AppendLine(description);
        }
        else if (Stats.OffHandStance?.DirectionalBlockBashes != null && Stats.OffHandStance.DirectionalBlockBashes.Count > 0)
        {
            IEnumerable<DamageDataJson> damageTypes = Stats.OffHandStance.DirectionalBlockBashes.Values.SelectMany(element => element.DamageTypes).Select(element => element.Damage);
            string description = GetAttackStatsDescription(inSlot, damageTypes, "combatoverhaul:iteminfo-melee-weapon-offhanded-bash");
            dsc.AppendLine(description);
        }

        ItemStackMeleeWeaponStats stackStats = ItemStackMeleeWeaponStats.FromItemStack(inSlot.Itemstack);

        if (Stats.OneHandedStance?.Block?.BlockTier != null)
        {
            dsc.AppendLine();
            float blockTier = 0;
            foreach ((string damageType, float tier) in Stats.OneHandedStance.Block.BlockTier)
            {
                blockTier = Math.Max(blockTier, tier) + stackStats.BlockTierBonus;
            }

            string bodyParts = Stats.OneHandedStance.Block.Zones.Length == 12 ? Lang.Get("combatoverhaul:detailed-damage-zone-All") : Stats.OneHandedStance.Block.Zones
                .Select(zone => Lang.Get($"combatoverhaul:detailed-damage-zone-{zone}"))
                .Aggregate((first, second) => $"{first}, {second}");

            dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-melee-weapon-blockStats", $"{blockTier:F0}", bodyParts, Lang.Get("combatoverhaul:iteminfo-melee-weapon-onehanded-block")));
        }

        if (Stats.OneHandedStance?.Parry?.BlockTier != null)
        {
            dsc.AppendLine();
            float blockTier = 0;
            foreach ((string damageType, float tier) in Stats.OneHandedStance.Parry.BlockTier)
            {
                blockTier = Math.Max(blockTier, tier) + stackStats.ParryTierBonus;
            }

            string bodyParts = Stats.OneHandedStance.Parry.Zones.Length == 12 ? Lang.Get("combatoverhaul:detailed-damage-zone-All") : Stats.OneHandedStance.Parry.Zones
                .Select(zone => Lang.Get($"combatoverhaul:detailed-damage-zone-{zone}"))
                .Aggregate((first, second) => $"{first}, {second}");

            dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-melee-weapon-parryStats", $"{blockTier:F0}", bodyParts, Lang.Get("combatoverhaul:iteminfo-melee-weapon-onehanded-block")));
        }

        if (Stats.TwoHandedStance?.Block?.BlockTier != null)
        {
            dsc.AppendLine();
            float blockTier = 0;
            foreach ((string damageType, float tier) in Stats.TwoHandedStance.Block.BlockTier)
            {
                blockTier = Math.Max(blockTier, tier) + stackStats.BlockTierBonus;
            }

            string bodyParts = Stats.TwoHandedStance.Block.Zones.Length == 12 ? Lang.Get("combatoverhaul:detailed-damage-zone-All") : Stats.TwoHandedStance.Block.Zones
                .Select(zone => Lang.Get($"combatoverhaul:detailed-damage-zone-{zone}"))
                .Aggregate((first, second) => $"{first}, {second}");

            dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-melee-weapon-blockStats", $"{blockTier:F0}", bodyParts, Lang.Get("combatoverhaul:iteminfo-melee-weapon-twohanded-block")));
        }

        if (Stats.TwoHandedStance?.Parry?.BlockTier != null)
        {
            dsc.AppendLine();
            float blockTier = 0;
            foreach ((string damageType, float tier) in Stats.TwoHandedStance.Parry.BlockTier)
            {
                blockTier = Math.Max(blockTier, tier) + stackStats.ParryTierBonus;
            }

            string bodyParts = Stats.TwoHandedStance.Parry.Zones.Length == 12 ? Lang.Get("combatoverhaul:detailed-damage-zone-All") : Stats.TwoHandedStance.Parry.Zones
                .Select(zone => Lang.Get($"combatoverhaul:detailed-damage-zone-{zone}"))
                .Aggregate((first, second) => $"{first}, {second}");

            dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-melee-weapon-parryStats", $"{blockTier:F0}", bodyParts, Lang.Get("combatoverhaul:iteminfo-melee-weapon-twohanded-block")));
        }

        if (Stats.OffHandStance?.Block?.BlockTier != null)
        {
            dsc.AppendLine();
            float blockTier = 0;
            foreach ((string damageType, float tier) in Stats.OffHandStance.Block.BlockTier)
            {
                blockTier = Math.Max(blockTier, tier) + stackStats.BlockTierBonus;
            }

            string bodyParts = Stats.OffHandStance.Block.Zones.Length == 12 ? Lang.Get("combatoverhaul:detailed-damage-zone-All") : Stats.OffHandStance.Block.Zones
                .Select(zone => Lang.Get($"combatoverhaul:detailed-damage-zone-{zone}"))
                .Aggregate((first, second) => $"{first}, {second}");

            dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-melee-weapon-blockStats", $"{blockTier:F0}", bodyParts, Lang.Get("combatoverhaul:iteminfo-melee-weapon-offhanded-block")));
        }

        if (Stats.OffHandStance?.Parry?.BlockTier != null)
        {
            dsc.AppendLine();
            float blockTier = 0;
            foreach ((string damageType, float tier) in Stats.OffHandStance.Parry.BlockTier)
            {
                blockTier = Math.Max(blockTier, tier) + stackStats.ParryTierBonus;
            }

            string bodyParts = Stats.OffHandStance.Parry.Zones.Length == 12 ? Lang.Get("combatoverhaul:detailed-damage-zone-All") : Stats.OffHandStance.Parry.Zones
                .Select(zone => Lang.Get($"combatoverhaul:detailed-damage-zone-{zone}"))
                .Aggregate((first, second) => $"{first}, {second}");

            dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-melee-weapon-parryStats", $"{blockTier:F0}", bodyParts, Lang.Get("combatoverhaul:iteminfo-melee-weapon-offhanded-block")));
        }
    }
    public bool RestrictRightHandAction() => !CheckState(true, MeleeWeaponState.Idle, MeleeWeaponState.Aiming, MeleeWeaponState.StartingAim, MeleeWeaponState.Cooldown) && GetStance<MeleeWeaponStance>(true) != MeleeWeaponStance.OffHandDualWield;
    public bool RestrictLeftHandAction() => !CheckState(false, MeleeWeaponState.Idle, MeleeWeaponState.Aiming, MeleeWeaponState.StartingAim, MeleeWeaponState.Cooldown) && GetStance<MeleeWeaponStance>(false) != MeleeWeaponStance.MainHandDualWield;

    public void PlayReadyAnimation(bool mainHand)
    {
        AnimationBehavior?.PlayReadyAnimation(mainHand);
        TpAnimationBehavior?.PlayReadyAnimation(mainHand);
    }

    public static void SetGlobalCooldown(ICoreAPI api, long cooldownMs = GlobalCooldownMs)
    {
        GlobalCooldownUntilMs = api.World.ElapsedMilliseconds + cooldownMs;
    }

    protected readonly Item Item;
    protected readonly ICoreClientAPI Api;
    protected readonly MeleeBlockSystemClient MeleeBlockSystem;
    protected readonly MeleeSystemClient MeleeAttackSystem;
    protected readonly ImpaleSystemClient? ImpaleSystem;
    protected readonly RangedWeaponSystemClient RangedWeaponSystem;
    protected readonly ClientAimingSystem AimingSystem;
    protected readonly Settings Settings;
    protected readonly float DefaultVerticalLimit;
    protected readonly float DefaultHorizontalLimit;
    protected FirstPersonAnimationsBehavior? AnimationBehavior;
    protected ThirdPersonAnimationsBehavior? TpAnimationBehavior;
    protected ActionsManagerPlayerBehavior? PlayerActionsBehavior;
    protected SoundsSynchronizerClient SoundsSystem;
    protected AimingAnimationController? AimingAnimationController;
    protected GripController? GripController;
    protected const int MaxState = 100;
    protected readonly MeleeWeaponStats Stats;
    protected const string PlayerStatsMainHandCategory = "CombatOverhaul:held-item-mainhand";
    protected const string PlayerStatsOffHandCategory = "CombatOverhaul:held-item-offhand";
    protected bool ParryButtonReleased = true;

    protected long MainHandBlockCooldownTimer = -1;
    protected long OffHandBlockCooldownTimer = -1;
    protected long MainHandAttackCooldownUntilMs = 0;
    protected long OffHandAttackCooldownUntilMs = 0;
    protected int MainHandAttackCounter = 0;
    protected int OffHandAttackCounter = 0;
    protected bool HandleHitTerrain = false;
    protected const bool EditColliders = false;
    protected AttackDirection CurrentMainHandDirection = AttackDirection.Top;
    protected AttackDirection CurrentOffHandDirection = AttackDirection.Top;
    protected static bool CanRiposteMainHand = false;
    protected static bool CanRiposteOffHand = false;
    protected static long RiposteTimerMainHand = 0;
    protected static long RiposteTimerOffHand = 0;
    protected bool RiposteMainHand = false;
    protected bool RiposteOffHand = false;
    protected bool CurrentMainHandAttackIsRiposte = false;
    protected bool CurrentOffHandAttackIsRiposte = false;
    protected bool MainHandPrimedDualWieldThrow = false;

    protected const long GlobalCooldownMs = 1000;
    protected const int RiposteGracePeriodMs = 300;

    protected readonly AimingStats? AimingStats;

    protected MeleeAttack? OneHandedAttack;
    protected MeleeAttack? TwoHandedAttack;
    protected MeleeAttack? OffHandAttack;
    protected Dictionary<string, MeleeAttack> MainHandDualWieldAttacks = [];
    protected Dictionary<string, MeleeAttack> OffHandDualWieldAttacks = [];
    protected Dictionary<AttackDirection, MeleeAttack>? DirectionalOneHandedAttacks;
    protected Dictionary<AttackDirection, MeleeAttack>? DirectionalTwoHandedAttacks;
    protected Dictionary<AttackDirection, MeleeAttack>? DirectionalOffHandAttacks;
    protected Dictionary<string, Dictionary<AttackDirection, MeleeAttack>> DirectionalMainHandDualWieldAttacks = [];
    protected Dictionary<string, Dictionary<AttackDirection, MeleeAttack>> DirectionalOffHandDualWieldAttacks = [];

    protected MeleeAttack? OneHandedRiposte;
    protected MeleeAttack? TwoHandedRiposte;
    protected MeleeAttack? OffHandRiposte;

    protected MeleeAttack? OneHandedBlockBash;
    protected MeleeAttack? TwoHandedBlockBash;
    protected MeleeAttack? OffHandBlockBash;
    protected Dictionary<AttackDirection, MeleeAttack>? DirectionalOneHandedBlockBashes = [];
    protected Dictionary<AttackDirection, MeleeAttack>? DirectionalTwoHandedBlockBashes = [];
    protected Dictionary<AttackDirection, MeleeAttack>? DirectionalOffHandBlockBashes = [];

    protected MeleeAttack? OneHandedHandleAttack;
    protected MeleeAttack? TwoHandedHandleAttack;
    protected MeleeAttack? OffHandHandleAttack;
    protected Dictionary<string, MeleeAttack> MainHandDualWieldHandleAttacks = [];
    protected Dictionary<string, MeleeAttack> OffHandDualWieldHandleAttacks = [];

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Active)]
    protected virtual bool Attack(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!Active) return false;

        EnsureStance(player, mainHand);

        if (TryThrowImpaledTarget(slot, player, mainHand, direction))
        {
            return true;
        }

        bool canRiposte = CanRiposteMainHand || CanRiposteOffHand;

        if (IsAttackOnCooldown(mainHand)) return false;
        if (InteractionsTester.PlayerTriesToInteract(player, mainHand, eventData)) return false;
        if (!CanAttack(player, mainHand)) return false;
        if (ItemInOtherHandBlocksAttack(player, mainHand) && !canRiposte) return false;
        if (ActionRestricted(player, mainHand)) return false;

        StanceStats? stats = GetStanceStats(player, mainHand);
        MeleeAttack? handle = GetStanceHandleAttack(player, mainHand);

        if (stats == null) return false;

        MeleeAttack? attack = GetStanceAttack(player, mainHand, direction, riposte: stats.CanRiposte && canRiposte);

        if (attack == null) return false;

        switch (GetState<MeleeWeaponState>(mainHand))
        {
            case MeleeWeaponState.Parrying:
                {
                    if (!stats.CanRiposte || !canRiposte) return false;

                    TurnOnRiposte(mainHand);
                    StartAttack(slot, player, mainHand, direction, attack, handle, stats, riposte: true);
                    ResetRiposte(mainHand);

                    break;
                }
            case MeleeWeaponState.Idle:
                {
                    TurnOffRiposte(mainHand);
                    StartAttack(slot, player, mainHand, direction, attack, handle, stats, riposte: stats.CanRiposte && canRiposte);
                    ResetRiposte(mainHand);
                }
                break;
            case MeleeWeaponState.WindingUp:
                break;
            case MeleeWeaponState.Cooldown:
                break;
            case MeleeWeaponState.Attacking:
                break;
            default:
                return false;
        }

        return true;
    }

    protected virtual bool TryThrowImpaledTarget(ItemSlot slot, EntityPlayer player, bool mainHand, AttackDirection direction)
    {
        if (ImpaleSystem?.HasImpaled(player.EntityId, mainHand) != true) return false;
        if (IsAttackOnCooldown(mainHand)) return true;
        if (!CanAttack(player, mainHand)) return false;
        if (ItemInOtherHandBlocksAttack(player, mainHand)) return false;
        if (ActionRestricted(player, mainHand)) return false;
        if (!CheckState(mainHand, MeleeWeaponState.Idle, MeleeWeaponState.Cooldown)) return true;

        StanceStats? stats = GetStanceStats(player, mainHand);
        if (stats == null) return false;

        MeleeAttack? attack = GetStanceAttack(player, mainHand, direction);
        if (attack == null) return false;

        if (mainHand)
        {
            CurrentMainHandDirection = direction;
            CurrentMainHandAttackIsRiposte = false;
        }
        else
        {
            CurrentOffHandDirection = direction;
            CurrentOffHandAttackIsRiposte = false;
        }

        int counter = mainHand ? MainHandAttackCounter : OffHandAttackCounter;
        string attackAnimation = GetImpaleThrowAnimation(stats, direction, counter)
            ?? throw new InvalidOperationException($"[Combat Overhaul] Item '{Item.Code}' does not have attack animation specified");
        float animationSpeed = GetAnimationSpeed(player, Stats) * ItemStackMeleeWeaponStats.GetAttackSpeed(slot.Itemstack) * stats.AttackSpeedMultiplier * Settings.MeleeWeaponAttackSpeedMultiplier;

        MeleeBlockSystem.StopBlock(mainHand);
        SetState(MeleeWeaponState.WindingUp, mainHand);
        MeleeAttackSystem.UpdateAttackStatus(player, MeleeAttackStatus.Start, mainHand);

        ImpaleSystem.RequestThrow(mainHand, player.Pos.GetViewVector());

        AnimationBehavior?.Play(
            mainHand,
            attackAnimation,
            animationSpeed: animationSpeed,
            category: AnimationCategory(mainHand),
            callback: () => ImpaleThrowAnimationCallback(player, mainHand, stats),
            callbackHandler: code => ImpaleThrowAnimationCallbackHandler(player, code, mainHand, stats));
        TpAnimationBehavior?.Play(
            mainHand,
            attackAnimation,
            animationSpeed: animationSpeed,
            category: AnimationCategory(mainHand));

        if (mainHand)
        {
            MainHandAttackCounter++;
        }
        else
        {
            OffHandAttackCounter++;
        }

        return true;
    }

    protected virtual string? GetImpaleThrowAnimation(StanceStats stats, AttackDirection direction, int counter)
    {
        if (stats.AttackAnimation.Count == 0) return null;

        string directionKey = direction.ToString();
        string[]? animations = null;
        if (DirectionsType != DirectionsConfiguration.None && stats.AttackAnimation.TryGetValue(directionKey, out string[]? directionalAnimations))
        {
            animations = directionalAnimations;
        }
        else if (stats.AttackAnimation.TryGetValue("Main", out string[]? mainAnimations))
        {
            animations = mainAnimations;
        }
        else
        {
            animations = stats.AttackAnimation.Values.FirstOrDefault();
        }

        if (animations == null || animations.Length == 0) return null;

        return animations[counter % animations.Length];
    }

    protected virtual bool ImpaleThrowAnimationCallback(EntityPlayer player, bool mainHand, StanceStats stats)
    {
        AnimationBehavior?.PlayReadyAnimation(mainHand);
        TpAnimationBehavior?.PlayReadyAnimation(mainHand);
        SetState(MeleeWeaponState.Idle, mainHand);
        StartAttackCooldown(mainHand, TimeSpan.FromMilliseconds(stats.AttackCooldownMs > 0 ? stats.AttackCooldownMs : 500));
        MeleeAttackSystem.UpdateAttackStatus(player, MeleeAttackStatus.End, mainHand);

        return true;
    }

    protected virtual void ImpaleThrowAnimationCallbackHandler(EntityPlayer player, string callbackCode, bool mainHand, StanceStats stats)
    {
        switch (callbackCode)
        {
            case "start":
                SetState(MeleeWeaponState.WindingUp, mainHand);
                break;
            case "stop":
                SetState(MeleeWeaponState.Cooldown, mainHand);
                break;
            case "ready":
                ImpaleThrowAnimationCallback(player, mainHand, stats);
                break;
        }
    }

    protected virtual void StartAttack(ItemSlot slot, EntityPlayer player, bool mainHand, AttackDirection direction, MeleeAttack attack, MeleeAttack? handle, StanceStats stats, bool riposte = false)
    {
        if (mainHand)
        {
            CurrentMainHandDirection = direction;
            CurrentMainHandAttackIsRiposte = riposte;
        }
        else
        {
            CurrentOffHandDirection = direction;
            CurrentOffHandAttackIsRiposte = riposte;
        }

        int counter = mainHand ? MainHandAttackCounter : OffHandAttackCounter;

        string attackAnimation =
            (riposte ? stats.RiposteAnimation :
            DirectionsType == DirectionsConfiguration.None ?
            stats.AttackAnimation["Main"][counter % stats.AttackAnimation["Main"].Length] :
            stats.AttackAnimation[direction.ToString()][counter % stats.AttackAnimation[direction.ToString()].Length])
            ?? throw new InvalidOperationException($"[Combat Overhaul] Item '{Item.Code}' does not have attack animation specified");

        float animationSpeed = GetAnimationSpeed(player, Stats) * ItemStackMeleeWeaponStats.GetAttackSpeed(slot.Itemstack) * stats.AttackSpeedMultiplier * Settings.MeleeWeaponAttackSpeedMultiplier;
        MeleeBlockSystem.StopBlock(mainHand);

        SetState(MeleeWeaponState.WindingUp, mainHand);

        MeleeAttackSystem.UpdateAttackStatus(player, MeleeAttackStatus.Start, mainHand);

        attack.Start(player.Player);
        handle?.Start(player.Player);
        AnimationBehavior?.Play(
            mainHand,
            attackAnimation,
            animationSpeed: animationSpeed,
            category: AnimationCategory(mainHand),
            callback: () => AttackAnimationCallback(player, mainHand),
            callbackHandler: code => AttackAnimationCallbackHandler(player, code, mainHand));
        TpAnimationBehavior?.Play(
            mainHand,
            attackAnimation,
            animationSpeed: animationSpeed,
            category: AnimationCategory(mainHand));

        if (mainHand && IsCurrentAttackOneHanded(stats))
        {
            TryPlayLinkedOffhandDaggerAnimation(player, attackAnimation, animationSpeed);
        }

        if (mainHand)
        {
            MainHandAttackCounter++;
        }
        else
        {
            OffHandAttackCounter++;
        }
        HandleHitTerrain = false;

        if (Settings.FlipDirectionAfterAttack)
        {
            PlayerActionsBehavior?.FlipDirectionToOpposite();
        }
    }
    protected virtual void TryAttack(MeleeAttack attack, MeleeAttack? handle, StanceStats stats, ItemSlot slot, EntityPlayer player, bool mainHand, out bool hitTerrain, bool ignoreTerrainBehind)
    {
        hitTerrain = false;
        ItemStackMeleeWeaponStats stackStats = ItemStackMeleeWeaponStats.FromItemStack(slot.Itemstack);
        AttackDirection attackDirection = mainHand ? CurrentMainHandDirection : CurrentOffHandDirection;

        bool handleAttacked = false;

        if (handle != null)
        {
            handleAttacked = handle.Attack(
                        player.Player,
                        slot,
                        mainHand,
                        out IEnumerable<(Block block, Vector3d point)> handleTerrainCollision,
                        out IEnumerable<(Vintagestory.API.Common.Entities.Entity entity, Vector3d point)> handleEntitiesCollision,
                        stackStats,
                        attackDirection);

            if (handleTerrainCollision.Any() && !handleAttacked)
            {
                if (Settings.DebugHitParticles)
                {
                    foreach ((_, Vector3d point) in handleTerrainCollision)
                    {
                        Vec3d pos8 = new(point.X, point.Y, point.Z);
                        player.Api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(125, 255, 125, 125), pos8, pos8, new Vec3f(), new Vec3f(), 1, 0, 1.0f, EnumParticleModel.Cube);
                    }
                }

                hitTerrain = true;

                if (ignoreTerrainBehind)
                {
                    Vector3d playerPosition = player.Pos.XYZ.ToOpenTK();
                    Vector3d eyesPosition = player.LocalEyePos.ToOpenTK() + playerPosition;
                    Vector3d viewDirection = player.Pos.GetViewVector().ToVec3d().ToOpenTK();

                    double eyesProjection = Vector3d.Dot(viewDirection, eyesPosition);

                    foreach ((_, Vector3d point) in handleTerrainCollision)
                    {
                        double hitProjection = Vector3d.Dot(viewDirection, point);

                        if (hitProjection < eyesProjection)
                        {
                            hitTerrain = false;
                            break;
                        }
                    }
                }

                if (hitTerrain)
                {
                    if (!HandleHitTerrain)
                    {
                        if (stats.HandleHitSound != null)
                        {
                            SoundsSystem.Play(stats.HandleHitSound);
                        }
                        HandleHitTerrain = true;
                    }

                    return;
                }
            }

            if (Settings.DebugHitParticles && handleEntitiesCollision.Any() && handleAttacked)
            {
                foreach ((_, Vector3d point) in handleEntitiesCollision)
                {
                    Vec3d pos8 = new(point.X, point.Y, point.Z);
                    player.Api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(125, 125, 255, 125), pos8, pos8, new Vec3f(), new Vec3f(), 1, 0, 1.0f, EnumParticleModel.Cube);
                }
            }
        }

        bool attacked = attack.Attack(
            player.Player,
            slot,
            mainHand,
            out IEnumerable<(Block block, Vector3d point)> terrainCollision,
            out IEnumerable<(Vintagestory.API.Common.Entities.Entity entity, Vector3d point)> entitiesCollision,
            stackStats,
            attackDirection);

        if (Settings.DebugHitParticles && terrainCollision.Any() && !attacked)
        {
            foreach ((_, Vector3d point) in terrainCollision)
            {
                Vec3d pos8 = new(point.X, point.Y, point.Z);
                player.Api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(0, 255, 0, 125), pos8, pos8, new Vec3f(), new Vec3f(), 1, 0, 1.0f, EnumParticleModel.Cube);
            }
        }

        if (Settings.DebugHitParticles && entitiesCollision.Any() && attacked)
        {
            foreach ((_, Vector3d point) in entitiesCollision)
            {
                Vec3d pos8 = new(point.X, point.Y, point.Z);
                player.Api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(0, 0, 255, 125), pos8, pos8, new Vec3f(), new Vec3f(), 1, 0, 1.0f, EnumParticleModel.Cube);
            }
        }

        if (attacked && mainHand && IsCurrentAttackOneHanded(stats) && !IsDualDaggerLoadout(player))
        {
            TrySendLinkedOffhandDaggerDamage(player, attack);
        }

        if (handle != null && !HandleHitTerrain && handleAttacked && !attacked)
        {
            if (stats.HandleHitSound != null)
            {
                SoundsSystem.Play(stats.HandleHitSound);
            }
            HandleHitTerrain = true;
        }

        if (handle != null)
        {
            handle.AddAttackedEntities(attack, player.EntityId);
        }

        if (terrainCollision.Any())
        {
            hitTerrain = true;
        }

        if (attacked && stats.AttackHitSound != null)
        {
            SoundsSystem.Play(stats.AttackHitSound);
        }

        if (attacked && Stats.AnimationStaggerOnHitDurationMs > 0)
        {
            AnimationBehavior?.SetSpeedModifier(AttackImpactFunction);
            Api.World.AddCameraShake(Stats.ScreenShakeStrength);
        }
    }


    protected virtual bool IsCurrentAttackOneHanded(StanceStats stats)
    {
        MeleeWeaponStance stance = GetStance<MeleeWeaponStance>(true);
        if (stance == MeleeWeaponStance.MainHand || stance == MeleeWeaponStance.MainHandDualWield)
        {
            return true;
        }

        if (ReferenceEquals(stats, Stats.OneHandedStance))
        {
            return true;
        }

        foreach (StanceStats dualWieldStats in Stats.MainHandDualWieldStances.Values)
        {
            if (ReferenceEquals(stats, dualWieldStats))
            {
                return true;
            }
        }

        return false;
    }

    protected virtual void TryPlayLinkedOffhandDaggerAnimation(EntityPlayer player, string mainHandAnimation, float mainHandAnimationSpeed)
    {
        if (!TryGetOffhandDaggerStack(player, out ItemStack? offhandStack) || offhandStack == null)
        {
            return;
        }

        MeleeWeaponStats? daggerStats = TryGetMeleeWeaponStats(offhandStack);
        StanceStats? animationStats = GetLinkedOffhandDaggerAnimationStance(player, daggerStats);
        if (animationStats == null)
        {
            return;
        }

        string linkedOffhandAnimation = GetLinkedOffhandAttackAnimation(animationStats, CurrentMainHandDirection, OffHandAttackCounter) ?? mainHandAnimation;
        float animationSpeed = mainHandAnimationSpeed * ItemStackMeleeWeaponStats.GetAttackSpeed(offhandStack) * animationStats.AttackSpeedMultiplier;

        AnimationBehavior?.Play(
            false,
            linkedOffhandAnimation,
            animationSpeed: animationSpeed,
            category: AnimationCategory(false),
            callback: () => LinkedOffhandDaggerAnimationCallback(player));
        TpAnimationBehavior?.Play(
            false,
            linkedOffhandAnimation,
            animationSpeed: animationSpeed,
            category: AnimationCategory(false));

        OffHandAttackCounter++;
    }

    protected virtual bool LinkedOffhandDaggerAnimationCallback(EntityPlayer player)
    {
        AnimationBehavior?.PlayReadyAnimation(false);
        TpAnimationBehavior?.PlayReadyAnimation(false);
        return true;
    }

    protected virtual void TrySendLinkedOffhandDaggerDamage(EntityPlayer player, MeleeAttack attack)
    {
        if (!TryGetOffhandDaggerStack(player, out ItemStack? offhandStack) || offhandStack == null)
        {
            return;
        }

        MeleeDamagePacket[] sourcePackets = attack.LastDamagePackets;
        if (sourcePackets.Length == 0)
        {
            return;
        }

        MeleeWeaponStats? daggerStats = TryGetMeleeWeaponStats(offhandStack);
        MeleeAttackStats? daggerAttack = GetLinkedOffhandDaggerDamageAttack(player, daggerStats);
        if (daggerAttack == null || daggerAttack.DamageTypes.Length == 0)
        {
            return;
        }

        ItemStackMeleeWeaponStats offhandStackStats = ItemStackMeleeWeaponStats.FromItemStack(offhandStack);
        List<MeleeDamagePacket> offhandPackets = new();
        HashSet<long> alreadyProcessedTargets = new();

        foreach (MeleeDamagePacket sourcePacket in sourcePackets)
        {
            if (!alreadyProcessedTargets.Add(sourcePacket.TargetEntityId))
            {
                continue;
            }

            Entity? target = Api.World.GetEntityById(sourcePacket.TargetEntityId);
            if (target == null)
            {
                continue;
            }

            Vector3d position = sourcePacket.Position.Length >= 3
                ? new Vector3d(sourcePacket.Position[0], sourcePacket.Position[1], sourcePacket.Position[2])
                : target.Pos.XYZ.ToOpenTK();

            ColliderTypes colliderType = (ColliderTypes)sourcePacket.ColliderType;

            foreach (MeleeDamageTypeJson damageTypeJson in daggerAttack.DamageTypes)
            {
                MeleeDamagePacket offhandPacket = damageTypeJson.ToDamageType().CreateDamagePacket(
                    player,
                    target,
                    position,
                    sourcePacket.Collider,
                    mainHand: false,
                    colliderType,
                    offhandStackStats,
                    damageMultiplier: 0.75f,
                    knockbackMultiplier: 0.75f);

                offhandPackets.Add(offhandPacket);
            }
        }

        if (offhandPackets.Count > 0)
        {
            MeleeAttackSystem.SendPackets(offhandPackets);
        }
    }

    protected virtual StanceStats? GetLinkedOffhandDaggerAnimationStance(EntityPlayer player, MeleeWeaponStats? daggerStats)
    {
        string mainHandCode = player.RightHandItemSlot?.Itemstack?.Collectible?.Code?.ToString() ?? "";
        if (mainHandCode != "" && daggerStats?.OffHandDualWieldStances != null && daggerStats.OffHandDualWieldStances.Count > 0)
        {
            foreach ((string wildcard, StanceStats stance) in daggerStats.OffHandDualWieldStances)
            {
                if (WildcardUtil.Match(wildcard, mainHandCode) && stance?.AttackAnimation != null && stance.AttackAnimation.Count > 0)
                {
                    return stance;
                }
            }
        }

        if (daggerStats?.OffHandStance?.AttackAnimation != null && daggerStats.OffHandStance.AttackAnimation.Count > 0)
        {
            return daggerStats.OffHandStance;
        }

        return daggerStats?.OneHandedStance;
    }

    protected virtual string? GetLinkedOffhandAttackAnimation(StanceStats stats, AttackDirection direction, int counter)
    {
        if (stats.AttackAnimation == null || stats.AttackAnimation.Count == 0)
        {
            return null;
        }

        string directionKey = direction.ToString();
        if (stats.AttackDirectionsType != "None"
            && stats.AttackAnimation.TryGetValue(directionKey, out string[]? directionalAnimations)
            && directionalAnimations != null
            && directionalAnimations.Length > 0)
        {
            return directionalAnimations[counter % directionalAnimations.Length];
        }

        if (stats.AttackAnimation.TryGetValue("Main", out string[]? mainAnimations)
            && mainAnimations != null
            && mainAnimations.Length > 0)
        {
            return mainAnimations[counter % mainAnimations.Length];
        }

        string[]? fallbackAnimations = stats.AttackAnimation.Values.FirstOrDefault(animations => animations != null && animations.Length > 0);
        if (fallbackAnimations == null || fallbackAnimations.Length == 0)
        {
            return null;
        }

        return fallbackAnimations[counter % fallbackAnimations.Length];
    }

    protected virtual MeleeAttackStats? GetLinkedOffhandDaggerDamageAttack(EntityPlayer player, MeleeWeaponStats? daggerStats)
    {
        string mainHandCode = player.RightHandItemSlot?.Itemstack?.Collectible?.Code?.ToString() ?? "";
        if (mainHandCode != "" && daggerStats?.OffHandDualWieldStances != null && daggerStats.OffHandDualWieldStances.Count > 0)
        {
            foreach ((string wildcard, StanceStats stance) in daggerStats.OffHandDualWieldStances)
            {
                if (WildcardUtil.Match(wildcard, mainHandCode) && stance?.Attack != null)
                {
                    return stance.Attack;
                }
            }
        }

        return daggerStats?.OneHandedStance?.Attack ?? daggerStats?.OffHandStance?.Attack;
    }

    protected virtual bool TryGetOffhandDaggerStack(EntityPlayer player, out ItemStack? stack)
    {
        stack = player.LeftHandItemSlot?.Itemstack;
        return IsDaggerStack(stack);
    }

    protected virtual bool IsDualDaggerLoadout(EntityPlayer player)
    {
        return IsDaggerStack(player.RightHandItemSlot?.Itemstack) && IsDaggerStack(player.LeftHandItemSlot?.Itemstack);
    }

    protected virtual bool IsDaggerStack(ItemStack? stack)
    {
        return CollectibleClassifier.IsDagger(stack);
    }

    protected virtual MeleeWeaponStats? TryGetMeleeWeaponStats(ItemStack stack)
    {
        try
        {
            return stack.Collectible?.Attributes?.AsObject<MeleeWeaponStats>();
        }
        catch
        {
            return null;
        }
    }

    protected virtual string? GetAttackAnimationForDirection(StanceStats stats, AttackDirection direction, int counter)
    {
        if (stats.RiposteAnimation != null)
        {
            return stats.RiposteAnimation;
        }

        if (stats.AttackAnimation == null || stats.AttackAnimation.Count == 0)
        {
            return null;
        }

        string directionKey = direction.ToString();
        string[]? animations = null;
        if (stats.AttackDirectionsType != "None" && stats.AttackAnimation.TryGetValue(directionKey, out string[]? directionalAnimations))
        {
            animations = directionalAnimations;
        }
        else if (stats.AttackAnimation.TryGetValue("Main", out string[]? mainAnimations))
        {
            animations = mainAnimations;
        }
        else
        {
            animations = stats.AttackAnimation.Values.FirstOrDefault();
        }

        if (animations == null || animations.Length == 0)
        {
            return null;
        }

        return animations[counter % animations.Length];
    }

    protected virtual bool AttackImpactFunction(TimeSpan duration, ref TimeSpan delta)
    {
        TimeSpan totalDuration = TimeSpan.FromMilliseconds(Stats.AnimationStaggerOnHitDurationMs);

        /*double multiplier = duration / totalDuration;
        multiplier = Math.Pow(multiplier, 3);
        delta = delta * multiplier;*/

        delta = TimeSpan.Zero;

        return duration < totalDuration;
    }
    protected virtual bool AttackAnimationCallback(EntityPlayer player, bool mainHand)
    {
        AnimationBehavior?.PlayReadyAnimation(mainHand);
        TpAnimationBehavior?.PlayReadyAnimation(mainHand);

        if (CheckState(mainHand, MeleeWeaponState.Cooldown, MeleeWeaponState.Attacking, MeleeWeaponState.WindingUp))
        {
            SetState(MeleeWeaponState.Idle, mainHand);
        }

        if (!CheckState(mainHand, MeleeWeaponState.Idle))
        {
            MeleeAttackSystem.UpdateAttackStatus(player, MeleeAttackStatus.End, mainHand);
        }

        if (mainHand)
        {
            MainHandAttackCounter = 0;
        }
        else
        {
            OffHandAttackCounter = 0;
        }

        return true;
    }
    protected virtual void AttackAnimationCallbackHandler(EntityPlayer player, string callbackCode, bool mainHand)
    {
        switch (callbackCode)
        {
            case "start":
                StopAttackCooldown(mainHand);
                SetState(MeleeWeaponState.Attacking, mainHand);
                break;
            case "stop":
                SetState(MeleeWeaponState.Cooldown, mainHand);
                break;
            case "ready":
                MeleeAttackSystem.UpdateAttackStatus(player, MeleeAttackStatus.End, mainHand);
                SetState(MeleeWeaponState.Idle, mainHand);
                break;
        }
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Released)]
    protected virtual bool StopAttack(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!Active) return false;

        return false;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Block(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!Active) return false;

        if (CanUseDualWieldPrimedThrow(player, mainHand)
            && CheckState(mainHand, MeleeWeaponState.Idle)
            && CanThrow(player, mainHand)
            && Stats.ThrowAttack != null
            && AimingStats != null)
        {
            // Let throw aiming handlers process RMB for dual-dagger throws.
            return false;
        }

        bool handleEvent = !Settings.VanillaActionsWhileBlocking;

        if (InteractionsTester.PlayerTriesToInteract(player, mainHand, eventData)) return false;
        if (!CanBlock(player, mainHand) && !CanParry(player, mainHand)) return false;
        if (!CheckState(mainHand, MeleeWeaponState.Idle, MeleeWeaponState.WindingUp, MeleeWeaponState.Cooldown)) return handleEvent;
        if (IsBlockOnCooldown(mainHand)) return handleEvent;
        EnsureStance(player, mainHand);
        if (mainHand && CanBlockWithOtherHand(player, mainHand)) return handleEvent;
        if (ActionRestricted(player, mainHand)) return handleEvent;

        StanceStats? stats = GetStanceStats(player, mainHand);
        DamageBlockJson? parryStats = stats?.Parry?.Clone();
        DamageBlockJson? blockStats = stats?.Block?.Clone();
        ItemStackMeleeWeaponStats stackStats = ItemStackMeleeWeaponStats.FromItemStack(slot.Itemstack);

        if (parryStats?.BlockTier != null)
        {
            foreach ((string damageType, float tier) in parryStats.BlockTier)
            {
                parryStats.BlockTier[damageType] += stackStats.ParryTierBonus;
            }

            if (player.Api.ModLoader.GetModSystem<CharacterSystem>().HasTrait(player.Player, "canParryProjectiles"))
            {
                parryStats.CanBlockProjectiles = true;
            }
        }

        if (blockStats?.BlockTier != null)
        {
            foreach ((string damageType, float tier) in blockStats.BlockTier)
            {
                blockStats.BlockTier[damageType] += stackStats.BlockTierBonus;
            }
        }

        if (CanParry(player, mainHand) && parryStats != null && stats != null)
        {
            if (!ParryButtonReleased) return handleEvent;

            SetState(MeleeWeaponState.Parrying, mainHand);
            if (stats.ParryWithoutDelay)
            {
                MeleeBlockSystem.StartBlock(parryStats, mainHand, () => RiposteCallback(mainHand, player));
            }
            AnimationBehavior?.Play(
                mainHand,
                stats.ParryAnimation ?? stats.BlockAnimation ?? throw new InvalidOperationException($"[Combat Overhaul] Item '{Item.Code}' does not have block or parry animations specified"),
                animationSpeed: PlayerActionsBehavior?.ManipulationSpeed ?? 1,
                category: AnimationCategory(mainHand),
                callback: () => BlockAnimationCallback(mainHand, player, TimeSpan.FromMilliseconds(stats.ParryCooldownMs)),
                callbackHandler: code => BlockAnimationCallbackHandler(code, mainHand, blockStats, parryStats, player));
            TpAnimationBehavior?.Play(
                mainHand,
                stats.ParryAnimation ?? stats.BlockAnimation ?? throw new InvalidOperationException($"[Combat Overhaul] Item '{Item.Code}' does not have block or parry animations specified"),
                animationSpeed: PlayerActionsBehavior?.ManipulationSpeed ?? 1,
                category: AnimationCategory(mainHand));

            ParryButtonReleased = false;
        }
        else if (CanBlock(player, mainHand) && blockStats != null && stats != null)
        {
            SetState(MeleeWeaponState.Blocking, mainHand);
            MeleeBlockSystem.StartBlock(blockStats, mainHand);
            if (ShouldUseVanillaShieldRaiseAnimation(slot, mainHand))
            {
                PlayVanillaShieldRaiseAnimation(player, mainHand);
            }
            else
            {
                AnimationBehavior?.Play(
                    mainHand,
                    stats.BlockAnimation ?? throw new InvalidOperationException($"[Combat Overhaul] Item '{Item.Code}' does not have block or parry animations specified"),
                    animationSpeed: GetAnimationSpeed(player, Stats),
                    category: AnimationCategory(mainHand),
                    callback: () => BlockAnimationCallback(mainHand, player, TimeSpan.FromMilliseconds(stats.BlockCooldownMs)),
                    callbackHandler: code => BlockAnimationCallbackHandler(code, mainHand, blockStats, parryStats, player));
                TpAnimationBehavior?.Play(
                    mainHand,
                    stats.BlockAnimation ?? throw new InvalidOperationException($"[Combat Overhaul] Item '{Item.Code}' does not have block or parry animations specified"),
                    animationSpeed: GetAnimationSpeed(player, Stats),
                    category: AnimationCategory(mainHand));
            }
        }

        SetSpeedPenalty(mainHand, player);

        return handleEvent;
    }
    protected virtual void BlockAnimationCallbackHandler(string callbackCode, bool mainHand, DamageBlockJson? blockStats, DamageBlockJson? parryStats, EntityPlayer player)
    {
        switch (callbackCode)
        {
            case "startParry":
                {
                    if (CanParry(player, mainHand) && parryStats != null)
                    {
                        SetState(MeleeWeaponState.Parrying, mainHand);
                        MeleeBlockSystem.StartBlock(parryStats, mainHand, () => RiposteCallback(mainHand, player));
                    }
                }
                break;
            case "stopParry":
                {
                    if (CanBlock(player, mainHand) && blockStats != null && PlayerActionsBehavior?.ActionListener.IsActive(EnumEntityAction.RightMouseDown) == true)
                    {
                        SetState(MeleeWeaponState.Blocking, mainHand);
                        MeleeBlockSystem.StartBlock(blockStats, mainHand);
                    }
                    else
                    {
                        MeleeBlockSystem.StopBlock(mainHand);
                    }
                }
                break;
        }
    }
    protected virtual bool BlockAnimationCallback(bool mainHand, EntityPlayer player, TimeSpan cooldown)
    {
        if (!CheckState(mainHand, MeleeWeaponState.Parrying)) return true;

        SetState(MeleeWeaponState.Idle, mainHand);
        AnimationBehavior?.PlayReadyAnimation(mainHand);
        TpAnimationBehavior?.PlayReadyAnimation(mainHand);

        if (cooldown > TimeSpan.Zero)
        {
            StartBlockCooldown(mainHand, cooldown);
        }

        SetSpeedPenalty(mainHand, player);

        ResetRiposte(mainHand);

        return true;
    }
    protected virtual void RiposteCallback(bool mainHand, EntityPlayer player)
    {
        if (!CheckState(mainHand, MeleeWeaponState.Parrying, MeleeWeaponState.Blocking)) return;

        SetRiposte(mainHand, true);
    }
    
    protected virtual void SetRiposte(bool mainHand, bool value)
    {
        if (mainHand)
        {
            CanRiposteMainHand = value;
            Api.World.UnregisterCallback(RiposteTimerMainHand);
        }
        else
        {
            CanRiposteOffHand = value;
            Api.World.UnregisterCallback(RiposteTimerOffHand);
        }
    }
    protected virtual void ResetRiposte(bool mainHand)
    {
        if (mainHand)
        {
            Api.World.UnregisterCallback(RiposteTimerMainHand);
            RiposteTimerMainHand = Api.World.RegisterCallback(_ => SetRiposte(mainHand, false), RiposteGracePeriodMs);
        }
        else
        {
            Api.World.UnregisterCallback(RiposteTimerOffHand);
            RiposteTimerOffHand = Api.World.RegisterCallback(_ => SetRiposte(mainHand, false), RiposteGracePeriodMs);
        }
    }
    protected virtual void TurnOnRiposte(bool mainHand)
    {
        if (mainHand)
        {
            RiposteMainHand = true;
        }
        else
        {
            RiposteOffHand = true;
        }
    }
    protected virtual void TurnOffRiposte(bool mainHand)
    {
        if (mainHand)
        {
            RiposteMainHand = false;
        }
        else
        {
            RiposteOffHand = false;
        }
    }
    protected virtual bool RiposteActive(bool mainHand)
    {
        if (mainHand)
        {
            return RiposteMainHand;
        }
        else
        {
            return RiposteOffHand;
        }
    }


    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    protected virtual bool StopBlock(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!Active) return false;

        ParryButtonReleased = true;
        if (!CheckState(mainHand, MeleeWeaponState.Blocking)) return false;

        MeleeBlockSystem.StopBlock(mainHand);
        if (ShouldUseVanillaShieldRaiseAnimation(slot, mainHand))
        {
            StopVanillaShieldRaiseAnimation(player, mainHand);
        }
        else
        {
            AnimationBehavior?.PlayReadyAnimation(mainHand);
            TpAnimationBehavior?.PlayReadyAnimation(mainHand);
        }
        SetState(MeleeWeaponState.Idle, mainHand);

        float cooldown = GetStanceStats(player, mainHand)?.BlockCooldownMs ?? 0;
        if (cooldown != 0)
        {
            StartBlockCooldown(mainHand, TimeSpan.FromMilliseconds(cooldown));
        }

        SetSpeedPenalty(mainHand, player);

        ResetRiposte(mainHand);

        return true;
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Active)]
    protected virtual bool Bash(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!Active) return false;

        if (InteractionsTester.PlayerTriesToInteract(player, mainHand, eventData)) return false;
        EnsureStance(player, mainHand);
        if (!CanBash(player, mainHand)) return false;
        if (IsAttackOnCooldown(mainHand)) return false;
        if (ActionRestricted(player, mainHand)) return false;
        if (!mainHand && CanThrowWithOtherHand(player, mainHand)) return false;

        MeleeAttack? attack = GetStanceBlockBash(player, mainHand, direction);
        StanceStats? stats = GetStanceStats(player, mainHand);
        MeleeAttack? handle = GetStanceHandleAttack(player, mainHand);

        if (attack == null || stats == null) return false;

        switch (GetState<MeleeWeaponState>(mainHand))
        {
            case MeleeWeaponState.Blocking:
                {
                    if (mainHand)
                    {
                        CurrentMainHandDirection = direction;
                    }
                    else
                    {
                        CurrentOffHandDirection = direction;
                    }

                    int counter = mainHand ? MainHandAttackCounter : OffHandAttackCounter;

                    string attackAnimation =
                        DirectionsType == DirectionsConfiguration.None ?
                        stats.BlockBashAnimation["Main"][counter % stats.BlockBashAnimation["Main"].Length] :
                        stats.BlockBashAnimation[direction.ToString()][counter % stats.BlockBashAnimation[direction.ToString()].Length];

                    float animationSpeed = GetAnimationSpeed(player, Stats) * ItemStackMeleeWeaponStats.GetAttackSpeed(slot.Itemstack) * stats.AttackSpeedMultiplier;
                    SetState(MeleeWeaponState.BlockBashWindingUp, mainHand);
                    ParryButtonReleased = true;

                    MeleeAttackSystem.UpdateAttackStatus(player, MeleeAttackStatus.Start, mainHand);
                    attack.Start(player.Player);
                    handle?.Start(player.Player);
                    AnimationBehavior?.Play(
                        mainHand,
                        attackAnimation,
                        animationSpeed: animationSpeed,
                        category: AnimationCategory(mainHand),
                        callback: () => BashAnimationCallback(slot, player, mainHand),
                        callbackHandler: code => BashAnimationCallbackHandler(player, code, mainHand));
                    TpAnimationBehavior?.Play(
                        mainHand,
                        attackAnimation,
                        animationSpeed: animationSpeed,
                        category: AnimationCategory(mainHand));

                    if (mainHand)
                    {
                        MainHandAttackCounter++;
                    }
                    else
                    {
                        OffHandAttackCounter++;
                    }
                    HandleHitTerrain = false;
                }
                break;
            case MeleeWeaponState.BlockBashWindingUp:
                break;
            case MeleeWeaponState.BlockBashCooldown:
                break;
            case MeleeWeaponState.BlockBashAttacking:
                break;
            default:
                return false;
        }

        SetSpeedPenalty(mainHand, player);

        return true;
    }
    protected virtual void TryBash(MeleeAttack attack, MeleeAttack? handle, StanceStats stats, ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        ItemStackMeleeWeaponStats stackStats = ItemStackMeleeWeaponStats.FromItemStack(slot.Itemstack);

        if (handle != null)
        {
            bool hanldeAttacked = handle.Attack(
                        player.Player,
                        slot,
                        mainHand,
                        out IEnumerable<(Block block, Vector3d point)> handleTerrainCollision,
                        out IEnumerable<(Vintagestory.API.Common.Entities.Entity entity, Vector3d point)> handleEntitiesCollision,
                        stackStats);

            if (!HandleHitTerrain && hanldeAttacked)
            {
                if (stats.HandleHitSound != null) SoundsSystem.Play(stats.HandleHitSound);
                HandleHitTerrain = true;
            }

            if (hanldeAttacked) return;
        }

        bool attacked = attack.Attack(
            player.Player,
            slot,
            mainHand,
            out IEnumerable<(Block block, Vector3d point)> terrainCollision,
            out IEnumerable<(Vintagestory.API.Common.Entities.Entity entity, Vector3d point)> entitiesCollision,
            stackStats);

        if (handle != null) handle.AddAttackedEntities(attack);

        if (attacked && stats.BashHitSound != null)
        {
            SoundsSystem.Play(stats.BashHitSound);
        }

        if (attacked && Stats.AnimationStaggerOnHitDurationMs > 0)
        {
            AnimationBehavior?.SetSpeedModifier(AttackImpactFunction);
            Api.World.AddCameraShake(Stats.ScreenShakeStrength);
        }
    }
    protected virtual bool BashAnimationCallback(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        bool rightMouseDown = PlayerActionsBehavior?.ActionListener.IsActive(EnumEntityAction.RightMouseDown) == true;

        MeleeAttackSystem.UpdateAttackStatus(player, MeleeAttackStatus.End, mainHand);

        if (!rightMouseDown)
        {
            if (CheckState(mainHand, MeleeWeaponState.BlockBashCooldown, MeleeWeaponState.BlockBashAttacking, MeleeWeaponState.BlockBashWindingUp))
            {
                SetState(MeleeWeaponState.Idle, mainHand);
            }

            if (CheckState(mainHand, MeleeWeaponState.Idle))
            {
                AnimationBehavior?.PlayReadyAnimation(mainHand);
                TpAnimationBehavior?.PlayReadyAnimation(mainHand);
            }

            MeleeBlockSystem.StopBlock(mainHand);
        }
        else
        {
            if (CheckState(mainHand, MeleeWeaponState.BlockBashCooldown, MeleeWeaponState.BlockBashAttacking, MeleeWeaponState.BlockBashWindingUp))
            {
                SetState(MeleeWeaponState.Blocking, mainHand);
            }

            ReturnToBlock(slot, player, mainHand);
        }

        if (mainHand)
        {
            MainHandAttackCounter = 0;
        }
        else
        {
            OffHandAttackCounter = 0;
        }

        return true;
    }
    protected virtual void BashAnimationCallbackHandler(EntityPlayer player, string callbackCode, bool mainHand)
    {
        bool rightMouseDown = PlayerActionsBehavior?.ActionListener.IsActive(EnumEntityAction.RightMouseDown) == true;

        switch (callbackCode)
        {
            case "start":
                SetState(MeleeWeaponState.BlockBashAttacking, mainHand);
                break;
            case "stop":
                SetState(MeleeWeaponState.BlockBashCooldown, mainHand);
                break;
            case "ready":
                MeleeAttackSystem.UpdateAttackStatus(player, MeleeAttackStatus.End, mainHand);
                SetState(rightMouseDown ? MeleeWeaponState.BlockBashCooldown : MeleeWeaponState.Idle, mainHand);
                break;
        }
    }
    protected virtual void ReturnToBlock(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        StanceStats? stats = GetStanceStats(player, mainHand);
        DamageBlockJson? parryStats = stats?.Parry?.Clone();
        DamageBlockJson? blockStats = stats?.Block?.Clone();
        ItemStackMeleeWeaponStats stackStats = ItemStackMeleeWeaponStats.FromItemStack(slot.Itemstack);

        if (parryStats?.BlockTier != null)
        {
            foreach ((string damageType, float tier) in parryStats.BlockTier)
            {
                parryStats.BlockTier[damageType] += stackStats.ParryTierBonus;
            }
        }

        if (blockStats?.BlockTier != null)
        {
            foreach ((string damageType, float tier) in blockStats.BlockTier)
            {
                blockStats.BlockTier[damageType] += stackStats.BlockTierBonus;
            }
        }

        if (CanBlock(player, mainHand) && blockStats != null && stats != null)
        {
            SetState(MeleeWeaponState.Blocking, mainHand);
            MeleeBlockSystem.StartBlock(blockStats, mainHand);
            AnimationBehavior?.Play(
                mainHand,
                stats.BlockAnimation,
                animationSpeed: GetAnimationSpeed(player, Stats),
                category: AnimationCategory(mainHand),
                callback: () => BlockAnimationCallback(mainHand, player, TimeSpan.FromMilliseconds(stats.BlockCooldownMs)),
                callbackHandler: code => BlockAnimationCallbackHandler(code, mainHand, blockStats, parryStats, player));
            TpAnimationBehavior?.Play(
                mainHand,
                stats.BlockAnimation,
                animationSpeed: GetAnimationSpeed(player, Stats),
                category: AnimationCategory(mainHand));
        }

        SetSpeedPenalty(mainHand, player);
    }

    // Generic primed-throw path intended for reuse by other dual-wield throwable weapons.
    protected virtual bool TryStartDualWieldPrimedThrow(ItemSlot slot, EntityPlayer player, ActionEventData eventData, bool mainHand)
    {
        if (!CanUseDualWieldPrimedThrow(player, mainHand)) return false;
        if (InteractionsTester.PlayerTriesToInteract(player, mainHand, eventData)) return false;
        if (!CanThrow(player, mainHand) || Stats.ThrowAttack == null || AimingStats == null) return false;
        if (!CheckState(mainHand, MeleeWeaponState.Idle)) return false;

        MainHandPrimedDualWieldThrow = true;
        StartThrowAiming(slot, player, mainHand);
        return true;
    }

    protected virtual bool CanUseDualWieldPrimedThrow(EntityPlayer player, bool mainHand)
    {
        // Default implementation for daggers; other weapons can reuse by overriding this method.
        return mainHand && IsDualDaggerLoadout(player);
    }

    protected virtual void ResetPrimedDualWieldThrow(bool mainHand)
    {
        if (mainHand)
        {
            MainHandPrimedDualWieldThrow = false;
        }
    }

    protected virtual void StartThrowAiming(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        SetState(MeleeWeaponState.StartingAim, mainHand);
        AimingSystem.AimingState = WeaponAimingState.Blocked;
        AimingAnimationController?.Play(mainHand);
        RangedWeaponSystem.SendStatusChange(player, RangedWeaponStatus.StartAiming, mainHand);

        ItemStackMeleeWeaponStats stackStats = ItemStackMeleeWeaponStats.FromItemStack(slot.Itemstack);
        AimingStats stats = AimingStats!.Clone();
        stats.AimDifficulty *= stackStats.ThrownAimingDifficulty;

        AimingStats.CursorType = Enum.Parse<AimingCursorType>(Settings.ThrownWeaponsCursorType, ignoreCase: true);
        AimingStats.VerticalLimit = Settings.ThrownWeaponsAimingVerticalLimit * DefaultVerticalLimit;
        AimingStats.HorizontalLimit = Settings.ThrownWeaponsAimingHorizontalLimit * DefaultHorizontalLimit;
        AimingSystem.StartAiming(AimingStats);

        AnimationBehavior?.Play(mainHand, Stats.ThrowAttack!.AimAnimation, animationSpeed: GetAnimationSpeed(player, Stats), callback: () => AimAnimationCallback(slot, mainHand));
        TpAnimationBehavior?.Play(mainHand, Stats.ThrowAttack.AimAnimation, animationSpeed: GetAnimationSpeed(player, Stats));
        if (TpAnimationBehavior == null) AnimationBehavior?.PlayVanillaAnimation(Stats.ThrowAttack.TpAimAnimation, mainHand);
    }

    protected virtual void TryRefillAfterPrimedThrow(EntityPlayer player, ItemSlot thrownSlot, bool mainHand, ItemStack? thrownReferenceStack = null)
    {
        if (!mainHand || !MainHandPrimedDualWieldThrow) return;
        if (thrownSlot?.Itemstack != null && thrownSlot.Itemstack.StackSize > 0) return;

        ItemSlot? replacement = FindInventoryReplacementForThrown(player, thrownSlot, thrownReferenceStack);
        if (replacement != null)
        {
            thrownSlot.TryFlipWith(replacement);
            thrownSlot.MarkDirty();
            replacement.MarkDirty();
            return;
        }

        // If no replacement dagger exists in inventory, move offhand dagger to main hand.
        ItemSlot offhandSlot = player.LeftHandItemSlot;
        if (IsDaggerStack(offhandSlot?.Itemstack))
        {
            thrownSlot.TryFlipWith(offhandSlot);
            thrownSlot.MarkDirty();
            offhandSlot.MarkDirty();
        }
    }

    protected virtual ItemSlot? FindInventoryReplacementForThrown(EntityPlayer player, ItemSlot thrownSlot, ItemStack? thrownReferenceStack = null)
    {
        ItemStack? referenceStack = thrownReferenceStack ?? player.RightHandItemSlot?.Itemstack ?? thrownSlot.Itemstack;
        string referenceCode = referenceStack?.Collectible?.Code?.ToString() ?? "";
        bool anyDaggerReplacementAllowed = IsDaggerStack(referenceStack);
        if (referenceCode == "" && !anyDaggerReplacementAllowed) return null;

        ItemSlot? replacementSlot = null;

        HashSet<ItemSlot> scannedSlots = [];

        void ScanInventory(IInventory? inv)
        {
            if (replacementSlot != null) return;
            if (inv == null) return;

            foreach (ItemSlot candidate in inv)
            {
                if (!scannedSlots.Add(candidate)) continue;
                if (candidate == null || candidate == thrownSlot || candidate == player.LeftHandItemSlot) continue;
                if (candidate.Itemstack?.Collectible?.Code == null) continue;
                if (!IsDaggerStack(candidate.Itemstack)) continue;

                string candidateCode = candidate.Itemstack.Collectible.Code.ToString();
                if (anyDaggerReplacementAllowed ||
                    candidateCode == referenceCode ||
                    WildcardUtil.Match(referenceCode + "-*", candidateCode) ||
                    WildcardUtil.Match(referenceCode, candidateCode))
                {
                    replacementSlot = candidate;
                    return;
                }
            }
        }

        IPlayerInventoryManager? inventoryManager = player.Player?.InventoryManager;
        ScanInventory(inventoryManager?.GetOwnInventory(GlobalConstants.hotBarInvClassName));
        ScanInventory(inventoryManager?.GetOwnInventory(GlobalConstants.backpackInvClassName));
        ScanInventory(player.GetBehavior<EntityBehaviorPlayerInventory>()?.Inventory);

        if (inventoryManager?.Inventories != null)
        {
            foreach ((_, IInventory? inventory) in inventoryManager.Inventories)
            {
                if (replacementSlot != null) break;
                if (inventory == null || inventory is InventoryPlayerCreative) continue;
                ScanInventory(inventory);
            }
        }

        return replacementSlot;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Aim(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!Active) return false;

        if (InteractionsTester.PlayerTriesToInteract(player, mainHand, eventData)) return false;
        if (!CanThrow(player, mainHand) || Stats.ThrowAttack == null || AimingStats == null) return false;
        if (!CheckState(mainHand, MeleeWeaponState.Idle)) return false;
        if (mainHand && CanBlockWithOtherHand(player, mainHand) && !CanUseDualWieldPrimedThrow(player, mainHand)) return false;

        StartThrowAiming(slot, player, mainHand);

        return true;
    }
    protected virtual bool AimAnimationCallback(ItemSlot slot, bool mainHand)
    {
        SetState(MeleeWeaponState.Aiming, mainHand);
        AimingSystem.AimingState = WeaponAimingState.FullCharge;

        return true;
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Active)]
    protected virtual bool AimWhenBlocking(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!Active) return false;

        if (!eventData.Modifiers.Contains(EnumEntityAction.RightMouseDown)) return false;
        if (InteractionsTester.PlayerTriesToInteract(player, mainHand, eventData)) return false;
        if (!CanThrow(player, mainHand) || Stats.ThrowAttack == null || AimingStats == null) return false;
        if (!CheckState(mainHand, MeleeWeaponState.Idle)) return false;
        if (mainHand && !CanBlockWithOtherHand(player, mainHand)) return false;

        StartThrowAiming(slot, player, mainHand);

        return true;
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Pressed)]
    protected virtual bool Throw(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!Active) return false;

        if (Stats.ThrowAttack == null) return false;
        if (!CheckState(mainHand, MeleeWeaponState.Aiming)) return false;

        SetState(MeleeWeaponState.Throwing, mainHand);
        AnimationBehavior?.Play(mainHand, Stats.ThrowAttack.ThrowAnimation, animationSpeed: GetAnimationSpeed(player, Stats), callback: () => ThrowAnimationCallback(slot, player, mainHand));
        TpAnimationBehavior?.Play(mainHand, Stats.ThrowAttack.ThrowAnimation, animationSpeed: GetAnimationSpeed(player, Stats));
        AnimationBehavior?.StopVanillaAnimation(Stats.ThrowAttack.TpAimAnimation, mainHand);
        if (TpAnimationBehavior == null) AnimationBehavior?.PlayVanillaAnimation(Stats.ThrowAttack.TpThrowAnimation, mainHand);

        RangedWeaponSystem.SendStatusChange(player, RangedWeaponStatus.TriggeredShot, mainHand);
        RangedWeaponSystem.SendStatusChange(player, RangedWeaponStatus.EndAiming, mainHand);

        return true;
    }
    protected virtual bool ThrowAnimationCallback(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        if (Stats.ThrowAttack == null) return false;

        SetState(MeleeWeaponState.Idle, mainHand);
        AimingSystem.AimingState = WeaponAimingState.FullCharge;

        Vintagestory.API.MathTools.Vec3d position = player.LocalEyePos + player.Pos.XYZ;
        Vector3 targetDirection = AimingSystem.TargetVec;

        targetDirection = ClientAimingSystem.Zeroing(targetDirection, Stats.ThrowAttack.Zeroing);

        RangedWeaponSystem.Shoot(slot, 1, new Vector3((float)position.X, (float)position.Y, (float)position.Z), new Vector3(targetDirection.X, targetDirection.Y, targetDirection.Z), mainHand, _ => { });
        RangedWeaponSystem.SendStatusChange(player, RangedWeaponStatus.SpawnedProjectile, mainHand);

        ItemStack? thrownReferenceStack = slot.Itemstack?.Clone();
        slot.TakeOut(1);
        TryRefillAfterPrimedThrow(player, slot, mainHand, thrownReferenceStack);
        ResetPrimedDualWieldThrow(mainHand);

        Api.World.AddCameraShake(Stats.ThrowScreenShakeStrength);

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    protected virtual bool Ease(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!Active) return false;

        if (!CheckState(mainHand, MeleeWeaponState.StartingAim, MeleeWeaponState.Aiming)) return false;

        AnimationBehavior?.PlayReadyAnimation(mainHand);
        TpAnimationBehavior?.PlayReadyAnimation(mainHand);
        SetState(MeleeWeaponState.Idle, mainHand);
        AimingAnimationController?.Stop(mainHand);
        AimingSystem.StopAiming();
        ResetPrimedDualWieldThrow(mainHand);
        AnimationBehavior?.StopVanillaAnimation(Stats.ThrowAttack?.TpAimAnimation ?? "", mainHand);
        RangedWeaponSystem.SendStatusChange(player, RangedWeaponStatus.EndAiming, mainHand);

        return true;
    }


    protected virtual void StartAttackCooldown(bool mainHand, TimeSpan time)
    {
        StopAttackCooldown(mainHand);

        if (mainHand)
        {
            MainHandAttackCooldownUntilMs = Api.World.ElapsedMilliseconds + (long)(time.TotalMilliseconds / PlayerActionsBehavior?.ManipulationSpeed ?? 1);
        }
        else
        {
            OffHandAttackCooldownUntilMs = Api.World.ElapsedMilliseconds + (long)(time.TotalMilliseconds / PlayerActionsBehavior?.ManipulationSpeed ?? 1);
        }
    }
    protected virtual void StopAttackCooldown(bool mainHand)
    {
        if (mainHand)
        {
            MainHandAttackCooldownUntilMs = 0;
        }
        else
        {
            OffHandAttackCooldownUntilMs = 0;
        }
    }
    protected virtual bool IsAttackOnCooldown(bool mainHand) => (mainHand ? Api.World.ElapsedMilliseconds <= MainHandAttackCooldownUntilMs : Api.World.ElapsedMilliseconds <= OffHandAttackCooldownUntilMs) || CheckGlobalCooldown(Api);
    protected virtual bool IsCooldownStopped(bool mainHand) => mainHand ? MainHandAttackCooldownUntilMs == 0 : OffHandAttackCooldownUntilMs == 0;

    protected virtual void StartBlockCooldown(bool mainHand, TimeSpan time)
    {
        StopBlockCooldown(mainHand);

        if (mainHand)
        {
            MainHandBlockCooldownTimer = Api.World.RegisterCallback(_ => MainHandBlockCooldownTimer = -1, (int)(time.TotalMilliseconds / PlayerActionsBehavior?.ManipulationSpeed ?? 1));
        }
        else
        {
            OffHandBlockCooldownTimer = Api.World.RegisterCallback(_ => OffHandBlockCooldownTimer = -1, (int)(time.TotalMilliseconds / PlayerActionsBehavior?.ManipulationSpeed ?? 1));
        }
    }
    protected virtual void StopBlockCooldown(bool mainHand)
    {
        if (mainHand)
        {
            if (MainHandBlockCooldownTimer != -1)
            {
                Api.World.UnregisterCallback(MainHandBlockCooldownTimer);
                MainHandBlockCooldownTimer = -1;
            }
        }
        else
        {
            if (OffHandBlockCooldownTimer != -1)
            {
                Api.World.UnregisterCallback(OffHandBlockCooldownTimer);
                OffHandBlockCooldownTimer = -1;
            }
        }
    }
    protected virtual bool IsBlockOnCooldown(bool mainHand) => mainHand ? MainHandBlockCooldownTimer != -1 : OffHandBlockCooldownTimer != -1;

    protected virtual void EnsureStance(EntityPlayer player, bool mainHand)
    {
        MeleeWeaponStance currentStance = GetStance<MeleeWeaponStance>(mainHand);
        MeleeWeaponStance newStance;

        string dualWieldKey = GetDualWieldKey(player, mainHand);
        bool dualWield = dualWieldKey != "";

        if (!mainHand)
        {
            if (dualWield)
            {
                SetStance(MeleeWeaponStance.OffHandDualWield, mainHand);
                newStance = MeleeWeaponStance.OffHandDualWield;
            }
            else
            {
                SetStance(MeleeWeaponStance.OffHand, mainHand);
                newStance = MeleeWeaponStance.OffHand;
            }

            if (currentStance != newStance)
            {
                AnimationBehavior?.PlayReadyAnimation(mainHand);
                TpAnimationBehavior?.PlayReadyAnimation(mainHand);
            }

            StanceStats? stats = GetStanceStats(player, mainHand);
            if (stats != null)
            {
                GripController?.AdjustGrip(mainHand, stats.GripMinLength, stats.GripMaxLength);
            }

            return;
        }

        bool offHandEmpty = CheckForOtherHandEmptyNoError(mainHand, player);
        if (offHandEmpty && Stats.TwoHandedStance != null)
        {
            SetStance(MeleeWeaponStance.TwoHanded, mainHand);
            newStance = MeleeWeaponStance.TwoHanded;

            DirectionsType = ParseDirectionsType(Stats.TwoHandedStance.AttackDirectionsType, DirectionsConfiguration.None);
        }
        else
        {
            if (dualWield)
            {
                SetStance(MeleeWeaponStance.MainHandDualWield, mainHand);
                newStance = MeleeWeaponStance.MainHandDualWield;

                if (Stats.MainHandDualWieldStances.TryGetValue(dualWieldKey, out StanceStats? dualWieldStats) && dualWieldStats != null)
                {
                    DirectionsType = ParseDirectionsType(dualWieldStats.AttackDirectionsType, DirectionsConfiguration.None);
                }
                else if (Stats.OneHandedStance != null)
                {
                    DirectionsType = ParseDirectionsType(Stats.OneHandedStance.AttackDirectionsType, DirectionsConfiguration.None);
                }
            }
            else
            {
                SetStance(MeleeWeaponStance.MainHand, mainHand);
                newStance = MeleeWeaponStance.MainHand;

                if (Stats.OneHandedStance != null)
                {
                    DirectionsType = ParseDirectionsType(Stats.OneHandedStance.AttackDirectionsType, DirectionsConfiguration.None);
                }
            }
        }

        if (currentStance != newStance)
        {
            AnimationBehavior?.PlayReadyAnimation(mainHand);
            TpAnimationBehavior?.PlayReadyAnimation(mainHand);
        }

        if (CheckState(mainHand, MeleeWeaponState.Cooldown) && !IsAttackOnCooldown(mainHand) && !IsCooldownStopped(mainHand))
        {
            StopAttackCooldown(mainHand);
            SetState(MeleeWeaponState.Idle, mainHand);
        }

        StanceStats? stanceStats = GetStanceStats(player, mainHand);
        if (stanceStats != null)
        {
            GripController?.AdjustGrip(mainHand, stanceStats.GripMinLength, stanceStats.GripMaxLength);
        }
    }
    protected virtual MeleeAttack? GetStanceAttack(EntityPlayer player, bool mainHand = true, AttackDirection direction = AttackDirection.Top, bool riposte = false)
    {
        if (!riposte)
        {
            riposte = RiposteActive(mainHand);
        }

        MeleeWeaponStance stance = GetStance<MeleeWeaponStance>(mainHand);
        string dualWieldKey = GetDualWieldKey(player, mainHand);
        bool hasDualWieldKey = !string.IsNullOrEmpty(dualWieldKey);
        return stance switch
        {
            MeleeWeaponStance.MainHand => riposte ? OneHandedRiposte : OneHandedAttack ?? DirectionalOneHandedAttacks?.GetValueOrDefault(direction),
            MeleeWeaponStance.OffHand => riposte ? OffHandRiposte : OffHandAttack ?? DirectionalOffHandAttacks?.GetValueOrDefault(direction),
            MeleeWeaponStance.TwoHanded => riposte ? TwoHandedRiposte : TwoHandedAttack ?? DirectionalTwoHandedAttacks?.GetValueOrDefault(direction),
            MeleeWeaponStance.MainHandDualWield => !hasDualWieldKey ? (riposte ? OneHandedRiposte : OneHandedAttack ?? DirectionalOneHandedAttacks?.GetValueOrDefault(direction)) : riposte ? OneHandedRiposte : GetMainHandDualWieldAttack(dualWieldKey, direction),
            MeleeWeaponStance.OffHandDualWield => !hasDualWieldKey ? (riposte ? OffHandRiposte : OffHandAttack ?? DirectionalOffHandAttacks?.GetValueOrDefault(direction)) : riposte ? OffHandRiposte : GetOffHandDualWieldAttack(dualWieldKey, direction),
            _ => OneHandedAttack,
        };
    }
    protected virtual MeleeAttack? GetStanceBlockBash(EntityPlayer player, bool mainHand = true, AttackDirection direction = AttackDirection.Top)
    {
        MeleeWeaponStance stance = GetStance<MeleeWeaponStance>(mainHand);
        return stance switch
        {
            MeleeWeaponStance.MainHand => OneHandedBlockBash ?? DirectionalOneHandedBlockBashes?.GetValueOrDefault(direction),
            MeleeWeaponStance.OffHand => OffHandBlockBash ?? DirectionalOffHandBlockBashes?.GetValueOrDefault(direction),
            MeleeWeaponStance.TwoHanded => TwoHandedBlockBash ?? DirectionalTwoHandedBlockBashes?.GetValueOrDefault(direction),
            _ => OneHandedBlockBash,
        };
    }
    protected virtual MeleeAttack? GetStanceHandleAttack(EntityPlayer player, bool mainHand = true)
    {
        MeleeWeaponStance stance = GetStance<MeleeWeaponStance>(mainHand);
        string dualWieldKey = GetDualWieldKey(player, mainHand);
        bool hasDualWieldKey = !string.IsNullOrEmpty(dualWieldKey);
        return stance switch
        {
            MeleeWeaponStance.MainHand => OneHandedHandleAttack,
            MeleeWeaponStance.OffHand => OffHandHandleAttack,
            MeleeWeaponStance.TwoHanded => TwoHandedHandleAttack,
            MeleeWeaponStance.MainHandDualWield => !hasDualWieldKey ? OneHandedHandleAttack : MainHandDualWieldHandleAttacks.TryGetValue(dualWieldKey, out MeleeAttack? mainHandle) ? mainHandle : OneHandedHandleAttack,
            MeleeWeaponStance.OffHandDualWield => !hasDualWieldKey ? OffHandHandleAttack : OffHandDualWieldHandleAttacks.TryGetValue(dualWieldKey, out MeleeAttack? offHandle) ? offHandle : OffHandHandleAttack,
            _ => OneHandedHandleAttack,
        };
    }
    protected virtual StanceStats? GetStanceStats(EntityPlayer player, bool mainHand = true)
    {
        MeleeWeaponStance stance = GetStance<MeleeWeaponStance>(mainHand);
        string dualWieldKey = GetDualWieldKey(player, mainHand);
        bool hasDualWieldKey = !string.IsNullOrEmpty(dualWieldKey);
        return stance switch
        {
            MeleeWeaponStance.MainHand => Stats.OneHandedStance,
            MeleeWeaponStance.OffHand => Stats.OffHandStance,
            MeleeWeaponStance.TwoHanded => Stats.TwoHandedStance,
            MeleeWeaponStance.MainHandDualWield => !hasDualWieldKey ? Stats.OneHandedStance : Stats.MainHandDualWieldStances.TryGetValue(dualWieldKey, out StanceStats? mainDualStats) ? mainDualStats : Stats.OneHandedStance,
            MeleeWeaponStance.OffHandDualWield => !hasDualWieldKey ? Stats.OffHandStance : Stats.OffHandDualWieldStances.TryGetValue(dualWieldKey, out StanceStats? offDualStats) ? offDualStats : Stats.OffHandStance,
            _ => Stats.OneHandedStance,
        };
    }

    protected virtual MeleeAttack? GetMainHandDualWieldAttack(string dualWieldKey, AttackDirection direction)
    {
        if (DirectionalMainHandDualWieldAttacks.TryGetValue(dualWieldKey, out Dictionary<AttackDirection, MeleeAttack>? directional) && directional != null)
        {
            if (directional.TryGetValue(direction, out MeleeAttack? attack)) return attack;
            if (directional.Values.FirstOrDefault() is MeleeAttack anyAttack) return anyAttack;
        }

        if (MainHandDualWieldHandleAttacks.TryGetValue(dualWieldKey, out MeleeAttack? handleAttack)) return handleAttack;

        return OneHandedAttack;
    }

    protected virtual MeleeAttack? GetOffHandDualWieldAttack(string dualWieldKey, AttackDirection direction)
    {
        if (DirectionalOffHandDualWieldAttacks.TryGetValue(dualWieldKey, out Dictionary<AttackDirection, MeleeAttack>? directional) && directional != null)
        {
            if (directional.TryGetValue(direction, out MeleeAttack? attack)) return attack;
            if (directional.Values.FirstOrDefault() is MeleeAttack anyAttack) return anyAttack;
        }

        if (OffHandDualWieldHandleAttacks.TryGetValue(dualWieldKey, out MeleeAttack? handleAttack)) return handleAttack;

        return OffHandAttack ?? OneHandedAttack;
    }
    protected virtual string GetDualWieldKey(EntityPlayer player, bool mainHand)
    {
        ItemSlot otherHandSlot = mainHand ? player.LeftHandItemSlot : player.RightHandItemSlot;
        string otherItemCode = otherHandSlot.Itemstack?.Collectible?.Code?.ToString() ?? string.Empty;
        if (otherItemCode == "") return "";

        IEnumerable<string> wildcards = mainHand ? Stats.MainHandDualWieldStances.Keys : Stats.OffHandDualWieldStances.Keys;

        return wildcards.Where(wildcard => WildcardUtil.Match(wildcard, otherItemCode)).FirstOrDefault("");
    }
    protected virtual bool CheckState<TState>(bool mainHand, params TState[] statesToCheck)
        where TState : struct, Enum
    {
        return statesToCheck.Contains((TState)Enum.ToObject(typeof(TState), PlayerActionsBehavior?.GetState(mainHand) % MaxState ?? 0));
    }
    protected virtual void SetStance<TStance>(TStance stance, bool mainHand = true)
        where TStance : struct, Enum
    {
        int stateCombined = PlayerActionsBehavior?.GetState(mainHand) ?? 0;
        int stateInt = stateCombined % MaxState;
        int stanceInt = (int)Enum.ToObject(typeof(TStance), stance);
        stateCombined = stateInt + MaxState * stanceInt;

        PlayerActionsBehavior?.SetState(stateCombined, mainHand);
    }
    protected virtual void SetState<TState>(TState state, bool mainHand = true)
        where TState : struct, Enum
    {
        int stateCombined = PlayerActionsBehavior?.GetState(mainHand) ?? 0;
        int stanceInt = stateCombined / MaxState;
        int stateInt = (int)Enum.ToObject(typeof(TState), state);
        stateCombined = stateInt + MaxState * stanceInt;

        PlayerActionsBehavior?.SetState(stateCombined, mainHand);
    }
    protected virtual void SetStateAndStance<TState, TStance>(TState state, TStance stance, bool mainHand = true)
        where TState : struct, Enum
        where TStance : struct, Enum
    {
        int stateInt = (int)Enum.ToObject(typeof(TState), state);
        int stanceInt = (int)Enum.ToObject(typeof(TStance), stance);
        int stateCombined = stateInt + MaxState * stanceInt;

        PlayerActionsBehavior?.SetState(stateCombined, mainHand);
    }
    protected virtual TState GetState<TState>(bool mainHand = true)
        where TState : struct, Enum
    {
        return (TState)Enum.ToObject(typeof(TState), PlayerActionsBehavior?.GetState(mainHand) % MaxState ?? 0);
    }
    protected virtual TStance GetStance<TStance>(bool mainHand = true)
        where TStance : struct, Enum
    {
        return (TStance)Enum.ToObject(typeof(TStance), PlayerActionsBehavior?.GetState(mainHand) / MaxState ?? 0);
    }
    protected virtual bool CanAttackWithOtherHand(EntityPlayer player, bool mainHand = true)
    {
        ItemSlot otherHandSlot = mainHand ? player.LeftHandItemSlot : player.RightHandItemSlot;
        IHasMeleeWeaponActions? item = otherHandSlot.Itemstack?.Collectible?.GetCollectibleInterface<IHasMeleeWeaponActions>();
        return item?.CanAttack(player, !mainHand) ?? false;
    }
    protected virtual bool CanBlockWithOtherHand(EntityPlayer player, bool mainHand = true)
    {
        ItemSlot otherHandSlot = mainHand ? player.LeftHandItemSlot : player.RightHandItemSlot;
        IHasMeleeWeaponActions? item = otherHandSlot.Itemstack?.Collectible?.GetCollectibleInterface<IHasMeleeWeaponActions>();
        return item?.CanBlock(player, !mainHand) ?? false;
    }
    protected virtual bool CanThrowWithOtherHand(EntityPlayer player, bool mainHand = true)
    {
        ItemSlot otherHandSlot = mainHand ? player.LeftHandItemSlot : player.RightHandItemSlot;
        IHasMeleeWeaponActions? item = otherHandSlot.Itemstack?.Collectible?.GetCollectibleInterface<IHasMeleeWeaponActions>();
        return item?.CanThrow(player, !mainHand) ?? false;
    }
    protected virtual bool CheckForOtherHandEmpty(bool mainHand, EntityPlayer player)
    {
        if (mainHand && !player.LeftHandItemSlot.Empty)
        {
            (player.World.Api as ICoreClientAPI)?.TriggerIngameError(this, "offhandShouldBeEmpty", Lang.Get("Offhand should be empty"));
            return false;
        }

        if (!mainHand && !player.RightHandItemSlot.Empty)
        {
            (player.World.Api as ICoreClientAPI)?.TriggerIngameError(this, "mainHandShouldBeEmpty", Lang.Get("Main hand should be empty"));
            return false;
        }

        return true;
    }
    protected virtual bool CheckForOtherHandEmptyNoError(bool mainHand, EntityPlayer player)
    {
        if (mainHand && !player.LeftHandItemSlot.Empty)
        {
            return false;
        }

        if (!mainHand && !player.RightHandItemSlot.Empty)
        {
            return false;
        }

        return true;
    }
    protected virtual void SetSpeedPenalty(bool mainHand, EntityPlayer player)
    {
        if (HasSpeedPenalty(mainHand, out float penalty, player))
        {
            PlayerActionsBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory, penalty);
        }
        else
        {
            PlayerActionsBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory);
        }
    }
    protected virtual bool HasSpeedPenalty(bool mainHand, out float penalty, EntityPlayer player)
    {
        penalty = 0;

        StanceStats? stance = GetStanceStats(player, mainHand);

        if (stance == null) return false;

        if (CheckState(mainHand, MeleeWeaponState.Blocking, MeleeWeaponState.Parrying))
        {
            penalty = stance.BlockSpeedPenalty;
        }
        else
        {
            penalty = stance.SpeedPenalty;
        }

        return MathF.Abs(penalty) > 1E-9f; // just some epsilon
    }
    protected virtual float GetAnimationSpeed(Entity player, string proficiencyStat, float min = 0.5f, float max = 2f)
    {
        float manipulationSpeed = PlayerActionsBehavior?.ManipulationSpeed ?? 1;
        float proficiencyBonus = proficiencyStat == "" ? 0 : player.Stats.GetBlended(proficiencyStat) - 1;
        return Math.Clamp(manipulationSpeed + proficiencyBonus, min, max);
    }
    protected virtual float GetAnimationSpeed(Entity player, WeaponStats stats, float min = 0.5f, float max = 2f)
    {
        float manipulationSpeed = PlayerActionsBehavior?.ManipulationSpeed ?? 1;
        float proficiencyBonus = stats.ProficiencyStat == "" ? 0 : player.Stats.GetBlended(stats.ProficiencyStat) - 1;
        foreach (string stat in stats.ProficiencyStats)
        {
            proficiencyBonus += player.Stats.GetBlended(stat) - 1;
        }
        return Math.Clamp(manipulationSpeed + proficiencyBonus, min, max);
    }

    protected virtual bool ActionRestricted(EntityPlayer player, bool mainHand = true)
    {
        ItemSlot slot = !mainHand ? player.RightHandItemSlot : player.LeftHandItemSlot;

        IRestrictAction? item = slot.Itemstack?.Collectible?.GetCollectibleInterface<IRestrictAction>();

        if (item == null)
        {
            return false;
        }

        return !mainHand ? item.RestrictRightHandAction() : item.RestrictLeftHandAction();
    }
    protected virtual string GetAttackStatsDescription(ItemSlot inSlot, IEnumerable<DamageDataJson> damageTypesData, string descriptionLangCode)
    {
        float maxDamage = 0;
        float minDamage = float.MaxValue;
        float maxTier = 0;
        float minTier = float.MaxValue;
        int armorPiercingTier = 0;
        HashSet<string> damageTypes = new();

        ItemStackMeleeWeaponStats stackStats = ItemStackMeleeWeaponStats.FromItemStack(inSlot.Itemstack);

        foreach (DamageDataJson attack in damageTypesData)
        {
            float attackDamage = attack.Damage * stackStats.DamageMultiplier;
            if (attackDamage > maxDamage)
            {
                maxDamage = attackDamage;
            }

            if (attackDamage < minDamage)
            {
                minDamage = attackDamage;
            }

            float currentTier = attack.Tier + stackStats.DamageTierBonus;
            if (currentTier > maxTier)
            {
                maxTier = currentTier;
            }
            if (currentTier < minTier)
            {
                minTier = currentTier;
            }

            int currentArmorPiercingTier = attack.ArmorPiercingTier;
            if (currentArmorPiercingTier > armorPiercingTier)
            {
                armorPiercingTier = currentArmorPiercingTier;
            }

            damageTypes.Add(attack.DamageType);
        }

        string damageType = damageTypes.Select(element => Lang.Get($"combatoverhaul:damage-type-{element}")).Aggregate((first, second) => $"{first}, {second}");

        string damageString = minDamage == maxDamage ? $"{maxDamage:F0}" : $"{minDamage:F0}-{maxDamage:F0}";
        string tierString = minTier == maxTier ? $"{maxTier:F0}" : $"{minTier:F0}-{maxTier:F0}";

        return Lang.Get(descriptionLangCode, damageString, tierString, damageType) + (armorPiercingTier > 0 ? "\n" + Lang.Get("combatoverhaul:iteminfo-melee-weapon-armorpiercing", armorPiercingTier) : "");
    }
    protected virtual IEnumerable<string> GetDirectionalAttackStatsDescriptions(ItemSlot inSlot, Dictionary<string, MeleeAttackStats> directionalAttacks, string descriptionLangCode)
    {
        HashSet<string> descriptions = new(StringComparer.Ordinal);

        bool isDagger = IsDaggerItemStack(inSlot.Itemstack);

        foreach ((string directionKey, MeleeAttackStats attackStats) in directionalAttacks)
        {
            if (attackStats.DamageTypes == null || attackStats.DamageTypes.Length == 0)
            {
                continue;
            }

            IEnumerable<DamageDataJson> damageTypesData = attackStats.DamageTypes.Select(element => element.Damage);
            if (isDagger && Enum.TryParse(directionKey, out AttackDirection direction) && IsTopOrSideDirection(direction))
            {
                damageTypesData = damageTypesData.Select(data =>
                    data.DamageType.Equals("Piercing", StringComparison.OrdinalIgnoreCase)
                        ? new DamageDataJson { Damage = data.Damage, DamageType = "Slashing", Tier = data.Tier, ArmorPiercingTier = data.ArmorPiercingTier }
                        : data);
            }

            string description = GetAttackStatsDescription(inSlot, damageTypesData, descriptionLangCode);
            descriptions.Add(description);
        }

        return descriptions;
    }
    protected virtual bool IsDaggerItemStack(ItemStack? stack)
    {
        return IsDaggerStack(stack);
    }
    protected virtual bool IsTopOrSideDirection(AttackDirection direction)
    {
        return direction == AttackDirection.Top
            || direction == AttackDirection.Left
            || direction == AttackDirection.Right
            || direction == AttackDirection.TopLeft
            || direction == AttackDirection.TopRight;
    }
    protected virtual bool ItemInOtherHandBlocksAttack(EntityPlayer player, bool mainHand)
    {
        return !mainHand && CanAttackWithOtherHand(player, mainHand) && GetStance<MeleeWeaponStance>(mainHand) != MeleeWeaponStance.OffHandDualWield;
    }


    protected static bool CheckState<TState>(int state, params TState[] statesToCheck)
        where TState : struct, Enum
    {
        return statesToCheck.Contains((TState)Enum.ToObject(typeof(TState), state % MaxState));
    }
    protected virtual bool ShouldUseVanillaShieldRaiseAnimation(ItemSlot slot, bool mainHand)
    {
        if (mainHand) return false;

        // Only real, unpatched Vintage Story shields should use the old Ctrl/raiseshield path.
        // Vanilla shields patched to CombatOverhaul:VanillaShield must use their configured
        // Combat Overhaul BlockAnimation/ReadyAnimation, the same as modded shields.
        return CollectibleClassifier.IsVanillaItemShield(slot.Itemstack?.Item);
    }
    protected virtual void PlayVanillaShieldRaiseAnimation(EntityPlayer player, bool mainHand)
    {
        string side = mainHand ? "right" : "left";
        AnimationBehavior?.PlayVanillaAnimation($"raiseshield-{side}", mainHand);
        AnimationBehavior?.PlayVanillaAnimation($"raiseshield-{side}-fp", mainHand);
        player.StartAnimation($"raiseshield-{side}");
        player.StartAnimation($"raiseshield-{side}-fp");
    }
    protected virtual void StopVanillaShieldRaiseAnimation(EntityPlayer player, bool mainHand)
    {
        string side = mainHand ? "right" : "left";
        AnimationBehavior?.StopVanillaAnimation($"raiseshield-{side}", mainHand);
        AnimationBehavior?.StopVanillaAnimation($"raiseshield-{side}-fp", mainHand);
        player.StopAnimation($"raiseshield-{side}");
        player.StopAnimation($"raiseshield-{side}-fp");
    }
    protected static string AnimationCategory(bool mainHand = true) => mainHand ? "main" : "mainOffhand";
    protected static DirectionsConfiguration ParseDirectionsType(string? value, DirectionsConfiguration fallback)
    {
        if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, true, out DirectionsConfiguration parsed))
        {
            return parsed;
        }

        return fallback;
    }
    protected static void RegisterCollider(string item, string type, MeleeAttack attack)
    {
#if DEBUG
        int typeIndex = 0;
        int modeIndex = _modeIndex++;
        foreach (MeleeDamageType damageType in attack.DamageTypes)
        {
            DebugWindowManager.RegisterCollider(item, $"{type}{typeIndex++}-{modeIndex}", damageType);
        }
#endif
    }
    protected static bool CheckGlobalCooldown(ICoreAPI api)
    {
        return GlobalCooldownUntilMs > api.World.ElapsedMilliseconds;
    }

#if DEBUG
    private static int _modeIndex = 0;
#endif
}
