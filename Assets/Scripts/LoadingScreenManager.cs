using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Scene-transition overlay — masks the hitch of loading and initializing a
/// scene's GameBootstrap (procedural device meshes, managers, HUD, ...)
/// without ever showing a dedicated "loading screen": every transition stays
/// on the current screen while the target scene preloads (see
/// PreloadSceneCorner's small, non-blocking corner readout), and the actual
/// scene swap itself carries straight through whatever's already on screen
/// (the menu → level swap specifically leaves the persistent background
/// fully visible right up to the cut rather than fading it out, AND shows
/// its own copy of that same pattern (BuildBackgroundBridge) on this
/// DontDestroyOnLoad canvas through the swap itself, in case the outgoing
/// scene's canvas tears down a frame or two before the incoming scene's own
/// background takes over — see ActivateAndFadeRoutine) — no glow/label/
/// progress-bar cover, no separate "screen" in between.
///
/// PreloadSceneCorner(...) + ActivateAndFade(...) is the whole flow: the
/// corner readout plays first, then once the caller has finished its own
/// in-place exit animation (sliding UI away, etc.) it calls ActivateAndFade
/// to actually flip the scene over.
///
/// Lives on a DontDestroyOnLoad GameObject created on first use, so it
/// survives the very scene swap it's covering for and is reused for every
/// future transition (level 2+, returning to the menu, ...).
/// </summary>
public class LoadingScreenManager : MonoBehaviour
{
    const string FontResourcePath = "Fonts/PixeloidMono SDF";

    // Never flashes on/off even if the target scene loads almost instantly —
    // the fade + hold always reads as a deliberate loading beat, not a glitch.
    const float MinShowTime = 0.6f;
    const float FadeTime    = 0.25f;

    /// <summary>Quicker cover fade for leaving/restarting a level (see PreloadDuringExit's
    /// callers) — something a player does over and over, so it shouldn't linger.</summary>
    public const float ExitFadeTime = 0.12f;

    static LoadingScreenManager instance;

    // Four sibling layers, each faded through its own CanvasGroup — never
    // one group on the canvas root, which would multiply its alpha into all
    // of them (a root group parked at 0 used to keep the bridge and corner
    // readout invisible no matter what their own groups were set to).
    CanvasGroup blipGroup;
    CanvasGroup bridgeGroup;
    RectTransform bridgeRoot;
    CanvasGroup freezeGroup;
    RawImage freezeImage;

    CanvasGroup cornerGroup;
    RectTransform cornerRect;
    TextMeshProUGUI cornerLabel;
    RectTransform cornerLabelRect;
    Image cornerFill;

    /// <summary>
    /// Starts preloading sceneName (paused just before activation) behind a
    /// small, non-blocking corner readout instead of covering the screen —
    /// the caller's current screen stays fully visible/interactive-looking.
    /// Once the load is ready (and the corner readout has shown for at least
    /// MinShowTime) the corner readout fades away and onReadyToActivate fires
    /// with the still-pending AsyncOperation — the caller decides when to
    /// actually flip the scene over (typically after its own exit animation)
    /// by passing that operation to ActivateAndFade.
    /// </summary>
    public static void PreloadSceneCorner(string sceneName, string displayLabel, Action<AsyncOperation> onReadyToActivate)
    {
        var mgr = GetOrCreate();
        mgr.StartCoroutine(mgr.PreloadCornerRoutine(sceneName, displayLabel, onReadyToActivate));
    }

    /// <summary>
    /// Activates a scene preloaded via PreloadSceneCorner. If backgroundGroup
    /// is given (the persistent menu background's own CanvasGroup — see
    /// MainMenuCanvasBuilder/MainMenuBootstrap), it's left fully visible
    /// (no fade-out) right up to the scene swap instead of the flat
    /// full-screen blip, so the transition reads as one continuous
    /// background carrying straight through the swap — its color is what
    /// changes afterward (GameBootstrap.FadeBackgroundToLevelColor), not an
    /// alpha fade to black. Callers with no equivalent background layer to
    /// hand off to (e.g. a gameplay-side transition) fall back to the plain
    /// full-screen blip.
    /// </summary>
    public static void ActivateAndFade(AsyncOperation op, CanvasGroup backgroundGroup = null, float fadeTime = FadeTime)
    {
        var mgr = GetOrCreate();
        mgr.StartCoroutine(mgr.ActivateAndFadeRoutine(op, backgroundGroup, fadeTime));
    }

    /// <summary>
    /// PreloadSceneCorner + ActivateAndFade for leaving the current scene
    /// through its own exit animation — playExit is handed a callback to
    /// invoke once that animation finishes. The preload and the animation
    /// run side by side, not one after the other, and the scene flips over
    /// once both are done: the preload's minimum readout time hides inside
    /// the animation instead of the player first watching a frozen screen
    /// with a loading readout, and only then the exit. playExit may be null
    /// (nothing to animate) — then it's just the preload.
    ///
    /// The swap itself is covered by the full-screen blip, unless
    /// holdLastFrame is set: then the exit's own last frame is held over the
    /// swap instead (see ActivateBehindFreezeFrameRoutine), for an exit that
    /// ends on a screen the incoming scene opens on too — see RestartLevel.
    /// </summary>
    public static void PreloadDuringExit(string sceneName, string displayLabel, Action<Action> playExit,
                                         float fadeTime = ExitFadeTime, bool holdLastFrame = false)
    {
        AsyncOperation readyOp = null;
        bool exitDone = playExit == null;
        bool activated = false;

        void TryActivate()
        {
            if (activated || readyOp == null || !exitDone) return;
            activated = true;
            if (holdLastFrame)
            {
                var mgr = GetOrCreate();
                mgr.StartCoroutine(mgr.ActivateBehindFreezeFrameRoutine(readyOp, fadeTime));
            }
            else
            {
                ActivateAndFade(readyOp, fadeTime: fadeTime);
            }
        }

        PreloadSceneCorner(sceneName, displayLabel, op => { readyOp = op; TryActivate(); });
        playExit?.Invoke(() => { exitDone = true; TryActivate(); });
    }

    /// <summary>
    /// The HUD's Restart (NetworkUIManager.OnReturnClicked) — reloads the
    /// current level through the same exit as ExitToLevelSelect
    /// (GameBootstrap.PlayExitSequence: HUD, topology, walls, platform), but
    /// with no fade over the swap: the exit ends on the level's bare
    /// background, that last frame is held over the swap, and the reloaded
    /// level opens on the same background in the same color
    /// (GameBootstrap.CarryBackgroundIntoNextLoad) rather than fading back up
    /// to it from the menu's untinted pattern. The caller sets
    /// LevelManager.PendingLevelNumber first, like any level load.
    /// </summary>
    public static void RestartLevel()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        var bootstrap = GameBootstrap.Instance;
        if (bootstrap == null)
        {
            PreloadDuringExit(sceneName, "RELOADING LEVEL", null);
            return;
        }

        bootstrap.CarryBackgroundIntoNextLoad();
        PreloadDuringExit(sceneName, "RELOADING LEVEL",
            done => bootstrap.PlayExitSequence(done, holdUntilSettled: true),
            holdLastFrame: true);
    }

    /// <summary>
    /// Shared gameplay-side "leave to Level Select" transition — used by
    /// NetworkUIManager's top-bar Home button and every level's own
    /// LevelCompletePanel "Continue" callback (Level2Guide/Level3Guide/
    /// Level4Guide/Level5-10Challenge/TutorialManager), which all used to
    /// duplicate the same two lines. Arms MainMenuBootstrap to land directly
    /// on Level Select, then preloads MainMenu while GameBootstrap.
    /// PlayExitSequence plays — the level's own entrance (HUD, topology,
    /// walls, platform) in reverse — see PreloadDuringExit. Level Select's
    /// own Open() already slides itself in on arrival (after its own
    /// background-first beat — see MainMenuBootstrap.
    /// OpenLevelSelectAfterBlankHold), so nothing further is needed on the
    /// receiving end.
    /// </summary>
    public static void ExitToLevelSelect()
    {
        MainMenuBootstrap.OpenLevelSelectOnLoad = true;
        // Level Select opens centered on the level being left, or on the
        // next one if it's now complete — see LevelSelectManager.FocusLevelOnOpen.
        LevelSelectManager.FocusLevelOnOpen = LevelManager.Instance != null ? LevelManager.Instance.CurrentLevel : 0;
        // Save before the exit freezes autosave (GameBootstrap.ExitStarted):
        // an open CLI terminal is closed on the way out, and its edits would
        // otherwise only have been saved by that close (AutoSaveManager.HandlePanelClosed).
        if (AutoSaveManager.Instance != null) AutoSaveManager.Instance.SaveNow();
        var bootstrap = GameBootstrap.Instance;
        PreloadDuringExit("MainMenu", "LOADING LEVEL SELECT",
            bootstrap != null ? bootstrap.PlayExitSequence : (Action<Action>)null);
    }

    static LoadingScreenManager GetOrCreate()
    {
        if (instance != null) return instance;

        var go = new GameObject("LoadingScreenManager");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<LoadingScreenManager>();
        instance.Build();
        return instance;
    }

    // ── Build ────────────────────────────────────────────────────────────────

    void Build()
    {
        var canvasGo = new GameObject("LoadingScreen_Canvas");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode    = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder  = 1000; // above every other canvas, in any scene

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);

        canvasGo.AddComponent<GraphicRaycaster>();

        // Plain backdrop only — no glow/label/progress-bar. This only ever
        // shows up as a very brief crossfade mask over the actual scene-swap
        // instant (see ActivateAndFadeRoutine's fallback branch), not as a
        // "loading screen" of its own; the corner readout below is the only
        // thing that ever communicates "loading" to the player.
        var blipGo = new GameObject("Blip", typeof(RectTransform));
        blipGo.transform.SetParent(canvasGo.transform, false);
        StretchFull(blipGo.GetComponent<RectTransform>());
        blipGroup = blipGo.AddComponent<CanvasGroup>();
        blipGroup.alpha          = 0f;
        blipGroup.blocksRaycasts = false;
        var blipImg = blipGo.AddComponent<Image>();
        blipImg.color = new Color(0.045f, 0.055f, 0.085f, 1f);

        BuildBackgroundBridge(canvasGo.transform);
        BuildFreezeFrame(canvasGo.transform);

        var font = Resources.Load<TMP_FontAsset>(FontResourcePath);
        BuildCornerReadout(canvasGo.transform, font);
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // A copy of the menu's own background — the same pattern layer and
    // vignette (MainMenuCanvasBuilder.BuildLayers) — on THIS persistent
    // (DontDestroyOnLoad) canvas, shown right before a menu→level scene swap
    // and held a beat after — see ActivateAndFadeRoutine. A hard scene swap
    // can have a frame or two where the outgoing scene's canvas is already
    // tearing down before the incoming scene's own background/camera has
    // actually taken over, which showed up as a flash to a flat dark color
    // with no pattern right at the cut. Every copy of the pattern shares one
    // screen-space grid and scroll clock (see ScrollingBackground), so
    // snapping this on over the menu changes nothing on screen, and fading
    // it off just reveals the level's own copy underneath — same pattern,
    // same vignette (GameBootstrap.SetupBackgroundQuad/SetupBackgroundVignette).
    // Built before BuildCornerReadout so the corner plate still draws on top
    // of it.
    void BuildBackgroundBridge(Transform parent)
    {
        var bridgeGo = new GameObject("BackgroundBridge", typeof(RectTransform));
        bridgeGo.transform.SetParent(parent, false);
        bridgeRoot = bridgeGo.GetComponent<RectTransform>();
        StretchFull(bridgeRoot);

        bridgeGroup = bridgeGo.AddComponent<CanvasGroup>();
        bridgeGroup.alpha = 0f;
        bridgeGroup.blocksRaycasts = false;

        MainMenuCanvasBuilder.BuildLayers(bridgeRoot, new Color(0.07f, 0.08f, 0.12f));
    }

    // The outgoing scene's own last frame, held over a swap whose incoming
    // scene opens on that same screen — a level restart, whose exit ends on
    // the bare level background the reload opens on (see RestartLevel). A
    // capture rather than a UI copy like the bridge above: the level's
    // background is drawn by the 3D camera, through the scene's
    // post-processing (vignette, tonemapping), which no overlay copy
    // matches — a copy snapped on over it visibly lightened the screen's
    // edges. Full-screen and unscaled, since it's the screen itself.
    void BuildFreezeFrame(Transform parent)
    {
        var go = new GameObject("FreezeFrame", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        StretchFull(go.GetComponent<RectTransform>());

        freezeGroup = go.AddComponent<CanvasGroup>();
        freezeGroup.alpha = 0f;
        freezeGroup.blocksRaycasts = false;

        freezeImage = go.AddComponent<RawImage>();
        freezeImage.raycastTarget = false;
    }

    // Small, subtle plate bottom-right of the screen — the same chamfered-
    // plate language as every other panel (see PlateSpriteBuilder) rather
    // than a bespoke widget, just tiny and off in a corner instead of
    // centered/full-screen. Never blocks raycasts and starts invisible —
    // PreloadCornerRoutine is the only thing that ever shows it.
    //
    // Sized to whatever label it's showing (see FitCornerToLabel), between
    // the min and max below: the label gets its own row on top and the
    // progress bar its own row underneath, so a long label widens the plate
    // — or, past CornerMaxWidth, wraps and makes it taller — instead of
    // running under the bar.
    const float CornerMinWidth  = 260f;
    const float CornerMaxWidth  = 440f;
    const float CornerMinHeight = 56f;
    const float CornerMargin    = 28f;
    const float CornerPadX      = 16f;
    const float CornerPadTop    = 10f;
    const float CornerPadBottom = 12f;
    const float CornerBarGap    = 8f;
    const float CornerBarHeight = 4f;

    void BuildCornerReadout(Transform parent, TMP_FontAsset font)
    {
        var go = new GameObject("CornerReadout", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(1f, 0f);
        rt.anchorMax        = new Vector2(1f, 0f);
        rt.pivot            = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-CornerMargin, CornerMargin);
        rt.sizeDelta        = new Vector2(CornerMinWidth, CornerMinHeight);
        cornerRect = rt;

        cornerGroup = go.AddComponent<CanvasGroup>();
        cornerGroup.alpha          = 0f;
        cornerGroup.blocksRaycasts = false;
        cornerGroup.interactable   = false;

        var plateImg = go.AddComponent<Image>();
        plateImg.sprite = PlateSpriteBuilder.GetSprite(
            new Color(0.055f, 0.07f, 0.11f, 0.92f), new Color(0.14f, 0.32f, 0.50f), new Color(0.02f, 0.03f, 0.05f),
            borderThickness: 3, chamfer: 12, pixelScale: 4);
        plateImg.type          = Image.Type.Sliced;
        plateImg.raycastTarget = false;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        cornerLabel             = labelGo.AddComponent<TextMeshProUGUI>();
        cornerLabel.font        = font;
        cornerLabel.fontSize    = 16;
        cornerLabel.color       = Color.white;
        cornerLabel.alignment   = TextAlignmentOptions.TopLeft;
        cornerLabel.fontStyle   = FontStyles.Bold;
        cornerLabel.textWrappingMode = TextWrappingModes.Normal;
        cornerLabel.raycastTarget = false;
        // Top row, full plate width minus padding; its height is set per
        // label by FitCornerToLabel.
        cornerLabelRect = labelGo.GetComponent<RectTransform>();
        cornerLabelRect.anchorMin = new Vector2(0f, 1f);
        cornerLabelRect.anchorMax = new Vector2(1f, 1f);
        cornerLabelRect.pivot     = new Vector2(0.5f, 1f);

        // Bottom row, stretched across the plate so it follows the plate's
        // width as that changes per label.
        var trackGo = new GameObject("ProgressTrack", typeof(RectTransform));
        trackGo.transform.SetParent(go.transform, false);
        var trackImg = trackGo.AddComponent<Image>();
        trackImg.color = new Color(1f, 1f, 1f, 0.14f);
        var trt = trackGo.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0f, 0f);
        trt.anchorMax = new Vector2(1f, 0f);
        trt.pivot     = new Vector2(0.5f, 0f);
        trt.offsetMin = new Vector2(CornerPadX, CornerPadBottom);
        trt.offsetMax = new Vector2(-CornerPadX, CornerPadBottom + CornerBarHeight);

        var fillGo = new GameObject("ProgressFill", typeof(RectTransform));
        fillGo.transform.SetParent(trackGo.transform, false);
        cornerFill              = fillGo.AddComponent<Image>();
        cornerFill.color        = GameTheme.ElectricBlue;
        cornerFill.type         = Image.Type.Filled;
        cornerFill.fillMethod   = Image.FillMethod.Horizontal;
        cornerFill.fillOrigin   = (int)Image.OriginHorizontal.Left;
        cornerFill.fillAmount   = 0f;
        cornerFill.raycastTarget = false;
        var frt = fillGo.GetComponent<RectTransform>();
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = Vector2.zero;
        frt.offsetMax = Vector2.zero;
    }

    // Resizes the plate around the label it's about to show. Measured with
    // the longest ellipsis frame ("...") so the plate holds still while
    // UpdateEllipsis animates the dots. Up to CornerMaxWidth the label stays
    // on one line and the plate just widens; past that it wraps and the
    // plate grows taller, the bar always staying on its own row underneath.
    void FitCornerToLabel(string displayLabel)
    {
        const float maxTextWidth = CornerMaxWidth - 2f * CornerPadX;
        const float slack = 2f; // an exact-fit width can still wrap the last glyph on rounding

        string widest = displayLabel + "...";
        float textWidth = cornerLabel.GetPreferredValues(widest, maxTextWidth, 10000f).x + slack;
        float plateWidth = Mathf.Clamp(Mathf.Ceil(textWidth) + 2f * CornerPadX, CornerMinWidth, CornerMaxWidth);
        float labelWidth = plateWidth - 2f * CornerPadX;

        // Height at the width the label will actually get, since that's what
        // decides how many lines it wraps to.
        float textHeight = Mathf.Ceil(cornerLabel.GetPreferredValues(widest, labelWidth, 10000f).y);
        float plateHeight = Mathf.Max(CornerMinHeight,
            CornerPadTop + textHeight + CornerBarGap + CornerBarHeight + CornerPadBottom);

        cornerRect.sizeDelta = new Vector2(plateWidth, plateHeight);
        cornerLabelRect.offsetMin = new Vector2(CornerPadX, -(CornerPadTop + textHeight));
        cornerLabelRect.offsetMax = new Vector2(-CornerPadX, -CornerPadTop);
    }

    // ── Corner preload + deferred activation flow ───────────────────────────

    IEnumerator PreloadCornerRoutine(string sceneName, string displayLabel, Action<AsyncOperation> onReadyToActivate)
    {
        // Fitted and filled in before the fade-in, so the plate doesn't
        // appear at the previous transition's size with its old text.
        FitCornerToLabel(displayLabel);
        cornerFill.fillAmount = 0f;
        UpdateEllipsis(cornerLabel, displayLabel);
        yield return FadeGroup(cornerGroup, 0f, 1f, FadeTime);

        float shownAt = Time.unscaledTime;
        var op = SceneManager.LoadSceneAsync(sceneName);
        op.allowSceneActivation = false;

        while (op.progress < 0.9f || Time.unscaledTime - shownAt < MinShowTime)
        {
            cornerFill.fillAmount = Mathf.Clamp01(op.progress / 0.9f);
            UpdateEllipsis(cornerLabel, displayLabel);
            yield return null;
        }
        cornerFill.fillAmount = 1f;

        yield return FadeGroup(cornerGroup, 1f, 0f, FadeTime);

        onReadyToActivate?.Invoke(op);
    }

    // How long the bridge background stays up after the swap before fading
    // away — gives the new scene's own GameBootstrap a moment to finish
    // setting up its own background (SetupBackgroundQuad runs synchronously
    // in Awake, but this also covers a frame or two of render catch-up)
    // before revealing it underneath.
    const float BridgeHoldAfterSwap = 0.2f;

    IEnumerator ActivateAndFadeRoutine(AsyncOperation op, CanvasGroup backgroundGroup, float fadeTime)
    {
        // Decided once, up front — the menu background is destroyed along
        // with its scene by the time the swap below finishes, so asking
        // `backgroundGroup == null` again afterward always says "blip", which
        // used to flash the blip on and leave the bridge stuck on screen.
        bool handOffToBridge = backgroundGroup != null;

        if (!handOffToBridge)
        {
            // No background layer to hand off to (e.g. a gameplay-side
            // transition) — fall back to the plain full-screen blip so the
            // activation hitch still doesn't read as a bare freeze.
            yield return FadeGroup(blipGroup, 0f, 1f, fadeTime);
            blipGroup.blocksRaycasts = true;
        }
        else
        {
            // Snapped fully visible, not faded in — it shows the exact same
            // pattern already on screen (see BuildBackgroundBridge), so
            // there's nothing to crossfade, just a persistent layer already
            // in place before the swap's own gap can show through. Takes on
            // the menu background's own scale first (MainMenu.unity's is
            // hand-tuned) — the pattern ignores it, but the vignette
            // stretches with it.
            bridgeRoot.localScale = backgroundGroup.transform.localScale;
            bridgeGroup.alpha = 1f;
        }

        op.allowSceneActivation = true;
        while (!op.isDone) yield return null;

        if (!handOffToBridge)
        {
            yield return FadeGroup(blipGroup, 1f, 0f, fadeTime);
            blipGroup.blocksRaycasts = false;
        }
        else
        {
            yield return new WaitForSecondsRealtime(BridgeHoldAfterSwap);
            yield return FadeGroup(bridgeGroup, 1f, 0f, fadeTime);
        }
    }

    // How many frames the held frame stays up once the swap is done — just
    // enough for the new scene to render under it, not a timed hold like
    // the bridge's: the capture is frozen while the live background keeps
    // drifting (ScrollingBackground), so the longer it's up, the further
    // apart the two are by the time they crossfade.
    const int FreezeHoldFramesAfterSwap = 2;

    IEnumerator ActivateBehindFreezeFrameRoutine(AsyncOperation op, float fadeTime)
    {
        // Captured once the frame has fully rendered — post-processing and
        // every overlay canvas included — and shown from the next frame on.
        yield return new WaitForEndOfFrame();
        var capture = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        ScreenCapture.CaptureScreenshotIntoRenderTexture(capture);
        freezeImage.texture = capture;
        // The capture comes back upside down wherever texture V starts at
        // the top (Direct3D, Metal, Vulkan).
        freezeImage.uvRect = SystemInfo.graphicsUVStartsAtTop ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
        freezeGroup.alpha = 1f;

        op.allowSceneActivation = true;
        while (!op.isDone) yield return null;

        for (int i = 0; i < FreezeHoldFramesAfterSwap; i++) yield return null;
        yield return FadeGroup(freezeGroup, 1f, 0f, fadeTime);

        freezeImage.texture = null;
        RenderTexture.ReleaseTemporary(capture);
    }

    void UpdateEllipsis(TextMeshProUGUI target, string baseLabel)
    {
        int dots = 1 + Mathf.FloorToInt(Time.unscaledTime * 2f) % 3;
        target.text = baseLabel + new string('.', dots);
    }

    static IEnumerator FadeGroup(CanvasGroup group, float from, float to, float duration)
    {
        group.alpha = from;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
            yield return null;
        }
        group.alpha = to;
    }
}
