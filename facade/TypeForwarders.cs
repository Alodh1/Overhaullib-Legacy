using System.Runtime.CompilerServices;

using CombatOverhaul.RangedSystems;
using CombatOverhaul.Armor;
using CombatOverhaul.Implementations;
using CombatOverhaul.DamageSystems;

[assembly: TypeForwardedTo(typeof(ProjectileEntity))]
[assembly: TypeForwardedTo(typeof(ArmorSlot))]
[assembly: TypeForwardedTo(typeof(ItemStackMeleeWeaponStats))]
[assembly: TypeForwardedTo(typeof(ItemStackRangedStats))]
[assembly: TypeForwardedTo(typeof(IWeaponDamageSource))]
[assembly: TypeForwardedTo(typeof(ArmorBehavior))]
[assembly: TypeForwardedTo(typeof(DamageResistData))]