using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.Utils;

public class GenericDisplayBlockEntity : BlockEntity
{
    public bool OnInteract(IPlayer byPlayer, BlockSelection blockSel) => false;
}

public class GenericDisplayBlock : Block
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is GenericDisplayBlockEntity be)
        {
            return be.OnInteract(byPlayer, blockSel);
        }

        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }
}
