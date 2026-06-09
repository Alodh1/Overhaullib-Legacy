using CombatOverhaul.Utils;
using Newtonsoft.Json.Linq;
using System.Diagnostics.CodeAnalysis;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace CombatOverhaul.Animations;

public sealed class AnimationsManager
{
    public Dictionary<string, Animation> Animations { get; private set; } = new();
    internal Dictionary<string, AnimationSource> AnimationSources { get; } = new();

    public AnimationsManager(ICoreClientAPI api, ParticleEffectsManager particleEffectsManager)
    {
        _api = api;
    }
    public void Load()
    {
        List<IAsset> configAnimations = _api.Assets.GetManyInCategory("config", "animations");
#if DEBUG
        List<IAsset> shapeAssets = _api.Assets.GetManyInCategory("shapes", "");
#endif

        Dictionary<string, Animation> animationsByCode = new();
        AnimationSources.Clear();
        IEnumerable<LoadedAnimation> loadedAnimations = configAnimations.SelectMany(FromAsset);
#if DEBUG
        loadedAnimations = loadedAnimations.Concat(shapeAssets.SelectMany(FromShapeAsset));
#endif

        foreach (LoadedAnimation loadedAnimation in loadedAnimations)
        {
            if (!animationsByCode.TryAdd(loadedAnimation.Code, loadedAnimation.Animation))
            {
                LoggerUtil.Warn(_api, this, $"Duplicate animation code '{loadedAnimation.Code}' from '{loadedAnimation.Source.DisplayPath}' ignored.");
                continue;
            }

            AnimationSources[loadedAnimation.Code] = loadedAnimation.Source;
        }

        Animations = animationsByCode;
    }

    public Animation? GetAnimation(string code, params string[] tags)
    {
        return GetAnimationRecursive(code, tags);
    }

    public Animation? GetAnimation(string code, EntityPlayer player, bool firstPerson = true)
    {
        return GetAnimationRecursive(code, GetTags(player, firstPerson).ToArray());
    }
    
    public bool GetAnimation([NotNullWhen(true)] out Animation? animation, string code, EntityPlayer player, bool firstPerson = true)
    {
        animation = GetAnimationRecursive(code, GetTags(player, firstPerson).ToArray());
        return animation != null;
    }

    public IEnumerable<string> GetTags(EntityPlayer player, bool firstPerson = true)
    {
        List<string> tags = [];

        if (!firstPerson)
        {
            tags.Add("tp");
        }

        float intoxication = player.WatchedAttributes.GetFloat("intoxication");

        if (intoxication > 0.1f)
        {
            tags.Add("drunk");
        }

        string modelPrefix = player.WatchedAttributes.GetString("skinModel", "").Replace(':', '-');
        if (modelPrefix != "")
        {
            tags.Add(modelPrefix);
        }

        return tags;
    }

    private readonly ICoreClientAPI _api;
    private Animation? GetAnimationRecursive(string code, IReadOnlyList<string> tags)
    {
        bool[] excluded = tags.Count == 0 ? [] : new bool[tags.Count];
        return GetAnimationRecursive(code, tags, excluded, tags.Count);
    }

    private Animation? GetAnimationRecursive(string code, IReadOnlyList<string> tags, bool[] excluded, int remainingTags)
    {
        if (remainingTags > 0)
        {
            for (int index = 0; index < tags.Count; index++)
            {
                if (excluded[index]) continue;

                excluded[index] = true;
                Animation? result = GetAnimationRecursive($"{code}-{tags[index]}", tags, excluded, remainingTags - 1);
                excluded[index] = false;

                if (result != null) return result;
            }

            for (int index = 0; index < tags.Count; index++)
            {
                if (excluded[index]) continue;

                excluded[index] = true;
                Animation? result = GetAnimationRecursive(code, tags, excluded, remainingTags - 1);
                excluded[index] = false;

                if (result != null) return result;
            }
        }

        if (Animations.TryGetValue(code, out Animation finalResult))
        {
            return finalResult;
        }

        return null;
    }

    private IEnumerable<LoadedAnimation> FromAsset(IAsset asset)
    {
        List<LoadedAnimation> result = new();

        string domain = asset.Location.Domain;

        JsonObject json;

        try
        {
            json = JsonObject.FromJson(asset.ToText());
        }
        catch (Exception exception)
        {
            LoggerUtil.Error(_api, this, $"Error on parsing animations file '{asset.Location}'.\nException: {exception}");
            return result;
        }

        foreach (KeyValuePair<string, JToken?> entry in json.Token as JObject)
        {
            string code = entry.Key;

            try
            {
                JsonObject animationJson = new(entry.Value);

                Animation animation = animationJson.AsObject<AnimationJson>().ToAnimation();

                string animationCode = code.Contains(':') ? code : $"{domain}:{code}";

                result.Add(new(animationCode, animation, new(domain, code)));
            }
            catch (Exception exception)
            {
                LoggerUtil.Error(_api, this, $"Error on parsing animation '{code}' from '{asset.Location}'.\nException: {exception}");
            }
        }

        return result;
    }

    private IEnumerable<LoadedAnimation> FromShapeAsset(IAsset asset)
    {
        List<LoadedAnimation> result = new();

        Shape shape;
        try
        {
            shape = asset.ToObject<Shape>();
            shape.ResolveReferences(_api.Logger, asset.Location.ToString());
        }
        catch (Exception exception)
        {
            LoggerUtil.Warn(_api, this, $"Error on parsing shape animations file '{asset.Location}'.\nException: {exception}");
            return result;
        }

        if (shape.Animations == null || shape.Animations.Length == 0)
        {
            return result;
        }

        foreach (Vintagestory.API.Common.Animation vanillaAnimation in shape.Animations)
        {
            if (string.IsNullOrWhiteSpace(vanillaAnimation.Code) || vanillaAnimation.KeyFrames == null || vanillaAnimation.KeyFrames.Length == 0)
            {
                continue;
            }

            try
            {
                string animationCode = BuildShapeAnimationCode(asset.Location, vanillaAnimation.Code);
                Animation animation = FromVanillaShapeAnimation(vanillaAnimation);
                AnimationSource source = new(
                    asset.Location.Domain,
                    vanillaAnimation.Code,
                    AnimationSourceKind.ShapeAnimation,
                    asset.Location.Path,
                    vanillaAnimation.Code);

                result.Add(new(animationCode, animation, source));
            }
            catch (Exception exception)
            {
                LoggerUtil.Warn(_api, this, $"Error on importing shape animation '{vanillaAnimation.Code}' from '{asset.Location}'.\nException: {exception}");
            }
        }

        return result;
    }

    private static Animation FromVanillaShapeAnimation(Vintagestory.API.Common.Animation vanillaAnimation)
    {
        List<PLayerKeyFrame> playerFrames = PLayerKeyFrame.FromVanillaAnimation(vanillaAnimation, out bool hasPlayerFrames);
        if (hasPlayerFrames)
        {
            return new(playerFrames);
        }

        List<ItemKeyFrame> itemFrames = ItemKeyFrame.FromVanillaAnimation(vanillaAnimation);
        return new(playerFrames, itemFrames);
    }

    private static string BuildShapeAnimationCode(AssetLocation location, string vanillaAnimationCode)
    {
        string shapePath = GetShapePathWithoutPrefixAndExtension(location.Path);
        return $"{location.Domain}:shape/{shapePath}/{vanillaAnimationCode}";
    }

    private static string GetShapePathWithoutPrefixAndExtension(string assetPath)
    {
        string path = assetPath.Replace('\\', '/');
        const string prefix = "shapes/";
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            path = path[prefix.Length..];
        }

        const string extension = ".json";
        if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^extension.Length];
        }

        return path;
    }

    private sealed record LoadedAnimation(string Code, Animation Animation, AnimationSource Source);
}

internal enum AnimationSourceKind
{
    ConfigAnimation,
    ShapeAnimation
}

internal sealed record AnimationSource(
    string Domain,
    string SourceKey,
    AnimationSourceKind Kind = AnimationSourceKind.ConfigAnimation,
    string? AssetPath = null,
    string? ShapeAnimationCode = null)
{
    public string DisplayPath => Kind == AnimationSourceKind.ShapeAnimation && AssetPath != null
        ? $"{Domain}:{AssetPath}#{ShapeAnimationCode}"
        : $"{Domain}:{SourceKey}";
}
