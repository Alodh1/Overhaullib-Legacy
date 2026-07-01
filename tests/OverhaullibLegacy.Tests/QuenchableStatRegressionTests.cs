using CombatOverhaul.Utils;

namespace OverhaullibLegacy.Tests;

public sealed class QuenchableStatRegressionTests
{
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
