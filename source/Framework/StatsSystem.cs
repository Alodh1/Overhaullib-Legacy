using ProtoBuf;
using CombatOverhaul.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Server;

namespace CombatOverhaul;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class StatsPacket
{
    public string Stat { get; set; } = "";
    public string Category { get; set; } = "";
    public float Value { get; set; } = 0;
}

public class StatsSystemClient
{
    public StatsSystemClient(ICoreClientAPI api)
    {
        _api = api;
        _clientChannel = api.Network.RegisterChannel(_networkChannelId)
            .RegisterMessageType<StatsPacket>();
    }

    public void SetStat(string stat, string category, float value)
    {
        _clientChannel.SendPacket(new StatsPacket
        {
            Stat = stat,
            Category = category,
            Value = value
        });

        _api.World.Player.Entity.Stats.Set(stat, category, value);
    }

    private const string _networkChannelId = "CombatOverhaul:stats";
    private readonly IClientNetworkChannel _clientChannel;
    private readonly ICoreClientAPI _api;
    
}

public class StatsSystemServer
{
    public StatsSystemServer(ICoreServerAPI api)
    {
        _api = api;
        api.Network.RegisterChannel(_networkChannelId)
            .RegisterMessageType<StatsPacket>()
            .SetMessageHandler<StatsPacket>(HandlePacket);
    }

    private const string _networkChannelId = "CombatOverhaul:stats";
    private const string _walkSpeedStat = "walkspeed";
    private const string _mainHandCategory = "CombatOverhaul:held-item-mainhand";
    private const string _offHandCategory = "CombatOverhaul:held-item-offhand";
    private const float _minWalkSpeedModifier = -0.95f;
    private const float _maxWalkSpeedModifier = 0.0f;
    private const int _maxRejectedPacketLogs = 5;
    private readonly ICoreServerAPI _api;
    private int _rejectedPacketLogs;

    private void HandlePacket(IServerPlayer player, StatsPacket packet)
    {
        if (!TryValidate(packet, out float value))
        {
            LogRejectedPacket(player, packet);
            return;
        }

        player.Entity.Stats.Set(packet.Stat, packet.Category, value);
        OnStatSet?.Invoke(player, packet.Stat, value);
    }

    private static bool TryValidate(StatsPacket packet, out float value)
    {
        value = 0;

        if (!string.Equals(packet.Stat, _walkSpeedStat, StringComparison.Ordinal)
            || !IsAllowedCategory(packet.Category)
            || float.IsNaN(packet.Value)
            || float.IsInfinity(packet.Value))
        {
            return false;
        }

        value = Math.Clamp(packet.Value, _minWalkSpeedModifier, _maxWalkSpeedModifier);
        return true;
    }

    private static bool IsAllowedCategory(string category)
    {
        return string.Equals(category, _mainHandCategory, StringComparison.Ordinal)
            || string.Equals(category, _offHandCategory, StringComparison.Ordinal);
    }

    private void LogRejectedPacket(IServerPlayer player, StatsPacket packet)
    {
        if (_rejectedPacketLogs >= _maxRejectedPacketLogs) return;

        _rejectedPacketLogs++;
        LoggerUtil.Warn(_api, this, $"Rejected stats packet from '{player.PlayerName}' ({player.PlayerUID}): stat='{packet.Stat}', category='{packet.Category}', value={packet.Value}");
    }

    internal event Action<IServerPlayer, string, float>? OnStatSet;
}
