using UnityEngine;

/// <summary>
/// Keeps a fixed-size, center-anchored panel fully on screen with at least
/// UIScale.ScreenMargin canvas units of breathing room on every side, whenever
/// its root canvas is too small to hold it at its authored size — e.g. a
/// 920-unit-tall Settings panel on a canvas that's only 720 units tall once
/// the player cranks UI Scale to 1.5×, or a window narrower/shorter than 16:9.
/// Never grows a panel past its authored size, so on screens with enough room
/// it looks exactly as built.
///
/// Two ways to make it fit (see FitMode):
///   - Shrink: uniformly scales the whole panel down (localScale). Works on
///     any panel, but also shrinks its text — so past the limit, raising UI
///     Scale stops making that panel's contents any bigger.
///   - ClampSize: caps the panel's rect size instead and leaves localScale at
///     1, so its contents keep growing with UI Scale. Only for panels whose
///     children are laid out against the panel's edges and whose body can
///     scroll (e.g. SettingsPanelManager's) — anything positioned from the
///     authored size would otherwise spill out of the smaller rect.
///
/// Re-checked every LateUpdate rather than once, since UIScale's slider
/// changes the canvas's logical size live while this very panel may be the
/// one showing that slider.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class FitToScreen : MonoBehaviour
{
    public enum FitMode { Shrink, ClampSize }

    FitMode mode;
    Vector2 authoredSize;
    RectTransform rt;
    RectTransform canvasRt;

    /// <summary>Call after the panel's sizeDelta is set — that size is captured
    /// as the largest the panel will ever be.</summary>
    public static FitToScreen Attach(GameObject panel, FitMode mode = FitMode.Shrink)
    {
        var fit = panel.TryGetComponent(out FitToScreen existing) ? existing : panel.AddComponent<FitToScreen>();
        fit.rt           = (RectTransform)panel.transform;
        fit.mode         = mode;
        fit.authoredSize = fit.rt.sizeDelta;
        return fit;
    }

    void LateUpdate()
    {
        if (rt == null || authoredSize.x <= 0f || authoredSize.y <= 0f) return;
        if (canvasRt == null)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            canvasRt = (RectTransform)canvas.rootCanvas.transform;
        }

        Vector2 available = canvasRt.rect.size - 2f * UIScale.ScreenMargin * Vector2.one;

        if (mode == FitMode.ClampSize)
        {
            Vector2 size = Vector2.Max(Vector2.Min(authoredSize, available), Vector2.one);
            if (rt.sizeDelta != size) rt.sizeDelta = size;
            return;
        }

        float scale = Mathf.Min(1f, available.x / authoredSize.x, available.y / authoredSize.y);
        scale = Mathf.Max(scale, 0.1f);
        if (!Mathf.Approximately(rt.localScale.x, scale))
            rt.localScale = new Vector3(scale, scale, 1f);
    }
}
