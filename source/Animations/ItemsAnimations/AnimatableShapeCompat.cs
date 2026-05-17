using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using System.Collections.Generic;
using System;

namespace CombatOverhaul.Animations;

public sealed class AnimatableShape : IDisposable
{
    public static bool HadRenderErrorLastCall { get; private set; } = false;
    public Shape Shape { get; }
    public MultiTextureMeshRef MeshRef { get; }

    private readonly ICoreClientAPI _api;

    private AnimatableShape(ICoreClientAPI api, Shape shape, MultiTextureMeshRef meshRef)
    {
        _api = api;
        Shape = shape;
        MeshRef = meshRef;
    }

    public static AnimatableShape Create(ICoreClientAPI api, string shapePath, Item item)
    {
        AssetLocation shapeLoc = AssetLocation.Create(shapePath, item.Code.Domain);
        if (!shapeLoc.Path.StartsWith("shapes/")) shapeLoc = shapeLoc.CopyWithPath("shapes/" + shapeLoc.Path);
        if (!shapeLoc.Path.EndsWith(".json")) shapeLoc = shapeLoc.CopyWithPath(shapeLoc.Path + ".json");

        Shape shape = Vintagestory.API.Common.Shape.TryGet(api, shapeLoc) ?? api.TesselatorManager.GetCachedShape(item.Shape.Base);
        api.Tesselator.TesselateShape(item, shape, out MeshData meshData);
        MultiTextureMeshRef meshRef = api.Render.UploadMultiTextureMesh(meshData);
        return new AnimatableShape(api, shape, meshRef);
    }

    public AnimatorBase? GetAnimator(long entityId) => null;

    public void Render(IShaderProgram shaderProgram, ItemRenderInfo itemStackRenderInfo, IRenderAPI render, ItemStack itemStack, Vec4f lightrgbs, Matrixf itemModelMat, Entity entity, float dt, MultiTextureMeshRef? meshOverride = null)
    {
        HadRenderErrorLastCall = false;
        MultiTextureMeshRef mesh = meshOverride ?? MeshRef;

        if (!TryBindItemTexture(shaderProgram, itemStackRenderInfo.TextureId))
        {
            HadRenderErrorLastCall = true;
            return;
        }

        if (!TrySetUniformMatrix(shaderProgram, "modelMatrix", itemModelMat.Values))
        {
            HadRenderErrorLastCall = true;
            return;
        }
        TrySetUniformColor(shaderProgram, "rgbaIn", lightrgbs);
        try
        {
            render.RenderMultiTextureMesh(mesh, "tex2d", 0);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            // Shader layout differs in newer API versions; skip this draw call instead of crashing.
            HadRenderErrorLastCall = true;
        }
    }

    private static bool TryBindItemTexture(IShaderProgram shaderProgram, int textureId)
    {
        string[] samplerCandidates = ["tex2d", "itemTex", "tex"];
        foreach (string sampler in samplerCandidates)
        {
            try
            {
                shaderProgram.BindTexture2D(sampler, textureId, 0);
                return true;
            }
            catch (KeyNotFoundException)
            {
            }
        }

        return false;
    }

    private static bool TrySetUniformMatrix(IShaderProgram shaderProgram, string uniformName, float[] values)
    {
        try
        {
            shaderProgram.UniformMatrix(uniformName, values);
            return true;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TrySetUniformColor(IShaderProgram shaderProgram, string uniformName, Vec4f values)
    {
        try
        {
            shaderProgram.Uniform(uniformName, values);
            return true;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        MeshRef.Dispose();
    }
}
