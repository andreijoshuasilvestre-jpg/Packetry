using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gameplay HUD button entrance: a split-second faint afterimage of the icon,
/// then the button's own click-glow (SidebarIconButtonFX.PlayClickPunch —
/// literally the same effect a press plays), with the icon snapping to fully
/// solid the instant the glow fires so the "reveal" reads as masked by the
/// flash rather than a separate fade-in. Used in place of the old per-button
/// slide-in (see NetworkUIManager.StaggerRevealFlashChildren) — the button
/// never moves, it just flashes into existence where it already sits.
/// </summary>
public static class HudButtonRevealFlash
{
    const float AfterimageUp   = 0.02f;
    const float AfterimageHold = 0.02f;
    const float AfterimageDown = 0.02f; // "only a split second"
    const float AfterimageAlpha = 0.35f;

    public static IEnumerator Play(SidebarIconButtonFX fx, Image icon)
    {
        Color c = icon.color;
        icon.color = new Color(c.r, c.g, c.b, 0f);

        yield return FadeIconAlpha(icon, 0f, AfterimageAlpha, AfterimageUp);
        yield return new WaitForSecondsRealtime(AfterimageHold);
        yield return FadeIconAlpha(icon, AfterimageAlpha, 0f, AfterimageDown);

        icon.color = new Color(c.r, c.g, c.b, 1f);
        if (fx != null) fx.PlayClickPunch();
    }

    static IEnumerator FadeIconAlpha(Image img, float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
            var c = img.color;
            img.color = new Color(c.r, c.g, c.b, a);
            yield return null;
        }
        var cc = img.color;
        img.color = new Color(cc.r, cc.g, cc.b, to);
    }
}
