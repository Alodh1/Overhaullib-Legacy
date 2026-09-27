using System.Reflection;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Xunit;

namespace OverhaullibLegacy.AtlasTests;

[Trait("Category", "E2E")]
public sealed class ItemInventoryBufferScenarios : AtlasScenarioBase
{
    private const string OverhaullibAssemblyName = "OverhaullibLegacyCompat";

    [AtlasScenario(TimeoutMs = 120000)]
    public async Task Write_Should_Tolerate_An_Empty_Or_AttributeLess_Slot()
    {
        await World.Ticks(5);

        Assembly overhaullib = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == OverhaullibAssemblyName)
            ?? throw new InvalidOperationException($"{OverhaullibAssemblyName} was not loaded by Atlas.");
        Type bufferType = overhaullib.GetType("CombatOverhaul.RangedSystems.ItemInventoryBuffer", throwOnError: true)!;
        object buffer = Activator.CreateInstance(bufferType)
            ?? throw new InvalidOperationException("ItemInventoryBuffer could not be created.");
        MethodInfo read = bufferType.GetMethod("Read", [typeof(ItemSlot), typeof(string)])
            ?? throw new MissingMethodException(bufferType.FullName, "Read");
        MethodInfo write = bufferType.GetMethod("Write", [typeof(ItemSlot)])
            ?? throw new MissingMethodException(bufferType.FullName, "Write");

        DummySlot emptySlot = new();
        write.Invoke(buffer, [emptySlot]);

        Item item = World.Api.World.GetItem(new AssetLocation("game:stick"))
            ?? throw new InvalidOperationException("The test item was not loaded.");
        ItemStack attributeLessStack = new(item)
        {
            Attributes = null!
        };
        DummySlot attributeLessSlot = new(attributeLessStack);

        read.Invoke(buffer, [attributeLessSlot, "magazine"]);
        write.Invoke(buffer, [attributeLessSlot]);

        Assert.NotNull(attributeLessStack.Attributes);
        Assert.True(attributeLessStack.Attributes.HasAttribute("CombatOverhaul:inventory.magazine"));
    }
}
