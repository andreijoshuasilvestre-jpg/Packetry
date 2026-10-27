using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Builds and owns both Settings surfaces the game has:
///   - A full-screen page (OpenFullScreen), wired to MainMenuBootstrap's
///     "Settings" button — same hand-off as Level Select: the menu slides
///     out, then this panel zooms in over the main menu's own background
///     (it has no backdrop of its own), and zooms back out on close.
///   - A floating dialog (OpenDialog), wired to NetworkUIManager's top-bar
///     "Settings" icon — a centered chamfered-plate panel over the running
///     level, same visual language as ConfirmDialogManager, that dims but
///     doesn't hide the game behind it. Zooms in and plays the arrival glow
///     (PanelArrivalGlow) the same way ConfirmDialogManager does.
///
/// Both share the same content-building code (BuildAudioSection, etc.) so a
/// new settings feature is added once and appears in both places. Sections
/// are added incrementally, one feature at a time — see CLAUDE.md's workflow
/// note — so this file starts with just Audio.
///
/// Pauses gameplay input through InteractionMode.IsSuspended (alongside
/// AnnotationLock): every interactive manager's Update() checks that right
/// alongside CLIPanelManager.Instance.IsOpen.
/// </summary>
public class SettingsPanelManager : MonoBehaviour
{
    public static SettingsPanelManager Instance { get; private set; }

    /// <summary>True while either surface is open — this is the flag every other
    /// manager's Update() checks before handling input.</summary>
    public bool IsOpen =>
        (dialogRoot != null && dialogRoot.activeSelf) ||
        (fullScreenRoot != null && fullScreenRoot.activeSelf);

    const string FontResourcePath = "Fonts/PixeloidMono SDF";

    // Matches ConfirmDialogManager's plate colors exactly — "keep them the
    // same style as the game" per request, and the dialog variant sits
    // alongside ConfirmDialogManager in the same in-game context.
    static readonly Color32 PanelFill   = new Color32(14, 18, 28, 250);
    static readonly Color32 PanelBorder = new Color32(36, 82, 130, 255);
    static readonly Color32 PanelTrim   = new Color32(5, 8, 13, 255);

    // Heights grew by ~160 to fit the new Camera section (header + 2 rows)
    // added alongside Audio/Display/Subnet Colors without squeezing them, then
    // by two rows (54 each) for Narration Volume and Tutorial Narration, then
    // by 202 for Animation Speed (header + 3 rows). Taller than a 1080 canvas
    // now — FitToScreen clamps the panel and the body scrolls.
    static readonly Vector2 DialogPanelSize     = new Vector2(620f, 1090f);
    static readonly Vector2 FullScreenPanelSize = new Vector2(860f, 1230f);

    // Row geometry shared by every settings control this panel ever builds.
    const float RowHeight    = 44f;
    const float RowSpacing   = 10f;
    const float LabelWidth   = 200f;
    const float SliderWidth  = 260f;
    const float SliderMinWidth = 160f; // how far a slider bar may shrink in a narrow column (see MakeSliderRow)
    const float ValueWidth   = 60f;
    const float ChoiceWidth    = 144f; // each button of a two-way switch row (see MakeToggleRow)
    const float ChoiceMinWidth = 110f;
    const float ChoiceHeight   = 36f;
    static readonly Color ChoiceIdle = new Color(0.30f, 0.32f, 0.38f);

    TMP_FontAsset font;

    GameObject dialogRoot;
    ZoomPanel dialogZoom;
    PanelArrivalGlow dialogGlow;
    Transform dialogContent;
    Transform dialogSubnetList;

    GameObject fullScreenRoot;
    ZoomPanel fullScreenZoom;
    Transform fullScreenContent;
    Transform fullScreenSubnetList;
    System.Action fullScreenOnClosed;
    bool fullScreenClosing;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        font = Resources.Load<TMP_FontAsset>(FontResourcePath);
        BuildDialog();
        BuildFullScreen();
        BuildColorPickerPopup();
        dialogRoot.SetActive(false);
        fullScreenRoot.SetActive(false);
        colorPickerRoot.SetActive(false);
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>In-game floating dialog — see NetworkUIManager's top-bar Settings icon.</summary>
    public void OpenDialog()
    {
        if (dialogRoot == null) return;
        dialogRoot.SetActive(true);
        RefreshControls();
        ForceRelayout(dialogContent);
        dialogZoom.ZoomIn(dialogGlow.Play);
    }

    public void CloseDialog()
    {
        if (dialogRoot == null) return;
        SaveManager.Save(); // flush whatever was changed while it was open
        dialogZoom.ZoomOut(() => dialogRoot.SetActive(false));
    }

    /// <summary>Full-screen page — see MainMenuBootstrap's "Settings" button.
    /// Zooms the panel in; onClosed fires once CloseFullScreen's zoom-out has
    /// finished and the page is hidden (MainMenuBootstrap slides the menu
    /// back in from there, same as returning from Level Select).</summary>
    public void OpenFullScreen(System.Action onClosed = null)
    {
        // Not built yet (Start hasn't run) — hand straight back so the caller
        // isn't left with its own screen hidden and nothing showing.
        if (fullScreenRoot == null) { onClosed?.Invoke(); return; }

        fullScreenOnClosed = onClosed;
        fullScreenClosing  = false;
        fullScreenRoot.SetActive(true);
        RefreshControls();
        ForceRelayout(fullScreenContent);
        fullScreenZoom.ZoomIn();
    }

    /// <summary>Unity's layout groups queue their rebuild for later in the frame
    /// rather than computing synchronously (see CanvasUpdateRegistry) — building
    /// this whole hierarchy by code in one call chain, on a GameObject that was
    /// inactive a moment ago, otherwise shows stale/zero sizes for at least one
    /// frame (caught via a runtime screenshot: the Subnet Colors swatches were
    /// positioned outside their scroll viewport's actual clip rect and got culled
    /// entirely). Forces every nested layout group under root to recompute now.</summary>
    static void ForceRelayout(Transform root)
    {
        if (root == null) return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(root as RectTransform);
    }

    public void CloseFullScreen()
    {
        if (fullScreenRoot == null || fullScreenClosing) return; // ignore repeat clicks on X while it's already zooming out
        fullScreenClosing = true;
        SaveManager.Save();
        fullScreenZoom.ZoomOut(() =>
        {
            fullScreenRoot.SetActive(false);
            var onClosed = fullScreenOnClosed;
            fullScreenOnClosed = null;
            onClosed?.Invoke();
        });
    }

    // ── Dialog (in-game, floating) ───────────────────────────────────────────

    void BuildDialog()
    {
        var canvasGo = new GameObject("SettingsDialog_Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 65; // above ConfirmDialogManager (60) — Settings wins if both would ever show

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);
        canvasGo.AddComponent<GraphicRaycaster>();

        var dimGo = new GameObject("Dim");
        dialogRoot = dimGo;
        dimGo.transform.SetParent(canvasGo.transform, false);
        var dimImg = dimGo.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.6f);
        var dimRt = dimGo.GetComponent<RectTransform>();
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = Vector2.zero;
        dimRt.offsetMax = Vector2.zero;

        var panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(dimGo.transform, false);
        var panelImg = panelGo.AddComponent<Image>();
        panelImg.sprite = PlateSpriteBuilder.GetSprite(PanelFill, PanelBorder, PanelTrim,
            borderThickness: 4, chamfer: 22, pixelScale: 4);
        panelImg.type = Image.Type.Sliced;

        var dialogPanelRt = panelGo.GetComponent<RectTransform>();
        dialogPanelRt.anchorMin = new Vector2(0.5f, 0.5f);
        dialogPanelRt.anchorMax = new Vector2(0.5f, 0.5f);
        dialogPanelRt.pivot     = new Vector2(0.5f, 0.5f);
        dialogPanelRt.sizeDelta = DialogPanelSize;
        // 780 tall — taller than the canvas at high UI Scale. ClampSize, not
        // Shrink: the panel stops at the screen margin but its text keeps
        // growing with UI Scale, and the body scrolls (MakeContentContainer).
        FitToScreen.Attach(panelGo, FitToScreen.FitMode.ClampSize);

        MakeTitle(panelGo.transform, 22);
        MakeCloseButton(panelGo.transform, DialogPanelSize, CloseDialog);
        dialogContent = MakeContentContainer(panelGo.transform);
        BuildAudioSection(dialogContent);
        BuildDisplaySection(dialogContent);
        BuildCameraSection(dialogContent);
        BuildAnimationSection(dialogContent);
        dialogSubnetList = BuildSubnetSection(dialogContent, 150f);

        // Same popup entrance as ConfirmDialogManager and the level
        // description popup: zoom up from nothing, then the arrival glow once
        // it's full size. Scale-only, so it doesn't fight FitToScreen's
        // ClampSize (size-only) on the same panel.
        dialogZoom = panelGo.AddComponent<ZoomPanel>();
        dialogGlow = PanelArrivalGlow.Attach(panelGo, GameTheme.ElectricBlue);
    }

    // ── Full-screen (main menu) ──────────────────────────────────────────────

    void BuildFullScreen()
    {
        var canvasGo = new GameObject("SettingsFullScreen_Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20; // above the main menu's own canvas, below any confirm dialog it might open

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);
        canvasGo.AddComponent<GraphicRaycaster>();

        fullScreenRoot = canvasGo;

        // No backdrop of its own — the main menu's scrolling background
        // (MainMenuCanvasBuilder, on the canvas underneath) shows through, the
        // same way it does behind Level Select. The menu's logo/buttons have
        // already slid out by the time this opens (see MainMenuBootstrap.
        // OpenSettings), so there's nothing behind the panel to cover or click.
        var panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        var panelImg = panelGo.AddComponent<Image>();
        panelImg.sprite = PlateSpriteBuilder.GetSprite(PanelFill, PanelBorder, PanelTrim,
            borderThickness: 5, chamfer: 30, pixelScale: 5);
        panelImg.type = Image.Type.Sliced;

        var panelRt = panelGo.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot     = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = FullScreenPanelSize;
        FitToScreen.Attach(panelGo, FitToScreen.FitMode.ClampSize); // 920 tall — see BuildDialog

        MakeTitle(panelGo.transform, 30);
        MakeCloseButton(panelGo.transform, FullScreenPanelSize, CloseFullScreen);
        fullScreenContent = MakeContentContainer(panelGo.transform);
        BuildAudioSection(fullScreenContent);
        BuildDisplaySection(fullScreenContent);
        BuildCameraSection(fullScreenContent);
        BuildAnimationSection(fullScreenContent);
        fullScreenSubnetList = BuildSubnetSection(fullScreenContent, 260f);

        // Scale-only, so it doesn't fight FitToScreen's ClampSize (size-only)
        // on the same panel.
        fullScreenZoom = panelGo.AddComponent<ZoomPanel>();
    }

    // ── Shared chrome ────────────────────────────────────────────────────────

    void MakeTitle(Transform parent, int fontSize)
    {
        var go = new GameObject("Title");
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font      = font;
        tmp.fontSize  = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = new Color(0.88f, 0.92f, 0.97f);
        tmp.text      = "SETTINGS";

        // Stretched across the panel (60 in from each side) rather than sized
        // from the authored panel width, so it follows FitToScreen's clamp.
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 1f);
        rt.anchorMax        = new Vector2(1f, 1f);
        rt.pivot            = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -24f);
        rt.sizeDelta        = new Vector2(-120f, 40f);
    }

    void MakeCloseButton(Transform parent, Vector2 panelSize, UnityEngine.Events.UnityAction onClick)
    {
        var btnGo = new GameObject("CloseButton");
        btnGo.transform.SetParent(parent, false);
        var btnImg = btnGo.AddComponent<Image>();
        btnImg.color = new Color(0.30f, 0.32f, 0.38f);
        var btnRt = btnGo.GetComponent<RectTransform>();
        btnRt.anchorMin        = new Vector2(1f, 1f);
        btnRt.anchorMax        = new Vector2(1f, 1f);
        btnRt.pivot            = new Vector2(1f, 1f);
        btnRt.anchoredPosition = new Vector2(-20f, -18f);
        btnRt.sizeDelta        = new Vector2(36f, 36f);

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(btnGo.transform, false);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.font      = font;
        label.fontSize  = 22;
        label.alignment = TextAlignmentOptions.Center;
        label.color     = Color.white;
        label.text      = "X";
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        var btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.onClick.AddListener(onClick);
    }

    /// <summary>Vertical stack every settings section (Audio, and whatever's added
    /// after it) appends its rows to. Lives inside a scroll view filling the
    /// panel below the title — at normal UI Scale everything fits and it never
    /// scrolls, but once FitToScreen caps the panel at the screen edge the
    /// still-growing rows overflow and scroll instead of getting cut off.</summary>
    Transform MakeContentContainer(Transform parent)
    {
        var scrollGo = new GameObject("ContentScroll", typeof(RectTransform));
        scrollGo.transform.SetParent(parent, false);
        var scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = new Vector2(50f, 40f);   // same 50-side / 40-bottom inset
        scrollRt.offsetMax = new Vector2(-50f, -80f); // the fixed-size container had

        var viewportGo = new GameObject("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        var viewportRt = viewportGo.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = Vector2.zero;
        viewportRt.offsetMax = Vector2.zero;
        viewportGo.AddComponent<RectMask2D>();
        // Invisible, but makes the gaps between rows a scroll/drag target too.
        var viewportImg = viewportGo.AddComponent<Image>();
        viewportImg.color = new Color(0f, 0f, 0f, 0f);

        var go = new GameObject("Content", typeof(RectTransform));
        go.transform.SetParent(viewportGo.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 1f);
        rt.anchorMax        = new Vector2(1f, 1f);
        rt.pivot            = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = Vector2.zero; // see MakeScrollList — default (100,100) overflows a stretch

        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scrollRect = scrollGo.AddComponent<SmoothScrollRect>();
        scrollRect.horizontal        = false;
        scrollRect.vertical          = true;
        scrollRect.movementType      = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;
        scrollRect.viewport          = viewportRt;
        scrollRect.content           = rt;

        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment      = TextAnchor.UpperCenter;
        vlg.spacing             = RowSpacing;
        vlg.childControlWidth   = true;
        vlg.childControlHeight  = false;
        // true — without forceExpand, a row's "preferred width" only comes from
        // its own ILayoutElement (a HorizontalLayoutGroup provides one automatically,
        // which is why the audio slider rows worked by accident; a bare ScrollRect
        // GameObject provides none and silently collapsed to 0 width — caught by
        // inspecting the runtime hierarchy after the Subnet Colors rows rendered
        // invisible). Forcing full width makes every row's width explicit instead
        // of accidental.
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        return go.transform;
    }

    /// <summary>Re-reads saved values into every built control — called each time
    /// either surface opens, so a value changed via the other surface (or another
    /// session) shows correctly instead of a stale value from when this was built.
    /// Each surface builds its own independent controls, and every control
    /// registers its own refresh as it's built (see controlRefreshers), so both
    /// surfaces are refreshed unconditionally rather than tracking which one is
    /// open — updating a hidden canvas's controls is harmless and far simpler
    /// than keeping that in sync.</summary>
    void RefreshControls()
    {
        foreach (var refresh in controlRefreshers) refresh();

        RebuildSubnetRows(dialogSubnetList);
        RebuildSubnetRows(fullScreenSubnetList);
    }

    // One per built control, across both surfaces — each re-reads its own saved
    // value (see RefreshControls). Registered by MakeSliderRow/MakeToggleRow
    // rather than kept in per-surface fields: a single shared field once ended
    // up owned by whichever surface built last (a real bug caught via
    // screenshot: the dialog's sliders never refreshed).
    readonly System.Collections.Generic.List<System.Action> controlRefreshers = new();

    // ── Audio section ────────────────────────────────────────────────────────

    /// <summary>Called once per surface (dialog + full-screen) right after
    /// MakeContentContainer, building this feature's rows into that container.</summary>
    void BuildAudioSection(Transform content)
    {
        MakeSectionHeader(content, "AUDIO");

        MakeSliderRow(content, "Music Volume", () => SaveManager.Settings.musicVolume,
            v => MusicManager.Instance?.SetVolume(v));
        MakeSliderRow(content, "SFX Volume", () => SaveManager.Settings.sfxVolume,
            v => AudioManager.Instance?.SetVolume(v));
        MakeSliderRow(content, "Narration Volume", () => SaveManager.Settings.narrationVolume,
            NarrationManager.SetVolume);
        MakeToggleRow(content, "Tutorial Narration",
            () => NarrationManager.Enabled, NarrationManager.SetEnabled);
    }

    // ── Display section (UI Scale) ──────────────────────────────────────────

    /// <summary>Accessibility-style "make everything bigger/smaller" slider —
    /// see UIScale's own doc comment for how this actually resizes UI (adjusting
    /// every registered CanvasScaler's reference resolution) without touching
    /// each manager's own layout code.</summary>
    void BuildDisplaySection(Transform content)
    {
        MakeSectionHeader(content, "DISPLAY");

        MakeSliderRow(content, "UI Scale", () => SaveManager.Settings.uiScale,
            v =>
            {
                SaveManager.Settings.uiScale = v;
                UIScale.Apply();
            },
            minValue: 0.8f, maxValue: 1.5f);
    }

    // ── Camera section (zoom speed, camera/pan speed) ───────────────────────
    // Multipliers on top of IsometricCamera's own base speeds, not raw values —
    // same "1 = unchanged" convention as uiScale, so old saves default to
    // exactly today's feel. The zoom *level* itself (IsometricCamera.cameraZoomDistance)
    // isn't a slider here — it's saved automatically as the player scrolls
    // (see IsometricCamera.FlushZoomSave), nothing to configure.

    void BuildCameraSection(Transform content)
    {
        MakeSectionHeader(content, "CAMERA");

        MakeSliderRow(content, "Zoom Speed", () => SaveManager.Settings.cameraZoomSpeedMultiplier,
            v => SaveManager.Settings.cameraZoomSpeedMultiplier = v,
            minValue: 0.5f, maxValue: 2f);

        MakeSliderRow(content, "Camera Speed", () => SaveManager.Settings.cameraPanSpeedMultiplier,
            v => SaveManager.Settings.cameraPanSpeedMultiplier = v,
            minValue: 0.5f, maxValue: 2f);
    }

    // ── Animation Speed section ──────────────────────────────────────────────
    // One multiplier per kind of animation (see AnimationSpeed), stepped in
    // quarters from 1x to 3x — a stepper rather than a slider, since only
    // those exact values mean anything. Each row explains on hover what it
    // speeds up.

    void BuildAnimationSection(Transform content)
    {
        MakeSectionHeader(content, "ANIMATION SPEED");

        MakeStepperRow(content, "Packets", AnimationKind.Packets,
            "How fast pings and packet transfers travel along the cables, and how quickly devices and PC screens react when one leaves or arrives.");
        MakeStepperRow(content, "Building", AnimationKind.Build,
            "How fast a device, table or cable you place builds itself in.");
        MakeStepperRow(content, "Level Intro & Exit", AnimationKind.Level,
            "How fast a level assembles when you enter it (floor, walls, starting devices, toolbar) and clears away when you leave or restart.");
    }

    /// <summary>Builds one "Label  [ &lt; ]  1.25x  [ &gt; ]" row stepping kind's
    /// multiplier by AnimationSpeed.Step, each click saved straight away (one
    /// click is one change — no drag to debounce, unlike MakeSliderRow). An
    /// arrow at the end of the range greys out.</summary>
    void MakeStepperRow(Transform parent, string label, AnimationKind kind, string explanation)
    {
        Transform row = MakeRow(parent, label);
        HoverTooltip.Attach(row.Find("Label").gameObject, () => new TooltipContent
        {
            Title  = $"{label} speed",
            Body   = explanation,
            Footer = $"{AnimationSpeed.Label(AnimationSpeed.Min)} is normal speed, up to {AnimationSpeed.Label(AnimationSpeed.Max)}.",
        });

        Button down = MakeStepButton(row, "<");

        var valueGo = new GameObject("Value", typeof(RectTransform));
        valueGo.transform.SetParent(row, false);
        valueGo.GetComponent<RectTransform>().sizeDelta = new Vector2(StepValueWidth, RowHeight);
        FixedRowWidth(valueGo, StepValueWidth, StepValueWidth);
        var valueTmp = valueGo.AddComponent<TextMeshProUGUI>();
        valueTmp.font      = font;
        valueTmp.fontSize  = 18;
        valueTmp.fontStyle = FontStyles.Bold;
        valueTmp.color     = new Color(0.88f, 0.92f, 0.97f);
        valueTmp.alignment = TextAlignmentOptions.Center;

        Button up = MakeStepButton(row, ">");

        void Refresh()
        {
            float value = AnimationSpeed.Get(kind);
            valueTmp.text = AnimationSpeed.Label(value);
            SetStepUsable(down, value > AnimationSpeed.Min);
            SetStepUsable(up, value < AnimationSpeed.Max);
        }

        void Change(int steps)
        {
            AnimationSpeed.Set(kind, AnimationSpeed.Get(kind) + steps * AnimationSpeed.Step);
            SaveManager.Save();
            Refresh();
        }

        down.onClick.AddListener(() => Change(-1));
        up.onClick.AddListener(() => Change(1));
        Refresh();
        controlRefreshers.Add(Refresh);
    }

    const float StepButtonWidth = 44f;
    const float StepValueWidth  = 80f;

    Button MakeStepButton(Transform row, string text)
    {
        var go = new GameObject($"Step_{text}", typeof(RectTransform));
        go.transform.SetParent(row, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(StepButtonWidth, ChoiceHeight);
        FixedRowWidth(go, StepButtonWidth, StepButtonWidth);
        var img = go.AddComponent<Image>();
        img.color = GameTheme.ElectricBlue;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = labelRt.offsetMax = Vector2.zero;
        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.font      = font;
        tmp.fontSize  = 20;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color     = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.text      = text;

        // Mouse-only, like the subnet slot buttons — an Enter press must not
        // keep stepping whichever arrow was clicked last.
        return MakeMouseOnlyButton(go, img);
    }

    static void SetStepUsable(Button b, bool usable)
    {
        b.interactable = usable;
        b.image.color = usable ? GameTheme.ElectricBlue : ChoiceIdle;
    }

    void MakeSectionHeader(Transform parent, string text)
    {
        var rowGo = new GameObject($"Header_{text}", typeof(RectTransform));
        rowGo.transform.SetParent(parent, false);
        var rowRt = rowGo.GetComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(0f, 30f);

        var tmp = rowGo.AddComponent<TextMeshProUGUI>();
        tmp.font      = font;
        tmp.fontSize  = 18;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color     = GameTheme.Info;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.text      = text;
    }

    /// <summary>Builds one "Label ─────slider───── 80%" row, wired to call
    /// onChanged live as it's dragged and to flush the save once per drag (on
    /// pointer-up) rather than on every one of the many onValueChanged events a
    /// single drag fires — see AudioManager/MusicManager.SetVolume's own note.
    /// minValue/maxValue default to a plain 0-1 fraction (volume sliders); the
    /// UI Scale slider passes a 0.8-1.5 range instead — the same "value*100%"
    /// readout still reads correctly either way (1.0 -> "100%"). get reads the
    /// saved value, now and on every RefreshControls.</summary>
    void MakeSliderRow(Transform parent, string label, System.Func<float> get, System.Action<float> onChanged,
        float minValue = 0f, float maxValue = 1f)
    {
        Transform row = MakeRow(parent, label);
        float initialValue = get();

        var sliderGo = new GameObject("Slider", typeof(RectTransform));
        sliderGo.transform.SetParent(row, false);
        sliderGo.GetComponent<RectTransform>().sizeDelta = new Vector2(SliderWidth, RowHeight);
        FixedRowWidth(sliderGo, SliderMinWidth, SliderWidth);
        var slider = BuildSliderVisuals(sliderGo, initialValue, minValue, maxValue);

        var valueGo = new GameObject("Value", typeof(RectTransform));
        valueGo.transform.SetParent(row, false);
        valueGo.GetComponent<RectTransform>().sizeDelta = new Vector2(ValueWidth, RowHeight);
        FixedRowWidth(valueGo, ValueWidth, ValueWidth);
        var valueTmp = valueGo.AddComponent<TextMeshProUGUI>();
        valueTmp.font      = font;
        valueTmp.fontSize  = 18;
        valueTmp.color     = new Color(0.88f, 0.92f, 0.97f);
        valueTmp.alignment = TextAlignmentOptions.MidlineLeft;
        valueTmp.text      = Mathf.RoundToInt(initialValue * 100f) + "%";

        slider.onValueChanged.AddListener(v =>
        {
            valueTmp.text = Mathf.RoundToInt(v * 100f) + "%";
            onChanged(v);
        });

        // Slider has no built-in "drag ended" event — EventTrigger's PointerUp
        // fires once per release regardless of how many onValueChanged ticks
        // happened during the drag, which is exactly the single flush point
        // AudioManager/MusicManager.SetVolume's in-memory-only update needs.
        var trigger = sliderGo.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        entry.callback.AddListener(_ => SaveManager.Save());
        trigger.triggers.Add(entry);

        // The plain .value setter, not SetValueWithoutNotify — the percentage text
        // only updates from onValueChanged, so SetValueWithoutNotify left it stale
        // (caught via screenshot: the UI Scale slider sat at 150% while its label
        // read "100%"). Re-running onChanged is harmless: Slider no-ops on an
        // unchanged value, and every onChanged here is idempotent.
        controlRefreshers.Add(() => slider.value = get());
    }

    /// <summary>Builds one "Label  [ Enabled ][ Disabled ]" row for an on/off
    /// setting: two buttons, the current choice lit. Clicking one calls set
    /// (which saves it); get reads the saved value, now and on every
    /// RefreshControls.</summary>
    void MakeToggleRow(Transform parent, string label, System.Func<bool> get, System.Action<bool> set)
    {
        Transform row = MakeRow(parent, label);
        Button on  = MakeChoiceButton(row, "Enabled");
        Button off = MakeChoiceButton(row, "Disabled");

        void Refresh()
        {
            bool value = get();
            on.image.color  = value ? GameTheme.ElectricBlue : ChoiceIdle;
            off.image.color = value ? ChoiceIdle : GameTheme.ElectricBlue;
        }

        on.onClick.AddListener(() => { set(true); Refresh(); });
        off.onClick.AddListener(() => { set(false); Refresh(); });
        Refresh();
        controlRefreshers.Add(Refresh);
    }

    Button MakeChoiceButton(Transform row, string text)
    {
        var go = new GameObject($"Choice_{text}", typeof(RectTransform));
        go.transform.SetParent(row, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(ChoiceWidth, ChoiceHeight);
        FixedRowWidth(go, ChoiceMinWidth, ChoiceWidth);
        var img = go.AddComponent<Image>();

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = labelRt.offsetMax = Vector2.zero;
        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.font      = font;
        tmp.fontSize  = 16;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color     = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.text      = text;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        return btn;
    }

    /// <summary>An empty settings row with its label in place — controls are
    /// appended after it. The row's HorizontalLayoutGroup controls child widths
    /// (via each child's LayoutElement, see FixedRowWidth) so a row can never be
    /// wider than the content column: label + slider + value + spacing is 552
    /// at full size, wider than the dialog's 520 column — with fixed widths a
    /// row overflowed 16 units past both edges, which the scroll viewport's
    /// RectMask2D then clipped (first letter of every label cut off). Now only
    /// a row's flexible control (a slider bar, a choice button) gives up width,
    /// so labels stay aligned across rows.</summary>
    Transform MakeRow(Transform parent, string label)
    {
        var rowGo = new GameObject($"Row_{label}", typeof(RectTransform));
        rowGo.transform.SetParent(parent, false);
        rowGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, RowHeight);

        var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.spacing                = 16f;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = false;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(rowGo.transform, false);
        labelGo.GetComponent<RectTransform>().sizeDelta = new Vector2(LabelWidth, RowHeight);
        FixedRowWidth(labelGo, LabelWidth, LabelWidth);
        var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
        labelTmp.font      = font;
        labelTmp.fontSize  = 18;
        labelTmp.color     = new Color(0.88f, 0.92f, 0.97f);
        labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
        labelTmp.text      = label;
        // One line always — a longer label ("Tutorial Narration") shrinks a
        // little to fit LabelWidth rather than wrapping into the row below.
        labelTmp.textWrappingMode = TextWrappingModes.NoWrap;
        labelTmp.enableAutoSizing = true;
        labelTmp.fontSizeMin      = 13f;
        labelTmp.fontSizeMax      = 18f;

        return rowGo.transform;
    }

    /// <summary>Width the row's HorizontalLayoutGroup gives this child: preferred
    /// when the row has room, squeezed toward min when it doesn't. Explicit, so
    /// a TextMeshProUGUI's own ILayoutElement (preferred = its text's width)
    /// can't make labels size to their text and misalign the slider column.</summary>
    static void FixedRowWidth(GameObject go, float minWidth, float preferredWidth)
    {
        var le = go.AddComponent<LayoutElement>();
        le.minWidth       = minWidth;
        le.preferredWidth = preferredWidth;
        le.flexibleWidth  = 0f;
    }

    /// <summary>Standard Unity UI Slider hierarchy (Background / Fill Area / Fill /
    /// Handle Slide Area / Handle), built by hand since this project has no Slider
    /// prefab to instantiate — every other runtime-built control here is a plain
    /// Image, so this mirrors that same "just AddComponent it" convention.</summary>
    Slider BuildSliderVisuals(GameObject sliderGo, float initialValue, float minValue = 0f, float maxValue = 1f)
    {
        var slider = sliderGo.AddComponent<Slider>();
        slider.direction   = Slider.Direction.LeftToRight;
        slider.minValue    = minValue;
        slider.maxValue    = maxValue;
        slider.wholeNumbers = false;

        var bgGo = new GameObject("Background", typeof(RectTransform));
        bgGo.transform.SetParent(sliderGo.transform, false);
        var bgImg = bgGo.AddComponent<Image>();
        bgImg.color = new Color(0.10f, 0.13f, 0.19f);
        var bgRt = bgGo.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0.35f);
        bgRt.anchorMax = new Vector2(1f, 0.65f);
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;

        var fillAreaGo = new GameObject("Fill Area", typeof(RectTransform));
        fillAreaGo.transform.SetParent(sliderGo.transform, false);
        var fillAreaRt = fillAreaGo.GetComponent<RectTransform>();
        fillAreaRt.anchorMin = new Vector2(0f, 0.35f);
        fillAreaRt.anchorMax = new Vector2(1f, 0.65f);
        fillAreaRt.offsetMin = new Vector2(4f, 0f);
        fillAreaRt.offsetMax = new Vector2(-4f, 0f);

        var fillGo = new GameObject("Fill", typeof(RectTransform));
        fillGo.transform.SetParent(fillAreaGo.transform, false);
        var fillImg = fillGo.AddComponent<Image>();
        fillImg.color = GameTheme.Info;
        var fillRt = fillGo.GetComponent<RectTransform>();
        fillRt.anchorMin = new Vector2(0f, 0f);
        fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.sizeDelta = Vector2.zero;

        var handleAreaGo = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleAreaGo.transform.SetParent(sliderGo.transform, false);
        var handleAreaRt = handleAreaGo.GetComponent<RectTransform>();
        handleAreaRt.anchorMin = Vector2.zero;
        handleAreaRt.anchorMax = Vector2.one;
        handleAreaRt.offsetMin = new Vector2(10f, 0f);
        handleAreaRt.offsetMax = new Vector2(-10f, 0f);

        var handleGo = new GameObject("Handle", typeof(RectTransform));
        handleGo.transform.SetParent(handleAreaGo.transform, false);
        var handleImg = handleGo.AddComponent<Image>();
        handleImg.color = new Color(0.92f, 0.94f, 0.98f);
        var handleRt = handleGo.GetComponent<RectTransform>();
        // Slider.UpdateVisuals sets the handle's own anchors (not just position) —
        // both anchorMin/anchorMax.y get stretched to (0,1) and .x pinned to the
        // value point — so sizeDelta means "offset from that stretch", not an
        // absolute size: sizeDelta.y=0 keeps it exactly row-height tall (not
        // row-height-PLUS-18, which is what a literal 18 produced — an oversized
        // handle bridging two rows, caught via an in-editor screenshot). Matches
        // Unity's own default Slider prefab's Handle sizeDelta convention.
        handleRt.sizeDelta = new Vector2(18f, 0f);

        slider.targetGraphic = handleImg;
        slider.fillRect       = fillRt;
        slider.handleRect     = handleRt;
        slider.SetValueWithoutNotify(initialValue);

        return slider;
    }

    // ── Subnet Colors section ────────────────────────────────────────────────
    // A global, order-based palette shared by every level — the first subnet
    // configured in any level uses slot 0, the second slot 1, and so on (see
    // SubnetColorRegistry). Starts at 5 customizable slots; '+' / '−' add or
    // remove slots rather than resizing the panel to fit — a ScrollRect
    // handles however many slots exist instead. Recoloring a slot mid-level
    // repaints the scene immediately (SaveManager.SubnetColorsChanged).

    const float SwatchSize    = 32f;
    const float SubnetRowH    = 40f;
    const float AddButtonSize = 32f;

    /// <summary>Builds the header, scrollable slot list, and '−' / '+' buttons;
    /// returns the scroll content Transform (see RebuildSubnetRows) that this
    /// surface's rows live under.</summary>
    Transform BuildSubnetSection(Transform content, float viewportHeight)
    {
        MakeSectionHeader(content, "SUBNET COLORS");

        var listContent = MakeScrollList(content, viewportHeight);

        var slotCountRowGo = new GameObject("SlotCountRow", typeof(RectTransform));
        slotCountRowGo.transform.SetParent(content, false);
        slotCountRowGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, AddButtonSize + 6f);

        const float gap = 12f;
        MakeSlotCountButton(slotCountRowGo.transform, "RemoveSlotButton", "-", -(AddButtonSize + gap) / 2f,
            new Color(0.30f, 0.32f, 0.38f), SaveManager.RemoveSubnetColorSlot);
        MakeSlotCountButton(slotCountRowGo.transform, "AddSlotButton", "+", (AddButtonSize + gap) / 2f,
            new Color(0.20f, 0.45f, 0.85f), () => SaveManager.AddSubnetColorSlot());

        return listContent;
    }

    /// <summary>One of the '−' / '+' slot-count buttons, xOffset from the row's center.</summary>
    void MakeSlotCountButton(Transform row, string name, string text, float xOffset, Color color,
        System.Action changeSlotCount)
    {
        var btnGo = new GameObject(name, typeof(RectTransform));
        btnGo.transform.SetParent(row, false);
        var btnRt = btnGo.GetComponent<RectTransform>();
        // Centered anchor, not left-edge — the row spans the content's full
        // width (see MakeContentContainer's childForceExpandWidth), so a
        // left-edge anchor would pin these to the far-left of that width
        // instead of the visual center of the panel.
        btnRt.anchorMin = new Vector2(0.5f, 0.5f);
        btnRt.anchorMax = new Vector2(0.5f, 0.5f);
        btnRt.pivot     = new Vector2(0.5f, 0.5f);
        btnRt.anchoredPosition = new Vector2(xOffset, 0f);
        btnRt.sizeDelta = new Vector2(AddButtonSize, AddButtonSize);
        var btnImg = btnGo.AddComponent<Image>();
        btnImg.color = color;

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(btnGo.transform, false);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.font = font; label.fontSize = 20; label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center; label.color = Color.white; label.text = text;
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero; labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero; labelRt.offsetMax = Vector2.zero;

        var btn = MakeMouseOnlyButton(btnGo, btnImg);
        btn.onClick.AddListener(() =>
        {
            changeSlotCount();
            RebuildSubnetRows(dialogSubnetList);
            RebuildSubnetRows(fullScreenSubnetList);
            // Without this, ScrollRect computes its scrollable range from the
            // content's pre-rebuild height until a deferred layout pass catches
            // up next frame — see ForceRelayout's doc comment for the same class
            // of bug on first-open.
            ForceRelayout(dialogSubnetList);
            ForceRelayout(fullScreenSubnetList);
        });
    }

    /// <summary>A Button that only reacts to actual clicks. With the default
    /// (Automatic) navigation, a clicked Button becomes the EventSystem's selected
    /// object, so every later Enter press (UI Submit) clicks it again, and
    /// WASD/arrow keys (UI Navigate — also the camera pan keys) can walk the
    /// selection onto a neighbouring button — for '+' that silently added a slot
    /// per Enter press. Navigation.Mode.None stops Selectable from selecting on
    /// click at all, so keyboard input never reaches these buttons.</summary>
    static Button MakeMouseOnlyButton(GameObject go, Graphic target)
    {
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = target;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        return btn;
    }

    /// <summary>Standard Unity UI ScrollRect hierarchy (Viewport+Mask / Content with
    /// a VerticalLayoutGroup) sized to viewportHeight — taller than that many rows
    /// scrolls instead of growing the panel, per request.</summary>
    Transform MakeScrollList(Transform parent, float viewportHeight)
    {
        var scrollGo = new GameObject("SubnetScroll", typeof(RectTransform));
        scrollGo.transform.SetParent(parent, false);
        scrollGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, viewportHeight);
        // Nested — passes the wheel on to the settings body's own scroll view
        // (MakeContentContainer) once this list is at its top/bottom.
        var scrollRect = scrollGo.AddComponent<NestedScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical   = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        var viewportGo = new GameObject("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        var viewportRt = viewportGo.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = Vector2.zero;
        viewportRt.offsetMax = Vector2.zero;
        // RectMask2D clips by RectTransform bounds alone — it needs no Graphic to
        // clip against (unlike the older Mask component), so no Image here.
        viewportGo.AddComponent<RectMask2D>();

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewportGo.transform, false);
        var contentRt = contentGo.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot     = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        // A freshly created RectTransform defaults sizeDelta to (100,100), not
        // (0,0) — for a horizontally *stretched* anchor (anchorMin.x=0,
        // anchorMax.x=1, as set above), that leftover 100 does NOT get treated as
        // "ignore it", it ADDS 100px of extra width split across the stretch (50px
        // overflow on each side). That silently made Content 620px wide against a
        // 520px viewport, positioning every row 50px further left than the
        // viewport's own clip rect — Unity's RectMask2D then culled every row's
        // swatch as fully outside the mask (caught by comparing GetWorldCorners of
        // Content vs Viewport directly; visually it read as "swatches never
        // render", not as a positioning issue, which is why it took hierarchy
        // inspection rather than eyeballing the screenshot to find). Zeroing it
        // out makes this a true edge-to-edge stretch.
        contentRt.sizeDelta = Vector2.zero;

        var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment     = TextAnchor.UpperCenter;
        vlg.spacing            = 4f;
        vlg.childControlWidth  = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewportRt;
        scrollRect.content  = contentRt;

        return contentGo.transform;
    }

    /// <summary>Destroys and rebuilds every slot row under listContent from current
    /// save data — called on every RefreshControls (surface open) and after any
    /// change (add slot, pick a color) so both surfaces' lists never go stale.</summary>
    void RebuildSubnetRows(Transform listContent)
    {
        if (listContent == null) return;

        for (int i = listContent.childCount - 1; i >= 0; i--)
            Destroy(listContent.GetChild(i).gameObject);

        int slotCount = Mathf.Max(1, SaveManager.Settings.subnetColorSlotCount);
        for (int slot = 0; slot < slotCount; slot++)
            BuildSubnetRow(listContent, slot);
    }

    void BuildSubnetRow(Transform parent, int slotIndex)
    {
        var rowGo = new GameObject($"Slot_{slotIndex}", typeof(RectTransform));
        rowGo.transform.SetParent(parent, false);
        rowGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, SubnetRowH);

        var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment        = TextAnchor.MiddleLeft;
        hlg.spacing                = 16f;
        hlg.childControlWidth      = false;
        hlg.childControlHeight     = false;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;

        var swatchGo = new GameObject("Swatch", typeof(RectTransform));
        swatchGo.transform.SetParent(rowGo.transform, false);
        swatchGo.GetComponent<RectTransform>().sizeDelta = new Vector2(SwatchSize, SwatchSize);
        var swatchImg = swatchGo.AddComponent<Image>();
        swatchImg.color = GameTheme.SlotColor(slotIndex);
        var swatchBtn = MakeMouseOnlyButton(swatchGo, swatchImg);
        swatchBtn.onClick.AddListener(() => OpenColorPicker(slotIndex));

        MakeSubnetRowText(rowGo.transform, "Label", $"Subnet {slotIndex + 1}", new Color(0.88f, 0.92f, 0.97f));

        // In a level, name the subnet this slot is coloring right now so the
        // player can tell which row to recolor. Empty on the main menu (no
        // level loaded — GameBootstrap resets the registry on the way out).
        // Shown with its prefix ("192.168.1.0/24") — two subnets can share a
        // network address and differ only by mask, each with its own slot.
        if (SubnetColorRegistry.TryGetSubnet(slotIndex, out Subnet subnet))
            MakeSubnetRowText(rowGo.transform, "InUse", subnet.ToString(), new Color(0.55f, 0.62f, 0.72f));
    }

    void MakeSubnetRowText(Transform row, string name, string text, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(row, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(LabelWidth, SubnetRowH);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font      = font;
        tmp.fontSize  = 18;
        tmp.color     = color;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.text      = text;
    }

    // ── Color picker popup (shared by both surfaces) ────────────────────────

    GameObject colorPickerRoot;
    int pickerTargetSlot = -1;

    void BuildColorPickerPopup()
    {
        var canvasGo = new GameObject("ColorPicker_Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 70; // above both the dialog (65) and full-screen (20) — only one is ever open

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);
        canvasGo.AddComponent<GraphicRaycaster>();

        colorPickerRoot = canvasGo;

        // Invisible full-screen catcher — click anywhere outside the grid to
        // dismiss it, same idea as InterfacePickerPopup's own click-away close.
        var catcherGo = new GameObject("Catcher", typeof(RectTransform));
        catcherGo.transform.SetParent(canvasGo.transform, false);
        var catcherImg = catcherGo.AddComponent<Image>();
        catcherImg.color = new Color(0f, 0f, 0f, 0.35f);
        var catcherRt = catcherGo.GetComponent<RectTransform>();
        catcherRt.anchorMin = Vector2.zero; catcherRt.anchorMax = Vector2.one;
        catcherRt.offsetMin = Vector2.zero; catcherRt.offsetMax = Vector2.zero;
        var catcherBtn = catcherGo.AddComponent<Button>();
        catcherBtn.targetGraphic = catcherImg;
        catcherBtn.onClick.AddListener(ClosePickerPopup);
        catcherGo.AddComponent<SilentUIButton>(); // not a button to the player — no hover/click sound

        const int columns = 4;
        const float cell = 56f;
        int rows = Mathf.CeilToInt(GameTheme.PresetPickerColors.Length / (float)columns);
        var gridSize = new Vector2(columns * cell, rows * cell);

        var panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        var panelImg = panelGo.AddComponent<Image>();
        panelImg.sprite = PlateSpriteBuilder.GetSprite(PanelFill, PanelBorder, PanelTrim,
            borderThickness: 3, chamfer: 14, pixelScale: 4);
        panelImg.type = Image.Type.Sliced;
        var panelRt = panelGo.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot     = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = gridSize + new Vector2(32f, 32f);

        var gridGo = new GameObject("Grid", typeof(RectTransform));
        gridGo.transform.SetParent(panelGo.transform, false);
        var gridRt = gridGo.GetComponent<RectTransform>();
        gridRt.anchorMin = new Vector2(0.5f, 0.5f);
        gridRt.anchorMax = new Vector2(0.5f, 0.5f);
        gridRt.pivot     = new Vector2(0.5f, 0.5f);
        gridRt.sizeDelta = gridSize;
        var grid = gridGo.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(cell - 8f, cell - 8f);
        grid.spacing  = new Vector2(8f, 8f);
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;

        foreach (var color in GameTheme.PresetPickerColors)
        {
            var swatchGo = new GameObject("Preset", typeof(RectTransform));
            swatchGo.transform.SetParent(gridGo.transform, false);
            var swatchImg = swatchGo.AddComponent<Image>();
            swatchImg.color = color;
            var swatchBtn = swatchGo.AddComponent<Button>();
            swatchBtn.targetGraphic = swatchImg;
            swatchBtn.onClick.AddListener(() => PickColor(color));
        }
    }

    void OpenColorPicker(int slotIndex)
    {
        pickerTargetSlot = slotIndex;
        colorPickerRoot.SetActive(true);
    }

    void ClosePickerPopup()
    {
        pickerTargetSlot = -1;
        colorPickerRoot.SetActive(false);
    }

    void PickColor(Color color)
    {
        if (pickerTargetSlot < 0) return;
        SaveManager.SetSubnetSlotColor(pickerTargetSlot, ColorUtility.ToHtmlStringRGB(color));
        RebuildSubnetRows(dialogSubnetList);
        RebuildSubnetRows(fullScreenSubnetList);
        ClosePickerPopup();
    }
}
