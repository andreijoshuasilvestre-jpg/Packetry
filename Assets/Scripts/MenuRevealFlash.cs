using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared "pop into existence with a glow flash" animation — the main menu's
/// first-appearance entrance for its title/logo and buttons, played one
/// element at a time (see MainMenuBootstrap.PlayFirstAppearanceReveal)
/// instead of the whole menu sliding in as one block. Reuses the exact same
/// glow-pulse language as MenuButtonFX's own click flash (RadialGlowSprite,
/// same peak alpha/scale) so an element "arriving" reads as the same visual
/// event as a button being pressed, just fading up from invisible instead of
/// reacting to a click. MenuButtonFX.PlayRevealIn() drives this for buttons;
/// MainMenuBootstrap drives it directly for the plain logo image, which has
/// no MenuButtonFX of its own.
/// </summary>
public static class MenuRevealFlash
{
    public const float Duration = 0.34f;
    public const float StartScale = 0.82f;
    const float OvershootScale = 1.08f;
    const float OvershootPoint = 0.6f; // fraction of Duration spent growing past 1 before settling back
    const float GlowPeak  = 0.65f;
    const float GlowScale = 1.35f;

    /// <summary>
    /// fadeGroup: faded 0→1 over the reveal (null to skip). scaleRect: popped
    /// from StartScale up through an overshoot and back to 1 (null to skip).
    /// glowImage: pulsed 0→GlowPeak→0, assumed to be a child of scaleRect (so
    /// its own scale is compensated the same way MenuButtonFX.ClickRoutine
    /// does) — null to skip. sparkTarget/sparkParent/eventCamera: passed
    /// straight to UISparks.EmitBurst at the moment the reveal starts; pass a
    /// null sparkParent to skip the spark burst.
    /// </summary>
    public static IEnumerator Play(CanvasGroup fadeGroup, RectTransform scaleRect, Image glowImage, Color glowColor,
        RectTransform sparkTarget, RectTransform sparkParent, Camera eventCamera)
    {
        if (sparkParent != null && sparkTarget != null)
            UISparks.EmitBurst(sparkTarget, sparkParent, eventCamera, glowColor);

        float t = 0f;
        while (t < Duration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / Duration);

            if (fadeGroup != null) fadeGroup.alpha = EaseOutCubic(p);

            float scale = 1f;
            if (scaleRect != null)
            {
                scale = p < OvershootPoint
                    ? Mathf.LerpUnclamped(StartScale, OvershootScale, EaseOutCubic(p / OvershootPoint))
                    : Mathf.LerpUnclamped(OvershootScale, 1f, EaseOutCubic((p - OvershootPoint) / (1f - OvershootPoint)));
                scaleRect.localScale = Vector3.one * scale;
            }

            if (glowImage != null)
            {
                float arch = Mathf.Sin(p * Mathf.PI); // 0 -> 1 -> 0, same single pulse as MenuButtonFX's click glow
                glowImage.color = new Color(glowColor.r, glowColor.g, glowColor.b, arch * GlowPeak);
                float desiredScale = 1f + arch * (GlowScale - 1f);
                glowImage.rectTransform.localScale = Vector3.one * (scale > 0.0001f ? desiredScale / scale : desiredScale);
            }

            yield return null;
        }

        if (fadeGroup != null) fadeGroup.alpha = 1f;
        if (scaleRect != null) scaleRect.localScale = Vector3.one;
        if (glowImage != null)
        {
            glowImage.color = new Color(glowColor.r, glowColor.g, glowColor.b, 0f);
            glowImage.rectTransform.localScale = Vector3.one;
        }
    }

    static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
}
