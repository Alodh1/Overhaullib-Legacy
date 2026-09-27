using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Atlas.XUnit;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Xunit;

namespace OverhaullibLegacy.AtlasTests;

[Trait("Category", "E2E")]
public sealed class DualWieldScenarios : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 120000)]
    public async Task BothHands_Should_Keep_Their_Poses_Attacks_And_Sequence()
    {
        await World.Ticks(5);
        var player = (await World.JoinPlayer("DualWield")).Player.Entity;
        var stick = World.Api.World.GetItem(new AssetLocation("game:stick"));
        player.RightHandItemSlot.Itemstack = new ItemStack(stick);
        player.LeftHandItemSlot.Itemstack = new ItemStack(stick);

        var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "OverhaullibLegacyCompat");
        Type Type(string name) => assembly.GetType(name, true)!;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var clientType = Type("CombatOverhaul.Implementations.MeleeWeaponClient");
        // Run the real client selection methods against Atlas's player and inventory;
        // omit construction of graphics, sound and network services they don't use.
        var client = RuntimeHelpers.GetUninitializedObject(clientType);
        var actionsType = Type("CombatOverhaul.Inputs.ActionsManagerPlayerBehavior");
        var actions = RuntimeHelpers.GetUninitializedObject(actionsType);
        void Field(string name, object value) => clientType.GetField(name, flags)!.SetValue(client, value);
        object? Call(string name, params object[] args) => clientType.GetMethod(name, flags)!.Invoke(client, args);
        void State(bool main, int state) => actionsType.GetMethod("SetState")!.Invoke(actions, [state, main]);
        Field("PlayerActionsBehavior", actions);
        var stats = JsonConvert.DeserializeObject("""
            {
              "OneHandedStance": {"ReadyAnimation":"test:single-ready"},
              "OffHandStance": {"ReadyAnimation":"test:single-off-ready"},
              "MainHandDualWieldStances": {"game:stick": {
                "IdleAnimation":"test:right-idle", "ReadyAnimation":"test:right-ready",
                "WalkAnimation":"test:right-walk", "RunAnimation":"test:right-run",
                "SwimAnimation":"test:right-swim", "SwimIdleAnimation":"test:right-swimidle",
                "AttackAnimation":{"Main":["right-1","right-2","right-3"]}}},
              "OffHandDualWieldStances": {"game:stick": {
                "IdleAnimation":"test:left-idle", "ReadyAnimation":"test:left-ready",
                "WalkAnimation":"test:left-walk", "RunAnimation":"test:left-run",
                "SwimAnimation":"test:left-swim", "SwimIdleAnimation":"test:left-swimidle",
                "AttackAnimation":{"Main":["left-1","left-2","left-3"]}}}
            }
            """, Type("CombatOverhaul.Implementations.MeleeWeaponStats"))!;
        Field("Stats", stats);
        State(true, 300); State(false, 400);

        foreach (bool main in new[] { true, false })
        {
            string side = main ? "right" : "left";
            foreach (string pose in new[] { "Idle", "Ready", "Walk", "Run", "Swim", "SwimIdle" })
            {
                var request = Call("Get" + pose + "Animation", player, main ? player.RightHandItemSlot : player.LeftHandItemSlot, main);
                Assert.NotNull(request);
                Assert.Equal($"test:{side}-{pose.ToLowerInvariant()}", request.GetType().GetField("Animation")!.GetValue(request));
                Assert.Equal(main ? "main" : "mainOffhand", request.GetType().GetField("Category")!.GetValue(request));
            }
        }

        State(true, 301); State(false, 402);
        Assert.False((bool)Call("RestrictRightHandAction")!);
        Assert.False((bool)Call("RestrictLeftHandAction")!);
        State(true, 1); State(false, 102);
        Assert.True((bool)Call("RestrictRightHandAction")!);
        Assert.True((bool)Call("RestrictLeftHandAction")!);
        State(true, 300); State(false, 400);

        foreach (string field in new[] { "MainHandDualWieldAttacks", "OffHandDualWieldAttacks", "DirectionalMainHandDualWieldAttacks", "DirectionalOffHandDualWieldAttacks", "MainHandDualWieldHandleAttacks", "OffHandDualWieldHandleAttacks" })
            Field(field, Activator.CreateInstance(clientType.GetField(field, flags)!.FieldType)!);
        var attackType = Type("CombatOverhaul.MeleeSystems.MeleeAttack");
        var direction = Enum.Parse(Type("CombatOverhaul.Inputs.AttackDirection"), "Top");
        foreach (bool main in new[] { true, false })
        {
            string prefix = main ? "MainHand" : "OffHand";
            var attack = RuntimeHelpers.GetUninitializedObject(attackType);
            ((IDictionary)clientType.GetField(prefix + "DualWieldAttacks", flags)!.GetValue(client)!)["game:stick"] = attack;
            var selected = Call("Get" + prefix + "DualWieldAttack", "game:stick", direction);
            Assert.Same(attack, selected);
            var stance = Call("GetStanceStats", player, main)!;
            for (int index = 0; index < 6; index++)
            {
                Field(prefix + "AttackCounter", index);
                Assert.Equal($"{(main ? "right" : "left")}-{index % 3 + 1}", Call("GetAttackAnimationForDirection", stance, direction, index));
                Call("AttackAnimationCallback", player, main);
                Assert.Equal(index, clientType.GetField(prefix + "AttackCounter", flags)!.GetValue(client));
            }
        }
    }
}
