#if DEBUG
using CombatOverhaul.Integration.Transpilers;
using ImGuiNET;
using NVector3 = System.Numerics.Vector3;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.Animations;

public sealed partial class DebugWindowManager
{
    private static readonly EnumAnimatedElement[] RigEditableParts =
    [
        EnumAnimatedElement.UpperTorso,
        EnumAnimatedElement.LowerTorso,
        EnumAnimatedElement.Neck,
        EnumAnimatedElement.Head,
        EnumAnimatedElement.UpperArmR,
        EnumAnimatedElement.LowerArmR,
        EnumAnimatedElement.ItemAnchor,
        EnumAnimatedElement.UpperArmL,
        EnumAnimatedElement.LowerArmL,
        EnumAnimatedElement.ItemAnchorL,
        EnumAnimatedElement.UpperFootR,
        EnumAnimatedElement.LowerFootR,
        EnumAnimatedElement.UpperFootL,
        EnumAnimatedElement.LowerFootL,
        EnumAnimatedElement.DetachedAnchor
    ];

    private static readonly string[] RigEditablePartNames = RigEditableParts.Select(part => part.ToString()).ToArray();

    private bool _rigPoseEditorEnabled;
    private bool _highlightSelectedRigPart = true;
    private int _rigPartIndex;
    private ModelTransform _rigGizmoTransform = CreateDefaultTransform();

    internal static bool DebugRigPoseOverrideActive { get; private set; }

    private bool DrawRigPoseEditor(string animationCode, Animation animation)
    {
        ImGui.SeparatorText("Rig pose editor");

        bool enabled = _rigPoseEditorEnabled;
        if (ImGui.Checkbox("Enable rig pose editor##rig", ref enabled))
        {
            _rigPoseEditorEnabled = enabled;
            DebugRigPoseOverrideActive = enabled;
            if (!enabled)
            {
                _detachedEditorCamera?.SetEnabled(false);
            }
        }

        ImGui.SameLine();
        ImGui.TextDisabled("Edits the selected player keyframe. Use Save to source above to persist.");

        if (!_rigPoseEditorEnabled)
        {
            DebugRigPoseOverrideActive = false;
            return false;
        }

        DebugRigPoseOverrideActive = true;
        if (animation.PlayerKeyFrames.Count == 0)
        {
            ImGui.Text("No player keyframes.");
            return true;
        }

        if (animation._playerFrameIndex >= animation.PlayerKeyFrames.Count) animation._playerFrameIndex = animation.PlayerKeyFrames.Count - 1;
        if (animation._playerFrameIndex < 0) animation._playerFrameIndex = 0;
        if (_rigPartIndex >= RigEditableParts.Length) _rigPartIndex = 0;

        ImGui.SetNextItemWidth(220);
        ImGui.SliderInt("Player keyframe##rig", ref animation._playerFrameIndex, 0, animation.PlayerKeyFrames.Count - 1);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180);
        ImGui.SliderFloat("Frame progress##rig", ref animation._frameProgress, 0, 1);
        ImGui.SetNextItemWidth(260);
        ImGui.Combo("Rig part##rig", ref _rigPartIndex, RigEditablePartNames, RigEditablePartNames.Length);
        ImGui.SameLine();
        ImGui.Checkbox("Highlight selected part##rig", ref _highlightSelectedRigPart);
        ImGui.TextDisabled("Shift-click a player body part to select it.");

        EnumAnimatedElement selectedPart = RigEditableParts[_rigPartIndex];
        PLayerKeyFrame keyFrame = animation.PlayerKeyFrames[animation._playerFrameIndex];
        AnimationElement element = GetRigElement(keyFrame.Frame, selectedPart, out bool exists);

        if (!exists)
        {
            ImGui.TextWrapped($"{selectedPart} is not present on this keyframe.");
            if (ImGui.Button("Create selected part frame##rig"))
            {
                SetRigElement(animation, selectedPart, AnimationElement.Zero);
                element = AnimationElement.Zero;
                exists = true;
            }
        }

        _detachedEditorCamera?.DrawControls("rig");

        if (!exists) return true;

        bool changed = DrawRigElementNumericEditor(selectedPart, element, out AnimationElement editedElement);
        if (changed)
        {
            SetRigElement(animation, selectedPart, editedElement);
            element = editedElement;
        }

        _rigGizmoTransform = RigElementToTransform(element);
        TryGetRigPartWorldCenter(selectedPart, out Vec3d? worldCenter);
        DrawTransformGizmoControls(
            "rig-pose",
            _rigGizmoTransform,
            TransformGizmoContext.RigPart,
            transform => ApplyRigTransform(animation, selectedPart, transform),
            worldCenter: worldCenter,
            allowScale: false);

        ImGui.TextDisabled($"Editing {animationCode} / keyframe {animation._playerFrameIndex} / {selectedPart}.");
        return true;
    }

    private bool DrawRigElementNumericEditor(EnumAnimatedElement selectedPart, AnimationElement element, out AnimationElement editedElement)
    {
        float speed = ImGui.GetIO().KeysDown[(int)ImGuiKey.LeftShift] ? 0.05f : 0.25f;
        NVector3 offset = new(element.OffsetX ?? 0, element.OffsetY ?? 0, element.OffsetZ ?? 0);
        NVector3 rotation = new(element.RotationX ?? 0, element.RotationY ?? 0, element.RotationZ ?? 0);

        bool changed = ImGui.DragFloat3($"Offset px##rig-{selectedPart}", ref offset, speed, -64, 64);
        changed |= ImGui.DragFloat3($"Rotation deg##rig-{selectedPart}", ref rotation, speed * 4, -180, 180);

        editedElement = new AnimationElement(offset.X, offset.Y, offset.Z, rotation.X, rotation.Y, rotation.Z);
        return changed;
    }

    private void ApplyRigTransform(Animation animation, EnumAnimatedElement selectedPart, ModelTransform transform)
    {
        AnimationElement element = new(
            transform.Translation.X * 16f,
            transform.Translation.Y * 16f,
            transform.Translation.Z * 16f,
            transform.Rotation.X,
            transform.Rotation.Y,
            transform.Rotation.Z);

        SetRigElement(animation, selectedPart, element);
    }

    private static ModelTransform RigElementToTransform(AnimationElement element)
    {
        ModelTransform transform = CreateDefaultTransform();
        transform.Origin.Set(0, 0, 0);
        transform.Translation.Set((element.OffsetX ?? 0) / 16f, (element.OffsetY ?? 0) / 16f, (element.OffsetZ ?? 0) / 16f);
        transform.Rotation.Set(element.RotationX ?? 0, element.RotationY ?? 0, element.RotationZ ?? 0);
        transform.ScaleXYZ.Set(1, 1, 1);
        return transform;
    }

    private void SetRigElement(Animation animation, EnumAnimatedElement selectedPart, AnimationElement element)
    {
        int index = animation._playerFrameIndex;
        if (index < 0 || index >= animation.PlayerKeyFrames.Count) return;

        PLayerKeyFrame keyFrame = animation.PlayerKeyFrames[index];
        PlayerFrame frame = SetRigElement(keyFrame.Frame, selectedPart, element);
        animation.PlayerKeyFrames[index] = new PLayerKeyFrame(frame, keyFrame.Time, keyFrame.EasingFunction, keyFrame.EasingType, keyFrame.FrameProgressRange);
        animation._playerFrameEdited = true;
    }

    private static PlayerFrame SetRigElement(PlayerFrame frame, EnumAnimatedElement selectedPart, AnimationElement element)
    {
        RightHandFrame? right = frame.RightHand;
        LeftHandFrame? left = frame.LeftHand;
        OtherPartsFrame? other = frame.OtherParts;
        AnimationElement? upperTorso = frame.UpperTorso;
        AnimationElement? lowerTorso = frame.LowerTorso;
        AnimationElement? detachedAnchor = frame.DetachedAnchorFrame;

        switch (selectedPart)
        {
            case EnumAnimatedElement.ItemAnchor:
            case EnumAnimatedElement.LowerArmR:
            case EnumAnimatedElement.UpperArmR:
                RightHandFrame r = right ?? RightHandFrame.Zero;
                right = selectedPart switch
                {
                    EnumAnimatedElement.ItemAnchor => new RightHandFrame(element, r.LowerArmR, r.UpperArmR),
                    EnumAnimatedElement.LowerArmR => new RightHandFrame(r.ItemAnchor, element, r.UpperArmR),
                    _ => new RightHandFrame(r.ItemAnchor, r.LowerArmR, element)
                };
                break;
            case EnumAnimatedElement.ItemAnchorL:
            case EnumAnimatedElement.LowerArmL:
            case EnumAnimatedElement.UpperArmL:
                LeftHandFrame l = left ?? LeftHandFrame.Zero;
                left = selectedPart switch
                {
                    EnumAnimatedElement.ItemAnchorL => new LeftHandFrame(element, l.LowerArmL, l.UpperArmL),
                    EnumAnimatedElement.LowerArmL => new LeftHandFrame(l.ItemAnchorL, element, l.UpperArmL),
                    _ => new LeftHandFrame(l.ItemAnchorL, l.LowerArmL, element)
                };
                break;
            case EnumAnimatedElement.Neck:
            case EnumAnimatedElement.Head:
            case EnumAnimatedElement.UpperFootR:
            case EnumAnimatedElement.UpperFootL:
            case EnumAnimatedElement.LowerFootR:
            case EnumAnimatedElement.LowerFootL:
                OtherPartsFrame o = other ?? OtherPartsFrame.Zero;
                other = selectedPart switch
                {
                    EnumAnimatedElement.Neck => new OtherPartsFrame(element, o.Head, o.UpperFootR, o.UpperFootL, o.LowerFootR, o.LowerFootL),
                    EnumAnimatedElement.Head => new OtherPartsFrame(o.Neck, element, o.UpperFootR, o.UpperFootL, o.LowerFootR, o.LowerFootL),
                    EnumAnimatedElement.UpperFootR => new OtherPartsFrame(o.Neck, o.Head, element, o.UpperFootL, o.LowerFootR, o.LowerFootL),
                    EnumAnimatedElement.UpperFootL => new OtherPartsFrame(o.Neck, o.Head, o.UpperFootR, element, o.LowerFootR, o.LowerFootL),
                    EnumAnimatedElement.LowerFootR => new OtherPartsFrame(o.Neck, o.Head, o.UpperFootR, o.UpperFootL, element, o.LowerFootL),
                    _ => new OtherPartsFrame(o.Neck, o.Head, o.UpperFootR, o.UpperFootL, o.LowerFootR, element)
                };
                break;
            case EnumAnimatedElement.UpperTorso:
                upperTorso = element;
                break;
            case EnumAnimatedElement.LowerTorso:
                lowerTorso = element;
                break;
            case EnumAnimatedElement.DetachedAnchor:
                detachedAnchor = element;
                break;
        }

        return new PlayerFrame(
            right,
            left,
            other,
            upperTorso,
            detachedAnchor,
            frame.DetachedAnchor,
            frame.SwitchArms,
            frame.PitchFollow,
            frame.FovMultiplier,
            frame.BobbingAmplitude,
            detachedAnchorFollow: frame.DetachedAnchorFollow,
            lowerTorso: lowerTorso);
    }

    private static AnimationElement GetRigElement(PlayerFrame frame, EnumAnimatedElement selectedPart, out bool exists)
    {
        exists = true;
        switch (selectedPart)
        {
            case EnumAnimatedElement.ItemAnchor when frame.RightHand != null:
                return frame.RightHand.Value.ItemAnchor;
            case EnumAnimatedElement.LowerArmR when frame.RightHand != null:
                return frame.RightHand.Value.LowerArmR;
            case EnumAnimatedElement.UpperArmR when frame.RightHand != null:
                return frame.RightHand.Value.UpperArmR;
            case EnumAnimatedElement.ItemAnchorL when frame.LeftHand != null:
                return frame.LeftHand.Value.ItemAnchorL;
            case EnumAnimatedElement.LowerArmL when frame.LeftHand != null:
                return frame.LeftHand.Value.LowerArmL;
            case EnumAnimatedElement.UpperArmL when frame.LeftHand != null:
                return frame.LeftHand.Value.UpperArmL;
            case EnumAnimatedElement.Neck when frame.OtherParts != null:
                return frame.OtherParts.Value.Neck;
            case EnumAnimatedElement.Head when frame.OtherParts != null:
                return frame.OtherParts.Value.Head;
            case EnumAnimatedElement.UpperFootR when frame.OtherParts != null:
                return frame.OtherParts.Value.UpperFootR;
            case EnumAnimatedElement.UpperFootL when frame.OtherParts != null:
                return frame.OtherParts.Value.UpperFootL;
            case EnumAnimatedElement.LowerFootR when frame.OtherParts != null:
                return frame.OtherParts.Value.LowerFootR;
            case EnumAnimatedElement.LowerFootL when frame.OtherParts != null:
                return frame.OtherParts.Value.LowerFootL;
            case EnumAnimatedElement.UpperTorso when frame.UpperTorso != null:
                return frame.UpperTorso.Value;
            case EnumAnimatedElement.LowerTorso when frame.LowerTorso != null:
                return frame.LowerTorso.Value;
            case EnumAnimatedElement.DetachedAnchor when frame.DetachedAnchorFrame != null:
                return frame.DetachedAnchorFrame.Value;
            default:
                exists = false;
                return AnimationElement.Zero;
        }
    }

    private bool TryGetRigPartWorldCenter(EnumAnimatedElement selectedPart, out Vec3d? center)
    {
        center = null;
        return TryGetRigPartWorldOrigin(selectedPart, out center);
    }

    private bool TryGetRigPartWorldOrigin(EnumAnimatedElement selectedPart, out Vec3d? origin)
    {
        origin = null;
        if (!TryGetRigPartPose(selectedPart, out EntityPlayer playerEntity, out ElementPose? pose)) return false;
        if (pose?.ForElement == null) return false;

        Matrixf matrix = new();
        BuildPlayerModelMatrix(matrix, playerEntity);
        matrix.Mul(pose.AnimModelMatrix);

        Vec3f localOrigin = GetElementLocalRotationOrigin(pose);
        Vec4f relative = matrix.TransformVector(new Vec4f(localOrigin.X, localOrigin.Y, localOrigin.Z, 1f));
        Vec3d camera = playerEntity.CameraPos;
        origin = new Vec3d(camera.X + relative.X, camera.Y + relative.Y, camera.Z + relative.Z);
        return true;
    }

    internal bool TryGetRigPartHighlightCorners(out Vec3d[] corners)
    {
        corners = Array.Empty<Vec3d>();
        if (!_rigPoseEditorEnabled || !_highlightSelectedRigPart) return false;
        if (_rigPartIndex < 0 || _rigPartIndex >= RigEditableParts.Length) return false;

        return TryGetRigPartWorldBox(RigEditableParts[_rigPartIndex], out corners);
    }

    internal bool TryPickRigPart(Vec3d rayOrigin, Vec3d rayDirection)
    {
        if (!_rigPoseEditorEnabled || !_showAnimationEditor) return false;

        int bestIndex = -1;
        double bestDistance = double.PositiveInfinity;

        for (int index = 0; index < RigEditableParts.Length; index++)
        {
            if (!TryGetRigPartWorldBox(RigEditableParts[index], out Vec3d[] corners)) continue;
            if (!TryIntersectRayBox(rayOrigin, rayDirection, corners, out double distance)) continue;
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            bestIndex = index;
        }

        if (bestIndex < 0) return false;

        _rigPartIndex = bestIndex;
        return true;
    }

    private bool TryGetRigPartWorldBox(EnumAnimatedElement selectedPart, out Vec3d[] corners)
    {
        corners = Array.Empty<Vec3d>();
        if (!TryGetRigPartPose(selectedPart, out EntityPlayer playerEntity, out ElementPose? pose)) return false;
        if (pose.ForElement == null) return false;

        Matrixf matrix = new();
        BuildPlayerModelMatrix(matrix, playerEntity);
        matrix.Mul(pose.AnimModelMatrix);

        Vec3f[] localCorners = GetElementLocalBoxCorners(pose.ForElement);
        corners = new Vec3d[localCorners.Length];
        Vec3d camera = playerEntity.CameraPos;

        for (int index = 0; index < localCorners.Length; index++)
        {
            Vec3f local = localCorners[index];
            Vec4f relative = matrix.TransformVector(new Vec4f(local.X, local.Y, local.Z, 1f));
            corners[index] = new Vec3d(camera.X + relative.X, camera.Y + relative.Y, camera.Z + relative.Z);
        }

        return true;
    }

    private bool TryGetRigPartPose(EnumAnimatedElement selectedPart, out EntityPlayer playerEntity, out ElementPose? pose)
    {
        playerEntity = _api.World.Player.Entity;
        pose = null;
        if (playerEntity.AnimManager?.Animator is not AnimatorBase animator || animator.RootPoses == null) return false;
        return TryFindPose(animator.RootPoses, selectedPart, out pose);
    }

    private static bool TryFindPose(IEnumerable<ElementPose> poses, EnumAnimatedElement selectedPart, out ElementPose? result)
    {
        foreach (ElementPose pose in poses)
        {
            if (PoseMatches(pose, selectedPart))
            {
                result = pose;
                return true;
            }

            if (TryFindPose(pose.ChildElementPoses, selectedPart, out result)) return true;
        }

        result = null;
        return false;
    }

    private static bool PoseMatches(ElementPose pose, EnumAnimatedElement selectedPart)
    {
        if (pose is ExtendedElementPose extendedPose && extendedPose.ElementNameEnum == selectedPart) return true;
        return Enum.TryParse(pose.ForElement?.Name, out EnumAnimatedElement parsed) && parsed == selectedPart;
    }

    private static Vec3f GetElementLocalCenter(ShapeElement element)
    {
        if (element.From == null || element.To == null || element.From.Length < 3 || element.To.Length < 3) return new Vec3f();
        return new Vec3f(
            (float)((element.To[0] - element.From[0]) / 32.0),
            (float)((element.To[1] - element.From[1]) / 32.0),
            (float)((element.To[2] - element.From[2]) / 32.0));
    }

    private static Vec3f GetElementLocalRotationOrigin(ElementPose pose)
    {
        ShapeElement element = pose.ForElement;
        if (element.From == null || element.From.Length < 3) return new Vec3f(-pose.translateX, -pose.translateY, -pose.translateZ);

        double[]? rotationOrigin = element.RotationOrigin;
        double originX = rotationOrigin != null && rotationOrigin.Length > 0 ? rotationOrigin[0] : 0;
        double originY = rotationOrigin != null && rotationOrigin.Length > 1 ? rotationOrigin[1] : 0;
        double originZ = rotationOrigin != null && rotationOrigin.Length > 2 ? rotationOrigin[2] : 0;

        return new Vec3f(
            (float)((originX - element.From[0]) / 16.0 - pose.translateX),
            (float)((originY - element.From[1]) / 16.0 - pose.translateY),
            (float)((originZ - element.From[2]) / 16.0 - pose.translateZ));
    }

    private static Vec3f[] GetElementLocalBoxCorners(ShapeElement element)
    {
        Vec3f center = GetElementLocalCenter(element);
        float halfX = 0.12f;
        float halfY = 0.12f;
        float halfZ = 0.12f;

        if (element.From != null && element.To != null && element.From.Length >= 3 && element.To.Length >= 3)
        {
            halfX = Math.Max(0.08f, (float)Math.Abs(element.To[0] - element.From[0]) / 32f);
            halfY = Math.Max(0.08f, (float)Math.Abs(element.To[1] - element.From[1]) / 32f);
            halfZ = Math.Max(0.08f, (float)Math.Abs(element.To[2] - element.From[2]) / 32f);
        }

        const float padding = 0.035f;
        halfX += padding;
        halfY += padding;
        halfZ += padding;

        float minX = center.X - halfX;
        float minY = center.Y - halfY;
        float minZ = center.Z - halfZ;
        float maxX = center.X + halfX;
        float maxY = center.Y + halfY;
        float maxZ = center.Z + halfZ;

        return
        [
            new Vec3f(minX, minY, minZ),
            new Vec3f(maxX, minY, minZ),
            new Vec3f(maxX, maxY, minZ),
            new Vec3f(minX, maxY, minZ),
            new Vec3f(minX, minY, maxZ),
            new Vec3f(maxX, minY, maxZ),
            new Vec3f(maxX, maxY, maxZ),
            new Vec3f(minX, maxY, maxZ)
        ];
    }

    private static bool TryIntersectRayBox(Vec3d origin, Vec3d direction, Vec3d[] corners, out double distance)
    {
        distance = double.PositiveInfinity;
        if (corners.Length < 8) return false;

        ReadOnlySpan<(int A, int B, int C)> triangles =
        [
            (0, 1, 2), (0, 2, 3),
            (4, 6, 5), (4, 7, 6),
            (0, 4, 5), (0, 5, 1),
            (1, 5, 6), (1, 6, 2),
            (2, 6, 7), (2, 7, 3),
            (3, 7, 4), (3, 4, 0)
        ];

        bool hit = false;
        foreach ((int a, int b, int c) in triangles)
        {
            if (!TryIntersectRayTriangle(origin, direction, corners[a], corners[b], corners[c], out double triangleDistance)) continue;
            if (triangleDistance >= distance) continue;

            distance = triangleDistance;
            hit = true;
        }

        return hit;
    }

    private static bool TryIntersectRayTriangle(Vec3d origin, Vec3d direction, Vec3d a, Vec3d b, Vec3d c, out double distance)
    {
        distance = 0;
        const double epsilon = 0.0000001;
        Vec3d edge1 = Sub(b, a);
        Vec3d edge2 = Sub(c, a);
        Vec3d h = direction.Cross(edge2);
        double det = Dot(edge1, h);
        if (det > -epsilon && det < epsilon) return false;

        double invDet = 1.0 / det;
        Vec3d s = Sub(origin, a);
        double u = invDet * Dot(s, h);
        if (u < 0 || u > 1) return false;

        Vec3d q = s.Cross(edge1);
        double v = invDet * Dot(direction, q);
        if (v < 0 || u + v > 1) return false;

        distance = invDet * Dot(edge2, q);
        return distance >= 0;
    }

    private static double Dot(Vec3d left, Vec3d right) => left.X * right.X + left.Y * right.Y + left.Z * right.Z;
    private static Vec3d Sub(Vec3d left, Vec3d right) => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static void BuildPlayerModelMatrix(Matrixf matrix, EntityPlayer playerEntity)
    {
        matrix.Identity();
        Vec3d camera = playerEntity.CameraPos;
        matrix.Translate(playerEntity.Pos.X - camera.X, playerEntity.Pos.InternalY - camera.Y, playerEntity.Pos.Z - camera.Z);

        float rotX = playerEntity.Properties.Client.Shape?.rotateX ?? 0;
        float rotY = playerEntity.Properties.Client.Shape?.rotateY ?? 0;
        float rotZ = playerEntity.Properties.Client.Shape?.rotateZ ?? 0;

        matrix.Translate(0, playerEntity.SelectionBox.Y2 / 2f, 0);
        matrix.RotateX(playerEntity.Pos.Roll + rotX * GameMath.DEG2RAD);
        matrix.RotateY(playerEntity.BodyYaw + (90f + rotY) * GameMath.DEG2RAD);
        matrix.RotateZ(playerEntity.WalkPitch + rotZ * GameMath.DEG2RAD);
        matrix.Translate(0, -playerEntity.SelectionBox.Y2 / 2f, 0);

        float size = playerEntity.Properties.Client.Size;
        matrix.Scale(size, size, size);
        matrix.Translate(-0.5f, 0, -0.5f);
    }

    private void OnDebugEditorClosed()
    {
        SetEditorFrameOverride(null);
        _detachedEditorCamera?.SetEnabled(false);
        _rigPoseEditorEnabled = false;
        DebugPoseFreezeActive = false;
        DebugRigPoseOverrideActive = false;
    }
}
#endif
