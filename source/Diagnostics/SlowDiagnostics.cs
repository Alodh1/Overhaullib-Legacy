using CombatOverhaul.RangedSystems;
using CombatOverhaul.Utils;
using HarmonyLib;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;

namespace CombatOverhaul;

internal static class SlowDiagnostics
{
    private static readonly FieldInfo? MessageTypesField = typeof(NetworkChannelBase).GetField("messageTypes", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? HandlersField = typeof(NetworkChannel).GetField("handlers", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly SlowLogRateLimiter RateLimiter = new();

    private static ICoreAPI? Api;
    private static string? HarmonyId;
    private static bool NetworkPatched;
    private static long NetworkThresholdTicks = MillisecondsToStopwatchTicks(50);
    private static long RangedStatusThresholdTicks = MillisecondsToStopwatchTicks(10);
    private static long LogCooldownTicks = MillisecondsToStopwatchTicks(5000);

    internal static bool Enabled { get; private set; }

    internal static event Action<string>? DiagnosticLogWritten;

    internal static void Configure(ICoreAPI api, Settings settings, string harmonyId)
    {
        if (api.Side != EnumAppSide.Server) return;

        Api = api;
        HarmonyId = harmonyId;
        NetworkThresholdTicks = MillisecondsToStopwatchTicks(settings.SlowDiagnosticsNetworkThresholdMs);
        RangedStatusThresholdTicks = MillisecondsToStopwatchTicks(settings.SlowDiagnosticsRangedStatusThresholdMs);
        LogCooldownTicks = MillisecondsToStopwatchTicks(settings.SlowDiagnosticsLogCooldownMs);

        if (settings.SlowDiagnosticsEnabled)
        {
            Enabled = true;
            PatchNetwork(harmonyId);
        }
        else
        {
            Enabled = false;
            UnpatchNetwork(harmonyId);
        }
    }

    internal static void Shutdown(string harmonyId)
    {
        Enabled = false;
        UnpatchNetwork(harmonyId);
        Api = null;
        HarmonyId = null;
        RateLimiter.Clear();
    }

    internal static void InvokeRangedWeaponStatusSubscribers(
        RangedWeaponSystemServer.RangedWeaponStatusChangedDelegate? subscribers,
        IServerPlayer player,
        ItemSlot weaponSlot,
        RangedWeaponStatus status,
        bool mainHand)
    {
        if (subscribers == null) return;

        foreach (RangedWeaponSystemServer.RangedWeaponStatusChangedDelegate subscriber in subscribers.GetInvocationList())
        {
            long startedAt = Stopwatch.GetTimestamp();
            try
            {
                subscriber(player.Entity, weaponSlot, status);
            }
            finally
            {
                RecordRangedStatusSubscriber(subscriber, player, weaponSlot, status, mainHand, startedAt);
            }
        }
    }

    internal static SlowCustomPacketDetails DescribeCustomPacket(NetworkChannel channel, Packet_CustomPacket packet)
    {
        return new SlowCustomPacketDetails(
            channel.ChannelName,
            packet.ChannelId,
            packet.MessageId,
            ResolveMessageTypeName(channel, packet.MessageId),
            packet.Data?.Length ?? 0,
            ResolveHandlerDescription(channel, packet.MessageId));
    }

    private static void PatchNetwork(string harmonyId)
    {
        if (NetworkPatched) return;

        MethodInfo? target = AccessTools.Method(typeof(NetworkChannel), nameof(NetworkChannel.OnPacket));
        MethodInfo? prefix = AccessTools.Method(typeof(SlowDiagnostics), nameof(NetworkPacketPrefix));
        MethodInfo? finalizer = AccessTools.Method(typeof(SlowDiagnostics), nameof(NetworkPacketFinalizer));
        if (target == null || prefix == null || finalizer == null)
        {
            LoggerUtil.Warn(Api, typeof(SlowDiagnostics), "Slow diagnostics could not find NetworkChannel.OnPacket patch methods.");
            return;
        }

        try
        {
            new Harmony(harmonyId).Patch(
                target,
                prefix: new HarmonyMethod(prefix),
                finalizer: new HarmonyMethod(finalizer));
            NetworkPatched = true;
        }
        catch (Exception exception)
        {
            LoggerUtil.Warn(Api, typeof(SlowDiagnostics), $"Slow diagnostics failed to patch NetworkChannel.OnPacket:\n{exception}");
        }
    }

    private static void UnpatchNetwork(string harmonyId)
    {
        if (!NetworkPatched) return;

        MethodInfo? target = AccessTools.Method(typeof(NetworkChannel), nameof(NetworkChannel.OnPacket));
        if (target != null)
        {
            Harmony harmony = new(harmonyId);
            harmony.Unpatch(target, HarmonyPatchType.Prefix, harmonyId);
            harmony.Unpatch(target, HarmonyPatchType.Finalizer, harmonyId);
        }

        NetworkPatched = false;
    }

    private static void NetworkPacketPrefix(out long __state)
    {
        __state = Enabled ? Stopwatch.GetTimestamp() : 0;
    }

    private static Exception? NetworkPacketFinalizer(NetworkChannel __instance, Packet_CustomPacket __0, IServerPlayer __1, long __state, Exception? __exception)
    {
        if (__state != 0)
        {
            RecordCustomPacket(__instance, __0, __1, __state, __exception);
        }

        return __exception;
    }

    private static void RecordCustomPacket(NetworkChannel channel, Packet_CustomPacket packet, IServerPlayer player, long startedAt, Exception? exception)
    {
        long elapsedTicks = Stopwatch.GetTimestamp() - startedAt;
        if (elapsedTicks < NetworkThresholdTicks) return;

        SlowCustomPacketDetails details = DescribeCustomPacket(channel, packet);
        string key = $"custom:{details.ChannelId}:{details.MessageId}:{details.HandlerDescription}";
        if (!RateLimiter.ShouldLog(key, Stopwatch.GetTimestamp(), LogCooldownTicks)) return;

        string exceptionText = exception == null ? "" : $" exception={exception.GetType().FullName}: {exception.Message}";
        LogVerbose(
            $"Slow custom packet {StopwatchTicksToMilliseconds(elapsedTicks):F2} ms " +
            $"channel='{details.ChannelName}' channelId={details.ChannelId} messageId={details.MessageId} " +
            $"messageType='{details.MessageTypeName}' payloadBytes={details.PayloadBytes} " +
            $"player='{FormatPlayer(player)}' handler='{details.HandlerDescription}'{exceptionText}");
    }

    private static void RecordRangedStatusSubscriber(
        RangedWeaponSystemServer.RangedWeaponStatusChangedDelegate subscriber,
        IServerPlayer player,
        ItemSlot weaponSlot,
        RangedWeaponStatus status,
        bool mainHand,
        long startedAt)
    {
        long elapsedTicks = Stopwatch.GetTimestamp() - startedAt;
        if (elapsedTicks < RangedStatusThresholdTicks) return;

        string subscriberDescription = DescribeDelegate(subscriber);
        string key = $"ranged-status:{subscriberDescription}";
        if (!RateLimiter.ShouldLog(key, Stopwatch.GetTimestamp(), LogCooldownTicks)) return;

        string itemCode = weaponSlot.Itemstack?.Collectible?.Code?.ToString() ?? "<empty>";
        LogVerbose(
            $"Slow ranged status subscriber {StopwatchTicksToMilliseconds(elapsedTicks):F2} ms " +
            $"status={status} hand={(mainHand ? "main" : "off")} item='{itemCode}' " +
            $"player='{FormatPlayer(player)}' subscriber='{subscriberDescription}'");
    }

    private static string ResolveMessageTypeName(NetworkChannel channel, int messageId)
    {
        if (MessageTypesField?.GetValue(channel) is Dictionary<Type, int> messageTypes)
        {
            foreach ((Type type, int id) in messageTypes)
            {
                if (id == messageId) return type.FullName ?? type.Name;
            }
        }

        return "<unknown>";
    }

    private static string ResolveHandlerDescription(NetworkChannel channel, int messageId)
    {
        if (HandlersField?.GetValue(channel) is Action<Packet_CustomPacket, IServerPlayer>[] handlers
            && messageId >= 0
            && messageId < handlers.Length)
        {
            return DescribeDelegate(UnwrapCapturedHandler(handlers[messageId]));
        }

        return "<none>";
    }

    private static Delegate? UnwrapCapturedHandler(Delegate? handler)
    {
        if (handler?.Target == null) return handler;

        Type targetType = handler.Target.GetType();
        foreach (FieldInfo field in targetType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!typeof(Delegate).IsAssignableFrom(field.FieldType)) continue;
            if (field.GetValue(handler.Target) is Delegate captured && !ReferenceEquals(captured, handler))
            {
                return captured;
            }
        }

        return handler;
    }

    private static string DescribeDelegate(Delegate? handler)
    {
        if (handler == null) return "<none>";

        MethodInfo method = handler.Method;
        Type? declaringType = method.DeclaringType ?? handler.Target?.GetType();
        string assemblyName = declaringType?.Assembly.GetName().Name ?? method.Module.Assembly.GetName().Name ?? "<unknown>";
        string typeName = declaringType?.FullName ?? "<unknown>";
        return $"{assemblyName}:{typeName}.{method.Name}";
    }

    private static string FormatPlayer(IServerPlayer player)
    {
        string name = string.IsNullOrEmpty(player.PlayerName) ? "<unknown>" : player.PlayerName;
        return $"{name}/{player.PlayerUID}";
    }

    private static void LogVerbose(string message)
    {
        DiagnosticLogWritten?.Invoke(message);
        LoggerUtil.Verbose(Api, typeof(SlowDiagnostics), message);
    }

    private static long MillisecondsToStopwatchTicks(int milliseconds)
    {
        if (milliseconds <= 0) return 0;

        return (long)Math.Ceiling(milliseconds * (double)Stopwatch.Frequency / 1000d);
    }

    private static double StopwatchTicksToMilliseconds(long ticks)
    {
        return ticks * 1000d / Stopwatch.Frequency;
    }
}

internal readonly record struct SlowCustomPacketDetails(
    string ChannelName,
    int ChannelId,
    int MessageId,
    string MessageTypeName,
    int PayloadBytes,
    string HandlerDescription);

internal sealed class SlowLogRateLimiter
{
    private readonly Dictionary<string, long> _lastLogTimestampByKey = [];

    public bool ShouldLog(string key, long timestamp, long cooldownTicks)
    {
        if (cooldownTicks <= 0)
        {
            _lastLogTimestampByKey[key] = timestamp;
            return true;
        }

        if (_lastLogTimestampByKey.TryGetValue(key, out long lastTimestamp)
            && timestamp - lastTimestamp < cooldownTicks)
        {
            return false;
        }

        _lastLogTimestampByKey[key] = timestamp;
        return true;
    }

    public void Clear()
    {
        _lastLogTimestampByKey.Clear();
    }
}
