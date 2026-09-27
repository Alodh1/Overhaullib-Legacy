using System.Reflection;
using Atlas.XUnit;
using Xunit;

namespace OverhaullibLegacy.AtlasTests;

[Trait("Category", "E2E")]
public sealed class ProjectileMotionScenarios : AtlasScenarioBase
{
    private const string OverhaullibAssemblyName = "OverhaullibLegacyCompat";

    [AtlasScenario(TimeoutMs = 120000)]
    public async Task ProjectileMotionBehaviors_Should_BeRegisteredAndOptIn()
    {
        await World.Ticks(5);

        Type curvedFlight = World.Api.ClassRegistry.GetEntityBehaviorClass("CombatOverhaul:CurvedFlight")
            ?? throw new InvalidOperationException("CombatOverhaul:CurvedFlight was not registered.");
        Type ovalFlight = World.Api.ClassRegistry.GetEntityBehaviorClass("CombatOverhaul:OvalFlight")
            ?? throw new InvalidOperationException("CombatOverhaul:OvalFlight was not registered.");
        Type autoAnimation = World.Api.ClassRegistry.GetEntityBehaviorClass("CombatOverhaul:AutoAnimation")
            ?? throw new InvalidOperationException("CombatOverhaul:AutoAnimation was not registered.");
        Assembly overhaullib = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == OverhaullibAssemblyName)
            ?? throw new InvalidOperationException($"{OverhaullibAssemblyName} was not loaded by Atlas.");
        Type modifier = overhaullib.GetType("CombatOverhaul.RangedSystems.IProjectileMotionModifier", throwOnError: true)!;
        Type projectile = overhaullib.GetType("CombatOverhaul.RangedSystems.ProjectileEntity", throwOnError: true)!;

        Assert.True(modifier.IsAssignableFrom(curvedFlight));
        Assert.True(modifier.IsAssignableFrom(ovalFlight));
        Assert.Equal("CombatOverhaul.RangedSystems.AutoAnimationBehavior", autoAnimation.FullName);
        Assert.NotNull(projectile.GetProperty("MotionModifiers", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(projectile.GetProperty("FlightSeconds", BindingFlags.Public | BindingFlags.Instance));

        object config = Activator.CreateInstance(
            overhaullib.GetType("CombatOverhaul.RangedSystems.CurvedFlightConfig", throwOnError: true)!)!;
        Assert.Equal(0f, (float)config.GetType().GetProperty("HorizontalTurnRate")!.GetValue(config)!);
        Assert.True((float)config.GetType().GetProperty("ReturnAfterSeconds")!.GetValue(config)! < 0);

        object ovalConfig = Activator.CreateInstance(
            overhaullib.GetType("CombatOverhaul.RangedSystems.OvalFlightConfig", throwOnError: true)!)!;
        Assert.Equal(8f, (float)ovalConfig.GetType().GetProperty("MaximumDistance")!.GetValue(ovalConfig)!);
        Assert.Equal(5f, (float)ovalConfig.GetType().GetProperty("LateralRadius")!.GetValue(ovalConfig)!);
        Assert.Equal(1.2f, (float)ovalConfig.GetType().GetProperty("FlightDurationSeconds")!.GetValue(ovalConfig)!);
    }
}
