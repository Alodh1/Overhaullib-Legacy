using System.Text;
using CombatOverhaul.Implementations;
using CombatOverhaul.RangedSystems;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace CombatOverhaul.WeaponBuffs;

public enum WeaponBuffConsumptionTrigger
{
    None,
    Manual,
    MeleeHit,
    RangedShot,
    RangedHit,
    ProjectileSpawn,
    ProjectileHit,
    Any
}

public static class WeaponBuffStatCodes
{
    public const string DamageMultiplier = "damageMultiplier";
    public const string DamageBonus = "damageBonus";
    public const string DamageTierBonus = "damageTierBonus";
    public const string AttackSpeed = "attackSpeed";
    public const string BlockTierBonus = "blockTierBonus";
    public const string ParryTierBonus = "parryTierBonus";
    public const string ThrownDamageMultiplier = "thrownDamageMultiplier";
    public const string ThrownDamageTierBonus = "thrownDamageTierBonus";
    public const string ThrownAimingDifficulty = "thrownAimingDifficulty";
    public const string ThrownProjectileSpeedMultiplier = "thrownProjectileSpeedMultiplier";
    public const string KnockbackMultiplier = "knockbackMultiplier";
    public const string ArmorPiercingBonus = "armorPiercingBonus";
    public const string ReloadSpeed = "reloadSpeed";
    public const string ProjectileSpeed = "projectileSpeed";
    public const string DispersionMultiplier = "dispersionMultiplier";
    public const string AimingDifficulty = "aimingDifficulty";
    public const string DropChanceMultiplier = "dropChanceMultiplier";
    public const string PenetrationBonus = "penetrationBonus";
    public const string AdditionalDurabilityCost = "additionalDurabilityCost";
}

public sealed class WeaponStatModifier
{
    public string StatCode { get; set; } = "";
    public float Add { get; set; } = 0f;
    public float Multiply { get; set; } = 1f;
    public int Priority { get; set; } = 0;
}

public sealed class WeaponBuffDefinition
{
    public string Code { get; set; } = "";
    public string SourceModId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string DisplayNameLangCode { get; set; } = "";
    public string Description { get; set; } = "";
    public string DescriptionLangCode { get; set; } = "";
    public double? DurationHours { get; set; }
    public int? Uses { get; set; }
    public int Priority { get; set; } = 0;
    public List<WeaponStatModifier> Modifiers { get; set; } = [];
    public List<WeaponBuffConsumptionTrigger> ConsumeOn { get; set; } = [];
    public ITreeAttribute? Data { get; set; }
}

public sealed class WeaponBuffApplyOptions
{
    public string InstanceId { get; set; } = "";
    public bool ReplaceExisting { get; set; } = true;
    public bool MarkDirty { get; set; } = true;
    public double? DurationHoursOverride { get; set; }
    public int? UsesOverride { get; set; }
}

public sealed class WeaponBuffInstance
{
    public string InstanceId { get; set; } = "";
    public string Code { get; set; } = "";
    public string SourceModId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string DisplayNameLangCode { get; set; } = "";
    public string Description { get; set; } = "";
    public string DescriptionLangCode { get; set; } = "";
    public double? ExpiresAtTotalHours { get; set; }
    public int? UsesRemaining { get; set; }
    public int Priority { get; set; } = 0;
    public List<WeaponStatModifier> Modifiers { get; set; } = [];
    public List<WeaponBuffConsumptionTrigger> ConsumeOn { get; set; } = [];
    public ITreeAttribute? Data { get; set; }
}

public sealed class WeaponBuffQueryContext
{
    public WeaponBuffQueryContext(ItemStack stack, string usage, ItemSlot? slot = null, Entity? actor = null, Entity? target = null)
    {
        Stack = stack;
        Usage = usage;
        Slot = slot;
        Actor = actor;
        Target = target;
    }

    public ItemStack Stack { get; }
    public string Usage { get; }
    public ItemSlot? Slot { get; }
    public Entity? Actor { get; }
    public Entity? Target { get; }
}

public sealed class WeaponBuffDamageContext
{
    public WeaponBuffDamageContext(Entity target, DamageSource damageSource, ItemSlot? slot, ItemStack? weaponStack, float damage)
    {
        Target = target;
        DamageSource = damageSource;
        Slot = slot;
        WeaponStack = weaponStack;
        Damage = damage;
    }

    public Entity Target { get; }
    public DamageSource DamageSource { get; }
    public ItemSlot? Slot { get; }
    public ItemStack? WeaponStack { get; }
    public float Damage { get; set; }
}

public sealed class WeaponBuffProjectileSpawnContext
{
    public WeaponBuffProjectileSpawnContext(
        ProjectileStats projectileStats,
        ProjectileSpawnStats spawnStats,
        ItemStack projectileStack,
        ItemStack? weaponStack,
        Entity shooter,
        Entity target)
    {
        ProjectileStats = projectileStats;
        SpawnStats = spawnStats;
        ProjectileStack = projectileStack;
        WeaponStack = weaponStack;
        Shooter = shooter;
        Target = target;
    }

    public ProjectileStats ProjectileStats { get; }
    public ProjectileSpawnStats SpawnStats { get; set; }
    public ItemStack ProjectileStack { get; }
    public ItemStack? WeaponStack { get; }
    public Entity Shooter { get; }
    public Entity Target { get; }
}

public sealed class WeaponBuffTooltipContext
{
    public WeaponBuffTooltipContext(ItemStack stack, StringBuilder description, IWorldAccessor world, bool withDebugInfo)
    {
        Stack = stack;
        Description = description;
        World = world;
        WithDebugInfo = withDebugInfo;
    }

    public ItemStack Stack { get; }
    public StringBuilder Description { get; }
    public IWorldAccessor World { get; }
    public bool WithDebugInfo { get; }
}

public sealed class WeaponBuffMeleeStats
{
    public float DamageMultiplier { get; set; }
    public float DamageBonus { get; set; }
    public int DamageTierBonus { get; set; }
    public float AttackSpeed { get; set; }
    public int BlockTierBonus { get; set; }
    public int ParryTierBonus { get; set; }
    public float ThrownDamageMultiplier { get; set; }
    public int ThrownDamageTierBonus { get; set; }
    public float ThrownAimingDifficulty { get; set; }
    public float ThrownProjectileSpeedMultiplier { get; set; }
    public float KnockbackMultiplier { get; set; }
    public int ArmorPiercingBonus { get; set; }

    public ItemStackMeleeWeaponStats ToReadonly()
    {
        return new(
            DamageMultiplier,
            DamageBonus,
            DamageTierBonus,
            AttackSpeed,
            BlockTierBonus,
            ParryTierBonus,
            ThrownDamageMultiplier,
            ThrownDamageTierBonus,
            ThrownAimingDifficulty,
            ThrownProjectileSpeedMultiplier,
            KnockbackMultiplier,
            ArmorPiercingBonus);
    }
}

public sealed class WeaponBuffRangedStats
{
    public float ReloadSpeed { get; set; }
    public float DamageMultiplier { get; set; }
    public int DamageTierBonus { get; set; }
    public float ProjectileSpeed { get; set; }
    public float DispersionMultiplier { get; set; }
    public float AimingDifficulty { get; set; }

    public ItemStackRangedStats ToReadonly()
    {
        return new(
            ReloadSpeed,
            DamageMultiplier,
            DamageTierBonus,
            ProjectileSpeed,
            DispersionMultiplier,
            AimingDifficulty);
    }
}

public sealed class WeaponBuffProjectileStats
{
    public float DamageMultiplier { get; set; }
    public int DamageTierBonus { get; set; }
    public float KnockbackMultiplier { get; set; }
    public float DropChanceMultiplier { get; set; }
    public float PenetrationBonus { get; set; }
    public int AdditionalDurabilityCost { get; set; }

    public ItemStackProjectileStats ToReadonly()
    {
        return new(
            DamageMultiplier,
            DamageTierBonus,
            KnockbackMultiplier,
            DropChanceMultiplier,
            PenetrationBonus,
            AdditionalDurabilityCost);
    }
}

public interface IWeaponBuffProvider
{
    bool Handles(WeaponBuffQueryContext context, WeaponBuffInstance buff);
    void ModifyMeleeStats(WeaponBuffQueryContext context, WeaponBuffInstance buff, WeaponBuffMeleeStats stats);
    void ModifyRangedStats(WeaponBuffQueryContext context, WeaponBuffInstance buff, WeaponBuffRangedStats stats);
    void ModifyProjectileStackStats(WeaponBuffQueryContext context, WeaponBuffInstance buff, WeaponBuffProjectileStats stats);
    void ModifyProjectileStats(WeaponBuffQueryContext context, WeaponBuffInstance buff, ProjectileStats stats);
    void ModifyProjectileSpawn(WeaponBuffProjectileSpawnContext context, WeaponBuffInstance buff);
    void ModifyMeleeDamage(WeaponBuffDamageContext context, WeaponBuffInstance buff);
    void ModifyRangedDamage(WeaponBuffDamageContext context, WeaponBuffInstance buff);
    void AppendTooltip(WeaponBuffTooltipContext context, WeaponBuffInstance buff);
    void OnConsumed(ItemStack stack, WeaponBuffInstance buff, WeaponBuffConsumptionTrigger trigger);
}

public abstract class WeaponBuffProvider : IWeaponBuffProvider
{
    public virtual bool Handles(WeaponBuffQueryContext context, WeaponBuffInstance buff) => true;
    public virtual void ModifyMeleeStats(WeaponBuffQueryContext context, WeaponBuffInstance buff, WeaponBuffMeleeStats stats) { }
    public virtual void ModifyRangedStats(WeaponBuffQueryContext context, WeaponBuffInstance buff, WeaponBuffRangedStats stats) { }
    public virtual void ModifyProjectileStackStats(WeaponBuffQueryContext context, WeaponBuffInstance buff, WeaponBuffProjectileStats stats) { }
    public virtual void ModifyProjectileStats(WeaponBuffQueryContext context, WeaponBuffInstance buff, ProjectileStats stats) { }
    public virtual void ModifyProjectileSpawn(WeaponBuffProjectileSpawnContext context, WeaponBuffInstance buff) { }
    public virtual void ModifyMeleeDamage(WeaponBuffDamageContext context, WeaponBuffInstance buff) { }
    public virtual void ModifyRangedDamage(WeaponBuffDamageContext context, WeaponBuffInstance buff) { }
    public virtual void AppendTooltip(WeaponBuffTooltipContext context, WeaponBuffInstance buff) { }
    public virtual void OnConsumed(ItemStack stack, WeaponBuffInstance buff, WeaponBuffConsumptionTrigger trigger) { }
}

public sealed class WeaponBuffSystem : ModSystem
{
    public const string AttributeKey = "combatOverhaul:weaponBuffs";

    public override void Start(ICoreAPI api)
    {
        _api = api;
        Current = this;
    }

    public override void Dispose()
    {
        if (Current == this)
        {
            Current = null;
        }
    }

    public WeaponBuffInstance ApplyBuff(ItemSlot slot, WeaponBuffDefinition definition, WeaponBuffApplyOptions? options = null)
    {
        if (slot.Itemstack == null)
        {
            throw new ArgumentException("Cannot apply a weapon buff to an empty slot.", nameof(slot));
        }

        WeaponBuffInstance instance = ApplyBuff(slot.Itemstack, definition, options);
        if (options?.MarkDirty != false)
        {
            slot.MarkDirty();
        }

        return instance;
    }

    public WeaponBuffInstance ApplyBuff(ItemStack stack, WeaponBuffDefinition definition, WeaponBuffApplyOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(definition.Code))
        {
            throw new ArgumentException("Weapon buff code is required.", nameof(definition));
        }

        options ??= new();
        EnsureAttributes(stack);

        if (options.ReplaceExisting)
        {
            RemoveBuff(stack, definition.Code, definition.SourceModId);
        }

        WeaponBuffInstance instance = CreateInstance(definition, options);
        ITreeAttribute root = GetOrCreateRoot(stack);
        root[instance.InstanceId] = ToTree(instance);

        return instance;
    }

    public int RemoveBuff(ItemSlot slot, string code, string? sourceModId = null)
    {
        int removed = RemoveBuff(slot.Itemstack, code, sourceModId);
        if (removed > 0)
        {
            slot.MarkDirty();
        }

        return removed;
    }

    public int RemoveBuff(ItemStack? stack, string code, string? sourceModId = null)
    {
        if (stack?.Attributes == null) return 0;

        ITreeAttribute? root = stack.Attributes.GetTreeAttribute(AttributeKey);
        if (root == null) return 0;

        int removed = 0;
        foreach (KeyValuePair<string, IAttribute> entry in root.ToArray())
        {
            if (entry.Value is not ITreeAttribute tree) continue;

            string buffCode = tree.GetString(nameof(WeaponBuffInstance.Code), "");
            string buffSource = tree.GetString(nameof(WeaponBuffInstance.SourceModId), "");
            if (!string.Equals(buffCode, code, StringComparison.Ordinal)) continue;
            if (sourceModId != null && !string.Equals(buffSource, sourceModId, StringComparison.Ordinal)) continue;

            root.RemoveAttribute(entry.Key);
            removed++;
        }

        RemoveRootIfEmpty(stack, root);
        return removed;
    }

    public IReadOnlyList<WeaponBuffInstance> GetBuffs(ItemStack? stack, bool activeOnly = true)
    {
        return OrderBuffs(GetBuffsInternal(stack, activeOnly)).ToArray();
    }

    public void RegisterProvider(IWeaponBuffProvider provider)
    {
        if (!_providers.Contains(provider))
        {
            _providers.Add(provider);
        }
    }

    public void UnregisterProvider(IWeaponBuffProvider provider)
    {
        _providers.Remove(provider);
    }

    public int Consume(ItemSlot? slot, WeaponBuffConsumptionTrigger trigger)
    {
        if (slot?.Itemstack == null) return 0;

        int consumed = Consume(slot.Itemstack, trigger);
        if (consumed > 0)
        {
            slot.MarkDirty();
        }

        return consumed;
    }

    public int Consume(ItemStack? stack, WeaponBuffConsumptionTrigger trigger)
    {
        return ConsumeInternal(stack, trigger);
    }

    public static WeaponBuffSystem? Current { get; private set; }

    internal static ItemStackMeleeWeaponStats ComposeMeleeStats(ItemStack stack, WeaponBuffMeleeStats stats)
    {
        WeaponBuffQueryContext context = new(stack, "melee");
        foreach (WeaponBuffInstance buff in GetActiveBuffs(stack))
        {
            ApplyStatModifiers(buff, stats);
            ApplyProvider(context, buff, provider => provider.ModifyMeleeStats(context, buff, stats));
        }

        return stats.ToReadonly();
    }

    internal static ItemStackRangedStats ComposeRangedStats(ItemStack stack, WeaponBuffRangedStats stats)
    {
        WeaponBuffQueryContext context = new(stack, "ranged");
        foreach (WeaponBuffInstance buff in GetActiveBuffs(stack))
        {
            ApplyStatModifiers(buff, stats);
            ApplyProvider(context, buff, provider => provider.ModifyRangedStats(context, buff, stats));
        }

        return stats.ToReadonly();
    }

    internal static ItemStackProjectileStats ComposeProjectileStackStats(ItemStack stack, WeaponBuffProjectileStats stats)
    {
        WeaponBuffQueryContext context = new(stack, "projectile");
        foreach (WeaponBuffInstance buff in GetActiveBuffs(stack))
        {
            ApplyStatModifiers(buff, stats);
            ApplyProvider(context, buff, provider => provider.ModifyProjectileStackStats(context, buff, stats));
        }

        return stats.ToReadonly();
    }

    internal static void ModifyProjectileStats(ItemStack stack, ProjectileStats stats)
    {
        WeaponBuffQueryContext context = new(stack, "projectile-stats");
        foreach (WeaponBuffInstance buff in GetActiveBuffs(stack))
        {
            ApplyProvider(context, buff, provider => provider.ModifyProjectileStats(context, buff, stats));
        }
    }

    internal static void ModifyProjectileSpawn(ProjectileStats projectileStats, ref ProjectileSpawnStats spawnStats, ItemStack projectileStack, ItemStack? weaponStack, Entity shooter, Entity target)
    {
        WeaponBuffProjectileSpawnContext context = new(projectileStats, spawnStats, projectileStack, weaponStack, shooter, target);

        foreach (WeaponBuffInstance buff in GetActiveBuffs(projectileStack))
        {
            WeaponBuffQueryContext query = new(projectileStack, "projectile-spawn", actor: shooter, target: target);
            ApplyProvider(query, buff, provider => provider.ModifyProjectileSpawn(context, buff));
        }

        foreach (WeaponBuffInstance buff in GetActiveBuffs(weaponStack))
        {
            WeaponBuffQueryContext query = new(weaponStack!, "weapon-projectile-spawn", actor: shooter, target: target);
            ApplyProvider(query, buff, provider => provider.ModifyProjectileSpawn(context, buff));
        }

        spawnStats = context.SpawnStats;
    }

    internal static void ModifyMeleeDamage(Entity target, DamageSource damageSource, ItemSlot? slot, ref float damage)
    {
        ItemStack? weaponStack = slot?.Itemstack;
        WeaponBuffDamageContext damageContext = new(target, damageSource, slot, weaponStack, damage);
        WeaponBuffQueryContext queryContext = new(weaponStack!, "melee-damage", slot, damageSource.SourceEntity, target);

        foreach (WeaponBuffInstance buff in GetActiveBuffs(weaponStack))
        {
            ApplyProvider(queryContext, buff, provider => provider.ModifyMeleeDamage(damageContext, buff));
        }

        damage = damageContext.Damage;
    }

    internal static void ModifyRangedDamage(Entity target, DamageSource damageSource, ItemStack? weaponStack, ref float damage)
    {
        WeaponBuffDamageContext damageContext = new(target, damageSource, null, weaponStack, damage);
        WeaponBuffQueryContext queryContext = new(weaponStack!, "ranged-damage", actor: damageSource.CauseEntity ?? damageSource.SourceEntity, target: target);

        foreach (WeaponBuffInstance buff in GetActiveBuffs(weaponStack))
        {
            ApplyProvider(queryContext, buff, provider => provider.ModifyRangedDamage(damageContext, buff));
        }

        damage = damageContext.Damage;
    }

    public static void AppendTooltip(ItemStack? stack, StringBuilder description, IWorldAccessor world, bool withDebugInfo)
    {
        if (stack == null) return;

        WeaponBuffTooltipContext context = new(stack, description, world, withDebugInfo);
        WeaponBuffQueryContext queryContext = new(stack, "tooltip");

        foreach (WeaponBuffInstance buff in GetActiveBuffs(stack))
        {
            AppendDefaultTooltip(context, buff);
            ApplyProvider(queryContext, buff, provider => provider.AppendTooltip(context, buff));
        }
    }

    internal static int ConsumeInternal(ItemStack? stack, WeaponBuffConsumptionTrigger trigger)
    {
        if (stack?.Attributes == null || trigger == WeaponBuffConsumptionTrigger.None) return 0;

        ITreeAttribute? root = stack.Attributes.GetTreeAttribute(AttributeKey);
        if (root == null) return 0;

        int changed = 0;
        double? now = Current?._api?.World.Calendar.TotalHours;

        foreach (KeyValuePair<string, IAttribute> entry in root.ToArray())
        {
            if (entry.Value is not ITreeAttribute tree) continue;

            WeaponBuffInstance buff = FromTree(entry.Key, tree);
            if (IsExpired(buff, now) || buff.UsesRemaining <= 0)
            {
                root.RemoveAttribute(entry.Key);
                changed++;
                continue;
            }

            if (!ShouldConsume(buff, trigger) || buff.UsesRemaining == null) continue;

            buff.UsesRemaining = Math.Max(0, buff.UsesRemaining.Value - 1);
            changed++;

            WeaponBuffQueryContext context = new(stack, "consume");
            foreach (IWeaponBuffProvider provider in _providers.ToArray())
            {
                if (!ProviderHandles(provider, context, buff)) continue;
                TryProvider(provider, () => provider.OnConsumed(stack, buff, trigger));
            }

            if (buff.UsesRemaining <= 0)
            {
                root.RemoveAttribute(entry.Key);
            }
            else
            {
                root[entry.Key] = ToTree(buff);
            }
        }

        RemoveRootIfEmpty(stack, root);
        return changed;
    }

    private static readonly List<IWeaponBuffProvider> _providers = [];
    private static readonly HashSet<string> _providerErrors = [];
    private ICoreAPI? _api;

    private static WeaponBuffInstance CreateInstance(WeaponBuffDefinition definition, WeaponBuffApplyOptions options)
    {
        double? duration = options.DurationHoursOverride ?? definition.DurationHours;
        int? uses = options.UsesOverride ?? definition.Uses;
        double? now = Current?._api?.World.Calendar.TotalHours;

        return new()
        {
            InstanceId = string.IsNullOrWhiteSpace(options.InstanceId) ? Guid.NewGuid().ToString("N") : options.InstanceId,
            Code = definition.Code,
            SourceModId = definition.SourceModId,
            DisplayName = definition.DisplayName,
            DisplayNameLangCode = definition.DisplayNameLangCode,
            Description = definition.Description,
            DescriptionLangCode = definition.DescriptionLangCode,
            ExpiresAtTotalHours = duration.HasValue && now.HasValue ? now.Value + Math.Max(0, duration.Value) : null,
            UsesRemaining = uses,
            Priority = definition.Priority,
            Modifiers = definition.Modifiers.Select(CloneModifier).ToList(),
            ConsumeOn = definition.ConsumeOn.ToList(),
            Data = definition.Data
        };
    }

    private static IEnumerable<WeaponBuffInstance> GetBuffsInternal(ItemStack? stack, bool activeOnly)
    {
        if (stack?.Attributes == null) yield break;

        ITreeAttribute? root = stack.Attributes.GetTreeAttribute(AttributeKey);
        if (root == null) yield break;

        double? now = Current?._api?.World.Calendar.TotalHours;
        bool removed = false;

        foreach (KeyValuePair<string, IAttribute> entry in root.ToArray())
        {
            if (entry.Value is not ITreeAttribute tree) continue;

            WeaponBuffInstance buff = FromTree(entry.Key, tree);
            bool inactive = IsExpired(buff, now) || buff.UsesRemaining <= 0;
            if (activeOnly && inactive)
            {
                root.RemoveAttribute(entry.Key);
                removed = true;
                continue;
            }

            if (!activeOnly || !inactive)
            {
                yield return buff;
            }
        }

        if (removed)
        {
            RemoveRootIfEmpty(stack, root);
        }
    }

    private static IEnumerable<WeaponBuffInstance> GetActiveBuffs(ItemStack? stack)
    {
        return OrderBuffs(GetBuffsInternal(stack, activeOnly: true));
    }

    private static IOrderedEnumerable<WeaponBuffInstance> OrderBuffs(IEnumerable<WeaponBuffInstance> buffs)
    {
        return buffs
            .OrderBy(buff => buff.Priority)
            .ThenBy(buff => buff.SourceModId, StringComparer.Ordinal)
            .ThenBy(buff => buff.Code, StringComparer.Ordinal)
            .ThenBy(buff => buff.InstanceId, StringComparer.Ordinal);
    }

    private static ITreeAttribute GetOrCreateRoot(ItemStack stack)
    {
        EnsureAttributes(stack);
        ITreeAttribute? root = stack.Attributes.GetTreeAttribute(AttributeKey);
        if (root != null) return root;

        root = new TreeAttribute();
        stack.Attributes[AttributeKey] = root;
        return root;
    }

    private static void EnsureAttributes(ItemStack stack)
    {
        stack.Attributes ??= new TreeAttribute();
    }

    private static void RemoveRootIfEmpty(ItemStack stack, ITreeAttribute root)
    {
        if (root.Count == 0)
        {
            stack.Attributes.RemoveAttribute(AttributeKey);
        }
    }

    private static ITreeAttribute ToTree(WeaponBuffInstance instance)
    {
        TreeAttribute tree = new();
        tree.SetString(nameof(WeaponBuffInstance.InstanceId), instance.InstanceId);
        tree.SetString(nameof(WeaponBuffInstance.Code), instance.Code);
        tree.SetString(nameof(WeaponBuffInstance.SourceModId), instance.SourceModId);
        tree.SetString(nameof(WeaponBuffInstance.DisplayName), instance.DisplayName);
        tree.SetString(nameof(WeaponBuffInstance.DisplayNameLangCode), instance.DisplayNameLangCode);
        tree.SetString(nameof(WeaponBuffInstance.Description), instance.Description);
        tree.SetString(nameof(WeaponBuffInstance.DescriptionLangCode), instance.DescriptionLangCode);
        tree.SetInt(nameof(WeaponBuffInstance.Priority), instance.Priority);

        if (instance.ExpiresAtTotalHours.HasValue)
        {
            tree.SetDouble(nameof(WeaponBuffInstance.ExpiresAtTotalHours), instance.ExpiresAtTotalHours.Value);
        }

        if (instance.UsesRemaining.HasValue)
        {
            tree.SetInt(nameof(WeaponBuffInstance.UsesRemaining), instance.UsesRemaining.Value);
        }

        if (instance.ConsumeOn.Count > 0)
        {
            tree.SetString(nameof(WeaponBuffInstance.ConsumeOn), string.Join(",", instance.ConsumeOn.Select(trigger => trigger.ToString())));
        }

        TreeAttribute modifiers = new();
        for (int index = 0; index < instance.Modifiers.Count; index++)
        {
            WeaponStatModifier modifier = instance.Modifiers[index];
            TreeAttribute modifierTree = new();
            modifierTree.SetString(nameof(WeaponStatModifier.StatCode), modifier.StatCode);
            modifierTree.SetFloat(nameof(WeaponStatModifier.Add), modifier.Add);
            modifierTree.SetFloat(nameof(WeaponStatModifier.Multiply), modifier.Multiply);
            modifierTree.SetInt(nameof(WeaponStatModifier.Priority), modifier.Priority);
            modifiers.SetAttribute(index.ToString(), modifierTree);
        }
        tree.SetAttribute(nameof(WeaponBuffInstance.Modifiers), modifiers);

        if (instance.Data != null)
        {
            tree.SetAttribute(nameof(WeaponBuffInstance.Data), instance.Data);
        }

        return tree;
    }

    private static WeaponBuffInstance FromTree(string key, ITreeAttribute tree)
    {
        WeaponBuffInstance instance = new()
        {
            InstanceId = tree.GetString(nameof(WeaponBuffInstance.InstanceId), key),
            Code = tree.GetString(nameof(WeaponBuffInstance.Code), ""),
            SourceModId = tree.GetString(nameof(WeaponBuffInstance.SourceModId), ""),
            DisplayName = tree.GetString(nameof(WeaponBuffInstance.DisplayName), ""),
            DisplayNameLangCode = tree.GetString(nameof(WeaponBuffInstance.DisplayNameLangCode), ""),
            Description = tree.GetString(nameof(WeaponBuffInstance.Description), ""),
            DescriptionLangCode = tree.GetString(nameof(WeaponBuffInstance.DescriptionLangCode), ""),
            Priority = tree.GetInt(nameof(WeaponBuffInstance.Priority), 0),
            Data = tree.GetTreeAttribute(nameof(WeaponBuffInstance.Data))
        };

        double expires = tree.GetDouble(nameof(WeaponBuffInstance.ExpiresAtTotalHours), double.NaN);
        if (!double.IsNaN(expires))
        {
            instance.ExpiresAtTotalHours = expires;
        }

        int uses = tree.GetInt(nameof(WeaponBuffInstance.UsesRemaining), int.MinValue);
        if (uses != int.MinValue)
        {
            instance.UsesRemaining = uses;
        }

        string consumeOn = tree.GetString(nameof(WeaponBuffInstance.ConsumeOn), "");
        if (!string.IsNullOrWhiteSpace(consumeOn))
        {
            foreach (string value in consumeOn.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Enum.TryParse(value, ignoreCase: true, out WeaponBuffConsumptionTrigger trigger))
                {
                    instance.ConsumeOn.Add(trigger);
                }
            }
        }

        ITreeAttribute? modifiers = tree.GetTreeAttribute(nameof(WeaponBuffInstance.Modifiers));
        if (modifiers != null)
        {
            foreach (IAttribute value in modifiers.Values)
            {
                if (value is not ITreeAttribute modifierTree) continue;
                string statCode = modifierTree.GetString(nameof(WeaponStatModifier.StatCode), "");
                if (string.IsNullOrWhiteSpace(statCode)) continue;

                instance.Modifiers.Add(new()
                {
                    StatCode = statCode,
                    Add = modifierTree.GetFloat(nameof(WeaponStatModifier.Add), 0f),
                    Multiply = modifierTree.GetFloat(nameof(WeaponStatModifier.Multiply), 1f),
                    Priority = modifierTree.GetInt(nameof(WeaponStatModifier.Priority), 0)
                });
            }
        }

        return instance;
    }

    private static WeaponStatModifier CloneModifier(WeaponStatModifier modifier)
    {
        return new()
        {
            StatCode = modifier.StatCode,
            Add = modifier.Add,
            Multiply = modifier.Multiply,
            Priority = modifier.Priority
        };
    }

    private static bool IsExpired(WeaponBuffInstance buff, double? now)
    {
        return buff.ExpiresAtTotalHours.HasValue && now.HasValue && now.Value >= buff.ExpiresAtTotalHours.Value;
    }

    private static bool ShouldConsume(WeaponBuffInstance buff, WeaponBuffConsumptionTrigger trigger)
    {
        return buff.ConsumeOn.Contains(WeaponBuffConsumptionTrigger.Any) || buff.ConsumeOn.Contains(trigger);
    }

    private static void ApplyStatModifiers(WeaponBuffInstance buff, WeaponBuffMeleeStats stats)
    {
        stats.DamageMultiplier = ApplyFloat(buff, WeaponBuffStatCodes.DamageMultiplier, stats.DamageMultiplier);
        stats.DamageBonus = ApplyFloat(buff, WeaponBuffStatCodes.DamageBonus, stats.DamageBonus);
        stats.DamageTierBonus = ApplyInt(buff, WeaponBuffStatCodes.DamageTierBonus, stats.DamageTierBonus);
        stats.AttackSpeed = ApplyFloat(buff, WeaponBuffStatCodes.AttackSpeed, stats.AttackSpeed);
        stats.BlockTierBonus = ApplyInt(buff, WeaponBuffStatCodes.BlockTierBonus, stats.BlockTierBonus);
        stats.ParryTierBonus = ApplyInt(buff, WeaponBuffStatCodes.ParryTierBonus, stats.ParryTierBonus);
        stats.ThrownDamageMultiplier = ApplyFloat(buff, WeaponBuffStatCodes.ThrownDamageMultiplier, stats.ThrownDamageMultiplier);
        stats.ThrownDamageTierBonus = ApplyInt(buff, WeaponBuffStatCodes.ThrownDamageTierBonus, stats.ThrownDamageTierBonus);
        stats.ThrownAimingDifficulty = ApplyFloat(buff, WeaponBuffStatCodes.ThrownAimingDifficulty, stats.ThrownAimingDifficulty);
        stats.ThrownProjectileSpeedMultiplier = ApplyFloat(buff, WeaponBuffStatCodes.ThrownProjectileSpeedMultiplier, stats.ThrownProjectileSpeedMultiplier);
        stats.KnockbackMultiplier = ApplyFloat(buff, WeaponBuffStatCodes.KnockbackMultiplier, stats.KnockbackMultiplier);
        stats.ArmorPiercingBonus = ApplyInt(buff, WeaponBuffStatCodes.ArmorPiercingBonus, stats.ArmorPiercingBonus);
    }

    private static void ApplyStatModifiers(WeaponBuffInstance buff, WeaponBuffRangedStats stats)
    {
        stats.ReloadSpeed = ApplyFloat(buff, WeaponBuffStatCodes.ReloadSpeed, stats.ReloadSpeed);
        stats.DamageMultiplier = ApplyFloat(buff, WeaponBuffStatCodes.DamageMultiplier, stats.DamageMultiplier);
        stats.DamageTierBonus = ApplyInt(buff, WeaponBuffStatCodes.DamageTierBonus, stats.DamageTierBonus);
        stats.ProjectileSpeed = ApplyFloat(buff, WeaponBuffStatCodes.ProjectileSpeed, stats.ProjectileSpeed);
        stats.DispersionMultiplier = ApplyFloat(buff, WeaponBuffStatCodes.DispersionMultiplier, stats.DispersionMultiplier);
        stats.AimingDifficulty = ApplyFloat(buff, WeaponBuffStatCodes.AimingDifficulty, stats.AimingDifficulty);
    }

    private static void ApplyStatModifiers(WeaponBuffInstance buff, WeaponBuffProjectileStats stats)
    {
        stats.DamageMultiplier = ApplyFloat(buff, WeaponBuffStatCodes.DamageMultiplier, stats.DamageMultiplier);
        stats.DamageTierBonus = ApplyInt(buff, WeaponBuffStatCodes.DamageTierBonus, stats.DamageTierBonus);
        stats.KnockbackMultiplier = ApplyFloat(buff, WeaponBuffStatCodes.KnockbackMultiplier, stats.KnockbackMultiplier);
        stats.DropChanceMultiplier = ApplyFloat(buff, WeaponBuffStatCodes.DropChanceMultiplier, stats.DropChanceMultiplier);
        stats.PenetrationBonus = ApplyFloat(buff, WeaponBuffStatCodes.PenetrationBonus, stats.PenetrationBonus);
        stats.AdditionalDurabilityCost = ApplyInt(buff, WeaponBuffStatCodes.AdditionalDurabilityCost, stats.AdditionalDurabilityCost);
    }

    private static float ApplyFloat(WeaponBuffInstance buff, string statCode, float value)
    {
        foreach (WeaponStatModifier modifier in buff.Modifiers
            .Where(modifier => string.Equals(modifier.StatCode, statCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(modifier => modifier.Priority))
        {
            float next = (value + modifier.Add) * modifier.Multiply;
            if (float.IsFinite(next))
            {
                value = next;
            }
        }

        return value;
    }

    private static int ApplyInt(WeaponBuffInstance buff, string statCode, int value)
    {
        float modified = ApplyFloat(buff, statCode, value);
        return float.IsFinite(modified) ? (int)MathF.Round(modified) : value;
    }

    private static void ApplyProvider(WeaponBuffQueryContext context, WeaponBuffInstance buff, Action<IWeaponBuffProvider> action)
    {
        foreach (IWeaponBuffProvider provider in _providers.ToArray())
        {
            if (!ProviderHandles(provider, context, buff)) continue;
            TryProvider(provider, () => action(provider));
        }
    }

    private static bool ProviderHandles(IWeaponBuffProvider provider, WeaponBuffQueryContext context, WeaponBuffInstance buff)
    {
        bool handles = false;
        TryProvider(provider, () => handles = provider.Handles(context, buff));
        return handles;
    }

    private static void TryProvider(IWeaponBuffProvider provider, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            string key = $"{provider.GetType().FullName}:{exception.GetType().FullName}:{exception.Message}";
            if (_providerErrors.Add(key))
            {
                Current?._api?.Logger.Warning($"[WeaponBuffSystem] Provider {provider.GetType().FullName} failed: {exception}");
            }
        }
    }

    private static void AppendDefaultTooltip(WeaponBuffTooltipContext context, WeaponBuffInstance buff)
    {
        string name = ResolveText(buff.DisplayNameLangCode, buff.DisplayName, buff.Code);
        if (!string.IsNullOrWhiteSpace(name))
        {
            context.Description.AppendLine(name);
        }

        string description = ResolveText(buff.DescriptionLangCode, buff.Description, "");
        if (!string.IsNullOrWhiteSpace(description))
        {
            context.Description.AppendLine(description);
        }
    }

    private static string ResolveText(string langCode, string literal, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(langCode))
        {
            string translated = Lang.Get(langCode);
            if (!string.Equals(translated, langCode, StringComparison.Ordinal))
            {
                return translated;
            }
        }

        return string.IsNullOrWhiteSpace(literal) ? fallback : literal;
    }
}
