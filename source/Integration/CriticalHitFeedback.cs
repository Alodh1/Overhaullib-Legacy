using ProtoBuf;
using CombatOverhaul.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CombatOverhaul.Integration;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class CriticalHitFeedbackPacket
{
    public float Intensity { get; set; } = 1f;
}

internal static class CriticalHitFeedback
{
    private const string NetworkChannelId = "CombatOverhaul:critical-hit-feedback";

    public static void Register(ICoreAPI api)
    {
        if (api is ICoreClientAPI clientApi && _clientChannel == null)
        {
            _clientApi = clientApi;
            _renderer = new(clientApi);
            clientApi.Event.RegisterRenderer(_renderer, EnumRenderStage.Ortho, "critical-hit-feedback");
            _clientChannel = clientApi.Network.RegisterChannel(NetworkChannelId)
                .RegisterMessageType<CriticalHitFeedbackPacket>()
                .SetMessageHandler<CriticalHitFeedbackPacket>(HandlePacket);
        }

        if (api is ICoreServerAPI serverApi && _serverChannel == null)
        {
            _serverChannel = serverApi.Network.RegisterChannel(NetworkChannelId)
                .RegisterMessageType<CriticalHitFeedbackPacket>();
        }
    }

    public static void Trigger(DamageSource? damageSource)
    {
        Entity? source = damageSource?.CauseEntity ?? damageSource?.SourceEntity;
        if (source is not EntityPlayer entityPlayer || entityPlayer.Player is not IServerPlayer player) return;

        _serverChannel?.SendPacket(new CriticalHitFeedbackPacket(), player);
    }

    public static void Dispose()
    {
        ICoreClientAPI? clientApi = _clientApi;
        CriticalHitFeedbackRenderer? renderer = _renderer;

        _renderer = null;
        _clientApi = null;
        _clientChannel = null;
        _serverChannel = null;

        ClientThreadCleanup.DisposeRenderer(clientApi, renderer, EnumRenderStage.Ortho, "critical-hit-feedback-dispose");
    }

    private static void HandlePacket(CriticalHitFeedbackPacket packet)
    {
        _renderer?.Pulse(Math.Clamp(packet.Intensity, 0.5f, 2f));
    }

    private static ICoreClientAPI? _clientApi;
    private static IClientNetworkChannel? _clientChannel;
    private static IServerNetworkChannel? _serverChannel;
    private static CriticalHitFeedbackRenderer? _renderer;
}

internal sealed class CriticalHitFeedbackRenderer : IRenderer
{
    private const float PulseDurationSeconds = 0.4f;

    public double RenderOrder => 1.02;
    public int RenderRange => 9999;

    public CriticalHitFeedbackRenderer(ICoreClientAPI api)
    {
        _api = api;
    }

    public void Pulse(float intensity)
    {
        _elapsedSeconds = 0f;
        _intensity = intensity;
        _active = true;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!_active) return;

        _elapsedSeconds += deltaTime;
        if (_elapsedSeconds >= PulseDurationSeconds)
        {
            _active = false;
            return;
        }

        float progress = Math.Clamp(_elapsedSeconds / PulseDurationSeconds, 0f, 1f);
        float fade = 1f - progress;
        float pulse = MathF.Sin(progress * MathF.PI);

        float scale = RuntimeEnv.GUIScale * (1f + 0.25f * pulse * _intensity);
        float centerX = _api.Render.FrameWidth / 2f;
        float centerY = _api.Render.FrameHeight / 2f;
        float halfSize = 14f * scale;
        float thickness = MathF.Max(3f, 4.5f * scale);

        int glowColor = ColorUtil.ColorFromRgba(255, 136, 0, (int)(115f * fade));
        int goldColor = ColorUtil.ColorFromRgba(255, 220, 58, (int)(255f * fade));
        int highlightColor = ColorUtil.ColorFromRgba(255, 248, 170, (int)(180f * fade));

        DrawX(centerX, centerY, halfSize + 3f * scale, thickness + 4f * scale, 10049f, glowColor);
        DrawX(centerX, centerY, halfSize, thickness, 10050f, goldColor);
        DrawX(centerX, centerY, halfSize * 0.58f, MathF.Max(2f, thickness * 0.45f), 10051f, highlightColor);
    }

    private void DrawX(float centerX, float centerY, float halfSize, float thickness, float z, int color)
    {
        int steps = Math.Max(7, (int)MathF.Ceiling(halfSize * 2f / MathF.Max(1f, thickness * 0.75f)));
        float spacing = halfSize * 2f / (steps - 1);
        float squareSize = thickness;

        for (int index = 0; index < steps; index++)
        {
            float offset = -halfSize + spacing * index;
            DrawSquare(centerX + offset, centerY + offset, squareSize, z, color);
            DrawSquare(centerX + offset, centerY - offset, squareSize, z, color);
        }
    }

    private void DrawSquare(float centerX, float centerY, float size, float z, int color)
    {
        _api.Render.RenderRectangle(centerX - size / 2f, centerY - size / 2f, z, size, size, color);
    }

    public void Dispose()
    {
    }

    private readonly ICoreClientAPI _api;
    private float _elapsedSeconds;
    private float _intensity = 1f;
    private bool _active;
}
