using CombatOverhaul.Implementations;
using CombatOverhaul.Utils;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace CombatOverhaul.Integration;

public static class ShieldAutoPatcher
{
    public static void Patch(ICoreAPI api)
    {
        foreach (Item item in api.World.Items)
        {
            if (item.Code?.Domain != "game" && item.Code?.Domain != "survival") continue;
            string path = item.Code?.Path ?? "";
            if (!path.StartsWith("shield-") && !path.Contains("shield")) continue;

            // Skip shields already handled by CO class/behavior.
            if (item.GetCollectibleInterface<IHasMeleeWeaponActions>() != null) continue;

            try
            {
                ApplyCombatAttributes(item);
                AttachMeleeBehavior(item, api);
            }
            catch (Exception exception)
            {
                LoggerUtil.Error(api, typeof(ShieldAutoPatcher), $"Error while patching shield '{item.Code}':\n{exception}");
            }
        }
    }

    private static void ApplyCombatAttributes(Item item)
    {
        if (item.Attributes?.Token is not JObject attributes)
        {
            return;
        }

        bool metalShield = item.Code?.Path?.Contains("blackguard") == true;

        string heavySound = metalShield ? "game:sounds/held/shieldblock-metal-heavy" : "game:sounds/held/shieldblock-wood-heavy";
        string lightSound = metalShield ? "game:sounds/held/shieldblock-metal-light" : "game:sounds/held/shieldblock-wood-light";
        int parryTier = metalShield ? 8 : 4;
        int blockTier = metalShield ? 4 : 2;
        int staggerTier = metalShield ? 8 : 4;

        JObject offHandStance = new()
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

        attributes["OffHandStance"] = offHandStance;
    }

    private static void AttachMeleeBehavior(Item item, ICoreAPI api)
    {
        MeleeWeaponBehavior behavior = new(item);
        behavior.Initialize(new JsonObject(new JObject()));
        behavior.OnLoaded(api);

        item.CollectibleBehaviors = (item.CollectibleBehaviors ?? []).Append(behavior).ToArray();
    }
}
