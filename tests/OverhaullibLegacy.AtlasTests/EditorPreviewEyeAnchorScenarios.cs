using System.Reflection;
using Atlas.XUnit;
using Xunit;

namespace OverhaullibLegacy.AtlasTests;

[Trait("Category", "E2E")]
public sealed class EditorPreviewEyeAnchorScenarios : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 120000)]
    public async Task FrozenDevToolsPreview_Should_Not_Add_ModelOnlyVerticalOffset()
    {
        await World.Ticks(5);

        Assembly overhaullib = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == "OverhaullibLegacyCompat")
            ?? throw new InvalidOperationException("OverhaullibLegacyCompat was not loaded by Atlas.");
        Type anchorType = overhaullib.GetType("CombatOverhaul.Animations.EditorPreviewEyeAnchor", throwOnError: true)!;
        MethodInfo resolveY = anchorType.GetMethod("ResolveY", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(anchorType.FullName, "ResolveY");

        float resolved = (float)resolveY.Invoke(null, [1.48f, 1.62f, true])!;

        Assert.Equal(1.62f, resolved, 4);
    }
}
