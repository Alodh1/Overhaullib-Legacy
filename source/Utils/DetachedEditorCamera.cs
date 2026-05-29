#if DEBUG
using ImGuiNET;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace CombatOverhaul.Animations;

internal sealed class DetachedEditorCamera : IRenderer
{
    private readonly ICoreClientAPI _api;
    private bool _initialized;
    private double _yaw;
    private double _pitch = -0.15;
    private double _distance = 4.0;
    private Vec3d _targetOffset = new();
    private float _moveSpeed = 2.5f;
    private float _orbitSensitivity = 0.006f;

    public bool Enabled { get; private set; }
    public double RenderOrder => 0.01;
    public int RenderRange => 9999;

    public DetachedEditorCamera(ICoreClientAPI api)
    {
        _api = api;
        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "overhaullib-detached-editor-camera");
    }

    public void Update(float deltaSeconds, bool editorOpen)
    {
        if (!editorOpen)
        {
            SetEnabled(false);
            return;
        }

        if (!Enabled) return;

        EnsureInitialized();
        UpdateControls(deltaSeconds);
    }

    public void DrawControls(string id)
    {
        bool enabled = Enabled;
        if (ImGui.Checkbox($"Detached camera##{id}", ref enabled))
        {
            SetEnabled(enabled);
        }

        ImGui.SameLine();
        if (ImGui.Button($"Focus player##{id}")) FocusPlayer();
        ImGui.SameLine();
        if (ImGui.Button($"Reset camera##{id}")) ResetCamera();

        float distance = (float)_distance;
        ImGui.SetNextItemWidth(140);
        if (ImGui.SliderFloat($"Distance##{id}", ref distance, 0.75f, 12f)) _distance = distance;

        ImGui.SameLine();
        ImGui.SetNextItemWidth(140);
        ImGui.SliderFloat($"Move speed##{id}", ref _moveSpeed, 0.25f, 12f);

        ImGui.TextDisabled("RMB drag orbits. Mouse wheel zooms. WASD pans, Q/E moves down/up. Shift speeds up.");
    }

    public void SetEnabled(bool enabled)
    {
        if (Enabled == enabled) return;

        Enabled = enabled;
        if (enabled)
        {
            EnsureInitialized(force: true);
        }
    }

    public void FocusPlayer()
    {
        _targetOffset.Set(0, 0, 0);
        EnsureInitialized(force: true);
    }

    public void ResetCamera()
    {
        _targetOffset.Set(0, 0, 0);
        _distance = 4.0;
        _pitch = -0.15;
        _yaw = _api.World.Player.CameraYaw;
        _initialized = true;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.Before || !Enabled) return;
        OverrideCamera();
    }

    private void EnsureInitialized(bool force = false)
    {
        if (_initialized && !force) return;

        _yaw = _api.World.Player.CameraYaw;
        _pitch = Math.Clamp(_api.World.Player.CameraPitch, -1.25, 1.25);
        if (Math.Abs(_pitch) < 0.01) _pitch = -0.15;
        _distance = Math.Clamp(_distance, 0.75, 12.0);
        _initialized = true;
    }

    private void UpdateControls(float deltaSeconds)
    {
        ImGuiIOPtr io = ImGui.GetIO();

        if (!io.WantCaptureMouse && io.MouseDown[1])
        {
            _yaw -= io.MouseDelta.X * _orbitSensitivity;
            _pitch = Math.Clamp(_pitch - io.MouseDelta.Y * _orbitSensitivity, -1.35, 1.35);
        }

        if (!io.WantCaptureMouse && Math.Abs(io.MouseWheel) > 0.001f)
        {
            _distance = Math.Clamp(_distance - io.MouseWheel * 0.35, 0.75, 12.0);
        }

        if (io.WantTextInput) return;

        GetBasis(out Vec3d forward, out Vec3d right, out Vec3d up);
        Vec3d move = new();
        if (ImGui.IsKeyDown(ImGuiKey.W)) move.Add(forward);
        if (ImGui.IsKeyDown(ImGuiKey.S)) move.Sub(forward);
        if (ImGui.IsKeyDown(ImGuiKey.D)) move.Add(right);
        if (ImGui.IsKeyDown(ImGuiKey.A)) move.Sub(right);
        if (ImGui.IsKeyDown(ImGuiKey.E)) move.Add(up);
        if (ImGui.IsKeyDown(ImGuiKey.Q)) move.Sub(up);

        if (move.LengthSq() < 0.000001) return;

        move.Normalize();
        double speed = _moveSpeed * (ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift) ? 3.0 : 1.0);
        _targetOffset.Add(move.Mul(speed * deltaSeconds));
    }

    private void OverrideCamera()
    {
        if (_api.World is not ClientMain client || client.MainCamera == null) return;

        GetCameraPoints(out Vec3d cameraPos, out Vec3d targetPos);
        double[] view = Mat4d.Create();
        Mat4d.LookAt(view, cameraPos.ToDoubleArray(), targetPos.ToDoubleArray(), new double[] { 0, 1, 0 });

        PlayerCamera camera = client.MainCamera;
        camera.CameraEyePos.Set(cameraPos);
        camera.CamSourcePosition.Set(cameraPos);
        camera.OriginPosition.Set(0, 0, 0);
        camera.Yaw = _yaw;
        camera.Pitch = _pitch;
        camera.CameraMatrix = view;
        camera.CameraMatrixOrigin = (double[])view.Clone();

        if (camera.CameraMatrixOriginf == null || camera.CameraMatrixOriginf.Length != 16)
        {
            camera.CameraMatrixOriginf = Mat4f.Create();
        }

        for (int i = 0; i < 16; i++) camera.CameraMatrixOriginf[i] = (float)view[i];
    }

    private void GetCameraPoints(out Vec3d cameraPos, out Vec3d targetPos)
    {
        EntityPlayer player = _api.World.Player.Entity;
        targetPos = new Vec3d(player.Pos.X, player.Pos.InternalY + player.SelectionBox.Y2 * 0.55, player.Pos.Z).Add(_targetOffset);
        GetBasis(out Vec3d forward, out _, out _);
        cameraPos = targetPos.SubCopy(forward.Mul(_distance));
    }

    private void GetBasis(out Vec3d forward, out Vec3d right, out Vec3d up)
    {
        forward = new Vec3d(-Math.Sin(_yaw) * Math.Cos(_pitch), Math.Sin(_pitch), -Math.Cos(_yaw) * Math.Cos(_pitch)).Normalize();
        right = new Vec3d(Math.Cos(_yaw), 0, -Math.Sin(_yaw)).Normalize();
        up = right.Cross(forward).Normalize();
        if (up.LengthSq() < 0.0001) up = new Vec3d(0, 1, 0);
    }

    public void Dispose()
    {
        _api.Event.UnregisterRenderer(this, EnumRenderStage.Before);
    }
}
#endif
