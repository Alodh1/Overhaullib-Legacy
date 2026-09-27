using System.Reflection;
using Atlas.XUnit;
using Xunit;

namespace OverhaullibLegacy.AtlasTests;

[Trait("Category", "E2E")]
public sealed class ArmorInventoryLifecycleScenarios : AtlasScenarioBase
{
    private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

    [AtlasScenario(TimeoutMs = 120000)]
    public async Task WearableRefresh_Should_Tolerate_Player_Behavior_Initialization()
    {
        await World.Ticks(5);

        var player = await World.JoinPlayer("ArmorLifecycle");
        Assembly overhaullib = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == "OverhaullibLegacyCompat")
            ?? throw new InvalidOperationException("OverhaullibLegacyCompat was not loaded by Atlas.");
        Type inventoryType = overhaullib.GetType("CombatOverhaul.Armor.ArmorInventory", throwOnError: true)!;
        object inventory = Activator.CreateInstance(inventoryType, ["character", player.Player.PlayerUID, World.Api])
            ?? throw new InvalidOperationException("ArmorInventory could not be created.");
        MethodInfo refresh = inventoryType.GetMethod("RefreshWearableStats", InstanceNonPublic)
            ?? throw new MissingMethodException(inventoryType.FullName, "RefreshWearableStats");

        object entityProperties = player.Entity.GetType().BaseType?.GetProperty("Properties")?.GetValue(player.Entity)
            ?? throw new InvalidOperationException("Player entity properties were not available.");
        FieldInfo serverProperties = entityProperties.GetType().GetField("Server")
            ?? throw new MissingFieldException(entityProperties.GetType().FullName, "Server");
        object? originalServerProperties = serverProperties.GetValue(entityProperties);

        try
        {
            serverProperties.SetValue(entityProperties, null);
            refresh.Invoke(inventory, null);
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
        finally
        {
            serverProperties.SetValue(entityProperties, originalServerProperties);
        }
    }
}
