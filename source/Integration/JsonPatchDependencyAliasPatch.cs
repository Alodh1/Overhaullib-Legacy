using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.ServerMods.NoObf;

namespace CombatOverhaul.Integration;

[HarmonyPatch(typeof(ModJsonPatchLoader), nameof(ModJsonPatchLoader.ApplyPatches))]
internal static class JsonPatchDependencyAliasPatch
{
    private static readonly FieldInfo ApiField = AccessTools.Field(typeof(ModJsonPatchLoader), "api");
    private static readonly MethodInfo AssetGetMany = AccessTools.Method(typeof(IAssetManager), nameof(IAssetManager.GetMany), new[] { typeof(string), typeof(string), typeof(bool) });
    private static readonly MethodInfo HashSetContains = AccessTools.Method(typeof(HashSet<string>), nameof(HashSet<string>.Contains), new[] { typeof(string) });
    private static readonly MethodInfo GetManyWithCompatibilityPatchesMethod = AccessTools.Method(typeof(JsonPatchDependencyAliasPatch), nameof(GetManyWithCompatibilityPatches));
    private static readonly MethodInfo ContainsModOrAliasMethod = AccessTools.Method(typeof(JsonPatchDependencyAliasPatch), nameof(ContainsModOrAlias));
    private static readonly object AliasLock = new();
    private static HashSet<string> _loadedAliases = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string[]> ExplicitAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["armory"] = new[] { "armoryfork" },
        ["combatoverhaul"] = new[] { "combatoverhaulfork" },
        ["crossbows"] = new[] { "crossbowsfork" },
        ["firearms"] = new[] { "firearmsfork" },
        ["quiversandsheaths"] = new[] { "quiversfork" },
        ["toolanimations"] = new[] { "toolsanimationsfork" },
        ["toolsanimations"] = new[] { "toolsanimationsfork" }
    };

    private static void Prefix(ModJsonPatchLoader __instance)
    {
        HashSet<string> aliases = new(StringComparer.OrdinalIgnoreCase);

        if (ApiField.GetValue(__instance) is ICoreAPI api)
        {
            foreach (Mod mod in api.ModLoader.Mods)
            {
                AddModIdAliases(aliases, mod.Info.ModID);
            }

            foreach (AssetLocation location in api.Assets.GetLocations("compatibility/", null))
            {
                if (TryGetCompatibilityPatchTarget(location.Path, out _))
                {
                    string domain = location.Domain;
                    if (!string.IsNullOrWhiteSpace(domain))
                    {
                        aliases.Add(domain);
                    }
                }
            }
        }

        lock (AliasLock)
        {
            _loadedAliases = aliases;
        }
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && Equals(instruction.operand, AssetGetMany))
            {
                yield return new CodeInstruction(OpCodes.Call, GetManyWithCompatibilityPatchesMethod);
                continue;
            }

            if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && Equals(instruction.operand, HashSetContains))
            {
                yield return new CodeInstruction(OpCodes.Call, ContainsModOrAliasMethod);
                continue;
            }

            yield return instruction;
        }
    }

    private static List<IAsset> GetManyWithCompatibilityPatches(IAssetManager assets, string pathBegins, string? domain, bool loadAsset)
    {
        List<IAsset> patches = assets.GetMany(pathBegins, domain, loadAsset);

        if (!string.Equals(pathBegins, "patches/", StringComparison.OrdinalIgnoreCase) || domain != null)
        {
            return patches;
        }

        HashSet<string> aliases;
        lock (AliasLock)
        {
            aliases = new HashSet<string>(_loadedAliases, StringComparer.OrdinalIgnoreCase);
        }

        HashSet<string> patchLocations = patches.Select(asset => asset.Location.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (AssetLocation location in assets.GetLocations("compatibility/", null))
        {
            if (!TryGetCompatibilityPatchTarget(location.Path, out string compatibilityTarget))
            {
                continue;
            }

            if (!aliases.Contains(compatibilityTarget))
            {
                continue;
            }

            IAsset? asset = assets.TryGet(location, loadAsset);
            if (asset == null || !patchLocations.Add(asset.Location.ToString()))
            {
                continue;
            }

            patches.Add(asset);
        }

        return patches;
    }

    private static bool ContainsModOrAlias(HashSet<string> modIds, string modId)
    {
        if (ContainsModId(modIds, modId))
        {
            return true;
        }

        lock (AliasLock)
        {
            return _loadedAliases.Contains(modId);
        }
    }

    private static void AddModIdAliases(HashSet<string> aliases, string modId)
    {
        aliases.Add(modId);

        foreach ((string originalModId, string[] forkModIds) in ExplicitAliases)
        {
            foreach (string forkModId in forkModIds)
            {
                if (string.Equals(modId, forkModId, StringComparison.OrdinalIgnoreCase))
                {
                    aliases.Add(originalModId);
                    break;
                }
            }
        }
    }

    private static bool ContainsModId(HashSet<string> modIds, string modId)
    {
        if (modIds.Contains(modId))
        {
            return true;
        }

        foreach (string loadedModId in modIds)
        {
            if (string.Equals(loadedModId, modId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetCompatibilityPatchTarget(string path, out string compatibilityTarget)
    {
        compatibilityTarget = "";

        string normalizedPath = path.Replace('\\', '/');
        const string prefix = "compatibility/";
        if (!normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int targetEnd = normalizedPath.IndexOf('/', prefix.Length);
        if (targetEnd <= prefix.Length)
        {
            return false;
        }

        string remainingPath = normalizedPath[(targetEnd + 1)..];
        if (!remainingPath.StartsWith("patches/", StringComparison.OrdinalIgnoreCase) || !remainingPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        compatibilityTarget = normalizedPath[prefix.Length..targetEnd];
        return compatibilityTarget.Length > 0;
    }
}
