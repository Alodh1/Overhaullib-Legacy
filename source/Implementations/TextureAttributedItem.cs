using CombatOverhaul.Utils;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace CombatOverhaul.Implementations;

public class TextureAttributedItem : Item, IHandBookPageCodeProvider
{
    public virtual string HandbookPageCodeForStack(IWorldAccessor world, ItemStack stack)
    {
        return TextureAttributeHandbook.PageCodeForStack(stack, TextureAttributeHandbook.GetTextureAttributes(this));
    }

    public override bool Satisfies(ItemStack thisStack, ItemStack otherStack)
    {
        if (TextureAttributeHandbook.SatisfiesIgnoringAttributes(api?.World, thisStack, otherStack, TextureAttributeHandbook.GetTextureAttributes(this)))
        {
            return true;
        }

        return base.Satisfies(thisStack, otherStack);
    }
}
