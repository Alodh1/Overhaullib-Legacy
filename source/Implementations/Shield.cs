﻿using CombatOverhaul.Utils;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace CombatOverhaul.Implementations;

public class VanillaShield : MeleeWeapon, IContainedMeshSource
{
    private const string HandbookRepresentativeAttribute = "combatOverhaulHandbookRepresentative";

    public string Construction => Variant["construction"];

    private Dictionary<int, MultiTextureMeshRef> Meshrefs => ObjectCacheUtil.GetOrCreate(api, "shieldmeshrefs", () => new Dictionary<int, MultiTextureMeshRef>());
    private ICoreClientAPI? _clientAPI;
    private Dictionary<string, Dictionary<string, int>> _durabilityGains = new();

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        _clientAPI = api as ICoreClientAPI;
        _durabilityGains = Attributes?["durabilityGains"].AsObject<Dictionary<string, Dictionary<string, int>>>() ?? new();

        AddAllTypesToCreativeInventory();
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        if (api.ObjectCache.ContainsKey("shieldmeshrefs") && Meshrefs.Count > 0)
        {
            foreach (var (_, meshRef) in Meshrefs)
            {
                meshRef.Dispose();
            }

            ObjectCacheUtil.Delete(api, "shieldmeshrefs");
        }

        base.OnUnloaded(api);
    }

    public override int GetMaxDurability(ItemStack itemstack)
    {
        int gain = 0;

        if (itemstack != null)
        {
            foreach (var val in _durabilityGains)
            {
                string mat = itemstack.Attributes?.GetString(val.Key);
                if (mat != null)
                {
                    val.Value.TryGetValue(mat, out var matgain);
                    gain += matgain;
                }
            }
        }

        return base.GetMaxDurability(itemstack) + gain;
    }

    public override List<ItemStack> GetHandBookStacks(ICoreClientAPI capi)
    {
        if (Code == null) return null;
        if (Attributes?["handbook"]?["exclude"].AsBool() == true) return null;
        if (Construction == "crude" || Construction == "blackguard") return base.GetHandBookStacks(capi);

        ItemStack stack = new(this);
        stack.Attributes.SetBool(HandbookRepresentativeAttribute, true);
        stack.TempAttributes.SetBool(HandbookRepresentativeAttribute, true);

        switch (Construction)
        {
            case "woodmetal":
                stack.Attributes.SetString("metal", "tinbronze");
                stack.Attributes.SetString("wood", "generic");
                stack.Attributes.SetString("deco", "none");
                break;

            case "woodmetalleather":
                stack.Attributes.SetString("metal", "tinbronze");
                stack.Attributes.SetString("wood", "generic");
                stack.Attributes.SetString("color", "brown");
                stack.Attributes.SetString("deco", "none");
                break;

            case "metal":
                stack.Attributes.SetString("metal", "tinbronze");
                stack.Attributes.SetString("color", "brown");
                stack.Attributes.SetString("deco", "none");
                break;
        }

        return new List<ItemStack> { stack };
    }

    public override bool Satisfies(ItemStack thisStack, ItemStack otherStack)
    {
        if (IsHandbookRepresentative(otherStack)
            && thisStack?.Collectible == this
            && otherStack.Collectible == this
            && thisStack.Class == otherStack.Class
            && thisStack.Id == otherStack.Id)
        {
            return true;
        }

        return base.Satisfies(thisStack, otherStack);
    }

    public void AddAllTypesToCreativeInventory()
    {
        if (Construction == "crude" || Construction == "blackguard") return;

        List<JsonItemStack> stacks = new List<JsonItemStack>();

        var vg = Attributes?["variantGroups"].AsObject<Dictionary<string, string[]>>();
        if (vg == null || !vg.ContainsKey("metal")) return;

        foreach (var metal in vg["metal"])
        {
            switch (Construction)
            {
                case "woodmetal":
                    if (!vg.ContainsKey("wood")) break;

                    foreach (var wood in vg["wood"])
                    {
                        stacks.Add(GenJstack(string.Format("{{ wood: \"{0}\", metal: \"{1}\", deco: \"none\" }}", wood, metal)));
                    }
                    break;

                case "woodmetalleather":
                    if (!vg.ContainsKey("color")) break;

                    foreach (var color in vg["color"])
                    {
                        stacks.Add(GenJstack(string.Format("{{ wood: \"{0}\", metal: \"{1}\", color: \"{2}\", deco: \"none\" }}", "generic", metal, color)));
                        if (color != "redblack")
                        {
                            stacks.Add(GenJstack(string.Format("{{ wood: \"{0}\", metal: \"{1}\", color: \"{2}\", deco: \"ornate\" }}", "generic", metal, color)));
                        }
                    }
                    break;

                case "metal":
                    stacks.Add(GenJstack(string.Format("{{ wood: \"{0}\", metal: \"{1}\", deco: \"none\" }}", "generic", metal)));

                    if (!vg.ContainsKey("color")) break;

                    foreach (var color in vg["color"])
                    {
                        if (color != "redblack")
                        {
                            stacks.Add(GenJstack(string.Format("{{ wood: \"{0}\", metal: \"{1}\", color: \"{2}\", deco: \"ornate\" }}", "generic", metal, color)));
                        }
                    }
                    break;
            }
        }

        CreativeInventoryStacks = new CreativeTabAndStackList[]
        {
            new CreativeTabAndStackList { Stacks = stacks.ToArray(), Tabs = new[] { "general", "items", "tools" } }
        };
    }

    private JsonItemStack GenJstack(string json)
    {
        var jstack = new JsonItemStack
        {
            Code = Code,
            Type = EnumItemClass.Item,
            Attributes = new JsonObject(JToken.Parse(json))
        };

        jstack.Resolve(api.World, "shield type");

        return jstack;
    }

    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
    {
        int meshrefid = itemstack.TempAttributes.GetInt("meshRefId");
        if (meshrefid == 0 || !Meshrefs.TryGetValue(meshrefid, out renderinfo.ModelRef))
        {
            int id = Meshrefs.Count + 1;
            var modelref = capi.Render.UploadMultiTextureMesh(GenMesh(itemstack, capi.ItemTextureAtlas));
            renderinfo.ModelRef = Meshrefs[id] = modelref;

            itemstack.TempAttributes.SetInt("meshRefId", id);
        }

        base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
    }

    public MeshData GenMesh(ItemStack itemstack, ITextureAtlasAPI targetAtlas)
    {
        var cnts = new ContainedTextureSource(api as ICoreClientAPI, targetAtlas, new Dictionary<string, AssetLocation>(), string.Format("For render in shield {0}", Code));

        MeshData mesh;
        cnts.Textures.Clear();

        string wood = itemstack.Attributes.GetString("wood");
        string metal = itemstack.Attributes.GetString("metal");
        string color = itemstack.Attributes.GetString("color");
        string deco = itemstack.Attributes.GetString("deco");

        if (wood == null && metal == null && Construction != "crude" && Construction != "blackguard") return new MeshData();

        if (wood == null || wood == "") wood = "generic";

        cnts.Textures["front"] = cnts.Textures["back"] = cnts.Textures["handle"] = new AssetLocation("block/wood/planks/generic.png");

        var shape = _clientAPI?.TesselatorManager.GetCachedShape(Shape.Base);
        if (shape == null) return new MeshData();

        foreach (var ctex in shape.Textures)
        {
            cnts.Textures[ctex.Key] = ctex.Value;
        }

        switch (Construction)
        {
            case "crude":
                break;

            case "blackguard":
                break;

            case "woodmetal":
                if (wood != "generic")
                {
                    cnts.Textures["handle"] = cnts.Textures["back"] = cnts.Textures["front"] = new AssetLocation("block/wood/debarked/" + wood + ".png");
                }

                cnts.Textures["rim"] = new AssetLocation("block/metal/sheet/" + metal + "1.png");

                if (deco == "ornate")
                {
                    cnts.Textures["front"] = new AssetLocation("item/tool/shield/ornate/" + color + ".png");
                }
                break;

            case "woodmetalleather":
                if (wood != "generic")
                {
                    cnts.Textures["handle"] = cnts.Textures["back"] = cnts.Textures["front"] = new AssetLocation("block/wood/debarked/" + wood + ".png");
                }

                cnts.Textures["front"] = new AssetLocation("item/tool/shield/leather/" + color + ".png");
                cnts.Textures["rim"] = new AssetLocation("block/metal/sheet/" + metal + "1.png");

                if (deco == "ornate")
                {
                    cnts.Textures["front"] = new AssetLocation("item/tool/shield/ornate/" + color + ".png");
                }
                break;

            case "metal":
                cnts.Textures["rim"] = cnts.Textures["handle"] = new AssetLocation("block/metal/sheet/" + metal + "1.png");
                cnts.Textures["front"] = cnts.Textures["back"] = new AssetLocation("block/metal/plate/" + metal + ".png");

                if (deco == "ornate")
                {
                    cnts.Textures["front"] = new AssetLocation("item/tool/shield/ornate/" + color + ".png");
                }
                break;
        }

        _clientAPI.Tesselator.TesselateItem(this, out mesh, cnts);

        return mesh;
    }

    public override string GetHeldItemName(ItemStack itemStack)
    {
        if (IsHandbookRepresentative(itemStack))
        {
            return Construction switch
            {
                "woodmetal" => Lang.Get("Wooden shield"),
                "woodmetalleather" => Lang.Get("Leather reinforced wooden shield"),
                "metal" => Lang.Get("Metal shield"),
                _ => base.GetHeldItemName(itemStack)
            };
        }

        bool ornate = itemStack.Attributes.GetString("deco") == "ornate";
        string metal = itemStack.Attributes.GetString("metal");
        string wood = itemStack.Attributes.GetString("wood");
        string color = itemStack.Attributes.GetString("color");

        switch (Construction)
        {
            case "crude":
                return Lang.Get("Crude shield");

            case "woodmetal":
                if (wood == "generic")
                {
                    return ornate ? Lang.Get("Ornate wooden shield") : Lang.Get("Wooden shield");
                }

                if (wood == "aged")
                {
                    return ornate ? Lang.Get("Aged ornate shield") : Lang.Get("Aged wooden shield");
                }

                return ornate ? Lang.Get("Ornate {0} shield", Lang.Get("material-" + wood)) : Lang.Get("{0} shield", Lang.Get("material-" + wood));

            case "woodmetalleather":
                return ornate ? Lang.Get("Ornate leather reinforced wooden shield") : Lang.Get("Leather reinforced wooden shield");

            case "metal":
                return GetMetalShieldName(metal, color, ornate);

            case "blackguard":
                return Lang.Get("Blackguard shield");
        }

        return base.GetHeldItemName(itemStack);
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        if (inSlot.Itemstack == null) return;

        switch (Construction)
        {
            case "woodmetal":
                if (IsHandbookRepresentative(inSlot.Itemstack)) return;

                dsc.AppendLine(Lang.Get("shield-woodtype", Lang.Get("material-" + inSlot.Itemstack.Attributes.GetString("wood"))));
                dsc.AppendLine(Lang.Get("shield-metaltype", Lang.Get("material-" + inSlot.Itemstack.Attributes.GetString("metal"))));
                break;

            case "woodmetalleather":
                if (IsHandbookRepresentative(inSlot.Itemstack)) return;

                dsc.AppendLine(Lang.Get("shield-metaltype", Lang.Get("material-" + inSlot.Itemstack.Attributes.GetString("metal"))));
                break;
        }
    }

    private string GetMetalShieldName(string? metal, string? color, bool ornate)
    {
        if (!string.IsNullOrEmpty(metal))
        {
            string key = ornate && !string.IsNullOrEmpty(color)
                ? $"item-{Code.Path}-{metal}-{color}-ornate"
                : $"item-{Code.Path}-{metal}-none";
            string? vanillaName = Lang.GetMatchingIfExists(key);
            if (!string.IsNullOrEmpty(vanillaName)) return vanillaName;
        }

        string materialName = GetMaterialName(metal);
        return Lang.Get("{0} round shield", materialName);
    }

    private static string GetMaterialName(string? material)
    {
        if (string.IsNullOrEmpty(material)) return Lang.Get("Metal");

        string key = "material-" + material;
        string translated = Lang.Get(key);
        return string.Equals(translated, key, StringComparison.Ordinal) ? HumanizeMaterialCode(material) : translated;
    }

    private static string HumanizeMaterialCode(string material)
    {
        return material switch
        {
            "tinbronze" => "Tin bronze",
            "bismuthbronze" => "Bismuth bronze",
            "blackbronze" => "Black bronze",
            "meteoriciron" => "Meteoric iron",
            _ when material.Length > 1 => char.ToUpperInvariant(material[0]) + material[1..],
            _ => material.ToUpperInvariant()
        };
    }

    public MeshData GenMesh(ItemStack itemstack, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
    {
        return GenMesh(itemstack, targetAtlas);
    }

    MeshData IContainedMeshSource.GenMesh(ItemSlot inSlot, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
    {
        return GenMesh(inSlot.Itemstack, targetAtlas, atBlockPos);
    }

    public string GetMeshCacheKey(ItemStack itemstack)
    {
        string wood = itemstack.Attributes.GetString("wood");
        string metal = itemstack.Attributes.GetString("metal");
        string color = itemstack.Attributes.GetString("color");
        string deco = itemstack.Attributes.GetString("deco");

        return Code.ToShortString() + "-" + wood + "-" + metal + "-" + color + "-" + deco;
    }

    string IContainedMeshSource.GetMeshCacheKey(ItemSlot inSlot) => GetMeshCacheKey(inSlot.Itemstack);

    public override void OnCreatedByCrafting(ItemSlot[] allInputslots, ItemSlot outputSlot, IRecipeBase byRecipe)
    {
        base.OnCreatedByCrafting(allInputslots, outputSlot, byRecipe);

        GeneralUtils.MarkItemStack(outputSlot);
        outputSlot.MarkDirty();
    }

    private static bool IsHandbookRepresentative(ItemStack? itemStack)
    {
        return itemStack?.TempAttributes?.GetBool(HandbookRepresentativeAttribute, false) == true
            || itemStack?.Attributes?.GetBool(HandbookRepresentativeAttribute, false) == true;
    }
}
