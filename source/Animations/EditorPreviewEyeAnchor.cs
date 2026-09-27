namespace CombatOverhaul.Animations;

internal static class EditorPreviewEyeAnchor
{
    internal static float ResolveY(float localEyePositionY, float eyeHeight, bool frozenEditorPreview)
    {
        // PlayerFrame converts LocalEyePos.Y - EyeHeight into a lower-torso translation. During a
        // frozen DevTools preview that translation affects only the rendered model; the real camera
        // remains stationary. Matching the anchor to EyeHeight removes that model-only displacement.
        return frozenEditorPreview ? eyeHeight : localEyePositionY;
    }
}
