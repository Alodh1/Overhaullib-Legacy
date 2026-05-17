using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Client;

namespace CombatOverhaul.MeleeSystems;

public sealed class ImpaleAttackStats
{
    public bool Enabled { get; set; } = false;
    public string TargetFilter { get; set; } = "HostileOnly";
    public float MaxTargetVolume { get; set; } = 3f;
    public float[] HoldOffset { get; set; } = [0f, 0f, 1.25f];
    public float[] TargetOffset { get; set; } = [0f, -0.6f, 0f];
    public float ThrowVelocity { get; set; } = 14f;
    public float ThrowUpBias { get; set; } = 0.15f;
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class ImpaleAttachPacket
{
    public long AttackerEntityId { get; set; }
    public long TargetEntityId { get; set; }
    public bool MainHand { get; set; }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class ImpaleReleasePacket
{
    public long AttackerEntityId { get; set; }
    public long TargetEntityId { get; set; }
    public bool MainHand { get; set; }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class ImpaleThrowRequestPacket
{
    public bool MainHand { get; set; }
    public float[] Direction { get; set; } = [];
}

public readonly struct ImpaleKey : IEquatable<ImpaleKey>
{
    public readonly long AttackerEntityId;
    public readonly bool MainHand;

    public ImpaleKey(long attackerEntityId, bool mainHand)
    {
        AttackerEntityId = attackerEntityId;
        MainHand = mainHand;
    }

    public bool Equals(ImpaleKey other) => AttackerEntityId == other.AttackerEntityId && MainHand == other.MainHand;
    public override bool Equals(object? obj) => obj is ImpaleKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(AttackerEntityId, MainHand);
}

public sealed class ImpaleSystemClient
{
    public ImpaleSystemClient(ICoreClientAPI api)
    {
        _channel = api.Network.RegisterChannel(ChannelId)
            .RegisterMessageType<ImpaleAttachPacket>()
            .RegisterMessageType<ImpaleReleasePacket>()
            .RegisterMessageType<ImpaleThrowRequestPacket>()
            .SetMessageHandler<ImpaleAttachPacket>(HandlePacket)
            .SetMessageHandler<ImpaleReleasePacket>(HandlePacket);
    }

    public const string ChannelId = "CombatOverhaul:impale";

    public bool HasImpaled(long attackerEntityId, bool mainHand) => _states.ContainsKey(new ImpaleKey(attackerEntityId, mainHand));

    public void RequestThrow(bool mainHand, Vec3f direction)
    {
        _channel.SendPacket(new ImpaleThrowRequestPacket()
        {
            MainHand = mainHand,
            Direction = [direction.X, direction.Y, direction.Z]
        });
    }

    private readonly IClientNetworkChannel _channel;
    private readonly Dictionary<ImpaleKey, long> _states = [];

    private void HandlePacket(ImpaleAttachPacket packet)
    {
        _states[new ImpaleKey(packet.AttackerEntityId, packet.MainHand)] = packet.TargetEntityId;
    }

    private void HandlePacket(ImpaleReleasePacket packet)
    {
        _states.Remove(new ImpaleKey(packet.AttackerEntityId, packet.MainHand));
    }
}

public sealed class ImpaleSystemServer : IDisposable
{
    public ImpaleSystemServer(ICoreServerAPI api)
    {
        _api = api;
        _channel = api.Network.RegisterChannel(ImpaleSystemClient.ChannelId)
            .RegisterMessageType<ImpaleAttachPacket>()
            .RegisterMessageType<ImpaleReleasePacket>()
            .RegisterMessageType<ImpaleThrowRequestPacket>()
            .SetMessageHandler<ImpaleThrowRequestPacket>(HandlePacket);

        _tickListener = api.Event.RegisterGameTickListener(OnTick, 20);
        api.Event.OnEntityDeath += OnEntityDeath;
        api.Event.OnEntityDespawn += OnEntityDespawn;
        api.Event.PlayerDisconnect += OnPlayerDisconnect;
    }

    public void TryAttach(MeleeDamagePacket packet, Entity attacker, Entity target, ItemSlot? weaponSlot, bool damageReceived)
    {
        if (!packet.ImpaleEnabled || !damageReceived || !target.Alive) return;
        if (attacker is not EntityPlayer player) return;
        if (!IsValidTarget(target, packet)) return;

        ImpaleKey key = new(attacker.EntityId, packet.MainHand);
        if (_states.ContainsKey(key)) return;
        if (_targets.ContainsKey(target.EntityId)) return;

        string weaponCode = weaponSlot?.Itemstack?.Collectible?.Code?.ToString() ?? "";
        if (weaponCode.Length == 0) return;

        ImpaleState state = new()
        {
            AttackerEntityId = attacker.EntityId,
            TargetEntityId = target.EntityId,
            MainHand = packet.MainHand,
            WeaponCode = weaponCode,
            HoldOffset = NormalizeOffset(packet.ImpaleHoldOffset, [0f, 0f, 1.25f]),
            TargetOffset = NormalizeOffset(packet.ImpaleTargetOffset, [0f, -0.6f, 0f]),
            ThrowVelocity = MathF.Max(0f, packet.ImpaleThrowVelocity),
            ThrowUpBias = packet.ImpaleThrowUpBias
        };

        _states[key] = state;
        _targets[target.EntityId] = key;
        target.WatchedAttributes.SetLong("combatOverhaulImpaledBy", attacker.EntityId);
        target.WatchedAttributes.SetBool("combatOverhaulImpaledMainHand", packet.MainHand);
        target.WatchedAttributes.MarkPathDirty("combatOverhaulImpaledBy");

        _channel.BroadcastPacket(new ImpaleAttachPacket()
        {
            AttackerEntityId = attacker.EntityId,
            TargetEntityId = target.EntityId,
            MainHand = packet.MainHand
        });
    }

    public void Dispose()
    {
        _api.Event.UnregisterGameTickListener(_tickListener);
        _api.Event.OnEntityDeath -= OnEntityDeath;
        _api.Event.OnEntityDespawn -= OnEntityDespawn;
        _api.Event.PlayerDisconnect -= OnPlayerDisconnect;

        foreach (ImpaleKey key in _states.Keys.ToArray())
        {
            Clear(key, applyVelocity: false, Vec3d.Zero);
        }
    }

    private readonly ICoreServerAPI _api;
    private readonly IServerNetworkChannel _channel;
    private readonly Dictionary<ImpaleKey, ImpaleState> _states = [];
    private readonly Dictionary<long, ImpaleKey> _targets = [];
    private readonly long _tickListener;

    private void HandlePacket(IServerPlayer player, ImpaleThrowRequestPacket packet)
    {
        ImpaleKey key = new(player.Entity.EntityId, packet.MainHand);
        Vec3d direction = ReadDirection(packet.Direction, player.Entity.ServerPos.GetViewVector());
        Clear(key, applyVelocity: true, direction);
    }

    private void OnTick(float dt)
    {
        foreach (ImpaleKey key in _states.Keys.ToArray())
        {
            if (!_states.TryGetValue(key, out ImpaleState? state)) continue;

            Entity? attacker = _api.World.GetEntityById(state.AttackerEntityId);
            Entity? target = _api.World.GetEntityById(state.TargetEntityId);

            if (attacker is not EntityAgent agent || target is not EntityAgent targetAgent || !attacker.Alive || !target.Alive)
            {
                Clear(key, applyVelocity: false, Vec3d.Zero);
                continue;
            }

            ItemSlot? slot = state.MainHand ? agent.RightHandItemSlot : agent.LeftHandItemSlot;
            string currentWeaponCode = slot?.Itemstack?.Collectible?.Code?.ToString() ?? "";
            if (currentWeaponCode != state.WeaponCode)
            {
                Clear(key, applyVelocity: false, Vec3d.Zero);
                continue;
            }

            Vec3d position = GetAttachedPosition(agent, target, state);
            targetAgent.Controls.StopAllMovement();
            targetAgent.ServerControls.StopAllMovement();
            target.ServerPos.SetPos(position);
            target.Pos.SetFrom(target.ServerPos);
            target.ServerPos.Motion.Set(0, 0, 0);
            target.Pos.Motion.Set(0, 0, 0);
        }
    }

    private void Clear(ImpaleKey key, bool applyVelocity, Vec3d direction)
    {
        if (!_states.Remove(key, out ImpaleState? state)) return;

        _targets.Remove(state.TargetEntityId);

        Entity? target = _api.World.GetEntityById(state.TargetEntityId);
        if (target != null)
        {
            target.WatchedAttributes.RemoveAttribute("combatOverhaulImpaledBy");
            target.WatchedAttributes.RemoveAttribute("combatOverhaulImpaledMainHand");
            target.WatchedAttributes.MarkAllDirty();

            if (applyVelocity && target.Alive)
            {
                Vec3d velocity = direction.Clone();
                if (velocity.LengthSq() < 0.0001) velocity = new Vec3d(0, 0, -1);
                velocity.Normalize();
                velocity.Y += state.ThrowUpBias;
                if (velocity.LengthSq() > 0.0001) velocity.Normalize();
                velocity.Mul(state.ThrowVelocity);

                target.ServerPos.Motion.Set(velocity);
                target.Pos.Motion.Set(velocity);
            }
        }

        _channel.BroadcastPacket(new ImpaleReleasePacket()
        {
            AttackerEntityId = state.AttackerEntityId,
            TargetEntityId = state.TargetEntityId,
            MainHand = state.MainHand
        });
    }

    private void OnEntityDeath(Entity entity, DamageSource damageSource)
    {
        ClearEntity(entity.EntityId);
    }

    private void OnEntityDespawn(Entity entity, EntityDespawnData reasonData)
    {
        ClearEntity(entity.EntityId);
    }

    private void OnPlayerDisconnect(IServerPlayer player)
    {
        ClearEntity(player.Entity.EntityId);
    }

    private void ClearEntity(long entityId)
    {
        foreach (ImpaleKey key in _states.Keys.ToArray())
        {
            ImpaleState state = _states[key];
            if (state.AttackerEntityId == entityId || state.TargetEntityId == entityId)
            {
                Clear(key, applyVelocity: false, Vec3d.Zero);
            }
        }
    }

    private static Vec3d GetAttachedPosition(EntityAgent attacker, Entity target, ImpaleState state)
    {
        Vec3f view = attacker.ServerPos.GetViewVector();
        Vec3d forward = new(view.X, view.Y, view.Z);
        if (forward.LengthSq() < 0.0001) forward = new Vec3d(0, 0, -1);
        forward.Normalize();

        Vec3d right = new(-forward.Z, 0, forward.X);
        if (right.LengthSq() < 0.0001) right = new Vec3d(1, 0, 0);
        right.Normalize();

        Vec3d up = new(0, 1, 0);
        float[] hold = state.HoldOffset;
        float[] targetOffset = state.TargetOffset;

        Vec3d position = attacker.ServerPos.XYZ
            .Add(0, attacker.LocalEyePos.Y * 0.7, 0)
            .AddCopy(right * (hold[0] + targetOffset[0]))
            .AddCopy(up * (hold[1] + targetOffset[1]))
            .AddCopy(forward * (hold[2] + targetOffset[2]));

        position.Y -= target.SelectionBox.YSize * 0.25;
        return position;
    }

    private static bool IsValidTarget(Entity target, MeleeDamagePacket packet)
    {
        if (target is EntityPlayer) return false;
        if (target is not EntityAgent agent) return false;
        if (!target.IsCreature || !target.Alive) return false;
        if (agent.MountedOn != null) return false;

        float volume = target.SelectionBox.XSize * target.SelectionBox.YSize * target.SelectionBox.ZSize;
        if (volume <= 0 || volume > packet.ImpaleMaxTargetVolume) return false;

        string targetFilter = packet.ImpaleTargetFilter.Length == 0 ? "HostileOnly" : packet.ImpaleTargetFilter;
        if (targetFilter.Equals("HostileOnly", StringComparison.OrdinalIgnoreCase) && !IsHostile(target)) return false;

        return true;
    }

    private static bool IsHostile(Entity target)
    {
        string runtimeGroup = target.Properties?.Server?.SpawnConditions?.Runtime?.Group ?? "";
        string worldgenGroup = target.Properties?.Server?.SpawnConditions?.Worldgen?.Group ?? "";

        return runtimeGroup.Equals("hostile", StringComparison.OrdinalIgnoreCase)
            || worldgenGroup.Equals("hostile", StringComparison.OrdinalIgnoreCase);
    }

    private static float[] NormalizeOffset(float[]? value, float[] fallback)
    {
        if (value == null || value.Length < 3) return fallback;
        return [value[0], value[1], value[2]];
    }

    private static Vec3d ReadDirection(float[]? value, Vec3f fallback)
    {
        if (value == null || value.Length < 3) return new Vec3d(fallback.X, fallback.Y, fallback.Z);
        return new Vec3d(value[0], value[1], value[2]);
    }

    private sealed class ImpaleState
    {
        public long AttackerEntityId { get; set; }
        public long TargetEntityId { get; set; }
        public bool MainHand { get; set; }
        public string WeaponCode { get; set; } = "";
        public float[] HoldOffset { get; set; } = [0f, 0f, 1.25f];
        public float[] TargetOffset { get; set; } = [0f, -0.6f, 0f];
        public float ThrowVelocity { get; set; } = 14f;
        public float ThrowUpBias { get; set; } = 0.15f;
    }
}
