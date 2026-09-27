using CombatOverhaul.Animations;

namespace OverhaullibLegacy.Tests;

public sealed class EditorPreviewEyeAnchorTests
{
    [Fact]
    public void ResolveY_UsesEyeHeightForFrozenEditorPreview()
    {
        float resolved = EditorPreviewEyeAnchor.ResolveY(1.48f, 1.62f, frozenEditorPreview: true);

        Assert.Equal(1.62f, resolved, 4);
    }

    [Fact]
    public void ResolveY_PreservesLiveEyePositionForRuntimeAnimation()
    {
        float resolved = EditorPreviewEyeAnchor.ResolveY(1.48f, 1.62f, frozenEditorPreview: false);

        Assert.Equal(1.48f, resolved, 4);
    }
}
