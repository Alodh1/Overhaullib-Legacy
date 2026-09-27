using CombatOverhaul.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace CombatOverhaul.Integration;

internal static class ArmorQuenchComponents
{
    internal static void Apply(ICoreAPI api)
    {
        if (!QuenchablePatchGate.Enabled) return;
        foreach (Item item in api.World.Items)
        {
            if (item.Code?.Domain != "game"
                || !(item.Code.Path.StartsWith("metalchain-") || item.Code.Path.StartsWith("metalscale-"))
                || !QuenchableStateUtil.IsFerrous(new ItemStack(item))) continue;

            item.Attributes ??= JsonObject.FromJson("{}");
            item.Attributes.Token!["forgable"] = true;
            item.Attributes.Token![QuenchableStateUtil.KindAttribute] = QuenchableStateUtil.ArmorKind;
            if (item.GetBehavior<CollectibleBehaviorQuenchable>() != null) continue;
            var quenchable = new CollectibleBehaviorQuenchable(item);
            quenchable.Initialize(JsonObject.FromJson("{\"metalVariantgroupCode\":\"metal\"}"));
            item.CollectibleBehaviors = [.. item.CollectibleBehaviors, quenchable];
            quenchable.OnLoaded(api);
        }
    }
}
