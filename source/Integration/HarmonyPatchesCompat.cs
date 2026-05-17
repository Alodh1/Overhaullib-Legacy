using Vintagestory.API.Common;

namespace CombatOverhaul.Integration;

internal static class HarmonyPatches
{
    public static Settings? ClientSettings { get; set; }
    public static Settings? ServerSettings { get; set; }

    public static void Patch(string harmonyId, ICoreAPI api) { }
    public static void Unpatch(string harmonyId, ICoreAPI api) { }
}
