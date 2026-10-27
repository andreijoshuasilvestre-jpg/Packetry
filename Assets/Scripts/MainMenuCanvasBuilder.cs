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
    public static Canvas Build(Transform parent, Color backgroundColor, out CanvasGroup backgroundGroup)
    {
        var canvasGo = new GameObject("MainMenu_Canvas");
        canvasGo.transform.SetParent(parent, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);

        canvasGo.AddComponent<GraphicRaycaster>();

        backgroundGroup = BuildBackground(canvasGo.transform, backgroundColor);

        return canvas;
    }

    const string BackgroundPatternPath = "MainMenuUI/MainMenuBg_Pattern";

    // First child of the canvas, so it renders behind everything else added
    // afterward (MenuRoot, LevelSelectShell, ...) without needing explicit
    // sibling-index management by the caller. Carries its own CanvasGroup so
    // a scene transition can fade it independently of every other panel
    // (see LoadingScreenManager.ActivateAndFade) — fading this one container
    // fades all the layers inside it together.
    //
    // Inside it: the scrolling icon pattern, plus a soft vignette on top so
    // the edges read a little darker than the center. Falls back to a flat
    // solid color under the vignette if the pattern resource is missing.
    static CanvasGroup BuildBackground(Transform parent, Color color)
    {
        var go = new GameObject("Background", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        BuildLayers(go.transform, color);

        return go.AddComponent<CanvasGroup>();
    }

    /// <summary>
    /// Same layers as BuildBackground, applied in place to an
    /// already-existing "Background" transform instead of building a new
    /// one — for the pre-placed-Canvas path (MainMenuBootstrap.ResolveCanvas),
    /// where a hand-dragged or baked Background still has the old flat
    /// single-RawImage layout that predates the pattern/vignette child layers.
    /// Idempotent: a no-op if this Background has already been upgraded
    /// (e.g. Play was entered twice against the same scene instance) — the
    /// vignette is built on both of BuildLayers' paths, so it's the marker.
    /// </summary>
    public static void UpgradeBackground(Transform background, Color fallbackColor)
    {
        if (background.Find("Background_Vignette") != null) return;

        // ScrollingBackground [RequireComponent]s RawImage, so it has to be
        // gone before Unity will let the RawImage underneath it go too —
        // DestroyImmediate (rather than the usual deferred Destroy) so the
        // second removal below doesn't run in the same frame as a
        // still-pending first one, which is what trips that dependency
        // check. One-time structural fixup at Awake, not per-frame, so the
        // usual "don't DestroyImmediate during gameplay" caution doesn't
        // apply here.
        var flatScroll = background.GetComponent<ScrollingBackground>();
        if (flatScroll != null) Object.DestroyImmediate(flatScroll);
        var flat = background.GetComponent<RawImage>();
        if (flat != null) Object.DestroyImmediate(flat);

        BuildLayers(background, fallbackColor);
    }

    /// <summary>
    /// The pattern layer + vignette, added as children of an existing
    /// full-screen transform. Also how LoadingScreenManager builds its
    /// bridge copy of this background — the pattern lands on the same
    /// screen-space grid wherever it's built (see ScrollingBackground), so
    /// the two are identical on screen.
    /// </summary>
    public static void BuildLayers(Transform background, Color fallbackColor)
    {
        var pattern = Resources.Load<Texture2D>(BackgroundPatternPath);
        if (pattern != null)
        {
            BuildPatternLayer(background, pattern);
        }
        else
        {
            var flatImg = background.gameObject.AddComponent<RawImage>();
            flatImg.raycastTarget = false;
            flatImg.color = fallbackColor;
        }

        BuildVignette(background);
    }

    static void BuildPatternLayer(Transform parent, Texture2D pattern)
    {
        var go = new GameObject("Background_Pattern", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<RawImage>();
        img.raycastTarget = false;
        img.texture = pattern; // already tinted — drawn as-is

        go.AddComponent<ScrollingBackground>(); // default tile size + drift, the same the gameplay quad uses
    }

    // Shared with the level's copy of the vignette (GameBootstrap.
    // SetupBackgroundVignette), so the background's edges keep darkening
    // exactly the same way after the menu → level swap.
    public static readonly Color VignetteColor = new Color(0f, 0f, 0f, 0.55f);

    // MainMenu.unity's Background is hand-scaled 1.61× (see ScrollingBackground.
    // DefaultTileSizePixels) and the vignette stretches with it, so on screen
    // it spans 1.61× the screen, not 1×. The level's copy has no menu scene
    // left to read that from, so it's mirrored here.
    public const float MenuVignetteScreenScale = 1.61f;

    static void BuildVignette(Transform parent)
    {
        var go = new GameObject("Background_Vignette", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        img.sprite = VignetteSprite.Get();
        img.type = Image.Type.Simple; // stretched non-uniformly to the rect (not preserving the sprite's square aspect) is what turns it elliptical, matching the screen
        img.color = VignetteColor;
    }
}
