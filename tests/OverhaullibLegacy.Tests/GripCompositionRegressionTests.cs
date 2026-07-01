using CombatOverhaul.Animations;

namespace OverhaullibLegacy.Tests;

/// <summary>
/// Regression coverage for the polearm grip-length feature (hold right-click + scroll). The grip is a
/// weight-0 "grip" animation layer that offsets the right-hand ItemAnchor and must be added on top of
/// the positive-weight main-hand animation. The frame compose step resolves positive weights with
/// "max weight wins" and treats weight &lt;= 0 layers as additive; those additive layers must apply
/// regardless of the order in which layers are composed. Sharing a single field between the two paths
/// let a positive-weight frame composed after the grip layer overwrite the grip offset, silently
/// disabling grip changes.
/// </summary>
public sealed class GripCompositionRegressionTests
{
    [Fact]
    public void AnimationElementCompose_AppliesAdditiveLayerRegardlessOfOrder()
    {
        AnimationElement baseFrame = new(10, null, null, null, null, null);
        AnimationElement additiveFrame = new(3, null, null, null, null, null);

        AnimationElement baseFirst = AnimationElement.Compose(new[]
        {
            (baseFrame, 1f),
            (additiveFrame, 0f)
        });

        AnimationElement additiveFirst = AnimationElement.Compose(new[]
        {
            (additiveFrame, 0f),
            (baseFrame, 1f)
        });

        Assert.Equal(13f, baseFirst.OffsetX!.Value);
        Assert.Equal(13f, additiveFirst.OffsetX!.Value);
    }

    [Fact]
    public void AnimationElementCompose_AddsAdditiveLayerOnTopOfMaxWeightWinner()
    {
        AnimationElement lowWeight = new(4, null, null, null, null, null);
        AnimationElement highWeight = new(7, null, null, null, null, null);
        AnimationElement additive = new(2, null, null, null, null, null);

        AnimationElement result = AnimationElement.Compose(new[]
        {
            (lowWeight, 0.5f),
            (highWeight, 1f),
            (additive, 0f)
        });

        // Max-weight winner (7) plus the additive layer (2).
        Assert.Equal(9f, result.OffsetX!.Value);
    }

    [Fact]
    public void PlayerFrameCompose_GripLayerOffsetsItemAnchorRegardlessOfOrder()
    {
        AnimationElement nullElement = new(null, null, null, null, null, null);

        // Mirrors the main-hand animation positioning the held item, weight 1.
        PlayerFrame mainHandFrame = new(rightHand: new RightHandFrame(
            new AnimationElement(10, null, null, null, null, null),
            AnimationElement.Zero,
            AnimationElement.Zero));

        // Mirrors GripController.GetAimingFrame: a weight-0 right-hand ItemAnchor offset.
        PlayerFrame gripFrame = new(rightHand: new RightHandFrame(
            new AnimationElement(5, null, null, null, null, null),
            nullElement,
            nullElement));

        PlayerFrame mainFirst = PlayerFrame.Compose(new[]
        {
            (mainHandFrame, 1f),
            (gripFrame, 0f)
        });

        PlayerFrame gripFirst = PlayerFrame.Compose(new[]
        {
            (gripFrame, 0f),
            (mainHandFrame, 1f)
        });

        Assert.NotNull(mainFirst.RightHand);
        Assert.NotNull(gripFirst.RightHand);
        Assert.Equal(15f, mainFirst.RightHand!.Value.ItemAnchor.OffsetX!.Value);
        Assert.Equal(15f, gripFirst.RightHand!.Value.ItemAnchor.OffsetX!.Value);
    }
}
