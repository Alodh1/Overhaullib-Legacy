using System.Reflection;
using Atlas.XUnit;
using Xunit;

namespace OverhaullibLegacy.AtlasTests;

[Trait("Category", "E2E")]
public sealed class BowOffhandSettingScenarios : AtlasScenarioBase
{
    private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string OverhaullibAssemblyName = "OverhaullibLegacyCompat";

    [AtlasScenario(TimeoutMs = 120000)]
    public async Task CombatOverhaulBow_Should_Use_RuntimeBowTwoHandedSetting()
    {
        await World.Ticks(5);

        Assembly overhaullib = RequireAssembly(OverhaullibAssemblyName);
        Type bowClient = RequireType(overhaullib, "CombatOverhaul.Implementations.BowClient");
        Type rangeWeaponClient = RequireType(overhaullib, "CombatOverhaul.RangedSystems.RangeWeaponClient");
        Type settings = RequireType(overhaullib, "CombatOverhaul.Settings");
        MethodInfo requirementHook = RequireMethod(bowClient, "RequiresEmptyOtherHand");
        MethodInfo getBowTwoHanded = settings.GetProperty("BowTwoHanded")?.GetMethod
            ?? throw new MissingMemberException(settings.FullName, "BowTwoHanded");
        FieldInfo twoHandedField = RequireField(rangeWeaponClient, "TwoHanded");

        Assert.Equal(bowClient, requirementHook.DeclaringType);
        Assert.True(MethodCalls(requirementHook, getBowTwoHanded));
        Assert.False(MethodAccessesField(requirementHook, twoHandedField));
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
