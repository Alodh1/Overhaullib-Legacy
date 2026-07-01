using System.Reflection;
using System.Reflection.Emit;
using CombatOverhaul.RangedSystems;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace OverhaullibLegacy.Tests;

public sealed class ProjectileCompatibilityTests
{
    [Fact]
    public void ProjectileSpawnStats_DamageStrengthAliasesDamageTier()
    {
        ProjectileSpawnStats stats = new()
        {
            DamageTier = 3
        };

#pragma warning disable CS0618
        Assert.Equal(3, stats.DamageStrength);

        stats.DamageStrength = 6.9f;
#pragma warning restore CS0618

        Assert.Equal(6, stats.DamageTier);
    }

    [Fact]
    public void ProjectileSystemServer_KeepsLegacySpawnOverloads()
    {
        Assert.NotNull(GetProjectileSystemMethod("Spawn", typeof(Guid), typeof(ProjectileStats), typeof(ProjectileSpawnStats), typeof(ItemStack), typeof(ItemStack), typeof(Entity)));
        Assert.NotNull(GetProjectileSystemMethod("Spawn", typeof(Guid), typeof(ProjectileStats), typeof(ProjectileSpawnStats), typeof(ItemStack), typeof(ItemStack), typeof(Entity), typeof(Entity)));
    }

    [Fact]
    public void ProjectileSystemServer_KeepsSlotAwareSpawnOverloads()
    {
        Assert.NotNull(GetProjectileSystemMethod("SpawnFromWeaponSlot", typeof(Guid), typeof(ProjectileStats), typeof(ProjectileSpawnStats), typeof(ItemStack), typeof(ItemSlot), typeof(Entity)));
        Assert.NotNull(GetProjectileSystemMethod("SpawnFromWeaponSlot", typeof(Guid), typeof(ProjectileStats), typeof(ProjectileSpawnStats), typeof(ItemStack), typeof(ItemSlot), typeof(Entity), typeof(Entity)));
    }

    [Fact]
    public void ProjectileSystemServer_LegacySpawnOverloadsDisableWeaponBuffs()
    {
        AssertSpawnInternalApplyBuffsFlag("Spawn", false, typeof(Guid), typeof(ProjectileStats), typeof(ProjectileSpawnStats), typeof(ItemStack), typeof(ItemStack), typeof(Entity));
        AssertSpawnInternalApplyBuffsFlag("Spawn", false, typeof(Guid), typeof(ProjectileStats), typeof(ProjectileSpawnStats), typeof(ItemStack), typeof(ItemStack), typeof(Entity), typeof(Entity));
    }

    [Fact]
    public void ProjectileSystemServer_SlotAwareSpawnOverloadsEnableWeaponBuffs()
    {
        AssertSpawnInternalApplyBuffsFlag("SpawnFromWeaponSlot", true, typeof(Guid), typeof(ProjectileStats), typeof(ProjectileSpawnStats), typeof(ItemStack), typeof(ItemSlot), typeof(Entity));
        AssertSpawnInternalApplyBuffsFlag("SpawnFromWeaponSlot", true, typeof(Guid), typeof(ProjectileStats), typeof(ProjectileSpawnStats), typeof(ItemStack), typeof(ItemSlot), typeof(Entity), typeof(Entity));
    }

    [Fact]
    public void ProjectileServer_KeepsOriginalConstructorAndAddsBuffAwareConstructor()
    {
        Type type = typeof(ProjectileServer);

        Assert.NotNull(type.GetConstructor([typeof(ProjectileEntity), typeof(ProjectileStats), typeof(ProjectileSpawnStats), typeof(ICoreAPI), typeof(Action<Guid>), typeof(ItemStack), typeof(ItemSlot)]));
        Assert.NotNull(type.GetConstructor([typeof(ProjectileEntity), typeof(ProjectileStats), typeof(ProjectileSpawnStats), typeof(ICoreAPI), typeof(Action<Guid>), typeof(ItemStack), typeof(ItemSlot), typeof(bool)]));
    }

    private static MethodInfo GetProjectileSystemMethod(string name, params Type[] parameterTypes)
    {
        return typeof(ProjectileSystemServer).GetMethod(name, BindingFlags.Public | BindingFlags.Instance, parameterTypes)
            ?? throw new MissingMethodException(typeof(ProjectileSystemServer).FullName, name);
    }

    private static void AssertSpawnInternalApplyBuffsFlag(string methodName, bool expectedApplyBuffs, params Type[] parameterTypes)
    {
        MethodInfo caller = GetProjectileSystemMethod(methodName, parameterTypes);
        MethodInfo spawnInternal = typeof(ProjectileSystemServer).GetMethod("SpawnInternal", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(typeof(ProjectileSystemServer).FullName, "SpawnInternal");

        byte[] il = caller.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException($"{caller.Name} has no IL body");

        int callIndex = FindCallInstruction(il, spawnInternal.MetadataToken);

        byte expectedOpcode = expectedApplyBuffs ? OpCodes.Ldc_I4_1.ValueAsByte() : OpCodes.Ldc_I4_0.ValueAsByte();
        Assert.Equal(expectedOpcode, il[callIndex - 1]);
    }

    private static int FindCallInstruction(byte[] il, int metadataToken)
    {
        byte[] tokenBytes = BitConverter.GetBytes(metadataToken);

        for (int index = 0; index <= il.Length - 5; index++)
        {
            if (il[index] != OpCodes.Call.ValueAsByte() && il[index] != OpCodes.Callvirt.ValueAsByte()) continue;
            if (il[index + 1] != tokenBytes[0]) continue;
            if (il[index + 2] != tokenBytes[1]) continue;
            if (il[index + 3] != tokenBytes[2]) continue;
            if (il[index + 4] != tokenBytes[3]) continue;

            return index;
        }

        throw new InvalidOperationException("Expected call to SpawnInternal was not found");
    }
}

internal static class OpCodeTestExtensions
{
    public static byte ValueAsByte(this System.Reflection.Emit.OpCode opcode)
    {
        short value = opcode.Value;
        Assert.InRange(value, 0, byte.MaxValue);
        return (byte)value;
    }
}
