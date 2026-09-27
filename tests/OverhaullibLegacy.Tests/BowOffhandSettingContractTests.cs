using System.Reflection;
using CombatOverhaul;
using CombatOverhaul.Implementations;
using CombatOverhaul.RangedSystems;

namespace OverhaullibLegacy.Tests;

public sealed class BowOffhandSettingContractTests
{
    private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void RangeWeaponClient_DelegatesOtherHandRequirementToVirtualHook()
    {
        MethodInfo checkOtherHand = RequireMethod(typeof(RangeWeaponClient), "CheckForOtherHandEmpty");
        MethodInfo requirementHook = RequireMethod(typeof(RangeWeaponClient), "RequiresEmptyOtherHand");

        Assert.True(MethodCalls(checkOtherHand, requirementHook));
    }

    [Fact]
    public void BowClient_UsesGlobalBowTwoHandedSettingForOtherHandRequirement()
    {
        MethodInfo requirementHook = RequireMethod(typeof(BowClient), "RequiresEmptyOtherHand");
        MethodInfo getBowTwoHanded = typeof(Settings).GetProperty(nameof(Settings.BowTwoHanded))?.GetMethod
            ?? throw new MissingMemberException(typeof(Settings).FullName, nameof(Settings.BowTwoHanded));
        FieldInfo twoHandedField = RequireField(typeof(RangeWeaponClient), "TwoHanded");

        Assert.Equal(typeof(BowClient), requirementHook.DeclaringType);
        Assert.True(MethodCalls(requirementHook, getBowTwoHanded));
        Assert.False(MethodAccessesField(requirementHook, twoHandedField));
    }

    [Fact]
    public void BowClient_DoesNotSnapshotPerItemTwoHandedAttribute()
    {
        ConstructorInfo constructor = typeof(BowClient).GetConstructors()
            .Single(ctor => ctor.GetParameters().Length == 3);
        FieldInfo twoHandedField = RequireField(typeof(RangeWeaponClient), "TwoHanded");

        Assert.False(MethodAccessesField(constructor, twoHandedField));
        Assert.Equal(typeof(bool), typeof(BowStats).GetProperty(nameof(BowStats.TwoHanded))?.PropertyType);
    }

    private static MethodInfo RequireMethod(Type type, string name)
    {
        return type.GetMethod(name, InstanceNonPublic)
            ?? throw new MissingMethodException(type.FullName, name);
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        return type.GetField(name, InstanceNonPublic)
            ?? throw new MissingFieldException(type.FullName, name);
    }

    private static bool MethodCalls(MethodBase caller, MethodBase target)
    {
        byte[] il = caller.GetMethodBody()?.GetILAsByteArray() ?? [];
        Module module = caller.Module;

        for (int index = 0; index < il.Length - sizeof(int); index++)
        {
            if (il[index] is not (0x28 or 0x6F))
            {
                continue;
            }

            MethodBase? calledMethod = TryResolveMethod(module, il, index + 1);
            if (calledMethod?.Module == target.Module && calledMethod.MetadataToken == target.MetadataToken)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MethodAccessesField(MethodBase method, FieldInfo field)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        Module module = method.Module;

        for (int index = 0; index < il.Length - sizeof(int); index++)
        {
            if (il[index] is not (0x7B or 0x7C or 0x7D or 0x7E or 0x7F or 0x80))
            {
                continue;
            }

            FieldInfo? accessedField = TryResolveField(module, il, index + 1);
            if (accessedField?.Module == field.Module && accessedField.MetadataToken == field.MetadataToken)
            {
                return true;
            }
        }

        return false;
    }

    private static MethodBase? TryResolveMethod(Module module, byte[] il, int tokenStart)
    {
        try
        {
            return module.ResolveMethod(BitConverter.ToInt32(il, tokenStart));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static FieldInfo? TryResolveField(Module module, byte[] il, int tokenStart)
    {
        try
        {
            return module.ResolveField(BitConverter.ToInt32(il, tokenStart));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
