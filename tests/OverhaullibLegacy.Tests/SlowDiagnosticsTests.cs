using CombatOverhaul;
using CombatOverhaul.RangedSystems;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace OverhaullibLegacy.Tests;

public sealed class SlowDiagnosticsTests
{
    [Fact]
    public void Settings_DefaultSlowDiagnosticsOffWithConservativeThresholds()
    {
        Settings settings = new();

        Assert.False(settings.SlowDiagnosticsEnabled);
        Assert.Equal(50, settings.SlowDiagnosticsNetworkThresholdMs);
        Assert.Equal(10, settings.SlowDiagnosticsRangedStatusThresholdMs);
        Assert.Equal(5000, settings.SlowDiagnosticsLogCooldownMs);
    }

    [Fact]
    public void SlowLogRateLimiter_SuppressesRepeatedKeysUntilCooldownPasses()
    {
        SlowLogRateLimiter limiter = new();

        Assert.True(limiter.ShouldLog("handler-a", timestamp: 100, cooldownTicks: 50));
        Assert.False(limiter.ShouldLog("handler-a", timestamp: 149, cooldownTicks: 50));
        Assert.True(limiter.ShouldLog("handler-a", timestamp: 150, cooldownTicks: 50));
        Assert.True(limiter.ShouldLog("handler-b", timestamp: 120, cooldownTicks: 50));
        Assert.True(limiter.ShouldLog("handler-a", timestamp: 151, cooldownTicks: 0));
    }

    [Fact]
    public void DescribeCustomPacket_ResolvesRangedStatusMessageAndCapturedHandler()
    {
        NetworkChannel channel = new(null!, 7, RangedWeaponSystemServer.NetworkChannelId);
        channel.RegisterMessageType<ReloadPacket>();
        channel.RegisterMessageType<ReloadConfirmPacket>();
        channel.RegisterMessageType<ShotPacket>();
        channel.RegisterMessageType<ShotConfirmPacket>();
        channel.RegisterMessageType<RangedWeaponStatusPacket>();
        channel.SetMessageHandler<RangedWeaponStatusPacket>(HandleStatusPacket);

        Packet_CustomPacket packet = new()
        {
            ChannelId = 7,
            MessageId = 4,
            Data = [1, 2, 3]
        };

        SlowCustomPacketDetails details = SlowDiagnostics.DescribeCustomPacket(channel, packet);

        Assert.Equal(RangedWeaponSystemServer.NetworkChannelId, details.ChannelName);
        Assert.Equal(7, details.ChannelId);
        Assert.Equal(4, details.MessageId);
        Assert.Equal(3, details.PayloadBytes);
        Assert.Contains(nameof(RangedWeaponStatusPacket), details.MessageTypeName);
        Assert.Contains(nameof(SlowDiagnosticsTests), details.HandlerDescription);
        Assert.Contains(nameof(HandleStatusPacket), details.HandlerDescription);
    }

    private static void HandleStatusPacket(IServerPlayer player, RangedWeaponStatusPacket packet)
    {
    }
}
