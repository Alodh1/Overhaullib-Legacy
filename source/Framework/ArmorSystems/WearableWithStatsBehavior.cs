using System.Text;
using CombatOverhaul.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace CombatOverhaul.Armor;

public sealed class WearableStatsJson
{
    public Dictionary<string, float> PlayerStats { get; set; } = [];
}

public class WearableWithStatsBehavior : CollectibleBehavior, IAffectsPlayerStats
{
    public WearableWithStatsBehavior(CollectibleObject collObj) : base(collObj)
    {
    }
    public Dictionary<string, float> Stats { get; set; } = new();
    public bool StatsChanged { get; set; } = false;

    public Dictionary<string, float> PlayerStats(ItemSlot slot, EntityPlayer player)
    {
        float penaltyMultiplier = QuenchableStatUtil.GetArmorPenaltyMultiplier(slot?.Itemstack);
        if (Math.Abs(penaltyMultiplier - 1f) < 0.0001f)
        {
            return Stats;
        }

        Dictionary<string, float> adjusted = new(Stats.Count);
        foreach ((string stat, float value) in Stats)
        {
            adjusted[stat] = IsPenaltyStat(stat) ? value * penaltyMultiplier : value;
        }

        return adjusted;
    }

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        WearableStatsJson stats = properties.AsObject<WearableStatsJson>();

        Stats = stats.PlayerStats;
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (Stats.Values.Any(value => value != 0))
        {
            dsc.AppendLine(Lang.Get("combatoverhaul:stat-stats"));
            foreach ((string stat, float value) in Stats)
            {
                if (value != 0f) dsc.AppendLine($"  {Lang.Get($"combatoverhaul:stat-{stat}")}: {value * 100:F1}%");
            }
            dsc.AppendLine();
        }
    }

    private static bool IsPenaltyStat(string statCode)
    {
        return string.Equals(statCode, "walkspeed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(statCode, "manipulationSpeed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(statCode, "steadyAim", StringComparison.OrdinalIgnoreCase)
            || string.Equals(statCode, "healingeffectivness", StringComparison.OrdinalIgnoreCase)
            || string.Equals(statCode, "hungerrate", StringComparison.OrdinalIgnoreCase);
    }
}

/*public class WearableWithItemStackStatsBehavior : CollectibleBehavior, IAffectsPlayerStats
{
    public WearableWithItemStackStatsBehavior(CollectibleObject collObj) : base(collObj)
    {
    }
   
    public bool StatsChanged { get; set; } = false;

    public Dictionary<string, float> PlayerStats(ItemSlot slot, EntityPlayer player) => Stats;

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        StatAttribute = properties["statAttribute"].AsString(StatAttribute);
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (Stats.Values.Any(value => value != 0))
        {
            dsc.AppendLine(Lang.Get("combatoverhaul:stat-stats"));
            foreach ((string stat, float value) in Stats)
            {
                if (value != 0f) dsc.AppendLine($"  {Lang.Get($"combatoverhaul:stat-{stat}")}: {value * 100:F1}%");
            }
            dsc.AppendLine();
        }
    }



    protected string StatAttribute = "playerStats";

    protected virtual Dictionary<string, float> GetItemStackStats(ItemStack stack)
    {
        return [];
    }


}*/
