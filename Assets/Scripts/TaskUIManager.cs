using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Builds a standalone task-list canvas (separate from NetworkUIManager's HUD)
/// showing each TaskManager task with a label and an auto-checking checkbox.
/// Purely a display of TaskManager's state — it never sets IsComplete itself.
///
/// Visually matches the CLI redesign's chamfered-plate look (see
/// CLIPanelManager's "Drag Commands" sidebar) — a navy/blue PlateSpriteBuilder
/// plate with PixeloidMono text, rather than NetworkUIManager's older flat
/// sidebar color — so every HUD panel in the game now reads as one consistent
/// visual family. Fully layout-group driven — panel and row heights are never
/// hardcoded, they grow to fit wrapped task text; a sliced plate sprite
/// tolerates that auto-resize exactly like a flat color would.
///
/// A task ticking over plays a short flash on its checkbox (white burst,
/// glow pulse, tick pop) and a softer tint pulse on its label — see
/// ApplyCompleteFlash.
/// </summary>
public class TaskUIManager : MonoBehaviour
{
    const string FontResourcePath = "Fonts/PixeloidMono SDF";
    TMP_FontAsset _font;

    // Same palette family as CLIPanelManager's sidebar/Run/queue-zone plates —
    // duplicated here (rather than shared) because each HUD panel class builds
    // its own UI independently (see ConfirmDialogManager/TutorialUIManager for
    // the same pattern), not because the colors are meant to drift apart.
    private static readonly Color TrimColor     = new Color(0.02f, 0.03f, 0.05f, 1f);
    private static readonly Color PanelFill     = new Color(0.055f, 0.07f, 0.11f, 1f); // navy
    private static readonly Color PanelBorder   = new Color(0.14f, 0.32f, 0.50f, 1f);  // blue
    private static readonly Color HeaderColor   = Color.white;
    private static readonly Color LabelColor    = new Color(0.88f, 0.92f, 0.97f, 1f);
    private static readonly Color BoxEmptyFill   = new Color(0.02f, 0.02f, 0.02f, 1f); // matches CLI's black queue-zone plate
    private static readonly Color BoxEmptyBorder = new Color(0.27f, 0.27f, 0.29f, 1f);
    private static readonly Color BoxDoneFill    = new Color(0.35f, 0.75f, 0.45f, 1f); // matches CLI's Run button plate
    private static readonly Color BoxDoneBorder  = new Color(0.15f, 0.45f, 0.22f, 1f);

    private const float PanelWidth     = 400f; // bigger than the old 260 baseline, to comfortably fit larger text
    private const float PaddingX       = 28f;  // clears the plate's chamfered corners + border so wrapped text never touches the edge
    private const float PaddingY       = 18f;
    private const float HeaderHeight   = 36f;
    private const float RowSpacing     = 16f;  // uniform gap between tasks — rows are sized to their text, so this is the only gap
    private const float RowInnerSpacing = 14f; // gap between checkbox and label within a row
    private const float ColumnSpacing  = 6f;   // gap between a task's label, its revealed hints, and its Hint link
    private const float ScreenBottomMargin = 20f; // keeps the panel's bottom edge on-screen; longer lists scroll instead
    private const float ScrollSensitivity  = 30f;
    private const float CheckboxSize   = 24f;
    private const float CheckIconSize  = 18f;  // 9 logical px × 2 — an integer multiple so the point-filtered tick stays crisp
    private const int   HeaderFontSize = 20;
    private const int   LabelFontSize  = 18;

    // Completion flash (see ApplyCompleteFlash) — the same glow-pulse +
    // ease-out-back pop language LevelCompletePanel.PopStar plays per star,
    // scaled down to the checkbox, plus a softer tint pulse on the task's
    // own label.
    const float CompleteFlashDuration = 0.45f;
    const float FlashOverlayPeak = 0.9f;  // white copy of the plate over the box, decays from here
    const float FlashGlowPeak    = 0.85f;
    const float FlashGlowPunch   = 1.3f;
    const float FlashBoxPunch    = 1.15f;
    const float CheckMinPopScale = 0.3f;
    const float CheckPopFraction = 0.7f;  // share of the flash the tick's pop takes
    const float LabelFlashAmount = 0.65f; // "slight" — the label never fully reaches LabelFlashColor
    const float FlashBleed       = 14f;   // how far the row list's clip extends so the glow isn't cut off at its edges
    static readonly Vector2 FlashGlowSize   = new Vector2(40f, 40f);
    static readonly Color   LabelFlashColor = new Color(0.62f, 1f, 0.70f, 1f);

    // Lets GameBootstrap.ExitSequenceRoutine reach PlayExitReveal without a
    // scene search — same simple pattern every other manager singleton in
    // this project already uses.
    public static TaskUIManager Instance { get; private set; }

    // Entry delay for the panel's own reveal flash (see BuildCanvas) —
    // "shortly after the buttons" per the request that added this: the HUD
    // buttons themselves start revealing GameBootstrap.HudButtonsDelay into
    // the entry sequence's HUD stage, this panel follows a beat later rather
    // than flashing in alongside them.
    const float TaskPanelRevealGap = 0.3f;

    // Panel-wide entrance/exit flash, matching the gameplay HUD buttons' own
    // reveal language (see HudButtonRevealFlash) instead of a slide — the
    // panel never moves, it just flashes into existence where it already
    // sits: a split-second faint afterimage of the whole plate, then the
    // header's own click-glow pulse (literally the same FX its toggle click
    // already plays), then the panel lands fully solid. PlayExitReveal is
    // the same sequence reversed — glow first, then the afterimage fades
    // back down to nothing — see there.
    const float AfterimageUp    = 0.02f;
    const float AfterimageHold  = 0.02f;
    const float AfterimageDown  = 0.02f;
    const float AfterimageAlpha = 0.35f;

    private RectTransform panelRect;
    private Transform rowContainer;
    private Sprite boxEmptySprite;
    private Sprite boxDoneSprite;
    private Sprite boxFlashSprite;
    private Sprite checkSprite;
    private CanvasGroup panelCanvasGroup;
    private MenuButtonFX headerFx;
    private bool panelRevealed;

    class TaskRow
    {
        public TaskManager.GameTask Task;
        public RectTransform Box; // container the flash scale-punches
        public Image Plate, Check, Flash, Glow;
        public TextMeshProUGUI Label;
    }
    private readonly List<TaskRow> rows = new();

    // Keyed by task, not row — RebuildRows recreates every row on any task
    // change (including the completion itself), so a flash has to outlive
    // the row that started it and carry on in its replacement.
    private readonly Dictionary<TaskManager.GameTask, bool>  lastSeenComplete = new();
    private readonly Dictionary<TaskManager.GameTask, float> flashStart = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Dropdown ─────────────────────────────────────────────────────────────
    // Header toggles the row list open/closed (see ToggleExpanded). Content
    // sits in its own "clip" wrapper (RectMask2D + a directly-driven
    // sizeDelta.y, not a LayoutElement/ContentSizeFitter) so the collapse
    // animation isn't fighting the inner list's own auto-height layout —
    // the inner Content still ContentSizeFitter's itself to its natural
    // height regardless of how much of it the wrapper is currently clipping.
    private const float ExpandDuration = 0.22f;
    private RectTransform contentWrapperRt;
    private RectMask2D contentMask;
    private RectTransform contentInnerRt;
    private CanvasGroup contentCanvasGroup;
    private bool expanded = true; // starts open, matching the old always-visible behavior
    private Coroutine toggleRoutine;

    // Dropdown arrow on the header's right edge — the header always toggled
    // the list, but nothing on screen said so. Points up while the list is
    // open, down while it's folded away, and turns between the two as the
    // list animates. Same pixel arrow (and hover-lit variant) as the
    // sidebar's flyout tabs, see FlyoutArrowIconBuilder.
    private const float ArrowSize = 26f; // 2 screen px per arrow cell, so it stays crisp
    private Image dropdownArrow;
    private float arrowAngle;

    static float ArrowAngle(bool open) => open ? 90f : -90f; // the art points right; +90 turns it up

    // Star meter on the header's left edge — the rating this run is heading
    // for, in the same filled/empty stars LevelCompletePanel ends on. A star
    // lost to a hint (TaskManager.OnStarsChanged) pops and then drains out of
    // its slot (see DrainStar) rather than just flipping, so the loss is seen
    // as it happens; the link that costs it says so first (see HintCostTag).
    const int   MaxStars          = 3;
    const float StarSize          = StarIconBuilder.GridSize * 2; // 2x2-pixel cells, small enough for the header
    const float StarSpacing       = 2f;
    const float StarDrainDuration = 0.55f;
    const float StarDrainPunch    = 1.4f;  // swells this much before shrinking away
    const float StarDrainPunchShare = 0.25f;
    const float StarDrainStagger  = 0.15f; // an answer that costs two stars drains them one after the other
    static string CostTagHex => ColorUtility.ToHtmlStringRGB(StarIconBuilder.EarnedColor);
    private Image[] starFills;
    private int shownStars = MaxStars;

    void Start()
    {
        _font = Resources.Load<TMP_FontAsset>(FontResourcePath);
        boxEmptySprite = SmallPlate(BoxEmptyFill, BoxEmptyBorder);
        boxDoneSprite  = SmallPlate(BoxDoneFill, BoxDoneBorder);
        // All-white copy of the same plate — the completion flash overlay,
        // so it keeps the box's chamfered silhouette instead of a square.
        boxFlashSprite = PlateSpriteBuilder.GetSprite(Color.white, Color.white, Color.white, borderThickness: 1, chamfer: 2, pixelScale: 2);
        checkSprite    = CheckmarkIconBuilder.GetSprite(Color.white, logicalSize: 9, strokeWidth: 2, pixelScale: 2);

        BuildCanvas();

        if (TaskManager.Instance != null)
        {
            TaskManager.Instance.OnTasksChanged += RebuildRows;
            TaskManager.Instance.OnStarsChanged += HandleStarsChanged;
        }

        RebuildRows();
    }

    // Same thin-bordered small-plate family CLIPanelManager's chips/buttons
    // use (borderThickness=1, chamfer=2, pixelScale=2 → 6px border) — safe at
    // this checkbox's 24px size, where a PNG button's baked-in large border
    // would overlap/break (see CLIPanelManager.SmallPlate's own doc comment).
    static Sprite SmallPlate(Color fill, Color border) =>
        PlateSpriteBuilder.GetSprite(fill, border, TrimColor, borderThickness: 1, chamfer: 2, pixelScale: 2);

    void OnDestroy()
    {
        if (TaskManager.Instance != null)
        {
            TaskManager.Instance.OnTasksChanged -= RebuildRows;
            TaskManager.Instance.OnStarsChanged -= HandleStarsChanged;
        }
    }

    void Update()
    {
        // Cheap per-row refresh; RebuildRows only runs when the task list/state changes.
        foreach (var row in rows)
        {
            TrackCompletion(row.Task);
            row.Plate.sprite  = row.Task.IsComplete ? boxDoneSprite : boxEmptySprite;
            row.Check.enabled = row.Task.IsComplete;
            StepCompleteFlash(row);
        }
    }

    // ── Completion flash ─────────────────────────────────────────────────────

    // Starts a task's flash on an actual incomplete → complete transition.
    // The first sighting only records state, so a task registered already
    // complete (restored from a save) just shows as done. A transition seen
    // before the panel has revealed is held until it has — a task finished
    // during the entry sequence still flashes once the player can see it.
    void TrackCompletion(TaskManager.GameTask task)
    {
        if (!lastSeenComplete.TryGetValue(task, out bool wasDone))
        {
            lastSeenComplete[task] = task.IsComplete;
            return;
        }
        if (task.IsComplete && !wasDone && panelRevealed)
        {
            lastSeenComplete[task] = true;
            flashStart[task] = Time.unscaledTime;
            AudioManager.Instance?.PlaySfx(SfxId.TaskChecked);
        }
    }

    void StepCompleteFlash(TaskRow row)
    {
        if (!flashStart.TryGetValue(row.Task, out float start)) return;
        float p = (Time.unscaledTime - start) / CompleteFlashDuration;
        ApplyCompleteFlash(row, p);
        if (p >= 1f) flashStart.Remove(row.Task);
    }

    // p = 0..1 through the flash; p >= 1 restores the row's resting look.
    // The box goes white and decays back to its green plate while a glow
    // pulses behind it and the tick pops in; the label gets the same pulse
    // as a gentle tint.
    void ApplyCompleteFlash(TaskRow row, float p)
    {
        bool active = p < 1f;
        row.Flash.enabled = active;
        row.Glow.enabled  = active;
        if (!active)
        {
            row.Box.localScale                 = Vector3.one;
            row.Check.rectTransform.localScale = Vector3.one;
            row.Label.color                    = LabelColor;
            return;
        }

        // sqrt front-loads the 0 → 1 → 0 pulse (peaks a quarter of the way
        // in) so it hits like a flash rather than swelling evenly.
        float pulse = Mathf.Sin(Mathf.Sqrt(p) * Mathf.PI);
        float decay = (1f - p) * (1f - p);

        row.Flash.color = new Color(1f, 1f, 1f, decay * FlashOverlayPeak);
        row.Glow.color  = new Color(BoxDoneFill.r, BoxDoneFill.g, BoxDoneFill.b, pulse * FlashGlowPeak);
        row.Glow.rectTransform.localScale  = Vector3.one * Mathf.Lerp(1f, FlashGlowPunch, pulse);
        row.Box.localScale                 = Vector3.one * Mathf.Lerp(1f, FlashBoxPunch, pulse);
        row.Check.rectTransform.localScale = Vector3.one *
            Mathf.LerpUnclamped(CheckMinPopScale, 1f, EaseOutBack(Mathf.Clamp01(p / CheckPopFraction)));
        row.Label.color = Color.Lerp(LabelColor, LabelFlashColor, pulse * LabelFlashAmount);
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    // ── Canvas ───────────────────────────────────────────────────────────────

    void BuildCanvas()
    {
        var canvasGo = new GameObject("Task_Canvas");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5; // below HUD_Canvas (10)

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);

        canvasGo.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("TaskPanel");
        panel.transform.SetParent(canvasGo.transform, false);
        var bg = panel.AddComponent<Image>();
        OccupiesScreen.Add(panel, OccupantLayer.Hud); // the tutorial callout steps around it
        bg.sprite = PlateSpriteBuilder.GetSprite(PanelFill, PanelBorder, TrimColor,
            borderThickness: 2, chamfer: 5, pixelScale: 5);
        bg.type = Image.Type.Sliced;

        panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin        = new Vector2(1, 1);
        panelRect.anchorMax        = new Vector2(1, 1);
        panelRect.pivot            = new Vector2(1, 1);
        panelRect.anchoredPosition = new Vector2(-20, -20);
        panelRect.sizeDelta        = new Vector2(PanelWidth, HeaderHeight + PaddingY * 2);

        // Panel drives its own height from its children (header + separator + rows) —
        // width stays fixed at PanelWidth, only the vertical axis auto-fits.
        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment         = TextAnchor.UpperCenter;
        layout.childControlWidth      = true;
        layout.childForceExpandWidth  = true;
        layout.childControlHeight     = false;
        layout.childForceExpandHeight = false;
        layout.spacing                = RowSpacing;
        layout.padding                = new RectOffset((int)PaddingX, (int)PaddingX, (int)PaddingY, (int)PaddingY);

        var fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; // width stays at PanelWidth
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        // Entrance flash, not a slide — panel stays put at its resting
        // position and anchoredPosition the whole time; only its alpha
        // moves. Snapped invisible immediately so it doesn't sit visible at
        // rest during the delay below, same reasoning SlidePanel.SnapForSlideIn
        // itself documents for its own off-screen snap.
        panelCanvasGroup = panel.AddComponent<CanvasGroup>();
        panelCanvasGroup.alpha = 0f;
        StartCoroutine(RevealFlashWithHud());

        // Header — same "title sits directly on the plate" look as the CLI
        // sidebar's "Drag Commands" title (see CLIPanelManager.BuildCommandBuilderPanel),
        // no separate background/separator of its own. Now also the dropdown's
        // toggle handle — Button + MenuButtonFX (the same click-glow every
        // menu/level-select button uses) wraps a Glow (behind) + the header
        // text (in front).
        var headerGo = new GameObject("HeaderButton", typeof(RectTransform));
        headerGo.transform.SetParent(panel.transform, false);
        var headerLe = headerGo.AddComponent<LayoutElement>();
        headerLe.preferredHeight = HeaderHeight;
        headerLe.minHeight       = HeaderHeight;
        // The panel's layout deliberately doesn't control child heights (the
        // content wrapper's height is animated directly, see AnimateExpanded),
        // so the LayoutElement above is ignored there — without this the
        // header kept a new RectTransform's default 100px height.
        headerGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, HeaderHeight);

        var headerGlowGo = new GameObject("Glow", typeof(RectTransform));
        headerGlowGo.transform.SetParent(headerGo.transform, false);
        var headerGlowImg = headerGlowGo.AddComponent<Image>();
        headerGlowImg.raycastTarget = false;
        headerGlowImg.color = new Color(0f, 0f, 0f, 0f);
        var headerGlowRt = headerGlowGo.GetComponent<RectTransform>();
        headerGlowRt.anchorMin        = new Vector2(0.5f, 0.5f);
        headerGlowRt.anchorMax        = new Vector2(0.5f, 0.5f);
        headerGlowRt.pivot            = new Vector2(0.5f, 0.5f);
        headerGlowRt.anchoredPosition = Vector2.zero;
        headerGlowRt.sizeDelta        = new Vector2(PanelWidth * 0.9f, HeaderHeight * 2.2f);

        var headerTextGo = new GameObject("Header", typeof(RectTransform));
        headerTextGo.transform.SetParent(headerGo.transform, false);
        var headerTmp = headerTextGo.AddComponent<TextMeshProUGUI>();
        headerTmp.font      = _font;
        headerTmp.text      = "TASKS";
        headerTmp.fontSize  = HeaderFontSize;
        headerTmp.fontStyle = FontStyles.Bold;
        headerTmp.color     = HeaderColor;
        headerTmp.alignment = TextAlignmentOptions.Center;
        var headerTextRt = headerTextGo.GetComponent<RectTransform>();
        headerTextRt.anchorMin = Vector2.zero;
        headerTextRt.anchorMax = Vector2.one;
        headerTextRt.offsetMin = Vector2.zero;
        headerTextRt.offsetMax = Vector2.zero;

        var headerBtn = headerGo.AddComponent<Button>();
        headerBtn.transition    = Selectable.Transition.None;
        headerBtn.targetGraphic = headerTmp;

        headerFx = headerGo.AddComponent<MenuButtonFX>();
        headerFx.visualRect = headerGo.GetComponent<RectTransform>();
        headerFx.glowImage  = headerGlowImg;
        headerFx.glowColor  = PanelBorder;
        headerFx.Init();
        headerBtn.onClick.AddListener(() => headerFx.PlayClickThen(ToggleExpanded));

        var arrowGo = new GameObject("DropdownArrow", typeof(RectTransform));
        arrowGo.transform.SetParent(headerGo.transform, false);
        dropdownArrow = arrowGo.AddComponent<Image>();
        dropdownArrow.sprite = FlyoutArrowIconBuilder.GetSprite(pointRight: true, lit: false);
        dropdownArrow.raycastTarget = false;
        var arrowRt = (RectTransform)arrowGo.transform;
        arrowRt.anchorMin = arrowRt.anchorMax = new Vector2(1f, 0.5f);
        arrowRt.pivot = new Vector2(0.5f, 0.5f);
        arrowRt.anchoredPosition = new Vector2(-ArrowSize * 0.5f, 0f);
        arrowRt.sizeDelta = new Vector2(ArrowSize, ArrowSize);
        SetArrowAngle(ArrowAngle(expanded));

        BuildStarMeter(headerGo.transform);

        var headerHover = headerGo.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        AddHeaderHover(headerHover, UnityEngine.EventSystems.EventTriggerType.PointerEnter, true);
        AddHeaderHover(headerHover, UnityEngine.EventSystems.EventTriggerType.PointerExit, false);
        HoverTooltip.Attach(headerGo, () => new TooltipContent
        {
            Title = "Tasks",
            Subtitle = $"On track for {shownStars} of {MaxStars} stars",
            Body  = "What this level asks you to do. Each box ticks itself once that part of your network works.\n\n" +
                    "The stars are the rating you're heading for. A task's first hint is free; a more specific hint " +
                    "drops the rating to 2 stars, and the answer to 1.",
            Footer = expanded ? "Click to fold the list away." : "Click to open the list again.",
        }, TooltipSide.Left);

        // Collapsible content — a clip wrapper (RectMask2D, height driven
        // directly by AnimateExpanded rather than a LayoutElement, so it
        // never fights the inner Content's own ContentSizeFitter) around the
        // actual row list, which keeps sizing itself to fit exactly as before.
        var wrapperGo = new GameObject("ContentWrapper", typeof(RectTransform));
        wrapperGo.transform.SetParent(panel.transform, false);
        contentWrapperRt = wrapperGo.GetComponent<RectTransform>();
        contentMask = wrapperGo.AddComponent<RectMask2D>();
        contentMask.padding = new Vector4(-FlashBleed, 0f, -FlashBleed, 0f); // see UpdateClipBleed
        contentCanvasGroup = wrapperGo.AddComponent<CanvasGroup>();
        contentCanvasGroup.alpha = 1f;

        // Transparent raycast target so the mouse wheel scrolls the list even
        // over the gaps between rows, not only over the text itself.
        var wrapperHit = wrapperGo.AddComponent<Image>();
        wrapperHit.color = new Color(0f, 0f, 0f, 0f);

        var innerGo = new GameObject("Content", typeof(RectTransform));
        innerGo.transform.SetParent(wrapperGo.transform, false);
        contentInnerRt = innerGo.GetComponent<RectTransform>();
        contentInnerRt.anchorMin        = new Vector2(0f, 1f);
        contentInnerRt.anchorMax        = new Vector2(1f, 1f);
        contentInnerRt.pivot            = new Vector2(0.5f, 1f);
        contentInnerRt.anchoredPosition = Vector2.zero;
        contentInnerRt.sizeDelta        = new Vector2(0f, 0f);

        var innerLayout = innerGo.AddComponent<VerticalLayoutGroup>();
        innerLayout.childAlignment         = TextAnchor.UpperCenter;
        innerLayout.childControlWidth      = true;
        innerLayout.childForceExpandWidth  = true;
        innerLayout.childControlHeight     = true; // each row's height = its wrapped text's height, so RowSpacing is the only gap between tasks
        innerLayout.childForceExpandHeight = false;
        innerLayout.spacing                = RowSpacing;

        var innerFitter = innerGo.AddComponent<ContentSizeFitter>();
        innerFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        innerFitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        // Scrolls only once the list is taller than the screen allows (see
        // MaxContentHeight) — shorter lists fit exactly and never move.
        var scroll = wrapperGo.AddComponent<ScrollRect>();
        scroll.viewport          = contentWrapperRt;
        scroll.content           = contentInnerRt;
        scroll.horizontal        = false;
        scroll.vertical          = true;
        scroll.movementType      = ScrollRect.MovementType.Clamped;
        scroll.inertia           = false;
        scroll.scrollSensitivity = ScrollSensitivity;

        rowContainer = innerGo.transform;
    }

    // Tallest the row list may be before it scrolls instead of pushing the
    // panel off the bottom of the screen — whatever canvas height is left
    // below the panel's top offset, header and padding.
    float MaxContentHeight()
    {
        float canvasHeight = ((RectTransform)panelRect.parent).rect.height;
        float available = canvasHeight + panelRect.anchoredPosition.y // anchoredPosition.y is the (negative) top offset
                        - ScreenBottomMargin - PaddingY * 2f - HeaderHeight - RowSpacing;
        return Mathf.Max(CheckboxSize * 3f, available);
    }

    // ── Star meter ───────────────────────────────────────────────────────────

    void BuildStarMeter(Transform header)
    {
        var meterGo = new GameObject("StarMeter", typeof(RectTransform));
        meterGo.transform.SetParent(header, false);
        var meterRt = (RectTransform)meterGo.transform;
        meterRt.anchorMin = meterRt.anchorMax = new Vector2(0f, 0.5f);
        meterRt.pivot = new Vector2(0f, 0.5f);
        meterRt.anchoredPosition = Vector2.zero;
        meterRt.sizeDelta = new Vector2(MaxStars * StarSize + (MaxStars - 1) * StarSpacing, StarSize);

        var emptySprite = StarIconBuilder.GetSprite(StarIconBuilder.UnearnedColor, pixelScale: 2);
        var fillSprite  = StarIconBuilder.GetSprite(StarIconBuilder.EarnedColor, pixelScale: 2);
        starFills = new Image[MaxStars];
        for (int i = 0; i < MaxStars; i++)
        {
            var slot = StarImage($"Star{i}", meterGo.transform, emptySprite);
            var slotRt = slot.rectTransform;
            slotRt.anchorMin = slotRt.anchorMax = new Vector2(0f, 0.5f);
            slotRt.pivot = new Vector2(0f, 0.5f);
            slotRt.anchoredPosition = new Vector2(i * (StarSize + StarSpacing), 0f);
            slotRt.sizeDelta = new Vector2(StarSize, StarSize);

            // The earned star sits on its empty slot, so draining it reveals the slot.
            starFills[i] = StarImage("Fill", slot.transform, fillSprite);
            var fillRt = starFills[i].rectTransform;
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
        }

        shownStars = TaskManager.Instance != null ? TaskManager.Instance.ComputeStars() : MaxStars;
        for (int i = 0; i < MaxStars; i++) starFills[i].enabled = i < shownStars;
    }

    static Image StarImage(string name, Transform parent, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false; // the header under it keeps every click and its own hover card
        return img;
    }

    void HandleStarsChanged(int stars)
    {
        // Right to left, one after another when an answer costs two at once.
        for (int i = shownStars - 1, n = 0; i >= stars; i--, n++)
            StartCoroutine(DrainStar(starFills[i], n * StarDrainStagger));
        shownStars = stars;

        NotificationManager.Instance?.Notify(
            $"Hint used — this level's best rating is now {stars} star{(stars == 1 ? "" : "s")}.", NotifyKind.Warning);
    }

    // Reverse of LevelCompletePanel's star pop: a quick swell (the hit), then
    // the star shrinks and fades into its empty slot.
    IEnumerator DrainStar(Image fill, float delay)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

        var rt = fill.rectTransform;
        float t = 0f;
        while (t < StarDrainDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / StarDrainDuration);
            float scale, alpha = 1f;
            if (p < StarDrainPunchShare)
                scale = Mathf.Lerp(1f, StarDrainPunch, Mathf.SmoothStep(0f, 1f, p / StarDrainPunchShare));
            else
            {
                float q = Mathf.SmoothStep(0f, 1f, (p - StarDrainPunchShare) / (1f - StarDrainPunchShare));
                scale = Mathf.Lerp(StarDrainPunch, 0f, q);
                alpha = 1f - q;
            }
            rt.localScale = Vector3.one * scale;
            fill.color = new Color(1f, 1f, 1f, alpha);
            yield return null;
        }

        fill.enabled = false;
        rt.localScale = Vector3.one;
        fill.color = Color.white;
    }

    IEnumerator RevealFlashWithHud()
    {
        while (!GameBootstrap.HudRevealStarted) yield return null;
        yield return new WaitForSecondsRealtime(AnimationSpeed.Seconds(AnimationKind.Level, GameBootstrap.HudButtonsDelay + TaskPanelRevealGap));

        yield return FadeCanvasAlpha(panelCanvasGroup, 0f, AfterimageAlpha, AfterimageUp);
        yield return new WaitForSecondsRealtime(AfterimageHold);
        yield return FadeCanvasAlpha(panelCanvasGroup, AfterimageAlpha, 0f, AfterimageDown);

        panelCanvasGroup.alpha = 1f;
        panelRevealed = true;
        if (headerFx != null) headerFx.PlayClickThen(null);
    }

    /// <summary>
    /// Reverse of the entrance flash — called by GameBootstrap.
    /// ExitSequenceRoutine alongside the HUD buttons' own fade-out, so the
    /// task panel retreats as part of leaving the level instead of just
    /// vanishing with the scene swap. Glow pulses first (the panel visibly
    /// "reacts"), then the afterimage fades back down to nothing — the same
    /// three beats as the entrance, just played back to front. onComplete
    /// fires immediately if the panel never finished revealing yet (nothing
    /// to reverse) as well as once the exit flash itself finishes.
    /// </summary>
    public void PlayExitReveal(System.Action onComplete = null)
    {
        if (panelCanvasGroup == null) { onComplete?.Invoke(); return; }
        StartCoroutine(ExitRevealRoutine(onComplete));
    }

    IEnumerator ExitRevealRoutine(System.Action onComplete)
    {
        if (headerFx != null) headerFx.PlayClickThen(null);

        yield return FadeCanvasAlpha(panelCanvasGroup, 1f, AfterimageAlpha, AfterimageUp);
        yield return new WaitForSecondsRealtime(AfterimageHold);
        yield return FadeCanvasAlpha(panelCanvasGroup, AfterimageAlpha, 0f, AfterimageDown);

        onComplete?.Invoke();
    }

    static IEnumerator FadeCanvasAlpha(CanvasGroup group, float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
            yield return null;
        }
        group.alpha = to;
    }

    // ── Dropdown toggle ──────────────────────────────────────────────────────

    void ToggleExpanded()
    {
        expanded = !expanded;
        if (toggleRoutine != null) StopCoroutine(toggleRoutine);
        toggleRoutine = StartCoroutine(AnimateExpanded(expanded));
    }

    IEnumerator AnimateExpanded(bool expand)
    {
        contentMask.padding = new Vector4(-FlashBleed, 0f, -FlashBleed, 0f); // see UpdateClipBleed
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentInnerRt);
        float targetHeight = expand ? Mathf.Min(contentInnerRt.rect.height, MaxContentHeight()) : 0f;
        float startHeight  = contentWrapperRt.sizeDelta.y;
        float startAlpha   = contentCanvasGroup.alpha;
        float targetAlpha  = expand ? 1f : 0f;
        float startAngle   = arrowAngle;
        float targetAngle  = ArrowAngle(expand);

        float t = 0f;
        while (t < ExpandDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / ExpandDuration);
            float eased = 1f - (1f - p) * (1f - p);
            contentWrapperRt.sizeDelta = new Vector2(contentWrapperRt.sizeDelta.x, Mathf.Lerp(startHeight, targetHeight, eased));
            contentCanvasGroup.alpha   = Mathf.Lerp(startAlpha, targetAlpha, eased);
            SetArrowAngle(Mathf.Lerp(startAngle, targetAngle, eased)); // swings through "pointing right" on the way
            LayoutRebuilder.MarkLayoutForRebuild(panelRect);
            yield return null;
        }

        contentWrapperRt.sizeDelta = new Vector2(contentWrapperRt.sizeDelta.x, targetHeight);
        contentCanvasGroup.alpha   = targetAlpha;
        SetArrowAngle(targetAngle);
        LayoutRebuilder.MarkLayoutForRebuild(panelRect);
        toggleRoutine = null;
        UpdateClipBleed();
    }

    void SetArrowAngle(float angle)
    {
        arrowAngle = angle;
        if (dropdownArrow != null) dropdownArrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    void AddHeaderHover(UnityEngine.EventSystems.EventTrigger trigger, UnityEngine.EventSystems.EventTriggerType type, bool lit)
    {
        var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => dropdownArrow.sprite = FlyoutArrowIconBuilder.GetSprite(pointRight: true, lit: lit));
        trigger.triggers.Add(entry);
    }

    // Extends the row list's clip past its edges so a completion flash's
    // glow fades out instead of being cut off by a hard line. Sideways
    // always (only panel padding is there); vertically only while the whole
    // list fits and isn't mid-toggle — otherwise rows scrolled out of view
    // would peek into the gap under the header, or below a shrinking wrapper.
    void UpdateClipBleed()
    {
        bool fits = expanded && toggleRoutine == null
                    && contentInnerRt.rect.height <= MaxContentHeight();
        float v = fits ? -FlashBleed : 0f;
        contentMask.padding = new Vector4(-FlashBleed, v, -FlashBleed, v);
    }

    // ── Rows ─────────────────────────────────────────────────────────────────

    void RebuildRows()
    {
        // Deactivated as well as destroyed: Destroy only lands at end of
        // frame, and until then the layout below would still measure the old
        // rows alongside the new ones, leaving the panel sized for both.
        foreach (Transform child in rowContainer)
        {
            if (!child.name.StartsWith("Row_")) continue;
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        rows.Clear();

        if (TaskManager.Instance == null) return;

        var tasks = TaskManager.Instance.Tasks;
        for (int i = 0; i < tasks.Count; i++)
            rows.Add(CreateRow(tasks[i], i));

        // Keep the wrapper's clip height in sync with the row list's own
        // (possibly just-changed) natural height while expanded — a hint
        // reveal or task completion can change row content/count mid-level,
        // not just the initial build. Skipped while a toggle animation is
        // actually in flight so it doesn't fight that coroutine.
        if (expanded && toggleRoutine == null && contentWrapperRt != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentInnerRt);
            contentWrapperRt.sizeDelta = new Vector2(contentWrapperRt.sizeDelta.x, Mathf.Min(contentInnerRt.rect.height, MaxContentHeight()));
            LayoutRebuilder.MarkLayoutForRebuild(panelRect);
            UpdateClipBleed();
        }
    }

    TaskRow CreateRow(TaskManager.GameTask task, int index)
    {
        // Rows are built as a task registers (RegisterTask → OnTasksChanged),
        // so this is where a task restored complete gets first sighted.
        TrackCompletion(task);

        var row = new GameObject($"Row_{index}");
        row.transform.SetParent(rowContainer, false);

        // Row height auto-fits its tallest child (the wrapped label), same
        // pattern the sidebar uses for its own buttons (height from content,
        // not a hardcoded constant).
        var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.childAlignment         = TextAnchor.UpperLeft;
        rowLayout.childControlWidth      = true;
        rowLayout.childControlHeight     = true;
        rowLayout.childForceExpandWidth  = false;
        rowLayout.childForceExpandHeight = false;
        rowLayout.spacing                = RowInnerSpacing;

        // Checkbox — a plain container (what the completion flash scale-
        // punches) holding, back to front: the flash's additive glow, the
        // small chamfered plate itself (same family as CLIPanelManager's
        // param-field/Run/Clear plates), the tick, and the white flash copy
        // of the plate.
        var boxGo = new GameObject("Checkbox", typeof(RectTransform));
        boxGo.transform.SetParent(row.transform, false);
        var boxLe = boxGo.AddComponent<LayoutElement>();
        boxLe.preferredWidth  = CheckboxSize;
        boxLe.preferredHeight = CheckboxSize;
        boxLe.minWidth        = CheckboxSize;
        boxLe.minHeight       = CheckboxSize;
        boxLe.flexibleWidth   = 0;
        boxLe.flexibleHeight  = 0;

        var glow = AddBoxLayer("Glow", boxGo.transform, RadialGlowSprite.Get(), FlashGlowSize);
        glow.material = RadialGlowSprite.GetAdditiveMaterial();
        glow.enabled  = false;

        var plate = AddBoxLayer("Plate", boxGo.transform, task.IsComplete ? boxDoneSprite : boxEmptySprite);
        plate.type = Image.Type.Sliced;

        // Pixel-art tick (CheckmarkIconBuilder) rather than a font glyph —
        // PixeloidMono has no checkmark, and the old "V" stand-in read as a
        // letter.
        var check = AddBoxLayer("Check", boxGo.transform, checkSprite, new Vector2(CheckIconSize, CheckIconSize));
        check.preserveAspect = true;
        check.enabled        = task.IsComplete;

        var flash = AddBoxLayer("Flash", boxGo.transform, boxFlashSprite);
        flash.type    = Image.Type.Sliced;
        flash.enabled = false;

        // TextColumn — label plus (optionally) revealed hint lines and a
        // "Hint" affordance, stacked vertically. Existing tasks (no hint text
        // on any tier) render exactly as before: just the label, no extra
        // lines — this column only ever adds rows, never removes the label.
        var columnGo = new GameObject("TextColumn");
        columnGo.transform.SetParent(row.transform, false);
        var columnLayout = columnGo.AddComponent<VerticalLayoutGroup>();
        columnLayout.childAlignment         = TextAnchor.UpperLeft;
        columnLayout.childControlWidth      = true;
        columnLayout.childForceExpandWidth  = true;
        // childControlHeight must be on: otherwise each TMP label keeps its
        // default 50px rect and extra wrapped lines spill over the next row.
        columnLayout.childControlHeight     = true;
        columnLayout.childForceExpandHeight = false;
        columnLayout.spacing                = ColumnSpacing;
        var columnLe = columnGo.AddComponent<LayoutElement>();
        columnLe.flexibleWidth = 1;
        columnLe.minWidth      = 0;

        // Label — word-wraps within the column's width, which is what makes
        // the row (and therefore the panel) grow to fit longer tasks.
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(columnGo.transform, false);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.font               = _font;
        label.text               = task.Description;
        label.fontSize           = LabelFontSize;
        label.color              = LabelColor;
        label.alignment          = TextAlignmentOptions.TopLeft;
        label.enableWordWrapping = true;
        label.overflowMode       = TextOverflowModes.Overflow;

        for (int tier = 1; tier <= task.HighestHintTierUsed; tier++)
        {
            string text = HintTextForTier(task, tier);
            if (string.IsNullOrEmpty(text)) continue;

            var hintLineGo = new GameObject($"Hint{tier}");
            hintLineGo.transform.SetParent(columnGo.transform, false);
            var hintLine = hintLineGo.AddComponent<TextMeshProUGUI>();
            hintLine.font               = _font;
            hintLine.text               = text;
            hintLine.fontSize           = LabelFontSize - 3;
            hintLine.color              = new Color(LabelColor.r, LabelColor.g, LabelColor.b, 0.7f);
            hintLine.fontStyle          = FontStyles.Italic;
            hintLine.alignment          = TextAlignmentOptions.TopLeft;
            hintLine.enableWordWrapping = true;
        }

        int nextTier = NextHintTier(task);
        if (!task.IsComplete && nextTier > 0)
        {
            var hintBtnGo = new GameObject("HintButton");
            hintBtnGo.transform.SetParent(columnGo.transform, false);
            var hintBtn = hintBtnGo.AddComponent<Button>();
            hintBtn.transition = Selectable.Transition.None;
            var hintBtnLabel = hintBtnGo.AddComponent<TextMeshProUGUI>();
            hintBtnLabel.font      = _font;
            hintBtnLabel.text      = (task.HighestHintTierUsed == 0 ? "Hint >" : "More hint >") + HintCostTag(nextTier); // PixeloidMono has no ▸ glyph
            hintBtnLabel.fontSize  = LabelFontSize - 3;
            hintBtnLabel.color     = GameTheme.Info;
            hintBtnLabel.alignment = TextAlignmentOptions.TopLeft;
            hintBtnLabel.raycastTarget = true;
            hintBtn.targetGraphic = hintBtnLabel;
            int tierToReveal = nextTier;
            hintBtn.onClick.AddListener(() => TaskManager.Instance?.UseHint(task, tierToReveal));
        }

        var taskRow = new TaskRow
        {
            Task = task, Box = boxGo.GetComponent<RectTransform>(),
            Plate = plate, Check = check, Flash = flash, Glow = glow, Label = label,
        };
        // Pick up a flash already in progress (this row replacing one that
        // was mid-flash) right away, rather than showing a frame at rest.
        StepCompleteFlash(taskRow);
        return taskRow;
    }

    // One non-raycasting Image inside the checkbox — centered at a fixed size
    // when given one, otherwise stretched to fill the box.
    static Image AddBoxLayer(string name, Transform box, Sprite sprite, Vector2? size = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(box, false);
        var img = go.AddComponent<Image>();
        img.sprite        = sprite;
        img.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        if (size is Vector2 s)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = s;
        }
        else
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
        return img;
    }

    // What opening `tier` does to the rating, written on the link itself so
    // a star is never spent by surprise. Only the deepest tier opened on any
    // task counts (see TaskManager.ComputeStars), so a tier another task
    // already paid for is labeled as costing nothing more. Words, not a "★"
    // glyph — PixeloidMono has none (see StarIconBuilder).
    static string HintCostTag(int tier)
    {
        var tm = TaskManager.Instance;
        if (tm == null) return "";
        int lost = tm.ComputeStars() - tm.StarsAfterHint(tier);
        if (lost > 0) return $" <color=#{CostTagHex}>(-{lost} star{(lost == 1 ? "" : "s")})</color>";
        return tier < TaskManager.SpecificTier ? " <alpha=#99>(free)" : " <alpha=#99>(no extra cost)";
    }

    static string HintTextForTier(TaskManager.GameTask task, int tier) => tier switch
    {
        1 => task.HintNudge,
        2 => task.HintSpecific,
        3 => task.HintAnswer,
        _ => null,
    };

    // First tier beyond what's already been used whose text is actually
    // populated — 0 if nothing further is available. Lets a level author
    // leave a tier blank (e.g. no "specific" hint, just nudge + answer)
    // without the button getting stuck offering an empty reveal.
    static int NextHintTier(TaskManager.GameTask task)
    {
        for (int tier = task.HighestHintTierUsed + 1; tier <= 3; tier++)
            if (!string.IsNullOrEmpty(HintTextForTier(task, tier))) return tier;
        return 0;
    }
}
