# Weapon Buff System

`CombatOverhaul.WeaponBuffs.WeaponBuffSystem` lets other mods attach temporary or permanent buffs to Combat Overhaul weapon item stacks, armory weapons that use CO stats, and projectile stacks.

Buffs are stored on the `ItemStack` under `combatOverhaul:weaponBuffs`. This is not vanilla `Buffable`.

A mod can either:

- apply declarative stat modifiers with `WeaponBuffDefinition.Modifiers`
- register a `WeaponBuffProvider` for custom behavior

## Apply A Weapon Buff

Use `ApplyBuff` for melee weapons, bows, crossbows, firearms, slings, and any other CO weapon stack.

```csharp
using CombatOverhaul.WeaponBuffs;
using Vintagestory.API.Common;

public static void ApplyFireCoating(ICoreAPI api, ItemSlot slot)
{
    WeaponBuffSystem buffs = api.ModLoader.GetModSystem<WeaponBuffSystem>();

    buffs.ApplyBuff(slot, new WeaponBuffDefinition
    {
        Code = "firecoating",
        SourceModId = "alchemy",
        DisplayNameLangCode = "alchemy:buff-firecoating-name",
        DescriptionLangCode = "alchemy:buff-firecoating-desc",
        DurationHours = 1.0,
        Uses = 20,
        ConsumeOn =
        [
            WeaponBuffConsumptionTrigger.MeleeHit,
            WeaponBuffConsumptionTrigger.RangedShot
        ],
        Modifiers =
        [
            new()
            {
                StatCode = WeaponBuffStatCodes.DamageMultiplier,
                Multiply = 1.15f
            },
            new()
            {
                StatCode = WeaponBuffStatCodes.DamageTierBonus,
                Add = 1
            }
        ]
    });
}
```

`ReplaceExisting` defaults to true for the same `Code` and `SourceModId`. Set `new WeaponBuffApplyOptions { ReplaceExisting = false }` to stack separate instances.

## Apply A Projectile Buff

Use `ApplyProjectileBuff` for ammo/projectile stacks, such as arrows, bolts, bullets, or sling ammo. The projectile-specific API validates that the target stack has CO `ProjectileBehavior`.

```csharp
using CombatOverhaul.WeaponBuffs;
using Vintagestory.API.Common;

public static bool ApplyPoisonedArrowCoating(ICoreAPI api, ItemSlot ammoSlot)
{
    WeaponBuffSystem buffs = api.ModLoader.GetModSystem<WeaponBuffSystem>();

    if (!buffs.IsProjectileBuffTarget(ammoSlot.Itemstack))
    {
        return false;
    }

    buffs.ApplyProjectileBuff(ammoSlot, new WeaponBuffDefinition
    {
        Code = "poisoned-arrow",
        SourceModId = "alchemy",
        DisplayNameLangCode = "alchemy:buff-poisoned-arrow-name",
        DescriptionLangCode = "alchemy:buff-poisoned-arrow-desc",
        Uses = 1,
        ConsumeOn =
        [
            WeaponBuffConsumptionTrigger.ProjectileHit
        ],
        Modifiers =
        [
            new()
            {
                StatCode = WeaponBuffStatCodes.DamageMultiplier,
                Multiply = 1.15f
            },
            new()
            {
                StatCode = WeaponBuffStatCodes.DamageTierBonus,
                Add = 1
            }
        ]
    });

    return true;
}
```

`ApplyProjectileBuff(ItemSlot, ...)` marks the slot dirty by default. Use `ApplyProjectileBuff(ItemStack, ...)` only when you already handle inventory sync yourself.

If you want a coating to apply to one arrow only, split the stack to size 1 before applying the buff or manage stack splitting in your coating item. Buffs live on the `ItemStack`, so applying a buff to a stack of 64 arrows means the stack carries that buff.

## Remove Buffs

```csharp
buffs.RemoveBuff(slot, "firecoating", "alchemy");
buffs.RemoveProjectileBuff(ammoSlot, "poisoned-arrow", "alchemy");
```

Passing `sourceModId: null` removes all buffs with that code regardless of source.

## Built-In Stat Codes

Weapon stat modifiers:

- `DamageMultiplier`
- `DamageBonus`
- `DamageTierBonus`
- `AttackSpeed`
- `BlockTierBonus`
- `ParryTierBonus`
- `ThrownDamageMultiplier`
- `ThrownDamageTierBonus`
- `ThrownAimingDifficulty`
- `ThrownProjectileSpeedMultiplier`
- `KnockbackMultiplier`
- `ArmorPiercingBonus`
- `ReloadSpeed`
- `ProjectileSpeed`
- `DispersionMultiplier`
- `AimingDifficulty`

Projectile stack stat modifiers:

- `DamageMultiplier`
- `DamageTierBonus`
- `KnockbackMultiplier`
- `DropChanceMultiplier`
- `PenetrationBonus`
- `AdditionalDurabilityCost`

Projectile stat modifiers flow through `ItemStackProjectileStats.FromItemStack`, then `ProjectileBehavior.GetStats`, then the fired projectile. Tooltips use the same path, so buffed projectile damage, damage tier, drop chance, penetration, knockback, and durability cost should display from the final projectile stats.

## Custom Behavior Provider

Use a provider when the buff needs behavior that is not expressible as a stat modifier.

```csharp
using CombatOverhaul.RangedSystems;
using CombatOverhaul.WeaponBuffs;

public sealed class PoisonCoatingProvider : WeaponBuffProvider
{
    public override bool Handles(WeaponBuffQueryContext context, WeaponBuffInstance buff)
    {
        return buff.SourceModId == "alchemy" && buff.Code == "poisoncoating";
    }

    public override void ModifyMeleeDamage(WeaponBuffDamageContext context, WeaponBuffInstance buff)
    {
        context.Damage += 2.0f;
    }

    public override void ModifyRangedDamage(WeaponBuffDamageContext context, WeaponBuffInstance buff)
    {
        context.Damage += 2.0f;
    }

    public override void ModifyProjectileStats(WeaponBuffQueryContext context, WeaponBuffInstance buff, ProjectileStats stats)
    {
        stats.Knockback *= 0.8f;
    }
}
```

Register it from your mod system:

```csharp
private readonly PoisonCoatingProvider _provider = new();

public override void Start(ICoreAPI api)
{
    api.ModLoader.GetModSystem<WeaponBuffSystem>().RegisterProvider(_provider);
}

public override void Dispose()
{
    WeaponBuffSystem.Current?.UnregisterProvider(_provider);
}
```

## Projectile Provider Notes

For ranged hits, `ModifyRangedDamage` runs weapon-stack providers and projectile-stack providers. `WeaponBuffDamageContext.WeaponStack` is the bow, crossbow, firearm, sling, or other launcher. `WeaponBuffDamageContext.ProjectileStack` is the actual fired ammo stack.

Useful `WeaponBuffQueryContext.Usage` values:

- `melee`
- `ranged`
- `projectile`
- `projectile-stats`
- `projectile-spawn`
- `weapon-projectile-spawn`
- `melee-damage`
- `ranged-damage`
- `projectile-ranged-damage`
- `tooltip`
- `consume`

Provider hooks:

- `ModifyMeleeStats`
- `ModifyRangedStats`
- `ModifyProjectileStackStats`
- `ModifyProjectileStats`
- `ModifyProjectileSpawn`
- `ModifyMeleeDamage`
- `ModifyRangedDamage`
- `AppendTooltip`
- `OnConsumed`

## Consumption Triggers

- `Manual`
- `MeleeHit`
- `RangedShot`
- `RangedHit`
- `ProjectileSpawn`
- `ProjectileHit`
- `Any`

For arrow coatings, prefer `ProjectileHit`. It consumes only after successful hit damage, so missed and recovered arrows keep their coating.

Use `ProjectileSpawn` when the coating should be spent as soon as the projectile is fired, even if it misses.

Use `RangedShot` or `RangedHit` for buffs on the weapon/launcher stack. Use `ProjectileSpawn` or `ProjectileHit` for buffs on the ammo/projectile stack.

Buffs are applied in ascending `Priority`, then source mod id, code, and instance id for deterministic ordering.
