using System.Reflection;
using Atlas.XUnit;
using Vintagestory.API.Config;
using Xunit;

namespace OverhaullibLegacy.AtlasTests;

[Trait("Category", "E2E")]
public sealed class ArmorQuenchConfigScenarios : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 120000)]
    public async Task JsonConfig_LoadsWithoutConfigLib_PersistsChanges_AndPreservesInvalidFiles()
    {
        await World.Ticks(5);
        const string filename = "overhaulliblegacy-armorquenching.json";
        string path = Path.Combine(GamePaths.ModConfig, filename);
        Assert.True(File.Exists(path));
        string original = File.ReadAllText(path);
        object system = World.Api.ModLoader.GetModSystem("CombatOverhaul.CombatOverhaulSystem");
        Type type = system.GetType();
        object settings = type.GetProperty("Settings")!.GetValue(system)!;
        PropertyInfo flat = settings.GetType().GetProperty("ArmorQuenchFlatReduction")!;
        PropertyInfo plateOnly = settings.GetType().GetProperty("ArmorQuenchPlateOnly")!;
        MethodInfo load = type.GetMethod("ApplyArmorQuenchConfig", BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo save = type.GetMethod("SaveArmorQuenchConfig", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            World.Api.StoreModConfig(new { ArmorQuenchFlatReduction = .37f, ArmorQuenchPlateOnly = true }, filename);
            Assert.True((bool)load.Invoke(system, [World.Api])!);
            Assert.Equal(.37f, flat.GetValue(settings));
            Assert.Equal(true, plateOnly.GetValue(settings));
            Assert.Equal(9, World.Api.LoadModConfig<Dictionary<string, object>>(filename).Count);
            flat.SetValue(settings, .45f);
            save.Invoke(system, [World.Api]);
            flat.SetValue(settings, .1f);
            Assert.True((bool)load.Invoke(system, [World.Api])!);
            Assert.Equal(.45f, flat.GetValue(settings));

            const string invalid = "{\"ArmorQuenchPlateOnly\": false, \"ArmorQuenchFlatReduction\": \"invalid\"}";
            File.WriteAllText(path, invalid);
            Assert.False((bool)load.Invoke(system, [World.Api])!);
            Assert.Equal(true, plateOnly.GetValue(settings));
            Assert.Equal(.45f, flat.GetValue(settings));
            save.Invoke(system, [World.Api]);
            Assert.Equal(invalid, File.ReadAllText(path));
        }
        finally
        {
            File.WriteAllText(path, original);
            load.Invoke(system, [World.Api]);
        }
    }
}
