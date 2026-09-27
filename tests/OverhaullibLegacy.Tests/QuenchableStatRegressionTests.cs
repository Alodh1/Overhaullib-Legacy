using CombatOverhaul.Utils;

namespace OverhaullibLegacy.Tests;

public sealed class QuenchableStatRegressionTests
{
    [Fact]
    public void ArmorSettings_Survive_ServerPacketSerialization()
    {
        var source = new CombatOverhaul.Settings();
        var properties = typeof(CombatOverhaul.Settings).GetProperties()
            .Where(property => property.Name.StartsWith("ArmorQuench")).ToArray();
        Assert.Equal(9, properties.Length);
        foreach (var property in properties)
            property.SetValue(source, property.PropertyType == typeof(bool) ? (object)true : .37f);
        using var stream = new MemoryStream();
        ProtoBuf.Serializer.Serialize(stream, CombatOverhaul.ServerGameplaySettingsPacket.From(source));
        stream.Position = 0;
        var packet = ProtoBuf.Serializer.Deserialize<CombatOverhaul.ServerGameplaySettingsPacket>(stream);
        var client = new CombatOverhaul.Settings();
        packet.ApplyTo(client);
        foreach (var property in properties)
            Assert.Equal(property.GetValue(source), property.GetValue(client));
    }

    [Fact]
    public void WeaponQuenchDamageBonus_UsesConfiguredMultiplier()
    {
        float previousMultiplier = QuenchableStatUtil.WeaponDamageMultiplier;

        try
        {
            QuenchableStatUtil.WeaponDamageMultiplier = 1f;
            float vanillaBonus = QuenchableStatUtil.GetWeaponQuenchDamageBonus(1, 0);

            QuenchableStatUtil.WeaponDamageMultiplier = 0f;
            Assert.Equal(0f, QuenchableStatUtil.GetWeaponQuenchDamageBonus(1, 0));

            QuenchableStatUtil.WeaponDamageMultiplier = 2f;
            Assert.Equal(vanillaBonus * 2f, QuenchableStatUtil.GetWeaponQuenchDamageBonus(1, 0), precision: 6);
        }
        finally
        {
            QuenchableStatUtil.WeaponDamageMultiplier = previousMultiplier;
        }
    }
}
