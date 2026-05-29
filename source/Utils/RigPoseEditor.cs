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
    private bool _rigIkFollowParents;
    private bool _rigIkDragActive;
    private int _rigIkDragKeyframeIndex = -1;
    private EnumAnimatedElement _rigIkDragPart = EnumAnimatedElement.Unknown;
    private PlayerFrame _rigIkDragStartFrame = PlayerFrame.Zero;
    private bool _rigPartClipboardHasValue;
    private EnumAnimatedElement _rigPartClipboardPart = EnumAnimatedElement.Unknown;
    private AnimationElement _rigPartClipboard = AnimationElement.Zero;
    private bool _rigFullPoseClipboardHasValue;
    private PlayerFrame _rigFullPoseClipboard = PlayerFrame.Zero;
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
        ImGui.SameLine();
        ImGui.Checkbox("IK follow parents##rig", ref _rigIkFollowParents);
        ImGui.TextDisabled("Shift-click a player body part to select it.");
        if (_rigIkFollowParents)
        {
            ImGui.TextDisabled("IK affects Move gizmo drags on child parts; Rotate stays FK.");
        }

        EnumAnimatedElement selectedPart = RigEditableParts[_rigPartIndex];
        PLayerKeyFrame keyFrame = animation.PlayerKeyFrames[animation._playerFrameIndex];
        AnimationElement element = GetRigElement(keyFrame.Frame, selectedPart, out bool exists);

        if (!exists)
        {
            ImGui.TextWrapped($"{selectedPart} is not present on this keyframe.");
            if (ImGui.Button("Create selected part frame##rig"))
            {
                _animationHistory.BeginEdit(animationCode, animation, $"Create {selectedPart} frame");
                SetRigElement(animation, selectedPart, AnimationElement.Zero);
                _animationHistory.CommitEdit(animationCode, animation);
                _animationHistoryExplicitEditThisFrame = true;
                element = AnimationElement.Zero;
                exists = true;
            }
        }

        _detachedEditorCamera?.DrawControls("rig");
        DrawRigPoseTools(animationCode, animation, selectedPart, ref element, ref exists);

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
            allowScale: false,
            dragStarted: () => BeginRigGizmoDrag(animationCode, animation, selectedPart),
            dragEnded: () => EndRigGizmoDrag(animationCode, animation));

        ImGui.TextDisabled($"Editing {animationCode} / keyframe {animation._playerFrameIndex} / {selectedPart}.");
        return true;
    }

    private void DrawRigPoseTools(string animationCode, Animation animation, EnumAnimatedElement selectedPart, ref AnimationElement element, ref bool exists)
    {
        ImGui.SeparatorText("Pose tools");

        if (!exists) ImGui.BeginDisabled();
        if (ImGui.Button("Copy part##rig-pose-tools"))
        {
            _rigPartClipboard = element;
            _rigPartClipboardPart = selectedPart;
            _rigPartClipboardHasValue = true;
        }
        if (!exists) ImGui.EndDisabled();

        ImGui.SameLine();
        if (!_rigPartClipboardHasValue) ImGui.BeginDisabled();
        if (ImGui.Button("Paste part##rig-pose-tools"))
        {
            ApplyRigPoseAction(animationCode, animation, "Paste rig part", () => SetRigElement(animation, selectedPart, _rigPartClipboard));
            element = _rigPartClipboard;
            exists = true;
        }
        if (!_rigPartClipboardHasValue) ImGui.EndDisabled();

        ImGui.SameLine();
        if (!exists) ImGui.BeginDisabled();
        if (ImGui.Button("Reset part##rig-pose-tools"))
        {
            ApplyRigPoseAction(animationCode, animation, $"Reset {selectedPart}", () => SetRigElement(animation, selectedPart, AnimationElement.Zero));
            element = AnimationElement.Zero;
            exists = true;
        }
        if (!exists) ImGui.EndDisabled();

        ImGui.SameLine();
        EnumAnimatedElement oppositePart = EnumAnimatedElement.Unknown;
        bool canMirrorPart = exists && TryGetOppositeRigPart(selectedPart, out oppositePart);
        if (!canMirrorPart) ImGui.BeginDisabled();
        if (ImGui.Button("Mirror to opposite side##rig-pose-tools"))
        {
            AnimationElement mirrored = MirrorRigElement(element);
            ApplyRigPoseAction(animationCode, animation, $"Mirror {selectedPart} to {oppositePart}", () => SetRigElement(animation, oppositePart, mirrored));
        }
        if (!canMirrorPart) ImGui.EndDisabled();

        if (_rigPartClipboardHasValue)
        {
            ImGui.TextDisabled($"Part clipboard: {_rigPartClipboardPart}");
        }
        else
        {
            ImGui.TextDisabled("Part clipboard: empty");
        }

        if (ImGui.Button("Copy full pose##rig-pose-tools"))
        {
            _rigFullPoseClipboard = animation.PlayerKeyFrames[animation._playerFrameIndex].Frame;
            _rigFullPoseClipboardHasValue = true;
        }

        ImGui.SameLine();
        if (!_rigFullPoseClipboardHasValue) ImGui.BeginDisabled();
        if (ImGui.Button("Paste full pose##rig-pose-tools"))
        {
            ApplyRigPoseAction(animationCode, animation, "Paste full pose", () => SetCurrentRigFrame(animation, _rigFullPoseClipboard));
            element = GetRigElement(animation.PlayerKeyFrames[animation._playerFrameIndex].Frame, selectedPart, out exists);
        }
        if (!_rigFullPoseClipboardHasValue) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Mirror full pose##rig-pose-tools"))
        {
            ApplyRigPoseAction(animationCode, animation, "Mirror full pose", () => SetCurrentRigFrame(animation, MirrorRigFrame(animation.PlayerKeyFrames[animation._playerFrameIndex].Frame)));
            element = GetRigElement(animation.PlayerKeyFrames[animation._playerFrameIndex].Frame, selectedPart, out exists);
        }

        ImGui.TextDisabled(_rigFullPoseClipboardHasValue ? "Full pose clipboard: populated" : "Full pose clipboard: empty");
    }

    private void ApplyRigPoseAction(string animationCode, Animation animation, string label, Action action)
    {
        _animationHistory.BeginEdit(animationCode, animation, label);
        action();
        _animationHistory.CommitEdit(animationCode, animation);
        _animationHistoryExplicitEditThisFrame = true;
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

        if (_rigIkFollowParents && GizmoMode == TransformGizmoMode.Move && ApplyRigIkMove(animation, selectedPart, element)) return;

        SetRigElement(animation, selectedPart, element);
    }

    private void BeginRigGizmoDrag(string animationCode, Animation animation, EnumAnimatedElement selectedPart)
    {
        int index = animation._playerFrameIndex;
        if (index < 0 || index >= animation.PlayerKeyFrames.Count)
        {
            ClearRigGizmoDrag();
            return;
        }

        if (!string.IsNullOrEmpty(animationCode))
        {
            _animationHistory.BeginEdit(animationCode, animation, $"Rig gizmo {selectedPart}");
            _animationHistoryExternalDragActive = true;
        }
        _rigIkDragActive = true;
        _rigIkDragKeyframeIndex = index;
        _rigIkDragPart = selectedPart;
        _rigIkDragStartFrame = animation.PlayerKeyFrames[index].Frame;
    }

    private void EndRigGizmoDrag(string animationCode, Animation animation)
    {
        _animationHistoryExternalDragActive = false;
        if (!string.IsNullOrEmpty(animationCode))
        {
            _animationHistory.CommitEdit(animationCode, animation);
            _animationHistoryExplicitEditThisFrame = true;
        }
        ClearRigGizmoDrag();
    }

    private void ClearRigGizmoDrag()
    {
        _animationHistoryExternalDragActive = false;
        _rigIkDragActive = false;
        _rigIkDragKeyframeIndex = -1;
        _rigIkDragPart = EnumAnimatedElement.Unknown;
        _rigIkDragStartFrame = PlayerFrame.Zero;
    }

    private bool ApplyRigIkMove(Animation animation, EnumAnimatedElement selectedPart, AnimationElement desiredElement)
    {
        if (!TryGetIkFollowRoot(selectedPart, out EnumAnimatedElement rootPart)) return false;

        int index = animation._playerFrameIndex;
        if (!_rigIkDragActive || _rigIkDragKeyframeIndex != index || _rigIkDragPart != selectedPart)
        {
            BeginRigGizmoDrag("", animation, selectedPart);
        }

        if (!_rigIkDragActive || index < 0 || index >= animation.PlayerKeyFrames.Count) return false;

        AnimationElement startSelected = GetRigElement(_rigIkDragStartFrame, selectedPart, out bool selectedExists);
        if (!selectedExists) return false;

        AnimationElement startRoot = GetRigElement(_rigIkDragStartFrame, rootPart, out bool rootExists);
        if (!rootExists) startRoot = AnimationElement.Zero;

        AnimationElement solvedRoot = TranslateElementBy(startRoot, desiredElement, startSelected);
        PLayerKeyFrame keyFrame = animation.PlayerKeyFrames[index];
        PlayerFrame solvedFrame = SetRigElement(_rigIkDragStartFrame, rootPart, solvedRoot);
        animation.PlayerKeyFrames[index] = new PLayerKeyFrame(solvedFrame, keyFrame.Time, keyFrame.EasingFunction, keyFrame.EasingType, keyFrame.FrameProgressRange);
        animation._playerFrameEdited = true;
        return true;
    }

    private static bool TryGetIkFollowRoot(EnumAnimatedElement selectedPart, out EnumAnimatedElement rootPart)
    {
        rootPart = selectedPart switch
        {
            EnumAnimatedElement.ItemAnchor => EnumAnimatedElement.UpperArmR,
            EnumAnimatedElement.LowerArmR => EnumAnimatedElement.UpperArmR,
            EnumAnimatedElement.ItemAnchorL => EnumAnimatedElement.UpperArmL,
            EnumAnimatedElement.LowerArmL => EnumAnimatedElement.UpperArmL,
            EnumAnimatedElement.LowerFootR => EnumAnimatedElement.UpperFootR,
            EnumAnimatedElement.LowerFootL => EnumAnimatedElement.UpperFootL,
            EnumAnimatedElement.Head => EnumAnimatedElement.Neck,
            _ => EnumAnimatedElement.Unknown
        };

        return rootPart != EnumAnimatedElement.Unknown;
    }

    private static AnimationElement TranslateElementBy(AnimationElement rootStart, AnimationElement desiredSelected, AnimationElement selectedStart)
    {
        float dx = (desiredSelected.OffsetX ?? 0) - (selectedStart.OffsetX ?? 0);
        float dy = (desiredSelected.OffsetY ?? 0) - (selectedStart.OffsetY ?? 0);
        float dz = (desiredSelected.OffsetZ ?? 0) - (selectedStart.OffsetZ ?? 0);

        return new AnimationElement(
            (rootStart.OffsetX ?? 0) + dx,
            (rootStart.OffsetY ?? 0) + dy,
            (rootStart.OffsetZ ?? 0) + dz,
            rootStart.RotationX,
            rootStart.RotationY,
            rootStart.RotationZ);
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

    private static void SetCurrentRigFrame(Animation animation, PlayerFrame frame)
    {
        int index = animation._playerFrameIndex;
        if (index < 0 || index >= animation.PlayerKeyFrames.Count) return;

        PLayerKeyFrame keyFrame = animation.PlayerKeyFrames[index];
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

    private static bool TryGetOppositeRigPart(EnumAnimatedElement selectedPart, out EnumAnimatedElement oppositePart)
    {
        oppositePart = selectedPart switch
        {
            EnumAnimatedElement.ItemAnchor => EnumAnimatedElement.ItemAnchorL,
            EnumAnimatedElement.ItemAnchorL => EnumAnimatedElement.ItemAnchor,
            EnumAnimatedElement.LowerArmR => EnumAnimatedElement.LowerArmL,
            EnumAnimatedElement.LowerArmL => EnumAnimatedElement.LowerArmR,
            EnumAnimatedElement.UpperArmR => EnumAnimatedElement.UpperArmL,
            EnumAnimatedElement.UpperArmL => EnumAnimatedElement.UpperArmR,
            EnumAnimatedElement.UpperFootR => EnumAnimatedElement.UpperFootL,
            EnumAnimatedElement.UpperFootL => EnumAnimatedElement.UpperFootR,
            EnumAnimatedElement.LowerFootR => EnumAnimatedElement.LowerFootL,
            EnumAnimatedElement.LowerFootL => EnumAnimatedElement.LowerFootR,
            _ => EnumAnimatedElement.Unknown
        };

        return oppositePart != EnumAnimatedElement.Unknown;
    }

    private static AnimationElement MirrorRigElement(AnimationElement element)
    {
        return new AnimationElement(
            Negate(element.OffsetX),
            element.OffsetY,
            element.OffsetZ,
            element.RotationX,
            Negate(element.RotationY),
            Negate(element.RotationZ));
    }

    private static PlayerFrame MirrorRigFrame(PlayerFrame frame)
    {
        RightHandFrame? right = frame.LeftHand == null
            ? null
            : new RightHandFrame(
                MirrorRigElement(frame.LeftHand.Value.ItemAnchorL),
                MirrorRigElement(frame.LeftHand.Value.LowerArmL),
                MirrorRigElement(frame.LeftHand.Value.UpperArmL));

        LeftHandFrame? left = frame.RightHand == null
            ? null
            : new LeftHandFrame(
                MirrorRigElement(frame.RightHand.Value.ItemAnchor),
                MirrorRigElement(frame.RightHand.Value.LowerArmR),
                MirrorRigElement(frame.RightHand.Value.UpperArmR));

        OtherPartsFrame? other = frame.OtherParts == null
            ? null
            : new OtherPartsFrame(
                MirrorRigElement(frame.OtherParts.Value.Neck),
                MirrorRigElement(frame.OtherParts.Value.Head),
                MirrorRigElement(frame.OtherParts.Value.UpperFootL),
                MirrorRigElement(frame.OtherParts.Value.UpperFootR),
                MirrorRigElement(frame.OtherParts.Value.LowerFootL),
                MirrorRigElement(frame.OtherParts.Value.LowerFootR));

        return new PlayerFrame(
            right,
            left,
            other,
            frame.UpperTorso == null ? null : MirrorRigElement(frame.UpperTorso.Value),
            frame.DetachedAnchorFrame == null ? null : MirrorRigElement(frame.DetachedAnchorFrame.Value),
            frame.DetachedAnchor,
            frame.SwitchArms,
            frame.PitchFollow,
            frame.FovMultiplier,
            frame.BobbingAmplitude,
            frame.DetachedAnchorFollow,
            frame.LowerTorso == null ? null : MirrorRigElement(frame.LowerTorso.Value));
    }

    private static float? Negate(float? value) => value == null ? null : -value.Value;

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
