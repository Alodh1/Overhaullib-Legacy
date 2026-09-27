using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Xunit;

namespace OverhaullibLegacy.AtlasTests;

[Trait("Category", "E2E")]
public sealed class DualWieldInputScenarios : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 120000)]
    public async Task Attack_Input_Should_Reach_Both_Item_Ids_Without_Repeating_Handled_Actions()
    {
        await World.Ticks(5);
        var player = (await World.JoinPlayer("DualInput")).Player.Entity;
        var first = World.Api.World.GetItem(new AssetLocation("game:stick"));
        var second = World.Api.World.GetItem(new AssetLocation("game:flaxfibers"));
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first.Id, second.Id);

        var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "OverhaullibLegacyCompat");
        Type Type(string name) => assembly.GetType("CombatOverhaul.Inputs." + name, true)!;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var managerType = Type("ActionsManagerPlayerBehavior");
        var listenerType = Type("ActionListener");
        var manager = RuntimeHelpers.GetUninitializedObject(managerType);
        var listener = RuntimeHelpers.GetUninitializedObject(listenerType);
        void Set(object target, string field, object value) => target.GetType().GetField(field, flags)!.SetValue(target, value);
        Set(manager, "_player", player);
        Set(manager, "_directionController", RuntimeHelpers.GetUninitializedObject(Type("DirectionController")));
        Set(manager, "<ActionListener>k__BackingField", listener);
        Set(listener, "_subscriptions", Activator.CreateInstance(listenerType.GetField("_subscriptions", flags)!.FieldType)!);
        Set(listener, "_activeActionsSnapshot", Array.Empty<EnumEntityAction>());
        // Exercise the real registration and listener dispatch. Only the weapon's
        // final action is recorded, since this headless world has no renderer.
        var registryField = managerType.GetField("_actionCallbacks", flags)!;
        Set(manager, registryField.Name, Activator.CreateInstance(registryField.FieldType)!);
        var register = managerType.GetMethod("RegisterActionHandlers", flags)!;
        var callbackType = managerType.GetNestedType("ActionEventCallbackDelegate")!;
        var active = Enum.Parse(Type("ActionState"), "Active");
        var eventId = Activator.CreateInstance(Type("ActionEventId"), EnumEntityAction.LeftMouseDown, active)!;
        var calls = new List<(int item, bool main, string handler)>();
        foreach (int id in new[] { first.Id, second.Id })
        {
            var handlers = (IDictionary)Activator.CreateInstance(register.GetParameters()[1].ParameterType)!;
            var callbacks = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(callbackType))!;
            foreach (var (name, handled) in new[] { ("inactive-mode", false), ("attack", true), ("must-not-run", true) })
            {
                var parameters = callbackType.GetMethod("Invoke")!.GetParameters()
                    .Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
                Action<bool> record = main => calls.Add((id, main, name));
                var body = Expression.Block(Expression.Invoke(Expression.Constant(record), parameters[4]), Expression.Constant(handled));
                callbacks.Add(Expression.Lambda(callbackType, body, parameters).Compile());
            }
            handlers.Add(eventId, callbacks);
            register.Invoke(manager, [id, handlers]);
        }

        foreach (var (main, off) in new[] { (second, first), (first, second), (first, first) })
        {
            player.RightHandItemSlot.Itemstack = new ItemStack(main);
            player.LeftHandItemSlot.Itemstack = new ItemStack(off);
            for (int tick = 0; tick < 3; tick++)
            {
                calls.Clear();
                Assert.True((bool)listenerType.GetMethod("CallSubscriptionsForState", flags)!.Invoke(listener, [EnumEntityAction.LeftMouseDown, active])!);
                Assert.Contains((main.Id, true, "attack"), calls);
                Assert.Contains((off.Id, false, "attack"), calls);
                Assert.Equal(2, calls.Count(c => c.handler == "attack"));
                Assert.DoesNotContain(calls, c => c.handler == "must-not-run");
            }
        }
    }
}
