using CombatOverhaul.Implementations;
using CombatOverhaul.Utils;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CombatOverhaul.Integration;

public static class ShieldAutoPatcher
{
    public static bool IsCombatOverhaulEnabled(ICoreAPI? api)
    {
        return api?.ModLoader.IsModEnabled("combatoverhaul") == true
            || api?.ModLoader.IsModEnabled("combatoverhaulfork") == true;
    }

    public static void Patch(ICoreAPI api)
    {
        if (!IsCombatOverhaulEnabled(api)) return;

        int patched = 0;

        foreach (Item item in api.World.Items)
        {
            if (!CollectibleClassifier.IsShield(item)) continue;
            if (CollectibleClassifier.HasMeleeWeaponActions(item)) continue;

            try
            {
                EnsureCombatAttributes(item);
                if (AttachMeleeBehavior(item, api))
                {
                    patched++;
                }
            }
            catch (Exception exception)
            {
                LoggerUtil.Error(api, typeof(ShieldAutoPatcher), $"Error while patching shield '{item.Code}':\n{exception}");
            }
        }

        if (patched > 0)
        {
            api.Logger.Notification($"[OverhaullibLegacyCompat] Applied Combat Overhaul shield behavior to {patched} shield item(s).");
        }
    }

    private static void EnsureCombatAttributes(Item item)
    {
        JObject attributes = item.Attributes?.Token as JObject ?? new JObject();
        item.Attributes = new JsonObject(attributes);

        JObject defaultOffHandStance = CreateDefaultOffHandStance(item);

        if (attributes["Modes"] is JObject modes && modes.Properties().Any())
        {
            foreach (JProperty mode in modes.Properties())
            {
                if (mode.Value is JObject modeStats && modeStats["OffHandStance"] == null)
                {
                    modeStats["OffHandStance"] = defaultOffHandStance.DeepClone();
                }
            }

            return;
        }

        if (attributes["OffHandStance"] == null)
        {
            attributes["OffHandStance"] = defaultOffHandStance;
        }
    }

    private static JObject CreateDefaultOffHandStance(Item item)
    {
        bool metalShield = item.Code?.Path?.Contains("blackguard") == true;

        string heavySound = metalShield ? "game:sounds/held/shieldblock-metal-heavy" : "game:sounds/held/shieldblock-wood-heavy";
        string lightSound = metalShield ? "game:sounds/held/shieldblock-metal-light" : "game:sounds/held/shieldblock-wood-light";
        int parryTier = metalShield ? 8 : 4;
        int blockTier = metalShield ? 4 : 2;
        int staggerTier = metalShield ? 8 : 4;

        return new JObject
        {
            ["CanAttack"] = false,
            ["CanParry"] = false,
            ["CanBlock"] = true,
            ["CanSprint"] = true,
            ["SpeedPenalty"] = 0f,
            ["BlockSpeedPenalty"] = -0.1f,
            ["ParryCooldownMs"] = 600,
            ["BlockCooldownMs"] = 0,
            ["Parry"] = new JObject
            {
                ["Zones"] = new JArray("Head", "Face", "Neck", "Torso", "LeftArm", "RightArm", "LeftHand", "RightHand", "LeftLeg", "RightLeg", "LeftFoot", "RightFoot"),
                ["Directions"] = new JArray(30, 45, 30, 45),
                ["Sound"] = heavySound,
                ["BlockTier"] = new JObject
                {
                    ["BluntAttack"] = parryTier,
                    ["SlashingAttack"] = parryTier,
                    ["PiercingAttack"] = parryTier
                },
                ["StaggerTier"] = staggerTier,
                ["StaggerTimeMs"] = 2000
            },
            ["Block"] = new JObject
            {
                ["Zones"] = new JArray("Face", "Neck", "Torso", "LeftArm", "RightArm", "LeftHand", "RightHand", "LeftLeg", "RightLeg"),
                ["Directions"] = new JArray(30, 60, 45, 60),
                ["Sound"] = lightSound,
                ["BlockTier"] = new JObject
                {
                    ["BluntAttack"] = blockTier,
                    ["SlashingAttack"] = blockTier,
                    ["PiercingAttack"] = blockTier
                }
            },
            ["BlockAnimation"] = "combatoverhaul:shield-light-parry",
            ["ReadyAnimation"] = "combatoverhaul:shield-light-ready",
            ["IdleAnimation"] = "combatoverhaul:shield-light-ready"
        };
    }

    private static bool AttachMeleeBehavior(Item item, ICoreAPI api)
    {
        if (CollectibleClassifier.HasMeleeWeaponActions(item)) return false;

        MeleeWeaponBehavior behavior = new(item);
        behavior.Initialize(new JsonObject(new JObject()));
        behavior.OnLoaded(api);

        item.CollectibleBehaviors = (item.CollectibleBehaviors ?? []).Append(behavior).ToArray();
        return true;
    }
}
