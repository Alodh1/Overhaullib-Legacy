using CombatOverhaul.Animations;
using CombatOverhaul.Colliders;
using CombatOverhaul.Integration.Transpilers;
using HarmonyLib;
using OpenTK.Mathematics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CombatOverhaul.Integration;

internal static class AnimationPatches
{
    public static event Action<Entity, float>? OnBeforeFrame;
    public static Settings ClientSettings { get; set; } = new();
    public static Settings ServerSettings { get; set; } = new();
    public static Dictionary<long, ThirdPersonAnimationsBehavior> AnimationBehaviors { get; } = [];
    public static FirstPersonAnimationsBehavior? FirstPersonAnimationBehavior { get; set; }
    public static long OwnerEntityId { get; set; } = 0;
    public static HashSet<long> ActiveEntities { get; set; } = [];
    public static ObjectCache<ClientAnimator, EntityPlayer>? Animators { get; private set; }

    private enum HeldItemAttachmentMode
    {
        Normal,
        SwitchArms,
        DetachedAnchor
    }

    public static void Patch(string harmonyId, ICoreAPI api)
    {
        Animators = new(api, "animators to players cache", 10000, 5 * 60 * 1000, threadSafe: true);

        new Harmony(harmonyId).Patch(
                typeof(EntityShapeRenderer).GetMethod("RenderHeldItem", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatches), nameof(RenderHeldItem)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatches), nameof(DoRender3DOpaque)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityPlayerShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatches), nameof(DoRender3DOpaquePlayer)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityShapeRenderer).GetMethod("BeforeRender", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatches), nameof(BeforeRender)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityPlayer).GetMethod(nameof(EntityPlayer.OnSelfBeforeRender), AccessTools.all),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatches), nameof(OnSelfBeforeRender)))
            );

        new Harmony(harmonyId).Patch(
                typeof(Vintagestory.API.Common.AnimationManager).GetMethod("OnClientFrame", AccessTools.all),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatches), nameof(AnimationManagerOnClientFrame)))
            );
    }

    public static void Unpatch(string harmonyId, ICoreAPI api)
    {
        new Harmony(harmonyId).Unpatch(typeof(EntityShapeRenderer).GetMethod("RenderHeldItem", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityPlayerShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityShapeRenderer).GetMethod("BeforeRender", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityPlayer).GetMethod(nameof(EntityPlayer.OnSelfBeforeRender), AccessTools.all), HarmonyPatchType.Postfix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(Vintagestory.API.Common.AnimationManager).GetMethod("OnClientFrame", AccessTools.all), HarmonyPatchType.Postfix, harmonyId);

        Animators?.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void OnFrameInvoke(ClientAnimator? animator, ElementPose pose)
    {
        if (ClientSettings.DisableAllAnimations || animator == null || Animators == null) return;

        if (pose is ExtendedElementPose extendedPose)
        {
            if (extendedPose.Player != null)
            {
                if (extendedPose.ElementNameEnum != EnumAnimatedElement.Unknown && !ClientSettings.DisableThirdPersonAnimations && AnimationBehaviors.TryGetValue(extendedPose.Player.EntityId, out ThirdPersonAnimationsBehavior? behavior))
                {
                    behavior.OnFrame(extendedPose.Player, pose, animator);
                }

                if (extendedPose.ElementNameEnum != EnumAnimatedElement.Unknown && extendedPose.Player.EntityId == OwnerEntityId)
                {
                    FirstPersonAnimationBehavior?.OnFrame(extendedPose.Player, pose, animator);
                }

                if (extendedPose.ElementNameEnum != EnumAnimatedElement.Unknown) return;
            }
        }

        if (Animators.Get(animator, out EntityPlayer? entity))
        {
            if (!ClientSettings.DisableThirdPersonAnimations && AnimationBehaviors.TryGetValue(entity.EntityId, out ThirdPersonAnimationsBehavior? behavior))
            {
                behavior.OnFrame(entity, pose, animator);
            }

            if (entity.EntityId == OwnerEntityId)
            {
                FirstPersonAnimationBehavior?.OnFrame(entity, pose, animator);
            }

            if (pose is ExtendedElementPose extendedPose2 && extendedPose2.Player == null)
            {
                extendedPose2.Player = entity;
            }
        }
    }

    private static void BeforeRender(EntityShapeRenderer __instance, float dt)
    {
        if (ClientSettings.DisableAllAnimations) return;

        if (__instance.entity is EntityPlayer player && IsLocalPlayer(player))
        {
            return;
        }

        OnBeforeFrame?.Invoke(__instance.entity, dt);
    }

    private static void OnSelfBeforeRender(EntityPlayer __instance, float dt)
    {
        if (ClientSettings.DisableAllAnimations) return;

        OnBeforeFrame?.Invoke(__instance, dt);
    }

    private static void AnimationManagerOnClientFrame(Vintagestory.API.Common.AnimationManager __instance, float dt)
    {
        // Do not re-apply the active player frame here.
        // FirstPersonAnimationsBehavior/ThirdPersonAnimationsBehavior already apply frames
        // through the animator pose hook. Reapplying the same frame here desyncs held-item
        // attachment points and makes firearms jump to the wrong hand during reload.
    }

    private static void DoRender3DOpaque(EntityShapeRenderer __instance, float dt, bool isShadowPass)
    {
        try
        {
            CollidersEntityBehavior behavior = __instance.entity?.GetBehavior<CollidersEntityBehavior>();
            behavior?.Render(__instance.entity?.Api as ICoreClientAPI, __instance.entity as EntityAgent, __instance);
        }
        catch (Exception)
        {
            // just ignore
        }

    }

    private static void DoRender3DOpaquePlayer(EntityPlayerShapeRenderer __instance, float dt, bool isShadowPass)
    {
        try
        {
            CollidersEntityBehavior behavior = __instance.entity?.GetBehavior<CollidersEntityBehavior>();
            behavior?.Render(__instance.entity?.Api as ICoreClientAPI, __instance.entity as EntityAgent, __instance);
        }
        catch (Exception)
        {
            // just ignore
        }
    }

    private static bool RenderHeldItem(EntityShapeRenderer __instance, float dt, bool isShadowPass, bool right)
    {
        EntityPlayer? player = __instance.entity as EntityPlayer;
        ItemSlot? slot = right ? player?.RightHandItemSlot : player?.LeftHandItemSlot;

        if (slot?.Itemstack?.Item == null) return true;
        if (player != null && IsTongsHeldItemRender(player, right))
        {
            // Let vanilla/tongs renderer handle workitems in tongs.
            return true;
        }

        Animatable? behavior = slot.Itemstack.Item.GetCollectibleBehavior(typeof(Animatable), true) as Animatable;
        if (behavior == null) return true;

        // Keep the old working transform path: animated held items are positioned with the
        // third-person hand transforms, even for the local first-person weapon model.
        // The first-person state is selected by BeforeRender/IsFirstPerson, not by using
        // HandFp as the attachment transform target. Using HandFp here moves reload-phase
        // firearm attachments to the wrong side/hand.
        EnumItemRenderTarget renderTarget = right ? EnumItemRenderTarget.HandTp : EnumItemRenderTarget.HandTpOff;
        ItemRenderInfo renderInfo = __instance.capi.Render.GetItemStackRenderInfo(slot, renderTarget, dt);

        // This intentionally stays HandFp like the original animation path. It ticks the
        // animated item model; CurrentFirstPerson is determined inside Animatable.
        behavior.BeforeRender(__instance.capi, slot.Itemstack, __instance.entity, EnumItemRenderTarget.HandFp, dt);

        if (slot.Itemstack.Item.Textures.Count > 0)
        {
            (string textureName, _) = slot.Itemstack.Item.Textures.First();
            TextureAtlasPosition atlasPos = __instance.capi.ItemTextureAtlas.GetPosition(slot.Itemstack.Item, textureName);
            renderInfo.TextureId = atlasPos.atlasTextureId;
        }

        Vec4f? lightrgbs = (Vec4f?)typeof(EntityShapeRenderer)
            .GetField("lightrgbs", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(__instance);

        try
        {
            behavior.AttachmentPointOverride = GetHeldItemAttachmentPointOverride(player, right);
            return !behavior.RenderHeldItem(__instance.ModelMat, __instance.capi, slot, __instance.entity, lightrgbs, dt, isShadowPass, right, renderInfo, renderTarget);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsTongsHeldItemRender(EntityPlayer player, bool right)
    {
        ItemStack? rightStack = player.RightHandItemSlot?.Itemstack;
        ItemStack? leftStack = player.LeftHandItemSlot?.Itemstack;

        bool rightIsTongs = IsTongsStack(rightStack);
        bool leftIsTongs = IsTongsStack(leftStack);

        // If either hand has tongs equipped, skip custom animatable held-item rendering
        // and let vanilla handle both hands for smithing/tongs visuals.
        if (rightIsTongs || leftIsTongs) return true;

        return false;
    }

    private static bool IsTongsStack(ItemStack? stack)
    {
        string domain = stack?.Collectible?.Code?.Domain ?? "";
        string path = stack?.Collectible?.Code?.Path ?? "";
        if (domain.Length == 0 || path.Length == 0) return false;

        // Matches vanilla tongs/tongsmetal and derived variants.
        return path.StartsWith("tongs", StringComparison.OrdinalIgnoreCase)
            || path.Contains("tongsmetal", StringComparison.OrdinalIgnoreCase);
    }
    private static string? GetHeldItemAttachmentPointOverride(EntityPlayer? player, bool right)
    {
        if (player == null) return null;

        PlayerItemFrame? frame = null;

        if (player.EntityId == OwnerEntityId && FirstPersonAnimationBehavior?.HasActiveAnimationFrame == true)
        {
            frame = FirstPersonAnimationBehavior.CurrentFrame;
        }
        else if (!ClientSettings.DisableThirdPersonAnimations
            && AnimationBehaviors.TryGetValue(player.EntityId, out ThirdPersonAnimationsBehavior? thirdPersonBehavior)
            && thirdPersonBehavior.HasActiveAnimationFrame)
        {
            frame = thirdPersonBehavior.CurrentFrame;
        }

        if (frame == null) return null;

        if (frame.Value.DetachedAnchor) return "DetachedAnchor";
        if (frame.Value.SwitchArms) return right ? "LeftHand" : "RightHand";

        return null;
    }

    private static void ApplyCurrentPlayerFrame(Entity? entity, ClientAnimator? explicitAnimator = null)
    {
        if (entity is not EntityPlayer player) return;
        if ((explicitAnimator ?? player.AnimManager?.Animator) is not ClientAnimator animator) return;

        PlayerItemFrame? frame = null;
        bool applyCameraPitch = false;

        if (player.EntityId == OwnerEntityId && FirstPersonAnimationBehavior?.HasActiveAnimationFrame == true)
        {
            frame = FirstPersonAnimationBehavior.CurrentFrame;

            // In first person the vanilla player renderer already applies held-item pitch
            // through HeldItemPitchFollowOverride. Applying camera pitch here too makes
            // ranged weapons over-follow vertically. Keep it for third person only.
            applyCameraPitch = !IsLocalFirstPerson(player);
        }
        else if (!ClientSettings.DisableThirdPersonAnimations && AnimationBehaviors.TryGetValue(player.EntityId, out ThirdPersonAnimationsBehavior? thirdPersonBehavior) && thirdPersonBehavior.HasActiveAnimationFrame)
        {
            frame = thirdPersonBehavior.CurrentFrame;
            applyCameraPitch = true;
        }

        if (frame == null) return;

        Vector3 eyePosition = new((float)player.LocalEyePos.X, (float)player.LocalEyePos.Y, (float)player.LocalEyePos.Z);
        float eyeHeight = (float)player.Properties.EyeHeight;
        float pitch = player.Pos.HeadPitch;
        float[] identity = Mat4f.Create();

        ApplyPlayerFrameToPoses(frame.Value, animator.RootPoses, identity, animator.TransformationMatrices, new HashSet<int>(), eyePosition, eyeHeight, pitch, applyCameraPitch);
        RefreshAttachmentPointMatrices(animator);
    }

    private static void RefreshAttachmentPointMatrices(ClientAnimator animator)
    {
        foreach (AttachmentPointAndPose attachmentPoint in animator.AttachmentPointByCode.Values)
        {
            ElementPose? cachedPose = attachmentPoint.CachedPose;
            if (cachedPose?.AnimModelMatrix == null || attachmentPoint.AnimModelMatrix == null) continue;

            Array.Copy(cachedPose.AnimModelMatrix, attachmentPoint.AnimModelMatrix, Math.Min(cachedPose.AnimModelMatrix.Length, attachmentPoint.AnimModelMatrix.Length));
        }
    }

    private static void ApplyPlayerFrameToPoses(PlayerItemFrame frame, List<ElementPose>? poses, float[] parentMatrix, float[]? transformationMatrices, HashSet<int> jointsDone, Vector3 eyePosition, float eyeHeight, float pitch, bool applyCameraPitch)
    {
        if (poses == null) return;

        float[] localTransform = Mat4f.Create();
        float[] jointTransform = Mat4f.Create();

        foreach (ElementPose pose in poses)
        {
            ShapeElement element = pose.ForElement;
            Array.Copy(parentMatrix, pose.AnimModelMatrix, Math.Min(parentMatrix.Length, pose.AnimModelMatrix.Length));

            if (Enum.TryParse(element.Name, out EnumAnimatedElement animatedElement) && animatedElement != EnumAnimatedElement.Unknown)
            {
                // Do not let OverhaulLib wipe/apply leg poses here.
                // Vintage Story's own locomotion animator already writes these every frame.
                // Clearing or applying a zero combat frame to them makes players glide.
                //
                // Keep clearing the torso/arms/head/etc. though; otherwise camera pitch is added
                // on top of the previous pose every render pass and the torso can spin around.
                bool isLegElement = animatedElement is EnumAnimatedElement.UpperFootR
                    or EnumAnimatedElement.UpperFootL
                    or EnumAnimatedElement.LowerFootR
                    or EnumAnimatedElement.LowerFootL;

#if DEBUG
                bool allowDebugLegPose = DebugWindowManager.DebugRigPoseOverrideActive;
#else
                const bool allowDebugLegPose = false;
#endif
                if (!isLegElement || allowDebugLegPose)
                {
                    pose.Clear();
                    frame.Apply(pose, animatedElement, eyePosition, eyeHeight, pitch, applyCameraPitch);
                }
            }

            Mat4f.Identity(localTransform);
            element.GetLocalTransformMatrix(0, localTransform, pose);
            Mat4f.Mul(pose.AnimModelMatrix, pose.AnimModelMatrix, localTransform);

            if (transformationMatrices != null && element.JointId > 0 && jointsDone.Add(element.JointId))
            {
                int index = 16 * element.JointId;
                if (index + 16 <= transformationMatrices.Length)
                {
                    Mat4f.Mul(jointTransform, pose.AnimModelMatrix, element.inverseModelTransform);
                    Array.Copy(jointTransform, 0, transformationMatrices, index, 16);
                }
            }

            ApplyPlayerFrameToPoses(frame, pose.ChildElementPoses, pose.AnimModelMatrix, transformationMatrices, jointsDone, eyePosition, eyeHeight, pitch, applyCameraPitch);
        }
    }

    private static bool IsLocalPlayer(EntityPlayer player)
    {
        return player.Api is ICoreClientAPI clientApi
            && clientApi.World?.Player?.Entity?.EntityId == player.EntityId;
    }

    private static bool IsLocalFirstPerson(EntityPlayer player)
    {
        return player.Api is ICoreClientAPI clientApi
            && clientApi.World?.Player?.Entity?.EntityId == player.EntityId
            && clientApi.World.Player.CameraMode == EnumCameraMode.FirstPerson;
    }

    private static readonly FieldInfo? _animationManagerEntity = typeof(Vintagestory.API.Common.AnimationManager).GetField("entity", BindingFlags.NonPublic | BindingFlags.Instance);
}
