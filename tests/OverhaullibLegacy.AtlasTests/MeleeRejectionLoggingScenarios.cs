using System.Reflection;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Xunit;

namespace OverhaullibLegacy.AtlasTests;

[Trait("Category", "E2E")]
public sealed class MeleeRejectionLoggingScenarios : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 120000)]
    public async Task RejectedPacket_LogsTierHandClassAndDetails_WithExistingLimit()
    {
        await World.Ticks(5);
        var player = await World.JoinPlayer("TierDiagPlayer");
        player.Player.Entity.WatchedAttributes.SetString("characterClass", "wanderer");
        Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "OverhaullibLegacyCompat");
        Type serverType = assembly.GetType("CombatOverhaul.MeleeSystems.MeleeSystemServer", true)!;
        object system = World.Api.ModLoader.GetModSystem("CombatOverhaul.CombatOverhaulSystem");
        object server = system.GetType().GetProperty("ServerMeleeSystem")!.GetValue(system)!;
        Type packetType = assembly.GetType("CombatOverhaul.MeleeSystems.MeleeDamagePacket", true)!;
        object packet = Activator.CreateInstance(packetType)!;
        packetType.GetProperty("Tier")!.SetValue(packet, 4);
        packetType.GetProperty("MainHand")!.SetValue(packet, true);
        MethodInfo log = serverType.GetMethod("LogRejectedAttackPacket", BindingFlags.Instance | BindingFlags.NonPublic)!;
        List<string> lines = [];
        void Capture(EnumLogType type, string message, params object[] args)
        {
            if (message.Contains("Rejected melee attack packet")) lines.Add(message);
        }
        World.Api.Logger.EntryAdded += Capture;
        try
        {
            for (int i = 0; i < 21; i++)
                log.Invoke(server, [player.Player, packet, "tier-too-high", "allowedTier=3"]);
            Assert.Equal(20, lines.Count);
            Assert.Contains("tier=4", lines[0]);
            Assert.Contains("mainHand=True", lines[0]);
            Assert.Contains("weapon='", lines[0]);
            Assert.Contains("characterClass='wanderer'", lines[0]);
            Assert.Contains("allowedTier=3", lines[0]);
        }
        finally
        {
            World.Api.Logger.EntryAdded -= Capture;
        }
    }
}
