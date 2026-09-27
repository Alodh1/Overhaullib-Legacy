using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CombatOverhaul.Animations;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class SoundPacket
{
    public string Code { get; set; } = "";
    public bool RandomizePitch { get; set; }
    public float Range { get; set; }
    public float Volume { get; set; }
    public float Pitch { get; set; } = 1;

    public SoundPacket()
    {
    }

    public SoundPacket(SoundFrame frame, int index) : this(frame, index, frame.GetPitch(1))
    {
    }

    public SoundPacket(SoundFrame frame, int index, float pitch)
    {
        Code = frame.Code[index];
        RandomizePitch = frame.RandomizePitch;
        Range = frame.Range;
        Volume = frame.Volume;
        Pitch = pitch;
    }
}

public class SoundsSynchronizerClient
{
    public SoundsSynchronizerClient(ICoreClientAPI api)
    {
        _api = api;
        _channel = _api.Network.RegisterChannel("CombatOverhaul:sounds")
            .RegisterMessageType<SoundPacket>();
    }

    public void Play(SoundFrame frame)
    {
        Play(frame, 1);
    }

    public void Play(SoundFrame frame, float animationSpeed)
    {
        int index = Math.Clamp((int)Math.Floor(_random.nextFloat(frame.Code.Length)), 0, frame.Code.Length - 1);
        float pitch = frame.GetPitch(animationSpeed);

        PlayFor(frame.Code[index], frame.RandomizePitch, frame.Range, frame.Volume, pitch);

        if (frame.Synchronize) _channel.SendPacket(new SoundPacket(frame, index, pitch));
    }

    public void Play(string code, bool randomizedPitch = false, float range = 32, float volume = 1, bool synchronize = true)
    {
        Play(code, randomizedPitch, range, volume, synchronize, 1);
    }

    public void Play(string code, bool randomizedPitch, float range, float volume, bool synchronize, float pitch)
    {
        pitch = NormalizePitch(pitch);
        PlayFor(code, randomizedPitch, range, volume, pitch);

        SoundPacket packet = new()
        {
            Code = code,
            RandomizePitch = randomizedPitch,
            Range = range,
            Volume = volume,
            Pitch = pitch
        };

        if (synchronize) _channel.SendPacket(packet);
    }

    private void PlayFor(string code, bool randomizedPitch, float range, float volume, float pitch)
    {
        pitch = NormalizePitch(pitch);
        if (IsDefaultPitch(pitch))
        {
            _api.World.PlaySoundFor(new(code), _api.World.Player, randomizedPitch, range, volume);
            return;
        }

        _api.World.PlaySoundFor(ToSoundAttributes(code, randomizedPitch, range, volume, pitch), _api.World.Player, 1);
    }

    internal static SoundAttributes ToSoundAttributes(string code, bool randomizedPitch, float range, float volume, float pitch)
    {
        pitch = NormalizePitch(pitch);
        return new SoundAttributes()
        {
            Location = new(code),
            Pitch = new(pitch, randomizedPitch ? pitch * 0.25f : 0, EnumDistribution.UNIFORM),
            Volume = new(volume, 0, EnumDistribution.UNIFORM),
            Range = range
        };
    }

    internal static float NormalizePitch(float pitch) => pitch <= 0 ? 1 : Math.Max(0.01f, pitch);
    internal static bool IsDefaultPitch(float pitch) => Math.Abs(NormalizePitch(pitch) - 1) < 0.0001f;

    private readonly ICoreClientAPI _api;
    private readonly IClientNetworkChannel _channel;
    private readonly NatFloat _random = new(0.5f, 0.5f, EnumDistribution.UNIFORM);
}

public class SoundsSynchronizerServer
{
    public SoundsSynchronizerServer(ICoreServerAPI api)
    {
        _api = api;
        _api.Network.RegisterChannel("CombatOverhaul:sounds")
            .RegisterMessageType<SoundPacket>()
            .SetMessageHandler<SoundPacket>(HandlePacket);
    }

    private readonly ICoreServerAPI _api;

    private void HandlePacket(IServerPlayer player, SoundPacket packet)
    {
        if (SoundsSynchronizerClient.IsDefaultPitch(packet.Pitch))
        {
            _api.World.PlaySoundAt(new(packet.Code), player.Entity, player, packet.RandomizePitch, packet.Range, packet.Volume);
            return;
        }

        _api.World.PlaySoundAt(SoundsSynchronizerClient.ToSoundAttributes(packet.Code, packet.RandomizePitch, packet.Range, packet.Volume, packet.Pitch), player.Entity, player, 1);
    }
}
