using CombatOverhaul.Colliders;
using CombatOverhaul.Integration;
using CombatOverhaul.Integration.Transpilers;
using CombatOverhaul.MeleeSystems;
using CombatOverhaul.Utils;
using ImGuiNET;
using Newtonsoft.Json.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
using VSImGui;
using VSImGui.API;

namespace CombatOverhaul.Animations;

public sealed partial class DebugWindowManager
{
    public static bool PlayAnimationsInThirdPerson { get; set; } = false;
    public static bool RenderDebugColliders { get; set; } = false;

    public DebugWindowManager(ICoreClientAPI api, ParticleEffectsManager particleEffectsManager)
    {
#if DEBUG
        api.ModLoader.GetModSystem<ImGuiModSystem>().Draw += DrawEditor;
        _transformGizmoRenderer = new TransformGizmoRenderer(api, this);
        _detachedEditorCamera = new DetachedEditorCamera(api);
#endif
        api.Input.RegisterHotKey("combatOverhaul_editor", "Show animation editor", GlKeys.L, ctrlPressed: true);
        api.Input.SetHotKeyHandler("combatOverhaul_editor", keys => _showAnimationEditor = !_showAnimationEditor);
        _instance = this;

        _api = api;
        _particleEffectsManager = particleEffectsManager;
        _colliders.Clear();
    }

    public void Load(ICoreClientAPI api)
    {
        _behavior = api.World.Player.Entity.GetBehavior<FirstPersonAnimationsBehavior>();
#if DEBUG
        RegisterCollectibleTransformAttributes(api);
#endif
    }

    public static void RegisterTransformByCode(ModelTransform transform, string code)
    {
        _instance.RegisterTransform(transform, code);
    }
    public void RegisterTransform(ModelTransform transform, string code)
    {
        _transforms[code] = new EditableTransform(transform);
    }
    private void RegisterTransform(ModelTransform transform, string code, Action<ModelTransform> apply, System.Func<ModelTransform, string>? saveToSource = null)
    {
        _transforms[code] = new EditableTransform(transform, apply, saveToSource);
    }

    private sealed class EditableTransform
    {
        public EditableTransform(ModelTransform transform, Action<ModelTransform>? apply = null, System.Func<ModelTransform, string>? saveToSource = null)
        {
            Transform = transform;
            Apply = apply;
            SaveToSource = saveToSource;
        }

        public ModelTransform Transform { get; }
        public Action<ModelTransform>? Apply { get; }
        public System.Func<ModelTransform, string>? SaveToSource { get; }
    }

    private void RegisterCollectibleTransformAttributes(ICoreClientAPI api)
    {
        foreach (Item item in api.World.Items)
        {
            if (item?.Code == null) continue;
            RegisterCollectibleTransformAttributes(item);
        }

        foreach (Block block in api.World.Blocks)
        {
            if (block?.Code == null) continue;
            RegisterCollectibleTransformAttributes(block);
        }
    }

    private void RegisterCollectibleTransformAttributes(CollectibleObject collectible)
    {
        foreach (string attributeCode in DirectTransformAttributeCodes)
        {
            RegisterDirectTransformAttribute(collectible, attributeCode);
        }

        foreach (string attributeCode in TypedTransformAttributeCodes)
        {
            RegisterTypedTransformAttribute(collectible, attributeCode);
        }
    }

    private void RegisterDirectTransformAttribute(CollectibleObject collectible, string attributeCode)
    {
        JsonObject? jsonTransform = collectible.Attributes?[attributeCode];
        if (jsonTransform?.Exists != true) return;

        ModelTransform? transform = jsonTransform.AsObject<ModelTransform>();
        if (transform == null) return;

        transform.EnsureDefaultValues();
        RegisterTransform(
            transform,
            $"{collectible.Code} / {attributeCode}",
            value => ApplyDirectTransformAttribute(collectible, attributeCode, value),
            value => TrySaveTransformToSource(collectible, attributeCode, value));
    }

    private void RegisterTypedTransformAttribute(CollectibleObject collectible, string attributeCode)
    {
        if (collectible.Attributes?[attributeCode].Token is not JObject transformsByType) return;

        foreach (JProperty property in transformsByType.Properties())
        {
            ModelTransform? transform = new JsonObject(property.Value).AsObject<ModelTransform>();
            if (transform == null) continue;

            string typeCode = property.Name;
            transform.EnsureDefaultValues();
            RegisterTransform(
                transform,
                $"{collectible.Code} / {attributeCode} / {typeCode}",
                value => ApplyTypedTransformAttribute(collectible, attributeCode, typeCode, value),
                value => TrySaveTransformToSource(collectible, attributeCode, value, typeCode));
        }
    }

    private static void ApplyDirectTransformAttribute(CollectibleObject collectible, string attributeCode, ModelTransform transform)
    {
        if (collectible.Attributes?.Token is not JObject source) return;

        JObject attributes = (JObject)source.DeepClone();
        attributes[attributeCode] = TransformToToken(transform);
        collectible.Attributes = new JsonObject(attributes);
    }

    private static void ApplyTypedTransformAttribute(CollectibleObject collectible, string attributeCode, string typeCode, ModelTransform transform)
    {
        if (collectible.Attributes?.Token is not JObject source) return;

        JObject attributes = (JObject)source.DeepClone();
        JObject transformsByType = attributes[attributeCode] as JObject ?? new JObject();
        transformsByType[typeCode] = TransformToToken(transform);
        attributes[attributeCode] = transformsByType;
        collectible.Attributes = new JsonObject(attributes);
    }

    private static JToken TransformToToken(ModelTransform transform)
    {
        return JToken.Parse(JsonUtil.ToPrettyString(transform));
    }

    private static string TrySaveTransformToSource(CollectibleObject collectible, string attributeCode, ModelTransform transform, string? typedKey = null)
    {
        string? sourceFile = FindCollectibleSourceFile(collectible);
        if (sourceFile == null)
        {
            return $"Source not found for {collectible.Code}.";
        }

        try
        {
            JObject json = JObject.Parse(File.ReadAllText(sourceFile));
            JObject attributes = json["attributes"] as JObject ?? new JObject();

            if (typedKey == null)
            {
                attributes[attributeCode] = TransformToToken(transform);
            }
            else
            {
                JObject transformsByType = attributes[attributeCode] as JObject ?? new JObject();
                transformsByType[typedKey] = TransformToToken(transform);
                attributes[attributeCode] = transformsByType;
            }

            json["attributes"] = attributes;
            File.WriteAllText(sourceFile, JsonUtil.ToPrettyString(json));
            return $"Saved {attributeCode} to {sourceFile}.";
        }
        catch (Exception exception)
        {
            return $"Save failed for {sourceFile}: {exception.Message}";
        }
    }

    private static string? FindCollectibleSourceFile(CollectibleObject collectible)
    {
        string? sourceRoot = GetSourceRoot();
        if (!Directory.Exists(sourceRoot)) return null;

        string domain = collectible.Code.Domain;
        string kind = collectible is Block ? "blocktypes" : "itemtypes";
        string path = collectible.Code.Path;

        IEnumerable<string> candidates = Directory.EnumerateFiles(sourceRoot, "*.json", SearchOption.AllDirectories)
            .Where(file =>
            {
                string normalized = file.Replace('\\', '/');
                return normalized.Contains($"/resources/assets/{domain}/{kind}/", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains($"/assets/{domain}/{kind}/", StringComparison.OrdinalIgnoreCase);
            });

        string? bestFile = null;
        int bestScore = -1;

        foreach (string file in candidates)
        {
            try
            {
                JObject json = JObject.Parse(File.ReadAllText(file));
                string? code = json["code"]?.ToString();
                if (string.IsNullOrEmpty(code)) continue;
                if (code.Contains(':')) code = code[(code.IndexOf(':') + 1)..];

                int score = -1;
                if (string.Equals(code, path, StringComparison.OrdinalIgnoreCase))
                {
                    score = 10000 + code.Length;
                }
                else if (path.StartsWith(code + "-", StringComparison.OrdinalIgnoreCase))
                {
                    score = 1000 + code.Length;
                }
                else if (Path.GetFileNameWithoutExtension(file).Equals(code, StringComparison.OrdinalIgnoreCase) &&
                    path.Contains(code, StringComparison.OrdinalIgnoreCase))
                {
                    score = 100 + code.Length;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestFile = file;
                }
            }
            catch
            {
                // Non-strict JSON/HJSON sources cannot be safely rewritten here.
            }
        }

        return bestFile;
    }

    private static string TrySaveAnimationToSource(string animationCode, Animation animation)
    {
        if (!AnimationsManager._instance.AnimationSources.TryGetValue(animationCode, out AnimationSource? source))
        {
            return $"Source not tracked for {animationCode}.";
        }

        string? sourceRoot = GetSourceRoot();
        if (sourceRoot == null || !Directory.Exists(sourceRoot))
        {
            return "ModsNeedUpdate source root not found.";
        }

        IEnumerable<string> candidates = Directory.EnumerateFiles(sourceRoot, "*.json", SearchOption.AllDirectories)
            .Where(file =>
            {
                string normalized = file.Replace('\\', '/');
                return normalized.Contains($"/resources/assets/{source.Domain}/config/animations/", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains($"/assets/{source.Domain}/config/animations/", StringComparison.OrdinalIgnoreCase);
            });

        foreach (string file in candidates)
        {
            try
            {
                JObject json = JObject.Parse(File.ReadAllText(file));
                if (!json.ContainsKey(source.SourceKey)) continue;

                json[source.SourceKey] = JToken.Parse(AnimationJson.FromAnimation(animation).ToString());
                File.WriteAllText(file, JsonUtil.ToPrettyString(json));
                return $"Saved {animationCode} to {file}.";
            }
            catch
            {
                // Non-strict JSON/HJSON animation files cannot be safely rewritten here.
            }
        }

        return $"Source JSON not found for {animationCode}.";
    }

    private static string? GetSourceRoot()
    {
        string sourceRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ModsNeedUpdate");
        return Directory.Exists(sourceRoot) ? sourceRoot : null;
    }

    private static ModelTransform CreateDefaultTransform()
    {
        ModelTransform transform = new()
        {
            Translation = new Vec3f(),
            Rotation = new Vec3f(),
            Origin = new Vec3f(0.5f, 0.5f, 0.5f),
            ScaleXYZ = new Vec3f(1, 1, 1)
        };
        transform.EnsureDefaultValues();
        return transform;
    }

    public static void RegisterCollider(string item, string type, MeleeDamageType collider)
    {
        if (!_colliders.ContainsKey(item))
        {
            _colliders.Add(item, new());
        }

        _colliders[item].Add(type, (value => collider.RelativeCollider = value, () => collider.RelativeCollider));
    }
    public static void RegisterCollider(string item, string type, Action<LineSegmentCollider> setter, System.Func<LineSegmentCollider> getter)
    {
        if (!_colliders.ContainsKey(item))
        {
            _colliders.Add(item, new());
        }

        _colliders[item].Add(type, (setter, getter));
    }

    private bool _showAnimationEditor = false;
    private int _selectedAnimationIndex = 0;
    private int _selectedAnimationIndexFiltered = 0;
    private FirstPersonAnimationsBehavior? _behavior;
    private readonly ICoreClientAPI _api;
    private string _itemAnimation = "";
    private string _animationKey = "";
    private readonly FieldInfo _mainCameraInfo = typeof(ClientMain).GetField("MainCamera", BindingFlags.NonPublic | BindingFlags.Instance);
    private readonly FieldInfo _cameraFov = typeof(Camera).GetField("Fov", BindingFlags.NonPublic | BindingFlags.Instance);
    private string _playerAnimationKey = "";
    private float _animationSpeed = 1;
    private ParticleEffectsManager _particleEffectsManager;
    private AnimationJson _animationBuffer;
    internal static DebugWindowManager _instance;

    private string _animationsFilter = "";
    private string _filter = "";
    private string _collidersItemsFilter = "";
    private int _transformIndex = 0;
    private int _colliderItemIndex = 0;
    private int _colliderIndex = 0;
    private int _heldTransformAttributeIndex = 0;
    private string _transformSaveStatus = "";
    private readonly Dictionary<string, EditableTransform> _transforms = new();
    private static Dictionary<string, Dictionary<string, (Action<LineSegmentCollider> setter, System.Func<LineSegmentCollider> getter)>> _colliders = new();
    internal static LineSegmentCollider? _currentCollider = null;
#if DEBUG
    private TransformGizmoRenderer? _transformGizmoRenderer;
    private DetachedEditorCamera? _detachedEditorCamera;
    private ModelTransform? _activeGizmoTransform;
    private TransformGizmoContext _activeGizmoContext = TransformGizmoContext.Free;
    private BlockPos? _activeGizmoBlockPos;
    private Vec3d? _activeGizmoWorldCenter;
    private Action<ModelTransform>? _activeGizmoApply;
    internal TransformGizmoMode GizmoMode { get; private set; } = TransformGizmoMode.Move;
    internal bool GizmoLocalSpace { get; private set; } = true;
    internal bool IncludeGizmoInIncrement { get; private set; } = true;
    internal float TransformGizmoIncrement { get; private set; } = 0.1f;
    internal static bool DebugPoseFreezeActive { get; private set; }
#endif
    private static readonly string[] DirectTransformAttributeCodes = new[]
    {
        "groundStorageTransform",
        "guiTransform",
        "groundTransform",
        "tpHandTransform",
        "tpOffHandTransform",
        "onDisplayTransform",
        "onshelfTransform",
        "toolrackTransform",
        "onTongTransform",
        "onMetalTongTransform",
        "inForgeTransform",
        "infirepitTransform",
        "inTrapTransform",
        "onAntlerMountTransform",
        "onmoldrackTransform",
        "onOmokTransform",
        "onscrollrackTransform",
        "inGenericDisplayTransform",
        "inWeaponrackTransform",
        "inWallmountTransform",
        "inPistolstandTransform",
        "inViceTransform",
        "inCrossbowWallmountTransform"
    };
    private static readonly string[] TypedTransformAttributeCodes = new[]
    {
        "onTongTransformByType",
        "onMetalTongTransformByType",
        "inForgeTransformByType"
    };

#if DEBUG
    private CallbackGUIStatus DrawEditor(float deltaSeconds)
    {
        _currentCollider = null;
        ClearActiveTransformGizmo();
        if (!_showAnimationEditor)
        {
            OnDebugEditorClosed();
            return CallbackGUIStatus.Closed;
        }

        if (ImGui.Begin("Combat Overhaul - Animations editor and debug tools", ref _showAnimationEditor))
        {
            ImGui.BeginTabBar($"##main_tab_bar");
            if (ImGui.BeginTabItem($"Animations"))
            {
                AnimationsTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem($"Transforms"))
            {
                TransformEditorTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Particle effects##tab"))
            {
                _particleEffectsManager.Draw("particle-effects");
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Colliders##tab"))
            {
                bool debugColliders = RenderDebugColliders;
                ImGui.Checkbox("Render weapon colliders", ref debugColliders);
                RenderDebugColliders = debugColliders;

                ImGui.InputText("Items filter##colliders", ref _collidersItemsFilter, 200);
                VSImGui.EditorsUtils.FilterElements(_collidersItemsFilter, _colliders.Keys, out IEnumerable<string> filteredItems, out _);
                if (_colliderItemIndex > filteredItems.Count())
                {
                    _colliderItemIndex = 0;
                }
                if (filteredItems.Count() != 0)
                {
                    ImGui.ListBox("Items##colliders", ref _colliderItemIndex, filteredItems.ToArray(), filteredItems.Count());
                    string selectedItem = filteredItems.ToArray()[_colliderItemIndex];

                    Dictionary<string, (Action<LineSegmentCollider> setter, Func<LineSegmentCollider> getter)> selectedColliders = _colliders[selectedItem];

                    string[] collidersTypes = selectedColliders.Select(entry => entry.Key).ToArray();

                    ImGui.ListBox("Colliders##colliders", ref _colliderIndex, collidersTypes, collidersTypes.Length);

                    if (collidersTypes.Length > 0)
                    {
                        (Action<LineSegmentCollider> setter, Func<LineSegmentCollider> getter) = selectedColliders[collidersTypes[_colliderIndex]];
                        System.Numerics.Vector3 position = getter().Position.ToSystem();
                        System.Numerics.Vector3 direction = getter().Direction.ToSystem();

                        float sliderSpeed = ImGui.IsKeyPressed(ImGuiKey.LeftShift) ? 0.01f : 0.1f;

                        ImGui.DragFloat3("Position##colliders", ref position, sliderSpeed);
                        ImGui.DragFloat3("Direction##colliders", ref direction, sliderSpeed);

                        _currentCollider = new(position.ToOpenTK(), direction.ToOpenTK());

                        setter(_currentCollider.Value);

                        System.Numerics.Vector3 head = position + direction;

                        string json = $"[{position.X}, {position.Y}, {position.Z}, {head.X}, {head.Y}, {head.Z}]";
                        if (ImGui.Button("To clipboard##colliders"))
                        {
                            ImGui.SetClipboardText(json);
                        }
                        ImGui.SameLine();
                        ImGui.Text($"JSON: {json}");
                    }
                }

                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Debug##tab"))
            {
                bool collidersRender = CollidersEntityBehavior.RenderColliders;
                ImGui.Checkbox("Render entities colliders", ref collidersRender);
                CollidersEntityBehavior.RenderColliders = collidersRender;



                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Generic Display##tab"))
            {
                GenericDisplayTab();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();

            ImGui.End();
        }

        _detachedEditorCamera?.Update(deltaSeconds, _showAnimationEditor);

        return _showAnimationEditor ? CallbackGUIStatus.GrabMouse : CallbackGUIStatus.Closed;
    }

    private void EditFov()
    {
        ClientMain? client = _api.World as ClientMain;
        if (client == null) return;

        PlayerCamera? camera = (PlayerCamera?)_mainCameraInfo.GetValue(client);
        if (camera == null) return;

        float? fovField = (float?)_cameraFov.GetValue(camera);
        if (fovField == null) return;

        float fovMultiplier = PlayerRenderingPatches.HandsFovMultiplier;
        ImGui.SliderFloat("FOV", ref fovMultiplier, 0.5f, 1.5f);
        PlayerRenderingPatches.HandsFovMultiplier = fovMultiplier;
        _cameraFov.SetValue(camera, ClientSettings.FieldOfView * GameMath.DEG2RAD * fovMultiplier);

        ImGui.Text($"FOV: {ClientSettings.FieldOfView * fovMultiplier}");
    }

    private void AnimationsTab()
    {
        string[] codes = AnimationsManager._instance.Animations.Keys.ToArray();

        if (ImGui.Button("Save to buffer"))
        {
            _animationBuffer = AnimationJson.FromAnimation(AnimationsManager._instance.Animations[codes[_selectedAnimationIndex]]);
        }
        ImGui.SameLine();

        if (ImGui.Button("Load from buffer"))
        {
            AnimationsManager._instance.Animations[codes[_selectedAnimationIndex]] = _animationBuffer.ToAnimation();
        }

        if (ImGui.Button("Save buffer to file"))
        {
            _api.StoreModConfig(_animationBuffer, "co-animation-export.json");
        }
        ImGui.SameLine();
        if (ImGui.Button("Load buffer from file"))
        {
            _animationBuffer = _api.LoadModConfig<AnimationJson>("co-animation-export.json");
        }

        if (ImGui.Button("Toggle rendering offset"))
        {
            if (PlayerRenderingPatches.FpHandsOffset != PlayerRenderingPatches.DefaultFpHandsOffset)
            {
                PlayerRenderingPatches.FpHandsOffset = PlayerRenderingPatches.DefaultFpHandsOffset;
            }
            else
            {
                PlayerRenderingPatches.FpHandsOffset = 0;
            }
        }
        ImGui.SameLine();

        bool tpAnimations = PlayAnimationsInThirdPerson;
        ImGui.Checkbox("Third person animations", ref tpAnimations);
        PlayAnimationsInThirdPerson = tpAnimations;

        if (ImGui.Button("Render fp model in tp"))
        {
            _api.World.Player.Entity.ActiveHandItemSlot.Itemstack?.Collectible?.GetCollectibleBehavior<AnimatableAttachable>(true)?.SetSwitchModels(_api.World.Player.Entity.EntityId, true);
        }
        ImGui.SameLine();
        if (ImGui.Button("Switch back"))
        {
            _api.World.Player.Entity.ActiveHandItemSlot.Itemstack?.Collectible?.GetCollectibleBehavior<AnimatableAttachable>(true)?.SetSwitchModels(_api.World.Player.Entity.EntityId, false);
        }

        ImGui.InputTextWithHint("Filter##" + "animations", "supports wildcards", ref _animationsFilter, 200);
        EditorsUtils.FilterElements(_animationsFilter, AnimationsManager._instance.Animations.Keys, out IEnumerable<string> filtered, out IEnumerable<int> indexes);

        ImGui.ListBox("transforms", ref _selectedAnimationIndexFiltered, filtered.ToArray(), filtered.Count());

        if (!filtered.Any()) return;

        if (_selectedAnimationIndexFiltered >= filtered.Count()) _selectedAnimationIndexFiltered = 0;

        _selectedAnimationIndex = AnimationsManager._instance.Animations.Keys.ToArray().IndexOf(filtered.ToArray()[_selectedAnimationIndexFiltered]);

        /*if (ImGui.Button("Remove##animations"))
        {
            Animations.Remove(Animations.Keys.ToArray()[_selectedAnimationIndex]);
            _selectedAnimationIndex--;
            if (_selectedAnimationIndex < 0) _selectedAnimationIndex = 0;
        }*/

        codes = AnimationsManager._instance.Animations.Keys.ToArray();

        if (ImGui.CollapsingHeader($"Add animation"))
        {
            CreateAnimationGui();
        }

        if (_selectedAnimationIndex < AnimationsManager._instance.Animations.Count)
        {
            ImGui.SeparatorText("Animation");

            if (ImGui.Button("Play") && AnimationsManager._instance.Animations.Count > 0)
            {
                AnimationRequest request = new(
                    AnimationsManager._instance.Animations[codes[_selectedAnimationIndex]],
                    _animationSpeed,
                    1,
                    "main",
                    TimeSpan.FromSeconds(0.6),
                    TimeSpan.FromSeconds(0.6),
                    true
                    );

                _behavior?.Play(request);
            }
            ImGui.SameLine();

            if (ImGui.Button("Export to clipboard") && AnimationsManager._instance.Animations.Count > 0)
            {
                ImGui.SetClipboardText(AnimationsManager._instance.Animations[codes[_selectedAnimationIndex]].ToString());
            }
            ImGui.SameLine();
            if (ImGui.Button("Save to source##animation") && AnimationsManager._instance.Animations.Count > 0)
            {
                _transformSaveStatus = TrySaveAnimationToSource(codes[_selectedAnimationIndex], AnimationsManager._instance.Animations[codes[_selectedAnimationIndex]]);
            }
            if (!string.IsNullOrEmpty(_transformSaveStatus))
            {
                ImGui.TextWrapped(_transformSaveStatus);
            }
            ImGui.SameLine();

            ImGui.SetNextItemWidth(200);
            ImGui.SliderFloat("Animation speed", ref _animationSpeed, 0.1f, 2);
            Animation selectedAnimation = AnimationsManager._instance.Animations[codes[_selectedAnimationIndex]];
            selectedAnimation.Edit(codes[_selectedAnimationIndex]);
            DrawRigPoseEditor(codes[_selectedAnimationIndex], selectedAnimation);
            if (_showAnimationEditor)
            {
                SetEditorFrameOverride(selectedAnimation.StillPlayerFrame(selectedAnimation._playerFrameIndex, selectedAnimation._frameProgress));
            }
            else
            {
                SetEditorFrameOverride(null);
            }
        }
    }

    private void TransformEditorTab()
    {
        DrawHeldTransformRegistration();
        ImGui.Separator();

        ImGui.InputTextWithHint("Filter##" + "transforms", "supports wildcards", ref _filter, 200);
        EditorsUtils.FilterElements(_filter, _transforms.Keys, out IEnumerable<string> filtered, out IEnumerable<int> indexes);

        string[] filteredTransforms = filtered.ToArray();
        if (_transformIndex >= filteredTransforms.Length)
        {
            _transformIndex = 0;
        }

        ImGui.ListBox("transforms", ref _transformIndex, filteredTransforms, filteredTransforms.Length);

        if (filteredTransforms.Length == 0) return;

        string currentTransform = filteredTransforms[_transformIndex];

        if (!_transforms.ContainsKey(currentTransform)) return;

        EditableTransform editableTransform = _transforms[currentTransform];
        ModelTransform transform = editableTransform.Transform;

        if (ImGui.Button($"Export to clipboard"))
        {
            ImGui.SetClipboardText(JsonUtil.ToPrettyString(transform));
        }
        ImGui.SameLine();
        if (editableTransform.SaveToSource != null && ImGui.Button("Save to source##transform"))
        {
            editableTransform.Apply?.Invoke(transform);
            _transformSaveStatus = editableTransform.SaveToSource(transform);
        }
        if (!string.IsNullOrEmpty(_transformSaveStatus))
        {
            ImGui.TextWrapped(_transformSaveStatus);
        }

        DrawTransformGizmoControls("transform", transform, GetGizmoContextForTransformCode(currentTransform), editableTransform.Apply);

        float speed = ImGui.GetIO().KeysDown[(int)ImGuiKey.LeftShift] ? 0.1f : 1;

        float scale = transform.ScaleXYZ.X;
        bool changed = ImGui.DragFloat("Scale##transform", ref scale);
        transform.Scale = scale;

        System.Numerics.Vector3 translation = new(transform.Translation.X, transform.Translation.Y, transform.Translation.Z);
        System.Numerics.Vector3 origin = new(transform.Origin.X, transform.Origin.Y, transform.Origin.Z);
        System.Numerics.Vector3 rotation = new(transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z);

        changed |= ImGui.DragFloat3("Translation##transform", ref translation, speed);
        changed |= ImGui.DragFloat3("Origin##transform", ref origin, speed);
        changed |= ImGui.DragFloat3("Rotation##transform", ref rotation, speed);

        transform.Translation.X = translation.X;
        transform.Translation.Y = translation.Y;
        transform.Translation.Z = translation.Z;
        transform.Origin.X = origin.X;
        transform.Origin.Y = origin.Y;
        transform.Origin.Z = origin.Z;
        transform.Rotation.X = rotation.X;
        transform.Rotation.Y = rotation.Y;
        transform.Rotation.Z = rotation.Z;

        if (changed)
        {
            editableTransform.Apply?.Invoke(transform);
        }
    }

    private void DrawHeldTransformRegistration()
    {
        ItemStack? heldStack = _api.World.Player.Entity.RightHandItemSlot.Itemstack;
        CollectibleObject? collectible = heldStack?.Collectible;
        if (collectible == null) return;

        ImGui.Text($"Held collectible: {collectible.Code}");
        ImGui.SetNextItemWidth(260);
        ImGui.Combo("Transform attribute##held-transform-attribute", ref _heldTransformAttributeIndex, DirectTransformAttributeCodes, DirectTransformAttributeCodes.Length);

        string attributeCode = DirectTransformAttributeCodes[_heldTransformAttributeIndex];
        bool exists = collectible.Attributes?[attributeCode].Exists == true;

        if (ImGui.Button(exists ? "Register held transform##held-transform" : "Create held transform##held-transform"))
        {
            ModelTransform transform = exists
                ? collectible.Attributes?[attributeCode].AsObject<ModelTransform>() ?? CreateDefaultTransform()
                : CreateDefaultTransform();

            transform.EnsureDefaultValues();
            if (!exists)
            {
                ApplyDirectTransformAttribute(collectible, attributeCode, transform);
            }

            RegisterTransform(
                transform,
                $"{collectible.Code} / {attributeCode}",
                value => ApplyDirectTransformAttribute(collectible, attributeCode, value),
                value => TrySaveTransformToSource(collectible, attributeCode, value));
        }
    }

    private ModelTransform? _currentTransform;
    private GenericDisplayProto? _currentBlock;
    private BlockPos? _currentDisplayBlockPos;
    private Action<ModelTransform>? _currentDisplayApply;
    private System.Func<ModelTransform, string>? _currentDisplaySaveToSource;
    private Action? _currentDisplayRedraw;
    private string _currentDisplayContext = "";
    private string _displaySaveStatus = "";
    private bool _selected = false;
    private bool _updateMesh = false;
    private void GenericDisplayTab()
    {
        BlockSelection? selection = _api.World.Player.CurrentBlockSelection;
        CollectibleObject? collectible = _api.World.Player.Entity.RightHandItemSlot.Itemstack?.Collectible;

        if (ImGui.Button("Select##GenericDisplayTab") && !_selected && selection?.Block != null && collectible != null)
        {
            SelectDisplayTransform(selection, collectible);
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear selection##GenericDisplayTab"))
        {
            ClearDisplayTransformSelection();
        }
        ImGui.SameLine();
        if (ImGui.Button("Redraw##GenericDisplayTab"))
        {
            _currentDisplayRedraw?.Invoke();
        }

        if (_currentTransform == null || !_selected) return;

        if (!string.IsNullOrEmpty(_currentDisplayContext))
        {
            ImGui.TextWrapped(_currentDisplayContext);
        }

        ModelTransform transform = _currentTransform;

        if (ImGui.Button($"Export to clipboard##GenericDisplayTab"))
        {
            ImGui.SetClipboardText(JsonUtil.ToPrettyString(transform));
        }

        ImGui.SameLine();
        if (_currentDisplaySaveToSource != null && ImGui.Button("Save to source##GenericDisplayTab"))
        {
            _currentDisplayApply?.Invoke(transform);
            _displaySaveStatus = _currentDisplaySaveToSource(transform);
        }

        ImGui.SameLine();
        ImGui.Checkbox("Update mesh", ref _updateMesh);

        if (!string.IsNullOrEmpty(_displaySaveStatus))
        {
            ImGui.TextWrapped(_displaySaveStatus);
        }

        DrawTransformGizmoControls("GenericDisplayTab", transform, TransformGizmoContext.Display, _currentDisplayApply, _currentDisplayBlockPos);

        float speed = ImGui.GetIO().KeysDown[(int)ImGuiKey.LeftShift] ? 0.1f : 1;

        float scale = transform.ScaleXYZ.X;
        bool changed = ImGui.DragFloat("Scale##GenericDisplayTab", ref scale, speed * 0.1f);
        transform.Scale = scale;

        System.Numerics.Vector3 translation = new(transform.Translation.X, transform.Translation.Y, transform.Translation.Z);
        System.Numerics.Vector3 origin = new(transform.Origin.X, transform.Origin.Y, transform.Origin.Z);
        System.Numerics.Vector3 rotation = new(transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z);

        changed |= ImGui.DragFloat3("Translation##GenericDisplayTab", ref translation, speed * 0.1f);
        changed |= ImGui.DragFloat3("Origin##GenericDisplayTab", ref origin, speed * 0.1f);
        changed |= ImGui.DragFloat3("Rotation##GenericDisplayTab", ref rotation, speed);

        transform.Translation.X = translation.X;
        transform.Translation.Y = translation.Y;
        transform.Translation.Z = translation.Z;
        transform.Origin.X = origin.X;
        transform.Origin.Y = origin.Y;
        transform.Origin.Z = origin.Z;
        transform.Rotation.X = rotation.X;
        transform.Rotation.Y = rotation.Y;
        transform.Rotation.Z = rotation.Z;

        if (changed)
        {
            _currentDisplayApply?.Invoke(transform);
        }

        if (_updateMesh && changed)
        {
            _currentDisplayRedraw?.Invoke();
        }

    }

    private void SelectDisplayTransform(BlockSelection selection, CollectibleObject collectible)
    {
        ClearDisplayTransformSelection();

        _currentBlock = selection.Block.GetBlockEntity<GenericDisplayProto>(selection);
        string? transformCode = _currentBlock?.AttributeTransformCode ?? GetSelectedBlockDisplayTransformCode(selection.Block);
        if (transformCode == null)
        {
            _displaySaveStatus = $"No known display transform context for {selection.Block.Code}.";
            return;
        }

        ModelTransform transform = collectible.Attributes?[transformCode].AsObject<ModelTransform>() ?? CreateDefaultTransform();
        transform.EnsureDefaultValues();

        _currentTransform = transform;
        _currentDisplayBlockPos = selection.Position.Copy();
        _currentDisplayContext = $"{selection.Block.Code} -> {collectible.Code} / {transformCode}";
        _currentDisplayApply = value =>
        {
            ApplyDirectTransformAttribute(collectible, transformCode, value);
            if (_currentBlock != null)
            {
                _currentBlock.EditedTransforms[collectible.Id] = value;
            }
        };
        _currentDisplaySaveToSource = value => TrySaveTransformToSource(collectible, transformCode, value);
        _currentDisplayRedraw = () =>
        {
            if (_currentBlock != null)
            {
                _currentBlock.EditedTransforms[collectible.Id] = transform;
                _currentBlock.RegenerateMeshes();
                return;
            }

            selection.Block.GetBlockEntity<BlockEntity>(selection)?.MarkDirty(true);
        };

        _currentDisplayApply(transform);
        _selected = true;
    }

    private void ClearDisplayTransformSelection()
    {
        _currentTransform = null;
        _currentBlock = null;
        _currentDisplayBlockPos = null;
        _currentDisplayApply = null;
        _currentDisplaySaveToSource = null;
        _currentDisplayRedraw = null;
        _currentDisplayContext = "";
        _displaySaveStatus = "";
        _selected = false;
    }

    private static string? GetSelectedBlockDisplayTransformCode(Block block)
    {
        string? configured = block.Attributes?["inventoryTransformAttribute"].AsString();
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        string className = block.GetType().Name;
        string path = block.Code.Path;

        if (className.Contains("Forge", StringComparison.OrdinalIgnoreCase) || path.Contains("forge", StringComparison.OrdinalIgnoreCase)) return "inForgeTransform";
        if (className.Contains("DisplayCase", StringComparison.OrdinalIgnoreCase) || path.Contains("displaycase", StringComparison.OrdinalIgnoreCase)) return "onDisplayTransform";
        if (className.Contains("Shelf", StringComparison.OrdinalIgnoreCase) || path.Contains("shelf", StringComparison.OrdinalIgnoreCase)) return "onshelfTransform";
        if (className.Contains("ToolRack", StringComparison.OrdinalIgnoreCase) || path.Contains("toolrack", StringComparison.OrdinalIgnoreCase)) return "toolrackTransform";
        if (className.Contains("Firepit", StringComparison.OrdinalIgnoreCase) || path.Contains("firepit", StringComparison.OrdinalIgnoreCase)) return "infirepitTransform";
        if (className.Contains("BasketTrap", StringComparison.OrdinalIgnoreCase) || path.Contains("trap", StringComparison.OrdinalIgnoreCase)) return "inTrapTransform";
        if (path.Contains("moldrack", StringComparison.OrdinalIgnoreCase)) return "onmoldrackTransform";
        if (path.Contains("omok", StringComparison.OrdinalIgnoreCase)) return "onOmokTransform";
        if (path.Contains("scrollrack", StringComparison.OrdinalIgnoreCase)) return "onscrollrackTransform";
        if (path.Contains("groundstorage", StringComparison.OrdinalIgnoreCase)) return "groundStorageTransform";

        return null;
    }

    internal bool TryGetActiveTransformGizmo(out ModelTransform transform, out TransformGizmoContext context, out BlockPos? blockPos)
    {
        return TryGetActiveTransformGizmo(out transform, out context, out blockPos, out _);
    }

    internal bool TryGetActiveTransformGizmo(out ModelTransform transform, out TransformGizmoContext context, out BlockPos? blockPos, out Vec3d? worldCenter)
    {
        transform = _activeGizmoTransform!;
        context = _activeGizmoContext;
        blockPos = _activeGizmoBlockPos;
        worldCenter = _activeGizmoWorldCenter;
        return _activeGizmoTransform != null;
    }

    internal void SetGizmoTranslation(float x, float y, float z)
    {
        if (_activeGizmoTransform == null) return;

        _activeGizmoTransform.Translation.X = x;
        _activeGizmoTransform.Translation.Y = y;
        _activeGizmoTransform.Translation.Z = z;
        _activeGizmoApply?.Invoke(_activeGizmoTransform);
    }

    internal void SetGizmoRotation(float x, float y, float z)
    {
        if (_activeGizmoTransform == null) return;

        _activeGizmoTransform.Rotation.X = x;
        _activeGizmoTransform.Rotation.Y = y;
        _activeGizmoTransform.Rotation.Z = z;
        _activeGizmoApply?.Invoke(_activeGizmoTransform);
    }

    internal void SetGizmoScale(float x, float y, float z)
    {
        if (_activeGizmoTransform == null) return;

        _activeGizmoTransform.ScaleXYZ.X = x;
        _activeGizmoTransform.ScaleXYZ.Y = y;
        _activeGizmoTransform.ScaleXYZ.Z = z;
        _activeGizmoApply?.Invoke(_activeGizmoTransform);
    }

    internal bool PointInsideDebugUi()
    {
        return ImGui.GetIO().WantCaptureMouse;
    }

    private void ClearActiveTransformGizmo()
    {
        _activeGizmoTransform = null;
        _activeGizmoApply = null;
        _activeGizmoBlockPos = null;
        _activeGizmoWorldCenter = null;
        _activeGizmoContext = TransformGizmoContext.Free;
    }

    private void DrawTransformGizmoControls(string id, ModelTransform transform, TransformGizmoContext context, Action<ModelTransform>? apply, BlockPos? blockPos = null, Vec3d? worldCenter = null, bool allowMove = true, bool allowScale = true, bool allowRotate = true)
    {
        ImGui.SeparatorText("Gizmo");

        if (GizmoMode == TransformGizmoMode.Move && !allowMove) GizmoMode = allowRotate ? TransformGizmoMode.Rotate : TransformGizmoMode.None;
        if (GizmoMode == TransformGizmoMode.Scale && !allowScale) GizmoMode = allowRotate ? TransformGizmoMode.Rotate : TransformGizmoMode.None;
        if (GizmoMode == TransformGizmoMode.Rotate && !allowRotate) GizmoMode = allowMove ? TransformGizmoMode.Move : TransformGizmoMode.None;

        if (allowMove)
        {
            if (ImGui.RadioButton($"Move##gizmo-mode-{id}", GizmoMode == TransformGizmoMode.Move)) GizmoMode = TransformGizmoMode.Move;
            ImGui.SameLine();
        }
        if (allowScale)
        {
            if (ImGui.RadioButton($"Scale##gizmo-mode-{id}", GizmoMode == TransformGizmoMode.Scale)) GizmoMode = TransformGizmoMode.Scale;
            ImGui.SameLine();
        }
        if (allowRotate)
        {
            if (ImGui.RadioButton($"Rotate##gizmo-mode-{id}", GizmoMode == TransformGizmoMode.Rotate)) GizmoMode = TransformGizmoMode.Rotate;
            ImGui.SameLine();
        }
        if (ImGui.RadioButton($"Off##gizmo-mode-{id}", GizmoMode == TransformGizmoMode.None)) GizmoMode = TransformGizmoMode.None;

        bool localSpace = GizmoLocalSpace;
        if (ImGui.Checkbox($"Local axes##gizmo-local-{id}", ref localSpace)) GizmoLocalSpace = localSpace;
        ImGui.SameLine();
        bool snap = IncludeGizmoInIncrement;
        if (ImGui.Checkbox($"Snap drag##gizmo-snap-{id}", ref snap)) IncludeGizmoInIncrement = snap;

        float increment = TransformGizmoIncrement;
        ImGui.SetNextItemWidth(120);
        if (ImGui.DragFloat($"Increment##gizmo-increment-{id}", ref increment, 0.01f, 0.001f, 10f))
        {
            TransformGizmoIncrement = Math.Max(0.001f, increment);
        }

        ImGui.TextDisabled("Drag colored axes in-world.");

        _activeGizmoTransform = transform;
        _activeGizmoContext = context;
        _activeGizmoApply = apply;
        _activeGizmoBlockPos = blockPos;
        _activeGizmoWorldCenter = worldCenter;
    }

    private void SetEditorFrameOverride(PlayerItemFrame? frame)
    {
        _behavior ??= _api.World.Player.Entity.GetBehavior<FirstPersonAnimationsBehavior>();
        ThirdPersonAnimationsBehavior? thirdPersonBehavior = _api.World.Player.Entity.GetBehavior<ThirdPersonAnimationsBehavior>();

        DebugPoseFreezeActive = frame != null;
        if (_behavior != null) _behavior.FrameOverride = frame;
        if (thirdPersonBehavior != null) thirdPersonBehavior.FrameOverride = frame;
    }

    private static TransformGizmoContext GetGizmoContextForTransformCode(string transformCode)
    {
        if (transformCode.Contains("tpOffHandTransform", StringComparison.OrdinalIgnoreCase)) return TransformGizmoContext.OffHand;
        if (transformCode.Contains("tpHandTransform", StringComparison.OrdinalIgnoreCase)) return TransformGizmoContext.MainHand;
        if (transformCode.Contains("onTongTransform", StringComparison.OrdinalIgnoreCase) ||
            transformCode.Contains("onMetalTongTransform", StringComparison.OrdinalIgnoreCase)) return TransformGizmoContext.MainHand;
        if (transformCode.Contains("groundTransform", StringComparison.OrdinalIgnoreCase) ||
            transformCode.Contains("groundStorageTransform", StringComparison.OrdinalIgnoreCase)) return TransformGizmoContext.Ground;
        if (transformCode.Contains("inForgeTransform", StringComparison.OrdinalIgnoreCase) ||
            transformCode.Contains("DisplayTransform", StringComparison.OrdinalIgnoreCase) ||
            transformCode.Contains("shelfTransform", StringComparison.OrdinalIgnoreCase) ||
            transformCode.Contains("toolrackTransform", StringComparison.OrdinalIgnoreCase) ||
            transformCode.Contains("rackTransform", StringComparison.OrdinalIgnoreCase)) return TransformGizmoContext.Display;

        return TransformGizmoContext.Free;
    }

    private void CreateAnimationGui()
    {
        ImGui.Indent();
        ImGui.SeparatorText("Just player");

        ImGui.InputText("Animation code##playeranimation", ref _playerAnimationKey, 300);

        bool canAddAnimation = !AnimationsManager._instance.Animations.ContainsKey(_playerAnimationKey) && _playerAnimationKey != "";
        if (!canAddAnimation) ImGui.BeginDisabled();
        if (ImGui.Button($"Create##playeranimation"))
        {
            AnimationsManager._instance.Animations.Add(_playerAnimationKey, new Animation(new PLayerKeyFrame[] { PLayerKeyFrame.Zero }));
        }
        if (!canAddAnimation) ImGui.EndDisabled();

        ImGui.SeparatorText("Item + Player");
        CreateFromItemAnimation();
        ImGui.Unindent();
    }
    private void CreateFromItemAnimation()
    {
        Item? item = _api.World.Player.Entity.RightHandItemSlot.Itemstack?.Item;
        if (item == null)
        {
            ImGui.Text("Take item in right hand");
            return;
        }

        Animatable? behavior = item.GetCollectibleBehavior(typeof(Animatable), true) as Animatable;
        if (behavior == null)
        {
            ImGui.Text("Take item with animatable behavior in right hand");
            return;
        }

        Shape? shape = behavior.CurrentShape;
        if (shape == null)
        {
            ImGui.Text("Take item with animatable behavior in right hand");
            return;
        }

        ImGui.InputText($"Item animation code", ref _itemAnimation, 300);
        ImGui.InputText($"New animation code", ref _animationKey, 300);

        bool canCreate = !AnimationsManager._instance.Animations.ContainsKey(_animationKey);

        if (!canCreate) ImGui.BeginDisabled();
        if (ImGui.Button("Create##itemanimation"))
        {
            try
            {
                AnimationsManager._instance.Animations.Add(_animationKey, new Animation(new PLayerKeyFrame[] { PLayerKeyFrame.Zero }, _itemAnimation, shape));
            }
            catch (Exception exception)
            {
                LoggerUtil.Warn(_api, this, $"Error on creating animation: {exception}");
            }
        }
        if (!canCreate) ImGui.EndDisabled();
    }
#endif
}
