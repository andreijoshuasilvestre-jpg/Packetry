using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Annotation callout (non-blocking, except on a step that waits on its own
/// Next button — see IsAwaitingButton) + player-facing "Skip Tutorial" button for
/// TutorialManager's step sequence. Mirrors the existing NetworkUIManager /
/// TaskUIManager split — TutorialManager owns step state and logic, this
/// class only owns the visual build. Also owns the pulsing sidebar-button
/// beacon (PulsingHighlight) and a small pool of world-space click-zone
/// markers (WorldPulseMarker) that TutorialManager re-targets per step.
///
/// The canvas/callout/highlight/markers are all built lazily, on first real
/// use (see EnsureBuilt/EnsureMarkers) rather than eagerly in Awake() — every
/// actual caller (TutorialManager.EnterStep, Level2Guide/Level3Guide/
/// Level4Guide.EnterStage) only ever calls Show()/ShowWorldMarker() after
/// GameBootstrap.IntroSequenceReady is already true, i.e. once the level's
/// starting topology has finished building and the whole opening sequence
/// (platform, walls, HUD buttons, task panel) has actually finished, so these
/// annotations/markers now genuinely initialize after the level is built
/// instead of mid-scene-construction. Being lazy (built the instant they're
/// first actually needed) rather than deferred-to-a-fixed-point-in-Start()
/// sidesteps the Unity Start()-call-order race a fixed-frame approach would
/// have to worry about.
/// </summary>
public class TutorialUIManager : MonoBehaviour, IScreenOccupant
{
    public static TutorialUIManager Instance { get; private set; }

    private static readonly Color TextColor  = new Color(0.90f, 0.94f, 1.00f, 1f);
    private static readonly Color SkipBg     = new Color(0.35f, 0.38f, 0.45f, 0.9f);

    // Callout box look — same procedurally-drawn "chamfered plate" style as
    // LevelSelectManager's container box (see PlateSpriteBuilder), just
    // scaled down and kept slightly translucent (alpha < 1 on calloutFill)
    // so it still reads as a floating annotation over live gameplay rather
    // than a fully opaque modal. Serialized (not consts) so it's tunable
    // per-inspector, same rationale as LevelSelectManager's Container Box.
    [Header("Callout Box")]
    [SerializeField] private Color calloutFill           = new Color(0.06f, 0.08f, 0.13f, 0.94f);
    [SerializeField] private Color calloutBorder         = new Color(0.16f, 0.34f, 0.55f, 1f);
    [SerializeField] private Color calloutTrim           = new Color(0.02f, 0.03f, 0.05f, 1f);
    [SerializeField] private int   calloutBorderThickness = 3;
    [SerializeField] private int   calloutChamfer         = 12;
    [SerializeField] private int   calloutPixelScale      = 4;

    // Keycap chip drawn behind any <key>...</key> span in step text (see
    // ApplyKeyChips) — a lighter, slightly blue-tinted rectangle so the exact
    // key to press reads as a distinct "button" against the callout's dark
    // fill, rather than just bold text blending into the rest of the line.
    [SerializeField] private Color keyChipColor = new Color(0.24f, 0.32f, 0.44f, 1f);

    // Sized generously for multi-line, specific "here's exactly what to do"
    // instructions rather than one-line hints — readability was the whole
    // point of the redesign. Height is no longer fixed: MinPanelHeight is
    // just the floor for short steps, and AutoSizeCallout() grows the panel
    // to fit whatever step text actually needs (see below) — a long Text
    // string used to overflow past the reserved button row instead of
    // growing the box (e.g. the "ports" info step), which is the bug this
    // replaced.
    private const float PanelWidth      = 460f;
    private const float MinPanelHeight  = 190f;
    private const float MaxPanelHeight  = 340f; // cap so a runaway string can't push the panel off-screen
    private const float PanelOffsetX    = 330f; // clears NetworkUIManager's 300px-wide sidebar — the callout's HOME, see UpdatePlacement
    private const float ButtonHeight    = 40f;
    private const float ActionButtonWidth  = 130f;
    private const float ActionButtonInset  = 16f; // from the panel's right edge
    private const float ActionButtonBottom = 14f; // from the panel's bottom edge — the bottom row the waveform shares
    private const float WaveformButtonGap  = 14f;
    private const float TextPaddingX      = 20f; // matches txtRt offsetMin/offsetMax.x magnitude
    private const float TextPaddingTop    = 18f; // matches txtRt offsetMax.y magnitude
    private const float TextPaddingBottom = 16f; // gap between text and the button row above it

    // Pop-in (on Show) + idle bounce (while visible) + typing animation tuning.
    // Pop-in eases outward from PopInStartScale to 1 once, then the idle bounce
    // takes over and loops for as long as the callout stays on screen — both
    // animate panelRect.localScale so they're chained in one coroutine
    // (PopInThenBounce) rather than fighting over the same value.
    private const float PopInDuration        = 0.25f;
    private const float PopInStartScale      = 0.85f;
    private const float BouncePeriod         = 1.6f;   // seconds per full up/down cycle
    private const float BounceAmount         = 0.025f; // +/- scale delta around 1.0
    private const float TypingCharsPerSecond = 90f;

    const string SpriteFolder = "MainMenuUI/";
    const string FontResourcePath = "Fonts/PixeloidMono SDF";

    // Author-facing shorthand for "this is a keyboard key" — step text writes
    // <key>WASD</key> and ApplyKeyChips rewrites it into TMP's <mark>/<b>
    // markup at display time. Kept separate from plain <b>, which the guide
    // scripts already use for UI button names and CLI commands (e.g.
    // <b>Ping</b>, <b>interface eth0</b>) — those shouldn't get a keycap chip.
    static readonly Regex KeyTagPattern = new Regex("<key>(.*?)</key>", RegexOptions.Compiled);

    private RectTransform canvasRect;
    private Canvas ownerCanvas;
    private RectTransform panelRect;
    // True once the player has manually dragged the callout by its title bar
    // this step — UpdatePlacement (auto-repositioning next to the highlighted
    // sidebar target) is skipped while true so a mid-step text update
    // (UpdateText/RetargetHighlight) doesn't yank the callout back out from
    // under a player who just moved it out of the way. Reset on every fresh
    // Show() (a new step), so the default position still applies next time.
    private bool userDragged;
    private TextMeshProUGUI bodyText;
    // The title bar's own label (see WindowChromeBuilder) — Show() writes the
    // step count into it ("Tutorial  4/17") so a player always knows how far in they are.
    private TextMeshProUGUI titleLabel;
    // True when HomePosition put the callout to the LEFT of its target — a
    // target in the screen's right half, like ViewControlsHud's turn buttons
    // — so the tail points right instead of back at the sidebar.
    private bool homeLeftOfTarget;
    const float TargetSideGap = 30f;
    private RectTransform lastTarget;   // remembered so UpdateText() can reposition after a resize
    private float lastOffsetX;
    private bool lastClearFlyoutBelow;
    private GameObject actionButtonGo;
    private TextMeshProUGUI actionButtonLabel;
    private NarrationWaveform waveform;
    private GameObject skipButtonGo;
    // Up only while the step has a Next/Continue button — see AnnotationLock.
    private GameObject inputBlocker;
    private Coroutine typingRoutine;
    private Coroutine scaleRoutine;
    private PulsingHighlight highlight;
    private WorldPulseMarker markerA;
    private WorldPulseMarker markerB;
    private GameObject tailGo;

    // "Drag from here to there" trail + its UI end markers (see DragPathGuide),
    // on their own canvas just above this one — built lazily like the markers.
    private DragPathGuide dragGuide;
    // A standalone clicking-mouse marker on a UI button (see ShowUiMarker),
    // on that same guide canvas.
    private UIPulseMarker uiMarker;

    // ── Placement (see UpdatePlacement) ──────────────────────────────────────
    // The callout has a HOME position (next to the highlighted sidebar button,
    // or the left-pinned default) and, when something else is covering that
    // spot — the CLI panel, a popup, a marker, a drag trail — it moves to the
    // best nearby free spot instead, gliding rather than jumping, and returns
    // home once the spot clears. placementKey identifies which candidate spot
    // it's currently committed to, for hysteresis.
    private PlacementKey placementKey;
    private bool hasPlacement;
    private Vector2 placementVelocity;
    private readonly List<ScreenOccupancy.Entry> occupiedScratch = new();
    private readonly List<Candidate> candidateScratch = new();

    private const float TitleBarHeight   = WindowChromeBuilder.BarHeight; // sits ABOVE panelRect's own top edge
    private const float PlacementMargin  = 16f;   // min gap to the screen edge / whatever the callout steps around
    private const float DistanceCost     = 25f;   // score per canvas unit moved away from home (vs. per unit² of overlap)
    private const float SwitchThreshold  = 2000f; // a new spot must beat the current one by this much score to move
    // Per-unit² overlap cost by what's being covered. Screen-space guidance
    // (a UI marker, a drag trail) and HUD buttons are small but critical —
    // covering either defeats the step or hides something clickable — so they
    // cost more than the same area of a big panel, and the callout will travel
    // further to keep them visible. World markers (WorldGuide) cost the same
    // as a panel: the callout used to flee a Router's marker straight onto
    // that Router's own open CLI input line, which is far worse.
    static float OverlapWeight(OccupantLayer layer) =>
        layer == OccupantLayer.Guide || layer == OccupantLayer.Hud ? 3f : 1f;
    private const float GlideTime        = 0.15f;

    // "Next"/"Continue" action button art — the actual menu-button plate
    // (BlankButton / BlankButton_Hover), same asset CreateMenuButton/
    // LevelSelectManager.CreateButton use, so the tutorial's own action
    // button reads as the same button, not a flat placeholder rectangle.
    Sprite actionNormalSprite;
    Sprite actionHoverSprite;
    TMP_FontAsset font;

    private bool built;

    /// <summary>True while the callout is showing a step that only its own
    /// Next/Continue button advances — the rest of the game is locked until it's
    /// clicked (see AnnotationLock). A "go do X" step has no button, and leaves
    /// everything live.</summary>
    public bool IsAwaitingButton => built && inputBlocker.activeSelf;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        font = Resources.Load<TMP_FontAsset>(FontResourcePath);

        // Canvas/highlight/markers themselves are built lazily (see
        // EnsureBuilt/EnsureMarkers) — not here. Only the singleton wiring
        // and a plain asset reference (no scene construction) happen in
        // Awake() now.
    }

    void OnEnable() => ScreenOccupancy.Register(this);

    void OnDisable()
    {
        ScreenOccupancy.Unregister(this);
        // A reveal cut short by this being disabled/destroyed never reaches
        // TypeText's own StopLoop, and the loop lives on AudioManager, not here.
        AudioManager.Instance?.StopLoop(SfxId.AnnotationTyping);
    }

    // Builds the callout canvas + sidebar-button pulse beacon on first actual
    // use rather than eagerly in Awake() — see the class doc comment. Safe to
    // call redundantly from every entry point that needs these fields.
    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        BuildCanvas();

        // Renders nothing of its own — just pulses whatever RectTransform
        // TutorialManager points it at (see PulsingHighlight).
        highlight = gameObject.AddComponent<PulsingHighlight>();
    }

    // Builds the two world markers on first actual use — see the class doc
    // comment and EnsureBuilt.
    void EnsureMarkers()
    {
        if (markerA != null) return;
        markerA = BuildWorldMarker("TutorialMarkerA");
        markerB = BuildWorldMarker("TutorialMarkerB");
    }

    static WorldPulseMarker BuildWorldMarker(string name)
    {
        var go = new GameObject(name);
        return go.AddComponent<WorldPulseMarker>();
    }

    void BuildCanvas()
    {
        var canvasGo = new GameObject("Tutorial_Canvas");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        ownerCanvas = canvas;
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 22; // above CLI_Canvas (20, see CLIPanelManager.BuildPanel) so the callout
                                  // stays readable over an open router CLI (e.g. Level4Guide's addressing
                                  // walkthrough) as well as over HUD_Canvas (10) — and over Guide_Canvas (21,
                                  // see EnsureDragGuide)

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);

        canvasGo.AddComponent<GraphicRaycaster>();
        canvasRect = canvasGo.GetComponent<RectTransform>();

        actionNormalSprite = Resources.Load<Sprite>(SpriteFolder + "BlankButton");
        actionHoverSprite  = Resources.Load<Sprite>(SpriteFolder + "BlankButton_Hover");

        // Behind the callout AND the Skip button, so skipping stays possible
        // while a Next step has everything else locked.
        inputBlocker = AnnotationLock.CreateBlocker(canvasGo.transform);
        BuildCallout(canvasGo.transform);
        BuildSkipButton(canvasGo.transform);

        SetCalloutVisible(false);
        skipButtonGo.SetActive(false);
    }

    // ── Callout bubble ───────────────────────────────────────────────────────

    void BuildCallout(Transform canvas)
    {
        var panel = new GameObject("TutorialCallout");
        panel.transform.SetParent(canvas, false);
        var bg = panel.AddComponent<Image>();
        bg.sprite = PlateSpriteBuilder.GetSprite(calloutFill, calloutBorder, calloutTrim,
            calloutBorderThickness, calloutChamfer, calloutPixelScale);
        bg.type = Image.Type.Sliced;
        // Click-through: UpdatePlacement steps around everything registered
        // with ScreenOccupancy, but when nothing is free (a small window, a
        // large UI Scale, long step text) it settles for the least-covered
        // spot, which can still sit over something clickable. So the callout
        // itself (background + body text) never blocks a raycast; only its own actual
        // buttons (ActionButton, SkipTutorialButton — separate Images with their
        // own default raycastTarget) still do.
        bg.raycastTarget = false;

        panelRect = panel.GetComponent<RectTransform>();
        // Anchored to the screen's vertical center, left-pinned — anchoredPosition.y
        // then lines up directly with RectTransformUtility's canvas-center-relative
        // local Y (see HomePosition), no extra top/bottom offset math needed.
        panelRect.anchorMin        = new Vector2(0f, 0.5f);
        panelRect.anchorMax        = new Vector2(0f, 0.5f);
        panelRect.pivot            = new Vector2(0f, 0.5f);
        panelRect.anchoredPosition = new Vector2(PanelOffsetX, 0f);
        panelRect.sizeDelta        = new Vector2(PanelWidth, MinPanelHeight);

        // Small diamond "tail" nudging toward the highlighted target — on the
        // panel's left edge for a sidebar target, flipped to the right edge
        // for a target on the right of the screen (see HomePosition/
        // homeLeftOfTarget and UpdatePlacement).
        // Hidden whenever the callout has had to move off its home spot next
        // to the target (see UpdatePlacement) — it'd point at nothing there.
        tailGo = new GameObject("Tail");
        tailGo.transform.SetParent(panel.transform, false);
        var tailImg = tailGo.AddComponent<Image>();
        tailImg.color = calloutFill; // flat-color diamond, tinted to match the plate's interior fill so it blends in
        tailImg.raycastTarget = false;
        var trt = tailGo.GetComponent<RectTransform>();
        trt.anchorMin        = new Vector2(0f, 0.5f);
        trt.anchorMax        = new Vector2(0f, 0.5f);
        trt.pivot             = new Vector2(0.5f, 0.5f);
        trt.anchoredPosition = new Vector2(-10f, 0f);
        trt.sizeDelta        = new Vector2(20f, 20f);
        trt.localRotation    = Quaternion.Euler(0f, 0f, 45f);

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(panel.transform, false);
        bodyText = textGo.AddComponent<TextMeshProUGUI>();
        bodyText.font      = font;
        bodyText.fontSize  = 20;
        bodyText.color     = TextColor;
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.lineSpacing = 4f;
        bodyText.raycastTarget = false; // click-through, see bg.raycastTarget above
        var txtRt = textGo.GetComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = new Vector2(TextPaddingX, ButtonHeight + TextPaddingBottom);
        txtRt.offsetMax = new Vector2(-TextPaddingX, -TextPaddingTop);

        BuildActionButton(panel.transform);

        // Speaking indicator in the bottom row, beside the action button —
        // space the body text already leaves free (see txtRt.offsetMin), so
        // the callout's size and layout don't change with narration on or off.
        waveform = NarrationWaveform.Create(panel.transform, this, GameTheme.Info);
        waveform.Rect.anchorMin = Vector2.zero;
        waveform.Rect.anchorMax = new Vector2(1f, 0f);

        // Windows-style draggable title bar — same chrome every floating
        // annotation in the game wears (see WindowChromeBuilder), so a
        // player can drag this callout off whatever it's currently sitting
        // on top of instead of it being stuck in place. See userDragged for
        // how this interacts with the callout's own auto-positioning.
        WindowChromeBuilder.Build(panel.transform, panelRect, ownerCanvas, font,
            "Tutorial", PanelWidth, GameTheme.Info, showCloseButton: false,
            onUserDragged: () => userDragged = true);
        titleLabel = panel.transform.Find("TitleBar/Label")?.GetComponent<TextMeshProUGUI>();
    }

    // Same Glow + Visual(Background+Label) composition and MenuButtonFX
    // hover/click behavior as LevelSelectManager.CreateButton / MainMenuBootstrap.
    // CreateMenuButton — the tutorial's "Next"/"Continue" button is now the
    // same button, art and feedback included, not a separate flat rectangle.
    void BuildActionButton(Transform panel)
    {
        actionButtonGo = new GameObject("ActionButton", typeof(RectTransform));
        actionButtonGo.transform.SetParent(panel, false);
        var art = actionButtonGo.GetComponent<RectTransform>();
        art.anchorMin        = new Vector2(1f, 0f);
        art.anchorMax        = new Vector2(1f, 0f);
        art.pivot            = new Vector2(1f, 0f);
        art.anchoredPosition = new Vector2(-ActionButtonInset, ActionButtonBottom);
        art.sizeDelta        = new Vector2(ActionButtonWidth, ButtonHeight);

        var glowGo = new GameObject("Glow", typeof(RectTransform));
        glowGo.transform.SetParent(actionButtonGo.transform, false);
        var glowImg = glowGo.AddComponent<Image>();
        glowImg.raycastTarget = false;
        var grt = glowGo.GetComponent<RectTransform>();
        grt.anchorMin        = new Vector2(0.5f, 0.5f);
        grt.anchorMax        = new Vector2(0.5f, 0.5f);
        grt.pivot            = new Vector2(0.5f, 0.5f);
        grt.anchoredPosition = Vector2.zero;
        grt.sizeDelta        = new Vector2(ActionButtonWidth * 1.15f, ButtonHeight * 1.6f);

        var visualGo = new GameObject("Visual", typeof(RectTransform));
        visualGo.transform.SetParent(actionButtonGo.transform, false);
        var vrt = visualGo.GetComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.offsetMin = Vector2.zero;
        vrt.offsetMax = Vector2.zero;

        var bgGo = new GameObject("Background", typeof(RectTransform));
        bgGo.transform.SetParent(visualGo.transform, false);
        var bgImg = bgGo.AddComponent<Image>();
        bgImg.sprite = actionNormalSprite;
        bgImg.preserveAspect = true;
        var brt = bgGo.GetComponent<RectTransform>();
        brt.anchorMin = Vector2.zero;
        brt.anchorMax = Vector2.one;
        brt.offsetMin = Vector2.zero;
        brt.offsetMax = Vector2.zero;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(visualGo.transform, false);
        actionButtonLabel = labelGo.AddComponent<TextMeshProUGUI>();
        actionButtonLabel.font      = font;
        actionButtonLabel.text      = "Next";
        actionButtonLabel.fontSize  = 18;
        actionButtonLabel.color     = Color.white;
        actionButtonLabel.alignment = TextAlignmentOptions.Center;
        actionButtonLabel.fontStyle = FontStyles.Bold;
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;

        var btn = actionButtonGo.AddComponent<Button>();
        btn.transition    = Selectable.Transition.None;
        btn.targetGraphic = bgImg;

        var fx = actionButtonGo.AddComponent<MenuButtonFX>();
        fx.visualRect      = vrt;
        fx.backgroundImage = bgImg;
        fx.glowImage       = glowImg;
        fx.normalSprite    = actionNormalSprite;
        fx.hoverSprite     = actionHoverSprite;
        fx.glowColor       = GameTheme.ElectricBlue;
        fx.Init();

        btn.onClick.AddListener(() => fx.PlayClickThen(() => TutorialManager.Instance?.AdvanceFromButton()));
    }

    // ── Skip button ──────────────────────────────────────────────────────────

    void BuildSkipButton(Transform canvas)
    {
        skipButtonGo = new GameObject("SkipTutorialButton");
        skipButtonGo.transform.SetParent(canvas, false);
        var bg = skipButtonGo.AddComponent<Image>();
        bg.color = SkipBg;
        var rt = skipButtonGo.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(1f, 1f);
        rt.anchorMax        = new Vector2(1f, 1f);
        rt.pivot            = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-20f, -20f);
        rt.sizeDelta        = new Vector2(160f, ButtonHeight);

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(skipButtonGo.transform, false);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.font      = font;
        label.text      = "Skip Tutorial";
        label.fontSize  = 16;
        label.color     = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;

        var btn = skipButtonGo.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(() => TutorialManager.Instance?.SkipTutorial());

        OccupiesScreen.Add(skipButtonGo, OccupantLayer.Hud); // the callout steps around it like any HUD button
    }

    // ── Public API (called by TutorialManager) ──────────────────────────────

    /// <summary>Shows/updates the callout for the current step. target = null means
    /// no sidebar button is being pointed at (welcome/completion/info steps) — the
    /// callout rests at a default center position and the highlight hides.
    /// buttonLabel = null means the step auto-advances (no button shown).</summary>
    /// <summary>offsetX overrides the callout's default horizontal position (PanelOffsetX,
    /// which clears the 300px sidebar) — pass a smaller value for a level whose sidebar
    /// is entirely hidden (see Level3Guide), where the wider default gap just wastes
    /// screen space. Leave null for the normal sidebar-clearing position. offsetY nudges
    /// the callout vertically away from target's own height — used for the one tutorial
    /// step that leaves NetworkUIManager's devices/cable compound button unrestricted
    /// (TutorialStep.TeachDeviceFlyout) so its right-click flyout has room to open
    /// without the callout sitting on top of it (the highlight beacon still attaches to
    /// the real target regardless of this offset). clearFlyoutBelow computes that
    /// clearance from the callout's own actual (auto-sized) height rather than a flat
    /// guess — see HomePosition — so a long step's taller callout still clears the
    /// flyout instead of drifting back into it.</summary>
    /// <summary>progress = (this step, total steps), shown in the title bar; null
    /// shows plain "Tutorial".</summary>
    public void Show(string text, RectTransform target, string buttonLabel, float? offsetX = null, bool clearFlyoutBelow = false,
        (int step, int total)? progress = null)
    {
        EnsureBuilt();
        if (titleLabel != null)
            titleLabel.text = progress.HasValue ? $"Tutorial  {progress.Value.step}/{progress.Value.total}" : "Tutorial";
        skipButtonGo.SetActive(true);
        SetCalloutVisible(true);
        lastTarget   = target;
        lastOffsetX  = offsetX ?? PanelOffsetX;
        lastClearFlyoutBelow = clearFlyoutBelow;
        userDragged = false; // fresh step — the default position applies again even if a previous step was dragged
        SetText(text);
        UpdatePlacement(snap: true); // a new step pops in at its spot rather than gliding there from the last one
        PlayPopInAndBounce();

        bool showButton = !string.IsNullOrEmpty(buttonLabel);
        actionButtonGo.SetActive(showButton);
        if (showButton) actionButtonLabel.text = buttonLabel;
        AnnotationLock.SetBlocker(inputBlocker, showButton);

        // The waveform spans the bottom row, stopping short of the button when there is one.
        float waveformRight = showButton ? ActionButtonInset + ActionButtonWidth + WaveformButtonGap : TextPaddingX;
        waveform.Rect.offsetMin = new Vector2(TextPaddingX, ActionButtonBottom);
        waveform.Rect.offsetMax = new Vector2(-waveformRight, ActionButtonBottom + ButtonHeight);

        if (target != null) { highlight.Attach(target); highlight.Show(); }
        else highlight.Hide();

        // A new step starts clean — a caller re-shows it if this step wants one.
        HideUiMarker();
    }

    /// <summary>Hides everything — callout, highlight, world markers, and the Skip
    /// button. Called once the tutorial ends, whether by finishing the last step
    /// or being skipped.</summary>
    public void Hide()
    {
        EnsureBuilt();
        SetCalloutVisible(false);
        skipButtonGo.SetActive(false);
        AnnotationLock.SetBlocker(inputBlocker, false);
        highlight.Hide();
        HideWorldMarkers();
        HideDragGuide();
        HideUiMarker();
        hasPlacement = false;

        if (typingRoutine != null) { StopCoroutine(typingRoutine); typingRoutine = null; }
        if (scaleRoutine  != null) { StopCoroutine(scaleRoutine);  scaleRoutine  = null; }
        AudioManager.Instance?.StopLoop(SfxId.AnnotationTyping);
        NarrationManager.Instance?.Stop(this);
        panelRect.localScale = Vector3.one;
    }

    /// <summary>Swaps just the callout's body text in place — the button and
    /// highlight are left untouched. Used mid-step (e.g. once the player has
    /// actually picked a device up) rather than Show(), which would also
    /// reset those. Still re-runs the panel auto-sizing (a TextAfterGate swap can
    /// be longer or shorter than the text it replaces) — LateUpdate's placement
    /// then glides it to wherever the new size fits.</summary>
    public void UpdateText(string text) => SetText(text);

    /// <summary>Swaps the callout's text AND moves the highlight beacon onto a
    /// new target — used by TutorialManager to shift the "look here" pulse
    /// between the compound button's face and a specific flyout icon as a
    /// right-click flyout opens/closes (see TutorialStep.FlyoutRevealGate/
    /// FlyoutItemTarget/TextAfterFlyoutOpen — both directions call this, not
    /// just the flyout-opening one). Deliberately leaves the callout PANEL's
    /// own position alone (still anchored off the face button via lastTarget/
    /// clearFlyoutBelow from the original Show() call) — only highlight.Attach
    /// retargets, so the box doesn't jump around while the flyout's own items
    /// are still mid pop-in/pop-out animation next to it. A null target hides the
    /// highlight — a beat that points into the world instead of at a button.</summary>
    public void RetargetHighlight(RectTransform target, string text)
    {
        SetText(text);

        if (target != null) { highlight.Attach(target); highlight.Show(); }
        else highlight.Hide();
    }

    /// <summary>Every way the callout's text changes (Show, UpdateText,
    /// RetargetHighlight) goes through here: the panel resizes to fit, the
    /// narrator reads the new line, and it types in — in step with the voice
    /// when narrated (see TextTypewriter).</summary>
    void SetText(string text)
    {
        bodyText.text = ApplyKeyChips(text);
        AutoSizeCallout();
        NarrationManager.Instance?.Speak(this, text);
        PlayTyping();
    }

    /// <summary>Shows a floating pulse marker above a world position — index 0 or 1,
    /// so up to two can be visible at once (e.g. a PC and the Switch to cable it to).
    /// Pass pointingAtDevice when pointing at an already-placed device so the pulse
    /// becomes a translucent echo of that device's own model instead of a
    /// generic circle (see WorldPulseMarker.Show). gesture is what the marker's
    /// mouse acts out there — a right-click for a device whose CLI/popup opens
    /// that way, a let-go for a drag's drop spot, a plain click otherwise.</summary>
    public void ShowWorldMarker(int index, Vector3 worldPos, NetworkDevice pointingAtDevice = null,
        MouseGesture gesture = MouseGesture.Click)
    {
        EnsureMarkers();
        (index == 0 ? markerA : markerB).Show(worldPos, pointingAtDevice, gesture);
    }

    public void HideWorldMarker(int index)
    {
        EnsureMarkers();
        (index == 0 ? markerA : markerB).Hide();
    }

    public void HideWorldMarkers()
    {
        EnsureMarkers();
        markerA.Hide();
        markerB.Hide();
    }

    /// <summary>Shows a "drag from here to there" trail (plus a pulsing UI marker
    /// on any end that's a UI element) along `route` — see DragPathGuide. A world
    /// end isn't given a marker here: steps already mark those spots through
    /// ShowWorldMarker. Replaces whatever route was showing.</summary>
    public void ShowDragGuide(DragGuideRoute route)
    {
        if (route == null) { HideDragGuide(); return; }
        EnsureDragGuide();
        dragGuide.Show(route);
    }

    public void HideDragGuide()
    {
        if (dragGuide != null) dragGuide.Hide();
    }

    /// <summary>Puts a clicking-mouse marker (see UIPulseMarker) on a UI element —
    /// alongside the step's pulsing highlight, for a button the player has to click
    /// next and might otherwise read past (e.g. Level 4's Cable button). Replaces
    /// whatever it was pointing at; Show() clears it for every new step.</summary>
    public void ShowUiMarker(GuideAnchor anchor, MouseGesture gesture = MouseGesture.Click)
    {
        EnsureDragGuide();
        uiMarker.Show(anchor, gesture: gesture);
    }

    public void HideUiMarker()
    {
        if (uiMarker != null) uiMarker.Hide();
    }

    // Own canvas rather than a child of Tutorial_Canvas, sorted between the
    // CLI panel (20) — so a trail/marker pointing into it draws on top — and
    // the callout (22): the callout steps around guidance (see
    // UpdatePlacement) and the trail bends around the callout (see
    // DragPathGuide), but where the layout leaves no clean route at all, the
    // trail passes BEHIND the callout rather than scribbling over its text.
    // Below AnnotationWarningManager (25) and ConceptCardManager (30), whose
    // full-attention windows should cover it.
    void EnsureDragGuide()
    {
        if (dragGuide != null) return;

        var canvasGo = new GameObject("Guide_Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 21;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);
        // No GraphicRaycaster — nothing on this canvas is ever clickable.

        dragGuide = DragPathGuide.Create(canvasGo.GetComponent<RectTransform>());
        uiMarker = UIPulseMarker.Create(canvasGo.GetComponent<RectTransform>(), "GuideUiMarker");
    }

    /// <summary>Tints both markers a specific color (e.g. table-corner clicks vs.
    /// device-drop spots reading as visually distinct kinds of target).</summary>
    public void SetMarkerColor(Color color)
    {
        EnsureMarkers();
        markerA.SetColor(color);
        markerB.SetColor(color);
    }

    /// <summary>Restores both markers to their default color — call before showing
    /// markers for a step that didn't call SetMarkerColor, so a color set for a
    /// previous step (both markers are reused across steps) doesn't linger.</summary>
    public void ResetMarkerColors()
    {
        EnsureMarkers();
        markerA.ResetColor();
        markerB.ResetColor();
    }

    void SetCalloutVisible(bool visible) => panelRect.gameObject.SetActive(visible);

    /// <summary>Rewrites author-facing &lt;key&gt;X&lt;/key&gt; spans into TMP's
    /// &lt;mark&gt; highlight tag (a background rectangle behind the text) plus
    /// &lt;b&gt;, so a keyboard key reads as a small contrasting "keycap" chip
    /// rather than plain bold text. padding values are percentages of font size,
    /// per TMP's &lt;mark&gt; spec (see TMP_Text's MARK/PADDING handling).</summary>
    string ApplyKeyChips(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string colorHex = ColorUtility.ToHtmlStringRGBA(keyChipColor);
        return KeyTagPattern.Replace(text,
            m => $"<mark=#{colorHex} padding=\"25,25,12,12\"><b>{m.Groups[1].Value}</b></mark>");
    }

    /// <summary>Restarts the callout's scale animation: a one-shot ease-out scale-up
    /// from PopInStartScale to 1 (the popup "arriving"), then chains straight into a
    /// looping, subtle sine-wave scale bounce for as long as the callout stays open.
    /// Both phases animate the same panelRect.localScale, so they're one coroutine
    /// rather than two competing over the value.</summary>
    void PlayPopInAndBounce()
    {
        if (scaleRoutine != null) StopCoroutine(scaleRoutine);
        scaleRoutine = StartCoroutine(PopInThenBounce());
    }

    IEnumerator PopInThenBounce()
    {
        panelRect.localScale = Vector3.one * PopInStartScale;
        float t = 0f;
        while (t < PopInDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = EaseOutCubic(Mathf.Clamp01(t / PopInDuration));
            panelRect.localScale = Vector3.one * Mathf.LerpUnclamped(PopInStartScale, 1f, p);
            yield return null;
        }
        panelRect.localScale = Vector3.one;

        float phase = 0f;
        while (true)
        {
            phase += Time.unscaledDeltaTime;
            float s = 1f + Mathf.Sin(phase * (2f * Mathf.PI / BouncePeriod)) * BounceAmount;
            panelRect.localScale = Vector3.one * s;
            yield return null;
        }
    }

    static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

    /// <summary>Restarts the typewriter reveal of bodyText.text (see TextTypewriter).
    /// A restarted reveal (UpdateText mid-typing) keeps the same typing loop going.</summary>
    void PlayTyping()
    {
        if (typingRoutine != null) StopCoroutine(typingRoutine);
        typingRoutine = StartCoroutine(TypeText());
    }

    IEnumerator TypeText()
    {
        yield return TextTypewriter.Reveal(bodyText, TypingCharsPerSecond, speaker: this);
        typingRoutine = null;
    }

    /// <summary>Grows (or shrinks back down) the callout to fit bodyText.text at the
    /// fixed PanelWidth, so a long step string pushes the box taller instead of
    /// overflowing past the reserved button row at the bottom — see the class
    /// comment on MinPanelHeight/MaxPanelHeight for the bug this replaced. Call
    /// this any time bodyText.text changes, before UpdatePlacement (the new height
    /// affects how far the panel can sit off-center).</summary>
    void AutoSizeCallout()
    {
        float textWidth   = PanelWidth - TextPaddingX * 2f;
        float textHeight  = bodyText.GetPreferredValues(bodyText.text, textWidth, 0f).y;
        float neededHeight = textHeight + ButtonHeight + TextPaddingBottom + TextPaddingTop;
        float height = Mathf.Clamp(neededHeight, MinPanelHeight, MaxPanelHeight);
        panelRect.sizeDelta = new Vector2(PanelWidth, height);
    }

    // Extra breathing room below the flyout's own bottom edge, on top of the
    // exact clearance HomePosition computes for clearFlyoutBelow.
    const float FlyoutClearanceGap = 20f;

    /// <summary>The callout's HOME anchoredPosition — right of the sidebar,
    /// vertically level with the highlighted target (or centered with none),
    /// dropped below the target's flyout row for clearFlyoutBelow steps. This is
    /// where it sits whenever nothing else is covering that spot; see
    /// UpdatePlacement for what happens when something is.</summary>
    Vector2 HomePosition()
    {
        RectTransform target = lastTarget;
        // A flyout-teaching step highlights the face button's arrow tab (see
        // FlyoutArrowTab), a child of that button. The flyout row is centered
        // on the button itself, so measure from the button.
        if (lastClearFlyoutBelow && target != null)
        {
            var button = target.GetComponentInParent<SidebarIconButtonFX>();
            if (button != null) target = (RectTransform)button.transform;
        }

        float y = 0f;
        if (target != null)
        {
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, target.position);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out Vector2 local))
                y = local.y;
        }

        if (lastClearFlyoutBelow && target != null)
        {
            // `y` above is target's TOP edge (target is top-pivoted — pivot =
            // (0.5, 1), see NetworkUIManager.CreateSidebarButton), while
            // CompoundSidebarGroup's flyout row is a single row the same
            // height as target itself, vertically CENTERED on target's true
            // center (see CompoundSidebarGroup.UpdatePosition) — so its bottom
            // edge sits a full target.rect.height below this top edge, not
            // just half of it. Push the callout's own top edge below that,
            // using its real auto-sized height (not a flat guess — a longer
            // step's taller callout used to drift back into the flyout's row
            // when a fixed pixel offset was tuned for a shorter one).
            float flyoutBottomEdge = y - target.rect.height;
            y = flyoutBottomEdge - FlyoutClearanceGap - panelRect.sizeDelta.y * 0.5f;
        }

        float panelHeight = panelRect.sizeDelta.y;
        float maxY = Mathf.Max(0f, canvasRect.rect.height * 0.5f - panelHeight * 0.5f - 20f);
        y = Mathf.Clamp(y, -maxY, maxY);

        // A target on the right of the screen gets the callout on ITS left —
        // right of the sidebar would leave the whole screen between them.
        float x = lastOffsetX;
        homeLeftOfTarget = false;
        if (target != null && !lastClearFlyoutBelow)
        {
            Rect screenRect = ScreenOccupancy.ScreenRectOf(target);
            if (screenRect.center.x > Screen.width * 0.5f)
            {
                float scale = canvasRect.lossyScale.x > 0f ? canvasRect.lossyScale.x : 1f;
                x = screenRect.xMin / scale - PanelWidth - TargetSideGap;
                homeLeftOfTarget = true;
            }
        }
        return new Vector2(x, y);
    }

    // ── Placement ────────────────────────────────────────────────────────────
    // Instead of a fixed spot tuned per level (the old PanelOffsetX/offsetX/
    // clearFlyoutBelow patches, each fixing one overlap until something else
    // moved), the callout scores a handful of candidate spots every frame
    // against everything registered with ScreenOccupancy — HUD buttons, the
    // CLI panel and popups, world/UI markers, drag trails — and sits in the
    // least-covered one, trading overlap against distance from home. Home
    // wins whenever it's clear. It also claims home as a Reservation, so a
    // freely-positioned panel (the CLI) steps aside from it in the first place
    // when there's room — see CLIPanelManager.PlaceWrapper.
    //
    // All math is in this canvas's units with a bottom-left origin, on the
    // callout's full "box": panelRect plus the title bar sitting above it.

    struct PlacementKey : System.IEquatable<PlacementKey>
    {
        public IScreenOccupant Owner; // the occupant this spot steps around; null = home / a screen-edge spot
        public int Slot;              // which side of Owner, or which fixed spot
        public bool Equals(PlacementKey o) => ReferenceEquals(Owner, o.Owner) && Slot == o.Slot;
    }

    struct Candidate
    {
        public PlacementKey Key;
        public Vector2 Box;   // bottom-left corner of the box
        public float Score;
    }

    const int HomeSlot = -1;

    void LateUpdate()
    {
        if (!built || !panelRect.gameObject.activeSelf || userDragged) return;
        UpdatePlacement(snap: false);
    }

    void UpdatePlacement(bool snap)
    {
        Vector2 canvasSize = canvasRect.rect.size;
        float h = panelRect.sizeDelta.y;
        var boxSize = new Vector2(PanelWidth, h + TitleBarHeight);
        float scale = canvasRect.lossyScale.x > 0f ? canvasRect.lossyScale.x : 1f;

        Vector2 home = ClampBox(BoxFromAnchored(HomePosition(), h, canvasSize), boxSize, canvasSize);

        occupiedScratch.Clear();
        ScreenOccupancy.Collect(occupiedScratch, OccupantLayer.All, exclude: this);
        for (int i = 0; i < occupiedScratch.Count; i++)
        {
            var e = occupiedScratch[i];
            e.Rect = new Rect(e.Rect.position / scale, e.Rect.size / scale);
            occupiedScratch[i] = e;
        }

        candidateScratch.Clear();
        AddCandidate(new PlacementKey { Slot = HomeSlot }, home, home, boxSize, canvasSize);

        // Just outside each occupant's bounds, on each side, keeping home's
        // other coordinate (Collect appends an occupant's rects contiguously).
        for (int i = 0; i < occupiedScratch.Count;)
        {
            IScreenOccupant owner = occupiedScratch[i].Owner;
            Rect b = occupiedScratch[i].Rect;
            for (i++; i < occupiedScratch.Count && occupiedScratch[i].Owner == owner; i++)
            {
                Rect r = occupiedScratch[i].Rect;
                b = Rect.MinMaxRect(Mathf.Min(b.xMin, r.xMin), Mathf.Min(b.yMin, r.yMin), Mathf.Max(b.xMax, r.xMax), Mathf.Max(b.yMax, r.yMax));
            }

            AddCandidate(new PlacementKey { Owner = owner, Slot = 0 }, new Vector2(b.xMin - PlacementMargin - boxSize.x, home.y), home, boxSize, canvasSize);
            AddCandidate(new PlacementKey { Owner = owner, Slot = 1 }, new Vector2(b.xMax + PlacementMargin, home.y), home, boxSize, canvasSize);
            AddCandidate(new PlacementKey { Owner = owner, Slot = 2 }, new Vector2(home.x, b.yMax + PlacementMargin), home, boxSize, canvasSize);
            AddCandidate(new PlacementKey { Owner = owner, Slot = 3 }, new Vector2(home.x, b.yMin - PlacementMargin - boxSize.y), home, boxSize, canvasSize);
        }

        // Screen corners/edge midpoints — last resorts when nothing nearer is
        // free (ClampBox pulls each in by PlacementMargin).
        float xl = 0f, xc = (canvasSize.x - boxSize.x) * 0.5f, xr = canvasSize.x;
        float yb = 0f, yc = (canvasSize.y - boxSize.y) * 0.5f, yt = canvasSize.y;
        AddCandidate(new PlacementKey { Slot = 10 }, new Vector2(xl, yt), home, boxSize, canvasSize);
        AddCandidate(new PlacementKey { Slot = 11 }, new Vector2(xl, yb), home, boxSize, canvasSize);
        AddCandidate(new PlacementKey { Slot = 12 }, new Vector2(xr, yt), home, boxSize, canvasSize);
        AddCandidate(new PlacementKey { Slot = 13 }, new Vector2(xr, yb), home, boxSize, canvasSize);
        AddCandidate(new PlacementKey { Slot = 14 }, new Vector2(xl, yc), home, boxSize, canvasSize);
        AddCandidate(new PlacementKey { Slot = 15 }, new Vector2(xr, yc), home, boxSize, canvasSize);
        AddCandidate(new PlacementKey { Slot = 16 }, new Vector2(xc, yt), home, boxSize, canvasSize);
        AddCandidate(new PlacementKey { Slot = 17 }, new Vector2(xc, yb), home, boxSize, canvasSize);

        Candidate best = candidateScratch[0];
        Candidate? current = null;
        foreach (var c in candidateScratch)
        {
            if (c.Score < best.Score) best = c;
            if (hasPlacement && c.Key.Equals(placementKey)) current = c;
        }

        // Hysteresis: stay put unless somewhere else is clearly better (or the
        // thing this spot was stepping around is gone). A fresh step (snap)
        // always takes the best spot outright.
        Candidate chosen = !snap && current.HasValue && best.Score > current.Value.Score - SwitchThreshold
            ? current.Value
            : best;
        placementKey = chosen.Key;
        hasPlacement = true;

        Vector2 targetPos = AnchoredFromBox(chosen.Box, h, canvasSize);
        if (snap)
        {
            panelRect.anchoredPosition = targetPos;
            placementVelocity = Vector2.zero;
        }
        else
        {
            panelRect.anchoredPosition = Vector2.SmoothDamp(panelRect.anchoredPosition, targetPos, ref placementVelocity,
                GlideTime, Mathf.Infinity, Time.unscaledDeltaTime);
        }

        // The tail points back at the target — meaningless anywhere but home —
        // from whichever side of the callout the target is on.
        tailGo.SetActive(chosen.Key.Owner == null && chosen.Key.Slot == HomeSlot);
        var tailRt = (RectTransform)tailGo.transform;
        tailRt.anchorMin = tailRt.anchorMax = new Vector2(homeLeftOfTarget ? 1f : 0f, 0.5f);
        tailRt.anchoredPosition = new Vector2(homeLeftOfTarget ? 10f : -10f, 0f);
    }

    void AddCandidate(PlacementKey key, Vector2 box, Vector2 home, Vector2 boxSize, Vector2 canvasSize)
    {
        box = ClampBox(box, boxSize, canvasSize);
        var rect = new Rect(box, boxSize);
        float overlap = 0f;
        foreach (var e in occupiedScratch)
            overlap += ScreenOccupancy.OverlapArea(rect, e.Rect) * OverlapWeight(e.Owner.Layer);
        candidateScratch.Add(new Candidate { Key = key, Box = box, Score = overlap + DistanceCost * Vector2.Distance(box, home) });
    }

    static Vector2 ClampBox(Vector2 box, Vector2 boxSize, Vector2 canvasSize) => new Vector2(
        Mathf.Clamp(box.x, PlacementMargin, Mathf.Max(PlacementMargin, canvasSize.x - boxSize.x - PlacementMargin)),
        Mathf.Clamp(box.y, PlacementMargin, Mathf.Max(PlacementMargin, canvasSize.y - boxSize.y - PlacementMargin)));

    // panelRect is left-middle anchored/pivoted (see BuildCallout): x is its
    // left edge, y its vertical center relative to the canvas's center.
    static Vector2 BoxFromAnchored(Vector2 anchored, float panelHeight, Vector2 canvasSize) =>
        new Vector2(anchored.x, canvasSize.y * 0.5f + anchored.y - panelHeight * 0.5f);

    static Vector2 AnchoredFromBox(Vector2 box, float panelHeight, Vector2 canvasSize) =>
        new Vector2(box.x, box.y + panelHeight * 0.5f - canvasSize.y * 0.5f);

    // ── IScreenOccupant: the home Reservation ────────────────────────────────

    public OccupantLayer Layer => OccupantLayer.Reservation;

    public void CollectScreenRects(List<Rect> into)
    {
        // Reserves HOME, not wherever the callout currently is, so a panel
        // stepping aside from it and the callout stepping aside from that
        // panel can't chase each other. Nothing reserved once the player has
        // dragged the callout themselves — they've chosen its spot.
        if (!built || !panelRect.gameObject.activeSelf || userDragged) return;

        Vector2 canvasSize = canvasRect.rect.size;
        float h = panelRect.sizeDelta.y;
        var boxSize = new Vector2(PanelWidth, h + TitleBarHeight);
        Vector2 home = ClampBox(BoxFromAnchored(HomePosition(), h, canvasSize), boxSize, canvasSize);
        float scale = canvasRect.lossyScale.x > 0f ? canvasRect.lossyScale.x : 1f;
        into.Add(new Rect(home * scale, boxSize * scale));
    }
}
