using System.Reflection;
using System.Text;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Xunit;

namespace OverhaullibLegacy.AtlasTests;

[Trait("Category", "E2E")]
public sealed class ShieldBehaviorScenarios : AtlasScenarioBase
{
    private const BindingFlags StaticPublic = BindingFlags.Static | BindingFlags.Public;
    private const string OverhaullibAssemblyName = "OverhaullibLegacyCompat";

    [AtlasScenario(TimeoutMs = 120000)]
    public async Task ShieldItems_Should_KeepVanillaBehavior_WhenCombatOverhaulIsAbsent()
    {
        await World.Ticks(5);

        Assembly overhaullib = RequireAssembly(OverhaullibAssemblyName);
        Type classifier = RequireType(overhaullib, "CombatOverhaul.Utils.CollectibleClassifier");
        Type meleeActions = RequireType(overhaullib, "CombatOverhaul.Implementations.IHasMeleeWeaponActions");
        MethodInfo isShield = RequireMethod(classifier, "IsShield", typeof(CollectibleObject));
        MethodInfo isVanillaItemShield = RequireMethod(classifier, "IsVanillaItemShield", typeof(Item));

        Assert.False(World.Api.ModLoader.IsModEnabled("combatoverhaul"));
        Assert.False(World.Api.ModLoader.IsModEnabled("combatoverhaulfork"));

        Item[] shields = World.Api.World.Items
            .Where(item => item?.Code != null && InvokeBool(isShield, item))
            .ToArray();

        Assert.NotEmpty(shields);

        string[] patchedShields = shields
            .Where(item => HasInterface(item, meleeActions))
            .Select(FormatItem)
            .ToArray();

        Assert.True(
            patchedShields.Length == 0,
            "Shield items received Combat Overhaul melee actions without Combat Overhaul:\n" + string.Join("\n", patchedShields)
        );

        Item[] vanillaShields = shields
            .Where(item => item.GetType().FullName == "Vintagestory.GameContent.ItemShield")
            .ToArray();

        Assert.NotEmpty(vanillaShields);

        string[] noLongerVanilla = vanillaShields
            .Where(item => !InvokeBool(isVanillaItemShield, item))
            .Select(FormatItem)
            .ToArray();

        Assert.True(
            noLongerVanilla.Length == 0,
            "Vanilla shield items no longer use vanilla shield behavior:\n" + string.Join("\n", noLongerVanilla)
        );

        string[] vanillaTooltips = shields
            .Select(item => (Item: item, Tooltip: GetTooltip(item)))
            .Where(entry => ContainsVanillaShieldStats(entry.Tooltip))
            .Select(entry => $"{FormatItem(entry.Item)}\n{entry.Tooltip}")
            .ToArray();

        Assert.True(
            vanillaTooltips.Length > 0,
            "No shield retained the vanilla shield tooltip stats."
        );
    }

    private static bool HasInterface(CollectibleObject collectible, Type interfaceType)
    {
        if (interfaceType.IsAssignableFrom(collectible.GetType())) return true;

        return collectible.CollectibleBehaviors?.Any(behavior => interfaceType.IsAssignableFrom(behavior.GetType())) == true;
    }

    private static bool InvokeBool(MethodInfo method, object argument)
    {
        return (bool)(method.Invoke(null, [argument]) ?? false);
    }

    private static string FormatItem(Item item)
    {
        return $"{item.Code} ({item.GetType().FullName})";
    }

    private string GetTooltip(Item item)
    {
        ItemStack stack = new(item);
        DummySlot slot = new(stack);
        StringBuilder tooltip = new();
        item.GetHeldItemInfo(slot, tooltip, World.Api.World, withDebugInfo: false);
        return tooltip.ToString();
    }

    private static bool ContainsVanillaShieldStats(string tooltip)
    {
        return tooltip.Contains("Block chance", StringComparison.OrdinalIgnoreCase)
            || tooltip.Contains("damage absorbed when blocked", StringComparison.OrdinalIgnoreCase)
            || tooltip.Contains("shield-stats", StringComparison.OrdinalIgnoreCase);
    }

    private static Assembly RequireAssembly(string assemblyName)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == assemblyName)
            ?? throw new InvalidOperationException($"{assemblyName} was not loaded by Atlas.");
    }

    private static Type RequireType(Assembly assembly, string fullName)
    {
        return assembly.GetType(fullName, throwOnError: false)
            ?? throw new InvalidOperationException($"{fullName} was not found in {assembly.GetName().Name}.");
    }

    private static MethodInfo RequireMethod(Type type, string name, params Type[] parameters)
    {
        return type.GetMethod(name, StaticPublic, null, parameters, null)
            ?? throw new MissingMethodException(type.FullName, name);
    }
}
