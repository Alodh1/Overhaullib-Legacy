using CombatOverhaul.Utils;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace CombatOverhaul;

public class TextureFromAttributes : CollectibleBehavior, IContainedMeshSource, IHandBookPageCodeProvider
{
    private const string MeshrefsCacheKey = "CombatOverhaul:TextureFromAttributesMeshrefs";
    private const string MeshUploadFailedKey = "CombatOverhaul:TextureFromAttributesMeshUploadFailed";

    private Dictionary<int, MultiTextureMeshRef> Meshrefs => ObjectCacheUtil.GetOrCreate(_api, MeshrefsCacheKey, () => new Dictionary<int, MultiTextureMeshRef>());
    private ICoreClientAPI? _clientAPI;
    private ICoreAPI? _api;
    private readonly Item _item;
    private List<string> _materialTypes = new();
    private string _textureCode = string.Empty;
    private string _textureAttribute = string.Empty;
    private string _defaultTexture = string.Empty;
    private string[] _creativeTabs = Array.Empty<string>();

    public TextureFromAttributes(CollectibleObject collObj) : base(collObj)
    {
        _item = collObj as Item ?? throw new Exception("Only for items");
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        _api = api;
        _clientAPI = api as ICoreClientAPI;

        AddAllTypesToCreativeInventory();
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        DisposeMeshrefs(api);
        base.OnUnloaded(api);
    }

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        _materialTypes = properties["textureTypes"].AsObject<List<string>>();
        _textureCode = properties["textureCode"].AsString();
        _textureAttribute = properties["textureAttribute"].AsString();
        _defaultTexture = properties["defaultTexture"].AsString();
        _creativeTabs = properties["creativeTabs"].AsObject<string[]>();
    }

    public string[] TextureAttributes => string.IsNullOrWhiteSpace(_textureAttribute) ? Array.Empty<string>() : new[] { _textureAttribute };

    public string HandbookPageCodeForStack(IWorldAccessor world, ItemStack stack)
    {
        return TextureAttributeHandbook.PageCodeForStack(stack, TextureAttributes);
    }

    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
    {
        if (itemstack.TempAttributes.GetInt(MeshUploadFailedKey) != 0) return;

        int meshrefId = itemstack.TempAttributes.GetInt("meshRefId");
        if (meshrefId == 0 || !Meshrefs.TryGetValue(meshrefId, out renderinfo.ModelRef))
        {
            int id = Meshrefs.Count + 1;
            TryUploadMeshRef(capi, itemstack, GenMesh(itemstack, capi.ItemTextureAtlas), id, ref renderinfo);
        }
    }

    public MeshData? GenMesh(ItemStack itemstack, ITextureAtlasAPI targetAtlas)
    {
        ContainedTextureSource textureSource = new(_api as ICoreClientAPI, targetAtlas, new Dictionary<string, AssetLocation>(), $"For render in '{_item.Code}'");

        textureSource.Textures.Clear();

        string? textureName = itemstack.Attributes.GetString(_textureAttribute);

        if (_clientAPI == null) return null;
        if (textureName == null) textureName = "";

        textureSource.Textures[_textureCode] = new AssetLocation(_defaultTexture);

        Shape? shape = _clientAPI.TesselatorManager.GetCachedShape(_item.Shape.Base);

        if (shape == null) return null;

        foreach ((string textureCode, AssetLocation textureLocation) in shape.Textures)
        {
            if (_item.Textures.TryGetValue(textureCode, out CompositeTexture texture))
            {
                textureSource.Textures[textureCode] = texture.Base;
            }
            else
            {
                textureSource.Textures[textureCode] = textureLocation;
            }
        }

        if (textureName != "")
        {
            textureSource.Textures[_textureCode] = new AssetLocation(textureName + ".png");
        }

        try
        {
            _clientAPI.Tesselator.TesselateItem(_item, out MeshData mesh, textureSource);
            return mesh;
        }
        catch (Exception exception)
        {
            LoggerUtil.Warn(_api, this, $"Error on tesselating shape for '{itemstack.Collectible?.Code}':\n{exception}");
            return null;
        }
    }

    public MeshData? GenMesh(ItemStack itemstack, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
    {
        return GenMesh(itemstack, targetAtlas);
    }
    MeshData IContainedMeshSource.GenMesh(ItemSlot inSlot, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
    {
        return GenMesh(inSlot.Itemstack, targetAtlas, atBlockPos) ?? new MeshData();
    }

    public string GetMeshCacheKey(ItemStack itemstack)
    {
        string wood = itemstack.Attributes.GetString(_textureAttribute).Replace('/', '-');
        return _item.Code.ToShortString() + "-" + wood;
    }
    string IContainedMeshSource.GetMeshCacheKey(ItemSlot inSlot) => GetMeshCacheKey(inSlot.Itemstack);

    private void AddAllTypesToCreativeInventory()
    {
        List<JsonItemStack> stacks = new();

        foreach (string material in _materialTypes)
        {
            stacks.Add(GenStackJson(string.Format("{{ {1}: \"{0}\" }}", material, _textureAttribute)));
        }

        JsonItemStack noAttributesStack = new()
        {
            Code = _item.Code,
            Type = EnumItemClass.Item
        };
        noAttributesStack.Resolve(_api.World, "handle type");

        if (_item.CreativeInventoryStacks == null)
        {
            _item.CreativeInventoryStacks = new CreativeTabAndStackList[] {
                new() { Stacks = stacks.ToArray(), Tabs = _creativeTabs },
                new() { Stacks = new JsonItemStack[] { noAttributesStack }, Tabs = _item.CreativeInventoryTabs }
            };
            _item.CreativeInventoryTabs = null;
        }
        else
        {
            _item.CreativeInventoryStacks = _item.CreativeInventoryStacks.Append(new CreativeTabAndStackList() { Stacks = stacks.ToArray(), Tabs = _creativeTabs });
        }
    }
    private JsonItemStack GenStackJson(string json)
    {
        JsonItemStack stackJson = new()
        {
            Code = _item.Code,
            Type = EnumItemClass.Item,
            Attributes = new JsonObject(JToken.Parse(json))
        };

        stackJson.Resolve(_api?.World, "handle type");

        return stackJson;
    }

    private bool TryUploadMeshRef(ICoreClientAPI capi, ItemStack itemstack, MeshData? mesh, int id, ref ItemRenderInfo renderinfo)
    {
        if (mesh == null)
        {
            itemstack.TempAttributes.SetInt(MeshUploadFailedKey, 1);
            LoggerUtil.Warn(_api, this, $"Skipping texture-attributed render for '{itemstack.Collectible?.Code}': generated mesh was null.");
            return false;
        }

        try
        {
            MultiTextureMeshRef modelref = capi.Render.UploadMultiTextureMesh(mesh);
            renderinfo.ModelRef = Meshrefs[id] = modelref;
            itemstack.TempAttributes.SetInt("meshRefId", id);
            return true;
        }
        catch (Exception exception)
        {
            itemstack.TempAttributes.SetInt(MeshUploadFailedKey, 1);
            LoggerUtil.Warn(_api, this, $"Error uploading texture-attributed mesh for '{itemstack.Collectible?.Code}':\n{exception}");
            return false;
        }
    }

    private static void DisposeMeshrefs(ICoreAPI api)
    {
        if (!api.ObjectCache.TryGetValue(MeshrefsCacheKey, out object? value) || value is not Dictionary<int, MultiTextureMeshRef> meshrefs) return;

        foreach (MultiTextureMeshRef meshRef in meshrefs.Values)
        {
            meshRef.Dispose();
        }

        meshrefs.Clear();
        ObjectCacheUtil.Delete(api, MeshrefsCacheKey);
    }
}


public class TextureConfig
{
    public string Code { get; set; } = "";
    public string Attribute { get; set; } = "";
    public string Default { get; set; } = "";
    public string[] HandbookValues { get; set; } = Array.Empty<string>();
}

public class TexturesFromAttributesProperties
{
    public TextureConfig[] Textures { get; set; } = Array.Empty<TextureConfig>();
    public Dictionary<string, TextureConfig[]> TexturesByType { get; set; } = new();
    public string[] CreativeTabs { get; set; } = Array.Empty<string>();
    public bool AddNoAttributesItem { get; set; } = true;
}

public class TexturesFromAttributes : CollectibleBehavior, IContainedMeshSource, IHandBookPageCodeProvider
{
    private const string MeshrefsCacheKey = "CombatOverhaul:TexturesFromAttributesMeshrefs";
    private const string MeshUploadFailedKey = "CombatOverhaul:TexturesFromAttributesMeshUploadFailed";

    private Dictionary<int, MultiTextureMeshRef> Meshrefs => ObjectCacheUtil.GetOrCreate(_api, MeshrefsCacheKey, () => new Dictionary<int, MultiTextureMeshRef>());
    private ICoreClientAPI? _clientAPI;
    private ICoreAPI? _api;
    private readonly Item _item;

    private TexturesFromAttributesProperties? _properties;

    public TexturesFromAttributes(CollectibleObject collObj) : base(collObj)
    {
        _item = collObj as Item ?? throw new Exception("Only for items");
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        _api = api;
        _clientAPI = api as ICoreClientAPI;

        AddAllTypesToCreativeInventory();
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        DisposeMeshrefs(api);
        base.OnUnloaded(api);
    }

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        _properties = properties.AsObject<TexturesFromAttributesProperties>();
    }

    public string[] TextureAttributes => GetAllTextureConfigs()
        .Select(texture => texture.Attribute)
        .Where(attribute => !string.IsNullOrWhiteSpace(attribute))
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public string HandbookPageCodeForStack(IWorldAccessor world, ItemStack stack)
    {
        return TextureAttributeHandbook.PageCodeForStack(stack, TextureAttributes);
    }

    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
    {
        if (itemstack.TempAttributes.GetInt(MeshUploadFailedKey) != 0) return;

        int meshrefId = itemstack.TempAttributes.GetInt("meshRefId");
        if (meshrefId == 0 || !Meshrefs.TryGetValue(meshrefId, out renderinfo.ModelRef))
        {
            int id = Meshrefs.Count + 1;
            TryUploadMeshRef(capi, itemstack, GenMesh(itemstack, capi.ItemTextureAtlas), id, ref renderinfo);
        }
    }

    public MultiTextureMeshRef GetMeshRef(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo, Shape shape)
    {
        if (itemstack.TempAttributes.GetInt(MeshUploadFailedKey) != 0) return renderinfo.ModelRef!;

        int meshrefId = itemstack.TempAttributes.GetInt("meshRefId");
        if (meshrefId == 0 || !Meshrefs.TryGetValue(meshrefId, out renderinfo.ModelRef))
        {
            int id = Meshrefs.Count + 1;
            TryUploadMeshRef(capi, itemstack, GenMesh(itemstack, capi.ItemTextureAtlas, shape), id, ref renderinfo);
        }
        return renderinfo.ModelRef!;
    }

    public MeshData? GenMesh(ItemStack itemstack, ITextureAtlasAPI targetAtlas, Shape? overrideShape = null)
    {
        ContainedTextureSource textureSource = new(_api as ICoreClientAPI, targetAtlas, new Dictionary<string, AssetLocation>(), $"For render in '{_item.Code}'");

        textureSource.Textures.Clear();

        if (_clientAPI == null || _properties == null) return new MeshData();

        Shape? shape = overrideShape ?? _clientAPI.TesselatorManager.GetCachedShape(_item.Shape.Base);

        if (shape == null) return new MeshData();

        foreach ((string textureCode, AssetLocation textureLocation) in shape.Textures)
        {
            if (_item.Textures.TryGetValue(textureCode, out CompositeTexture? texture))
            {
                textureSource.Textures[textureCode] = texture.Base;
            }
            else
            {
                textureSource.Textures[textureCode] = textureLocation;
            }
        }

        foreach (TextureConfig textureProperty in GetTextureConfigs(itemstack))
        {
            string texturePath = itemstack.Attributes.GetString(textureProperty.Attribute) ?? textureProperty.Default;

            textureSource.Textures[textureProperty.Code] = new AssetLocation(texturePath + ".png");
        }

        try
        {
            _clientAPI.Tesselator.TesselateItem(_item, out MeshData mesh, textureSource);
            return mesh;
        }
        catch (Exception exception)
        {
            LoggerUtil.Warn(_api, this, $"Error on tesselating shape for '{itemstack.Collectible?.Code}':\n{exception}");
            return null;
        }
    }

    public MeshData? GenMesh(ItemStack itemstack, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
    {
        return GenMesh(itemstack, targetAtlas);
    }
    MeshData IContainedMeshSource.GenMesh(ItemSlot inSlot, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
    {
        return GenMesh(inSlot.Itemstack, targetAtlas, atBlockPos) ?? new MeshData();
    }

    public string GetMeshCacheKey(ItemStack itemstack)
    {
        string cacheKey = _item.Code.ToShortString();

        foreach (TextureConfig textureProperty in GetTextureConfigs(itemstack))
        {
            string texturePath = itemstack.Attributes.GetString(textureProperty.Attribute) ?? textureProperty.Default;
            cacheKey += "-" + texturePath.Replace('/', '-');
        }

        return cacheKey;
    }
    string IContainedMeshSource.GetMeshCacheKey(ItemSlot inSlot) => GetMeshCacheKey(inSlot.Itemstack);

    private void ConstructStackRecursively(List<JsonItemStack> stacks, string jsonAttributes, TextureConfig[] textureConfigs, int index)
    {
        if (_properties == null) return;

        if (textureConfigs.Length <= index)
        {
            jsonAttributes += "}";
            stacks.Add(GenStackJson(jsonAttributes));
            return;
        }

        TextureConfig textureProperties = textureConfigs[index];

        if (textureProperties.HandbookValues.Length == 0)
        {
            ConstructStackRecursively(stacks, jsonAttributes, textureConfigs, index + 1);
            return;
        }

        if (jsonAttributes != "{") jsonAttributes += ", ";
        foreach (string texturePath in textureProperties.HandbookValues)
        {
            string jsonAttributesCopy = (string)jsonAttributes.Clone();
            jsonAttributesCopy += $"{textureProperties.Attribute}: \"{texturePath}\"";
            ConstructStackRecursively(stacks, jsonAttributesCopy, textureConfigs, index + 1);
        }
    }

    private void AddAllTypesToCreativeInventory()
    {
        if (_properties == null) return;
        
        List<JsonItemStack> stacks = new();

        ConstructStackRecursively(stacks, "{", GetAllTextureConfigs(), 0);

        JsonItemStack noAttributesStack = new()
        {
            Code = _item.Code,
            Type = EnumItemClass.Item
        };
        noAttributesStack.Resolve(_api?.World, "handle type");

        if (_item.CreativeInventoryStacks == null)
        {
            if (_properties.AddNoAttributesItem || stacks.Count == 0)
            {
                _item.CreativeInventoryStacks = new CreativeTabAndStackList[] {
                    new() { Stacks = stacks.ToArray(), Tabs = _properties.CreativeTabs },
                    new() { Stacks = new JsonItemStack[] { noAttributesStack }, Tabs = _item.CreativeInventoryTabs }
                };
                _item.CreativeInventoryTabs = null;
            }
            else
            {
                _item.CreativeInventoryStacks = new CreativeTabAndStackList[] {
                    new() { Stacks = stacks.ToArray(), Tabs = _properties.CreativeTabs },
                    new() { Stacks = new JsonItemStack[] { stacks[0] }, Tabs = _item.CreativeInventoryTabs }
                };
                _item.CreativeInventoryTabs = null;
            }
            
        }
    }
    private JsonItemStack GenStackJson(string json)
    {
        JsonItemStack stackJson = new()
        {
            Code = _item.Code,
            Type = EnumItemClass.Item,
            Attributes = new JsonObject(JToken.Parse(json))
        };

        stackJson.Resolve(_api?.World, "textures type");

        return stackJson;
    }

    private TextureConfig[] GetTextureConfigs(ItemStack itemstack)
    {
        if (_properties == null) return Array.Empty<TextureConfig>();

        List<TextureConfig> textureConfigs = new();
        textureConfigs.AddRange(_properties.Textures);

        string stackCode = itemstack.Collectible?.Code?.ToShortString() ?? _item.Code.ToShortString();
        bool matchedTypedConfig = false;

        foreach ((string pattern, TextureConfig[] typedConfigs) in _properties.TexturesByType)
        {
            if (pattern == "*") continue;
            if (!WildcardUtil.Match(pattern, stackCode)) continue;

            textureConfigs.AddRange(typedConfigs);
            matchedTypedConfig = true;
            break;
        }

        if (!matchedTypedConfig && _properties.TexturesByType.TryGetValue("*", out TextureConfig[]? fallbackConfigs))
        {
            textureConfigs.AddRange(fallbackConfigs);
        }

        return textureConfigs.ToArray();
    }

    private TextureConfig[] GetAllTextureConfigs()
    {
        if (_properties == null) return Array.Empty<TextureConfig>();

        Dictionary<string, TextureConfig> textureConfigs = new(StringComparer.Ordinal);
        AddTextureConfigs(textureConfigs, _properties.Textures);

        foreach (TextureConfig[] typedConfigs in _properties.TexturesByType.Values)
        {
            AddTextureConfigs(textureConfigs, typedConfigs);
        }

        return textureConfigs.Values.ToArray();
    }

    private static void AddTextureConfigs(Dictionary<string, TextureConfig> textureConfigs, IEnumerable<TextureConfig> configs)
    {
        foreach (TextureConfig config in configs)
        {
            string key = $"{config.Code}\n{config.Attribute}";
            textureConfigs.TryAdd(key, config);
        }
    }

    private bool TryUploadMeshRef(ICoreClientAPI capi, ItemStack itemstack, MeshData? mesh, int id, ref ItemRenderInfo renderinfo)
    {
        if (mesh == null)
        {
            itemstack.TempAttributes.SetInt(MeshUploadFailedKey, 1);
            LoggerUtil.Warn(_api, this, $"Skipping texture-attributed render for '{itemstack.Collectible?.Code}': generated mesh was null.");
            return false;
        }

        try
        {
            MultiTextureMeshRef modelref = capi.Render.UploadMultiTextureMesh(mesh);
            renderinfo.ModelRef = Meshrefs[id] = modelref;
            itemstack.TempAttributes.SetInt("meshRefId", id);
            return true;
        }
        catch (Exception exception)
        {
            itemstack.TempAttributes.SetInt(MeshUploadFailedKey, 1);
            LoggerUtil.Warn(_api, this, $"Error uploading texture-attributed mesh for '{itemstack.Collectible?.Code}':\n{exception}");
            return false;
        }
    }

    private static void DisposeMeshrefs(ICoreAPI api)
    {
        if (!api.ObjectCache.TryGetValue(MeshrefsCacheKey, out object? value) || value is not Dictionary<int, MultiTextureMeshRef> meshrefs) return;

        foreach (MultiTextureMeshRef meshRef in meshrefs.Values)
        {
            meshRef.Dispose();
        }

        meshrefs.Clear();
        ObjectCacheUtil.Delete(api, MeshrefsCacheKey);
    }
}
