using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace CombatOverhaul.Implementations;

public class HandbookAntlerMount : BlockAntlerMount
{
    private const string HandbookRepresentativeAttribute = "combatOverhaulHandbookRepresentative";

    public override List<ItemStack> GetHandBookStacks(ICoreClientAPI capi)
    {
        if (Code == null) return null;
        if (Attributes?["handbook"]?["exclude"].AsBool() == true) return null;

        string[] types = Attributes?["types"].AsArray<string>() ?? System.Array.Empty<string>();
        List<ItemStack> stacks = new();

        foreach (string type in types)
        {
            ItemStack stack = new(this);
            stack.Attributes.SetString("type", type);
            stack.Attributes.SetString("material", "oak");
            stack.Attributes.SetBool(HandbookRepresentativeAttribute, true);
            stack.TempAttributes.SetBool(HandbookRepresentativeAttribute, true);
            stacks.Add(stack);
        }

        return stacks.Count > 0 ? stacks : base.GetHandBookStacks(capi);
    }

    public override bool Satisfies(ItemStack thisStack, ItemStack otherStack)
    {
        if (IsHandbookRepresentative(otherStack)
            && thisStack?.Collectible == this
            && otherStack.Collectible == this
            && thisStack.Class == otherStack.Class
            && thisStack.Id == otherStack.Id)
        {
            string recipeType = thisStack.Attributes.GetString("type");
            string handbookType = otherStack.Attributes.GetString("type");
            return recipeType == handbookType;
        }

        return base.Satisfies(thisStack, otherStack);
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (IsHandbookRepresentative(inSlot.Itemstack)) return;

        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
    }

    private static bool IsHandbookRepresentative(ItemStack? itemStack)
    {
        return itemStack?.TempAttributes?.GetBool(HandbookRepresentativeAttribute, false) == true
            || itemStack?.Attributes?.GetBool(HandbookRepresentativeAttribute, false) == true;
    }
}
