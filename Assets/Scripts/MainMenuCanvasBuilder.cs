using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the main menu's Canvas — Screen Space - Overlay (camera-independent,
/// so it renders identically in the Scene view without Play and in the actual
/// Game view — no perspective/parallax tricks left to depend on a camera for)
/// plus a full-screen solid-color Background image so the menu's backdrop is
/// real, visible scene content rather than only ever showing up via
/// Camera.backgroundColor at Play time.
///
/// Used by MainMenuBootstrap.ResolveCanvas's runtime fallback; a pre-placed
/// Canvas in the scene should be built the same way by hand (or by calling
/// this once from an Editor script) so the two never diverge.
/// </summary>
public static class MainMenuCanvasBuilder
{
    public static Canvas Build(Transform parent, Color backgroundColor)
    {
        var canvasGo = new GameObject("MainMenu_Canvas");
        canvasGo.transform.SetParent(parent, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);

        canvasGo.AddComponent<GraphicRaycaster>();

        BuildBackground(canvasGo.transform, backgroundColor);

        return canvas;
    }

    // First child of the canvas, so it renders behind everything else added
    // afterward (MenuRoot, LevelSelectShell, ...) without needing explicit
    // sibling-index management by the caller.
    static void BuildBackground(Transform parent, Color color)
    {
        var go = new GameObject("Background", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
    }
}
