using CombatOverhaul.Animations;
using CombatOverhaul.Colliders;
using CombatOverhaul.Integration.Transpilers;
using CombatOverhaul.Utils;
using HarmonyLib;
using OpenTK.Mathematics;
using System.Linq.Expressions;
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
    public static AnimatorPlayerMap? Animators { get; private set; }
    private static readonly FieldInfo? _lightrgbsField = typeof(EntityShapeRenderer).GetField("lightrgbs", BindingFlags.NonPublic | BindingFlags.Instance);
    // Compiled accessor for the private 'lightrgbs' field, built once. Avoids a reflection
    // FieldInfo.GetValue invoke on every animated held-item render (both hands, opaque + each
    // shadow pass, every frame). Falls back to the reflection path if it can't be built.
    private static readonly System.Func<EntityShapeRenderer, Vec4f?>? _getLightRgbs = BuildLightRgbsGetter();
    // Resolve the collider behavior once per renderer instead of scanning the entity's behavior list
    // on every render pass (opaque + each shadow cascade) every frame. Weak key: the entry is
    // collected together with the renderer.
    private static readonly ConditionalWeakTable<EntityShapeRenderer, StrongBox<CollidersEntityBehavior?>> _colliderBehaviorCache = new();
    // Resolve the held item's Animatable collectible behavior once per item id instead of walking the
    // collectible's behavior list on every held-item render (both hands, opaque + each shadow pass,
    // every frame, for every visible entity). The behavior instance is per-collectible and stable for
    // the session, so a plain id-keyed cache is safe; null results are cached too so plain vanilla
    // items don't re-scan. Bounded by the number of registered item types.
    private static readonly Dictionary<int, Animatable?> _heldItemAnimatableCache = [];
    private static bool _reportedColliderRenderError;
    private static bool _reportedHeldItemRenderError;

    private enum HeldItemAttachmentMode
    {
        Normal,
        SwitchArms,
        DetachedAnchor
    }

    public static void Patch(string harmonyId, ICoreAPI api)
    {
        Animators = new();
        Harmony harmony = new(harmonyId);

        harmony.Patch(
                typeof(EntityShapeRenderer).GetMethod("RenderHeldItem", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatches), nameof(RenderHeldItem)))
            );

        harmony.Patch(
                typeof(EntityShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatches), nameof(DoRender3DOpaque)))
            );

        harmony.Patch(
                typeof(EntityPlayerShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatches), nameof(DoRender3DOpaquePlayer)))
            );

        harmony.Patch(
                typeof(EntityShapeRenderer).GetMethod("BeforeRender", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatches), nameof(BeforeRender)))
            );

        harmony.Patch(
                typeof(EntityPlayer).GetMethod(nameof(EntityPlayer.OnSelfBeforeRender), AccessTools.all),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatches), nameof(OnSelfBeforeRender)))
            );

        harmony.Patch(
                typeof(Vintagestory.API.Common.AnimationManager).GetMethod("OnClientFrame", AccessTools.all),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatches), nameof(AnimationManagerOnClientFrame)))
            );
    }

    public static void Unpatch(string harmonyId, ICoreAPI api)
    {
        Harmony harmony = new(harmonyId);

        harmony.Unpatch(typeof(EntityShapeRenderer).GetMethod("RenderHeldItem", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        harmony.Unpatch(typeof(EntityShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        harmony.Unpatch(typeof(EntityPlayerShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        harmony.Unpatch(typeof(EntityShapeRenderer).GetMethod("BeforeRender", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        harmony.Unpatch(typeof(EntityPlayer).GetMethod(nameof(EntityPlayer.OnSelfBeforeRender), AccessTools.all), HarmonyPatchType.Postfix, harmonyId);
        harmony.Unpatch(typeof(Vintagestory.API.Common.AnimationManager).GetMethod("OnClientFrame", AccessTools.all), HarmonyPatchType.Postfix, harmonyId);

        // Insurance for a within-process world reload: per-entity despawn/dispose normally clears
        // these, but clear them here too so a new session never starts with stale entries.
        AnimationBehaviors.Clear();
        ActiveEntities.Clear();
        _heldItemAnimatableCache.Clear();

        Animators?.Clear();
        Animators = null;
    }

    private static System.Func<EntityShapeRenderer, Vec4f?>? BuildLightRgbsGetter()
    {
        if (_lightrgbsField == null) return null;

        try
        {
            ParameterExpression instance = Expression.Parameter(typeof(EntityShapeRenderer), "renderer");
            Expression field = Expression.Field(instance, _lightrgbsField);
            return Expression.Lambda<System.Func<EntityShapeRenderer, Vec4f?>>(field, instance).Compile();
        }
        catch
        {
            return null;
        }
    }

    private static CollidersEntityBehavior? GetCachedColliderBehavior(EntityShapeRenderer renderer)
    {
        if (renderer.entity == null) return null;

        return _colliderBehaviorCache.GetValue(
            renderer,
            static r => new StrongBox<CollidersEntityBehavior?>(r.entity?.GetBehavior<CollidersEntityBehavior>())).Value;
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

        DispatchBeforeFrame(__instance.entity, dt);
        OnBeforeFrame?.Invoke(__instance.entity, dt);
    }

    private static void OnSelfBeforeRender(EntityPlayer __instance, float dt)
    {
        if (ClientSettings.DisableAllAnimations) return;

        DispatchBeforeFrame(__instance, dt);
        OnBeforeFrame?.Invoke(__instance, dt);
    }

    private static void DispatchBeforeFrame(Entity entity, float dt)
    {
        if (entity is not EntityPlayer player) return;

        if (!ClientSettings.DisableThirdPersonAnimations && AnimationBehaviors.TryGetValue(player.EntityId, out ThirdPersonAnimationsBehavior? thirdPersonBehavior))
        {
            thirdPersonBehavior.OnBeforeFrame(entity, dt);
        }

        if (player.EntityId == OwnerEntityId)
        {
            FirstPersonAnimationBehavior?.OnBeforeFrame(entity, dt);
        }
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
            CollidersEntityBehavior? behavior = GetCachedColliderBehavior(__instance);
            behavior?.Render(__instance.entity?.Api as ICoreClientAPI, __instance.entity as EntityAgent, __instance);
        }
        catch (Exception exception)
        {
            LogColliderRenderError(__instance.entity?.Api, exception);
        }

    }

    public sealed class AnimatorPlayerMap
    {
        // Keyed weakly by ClientAnimator so stale entries are collected automatically once the
        // game drops an entity's animator (player leaves view range, renderer rebuild, respawn).
        // A strong Dictionary here pins every ClientAnimator and its EntityPlayer for the whole
        // session, which is a large entity-graph leak on populated servers.
        private readonly ConditionalWeakTable<ClientAnimator, EntityPlayer> _mapping = new();

        public void Add(ClientAnimator animator, EntityPlayer player)
        {
            if (_mapping.TryGetValue(animator, out EntityPlayer? existing) && existing.EntityId == player.EntityId)
            {
                return;
            }

            _mapping.AddOrUpdate(animator, player);
        }

        public bool Get(ClientAnimator animator, out EntityPlayer? player)
        {
            return _mapping.TryGetValue(animator, out player);
        }

        public void Clear()
        {
            _mapping.Clear();
        }
    }

    private static void DoRender3DOpaquePlayer(EntityPlayerShapeRenderer __instance, float dt, bool isShadowPass)
    {
        try
        {
            CollidersEntityBehavior? behavior = GetCachedColliderBehavior(__instance);
            behavior?.Render(__instance.entity?.Api as ICoreClientAPI, __instance.entity as EntityAgent, __instance);
        }
        catch (Exception exception)
        {
            LogColliderRenderError(__instance.entity?.Api, exception);
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

        int itemId = slot.Itemstack.Item.Id;
        if (!_heldItemAnimatableCache.TryGetValue(itemId, out Animatable? behavior))
        {
            behavior = slot.Itemstack.Item.GetCollectibleBehavior(typeof(Animatable), true) as Animatable;
            _heldItemAnimatableCache[itemId] = behavior;
        }
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
            // Manual first-entry read instead of LINQ .First() to avoid the boxed-enumerator
            // allocation on every held-item render.
            foreach ((string textureName, _) in slot.Itemstack.Item.Textures)
            {
                TextureAtlasPosition atlasPos = __instance.capi.ItemTextureAtlas.GetPosition(slot.Itemstack.Item, textureName);
                renderInfo.TextureId = atlasPos.atlasTextureId;
                break;
            }
        }

        Vec4f? lightrgbs = _getLightRgbs != null ? _getLightRgbs(__instance) : (Vec4f?)_lightrgbsField?.GetValue(__instance);

        try
        {
            behavior.AttachmentPointOverride = GetHeldItemAttachmentPointOverride(player, right);
            return !behavior.RenderHeldItem(__instance.ModelMat, __instance.capi, slot, __instance.entity, lightrgbs, dt, isShadowPass, right, renderInfo, renderTarget);
        }
        catch (Exception exception)
        {
            if (!_reportedHeldItemRenderError)
            {
                _reportedHeldItemRenderError = true;
                LoggerUtil.Warn(__instance.entity?.Api, typeof(AnimationPatches), $"Error while rendering animated held item for '{slot.Itemstack.Collectible?.Code}':\n{exception}");
            }

            return true;
        }
    }

    private static void LogColliderRenderError(ICoreAPI? api, Exception exception)
    {
        if (_reportedColliderRenderError) return;

        _reportedColliderRenderError = true;
        LoggerUtil.Warn(api, typeof(AnimationPatches), $"Error while rendering collider debug overlay:\n{exception}");
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
        return CollectibleClassifier.IsTongs(stack);
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
