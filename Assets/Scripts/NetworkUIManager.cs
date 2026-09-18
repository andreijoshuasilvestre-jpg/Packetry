using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Builds the game HUD:
///   - Left sidebar with device/cable buttons
///   - Top-center Return/Home/Settings/Exit bar
/// The bottom status bar's visible bar/text was removed per request (see
/// BuildStatusBar) — its label still exists, just hidden, because Update()
/// also uses it to drive the sidebar buttons' forced-active glow.
/// </summary>
public class NetworkUIManager : MonoBehaviour
{
    public static NetworkUIManager Instance { get; private set; }

    private TextMeshProUGUI statusLabel;
    private Button cableBtn;
    private SidebarIconButtonFX cableFx;
    private Button removeCableBtn;
    private SidebarIconButtonFX removeCableFx;
    private Button removeDeviceBtn;
    private SidebarIconButtonFX removeDeviceFx;
    private Button tableBtn;
    private SidebarIconButtonFX tableFx;
    private Button pingBtn;
    private SidebarIconButtonFX pingFx;
    private Button sendPacketBtn;
    private SidebarIconButtonFX sendPacketFx;
    private readonly System.Collections.Generic.Dictionary<DeviceType, SidebarIconButtonFX> deviceFx = new();

    private static readonly Color SidebarBg = new Color(0.06f, 0.08f, 0.13f, 0.95f);

    // Each sidebar button's artwork (Assets/Resources/UIIcons/) already bakes in its
    // own box + icon + label — these tints are only used for the click glow, so
    // each button's pulse still reads as "belonging" to its device/action type.
    private static readonly Color IconPC      = new Color(0.35f, 0.75f, 0.95f, 1f);
    private static readonly Color IconSwitch  = new Color(0.40f, 0.85f, 0.55f, 1f);
    private static readonly Color IconRouter  = new Color(0.95f, 0.70f, 0.30f, 1f);
    private static readonly Color IconCable   = new Color(0.55f, 0.75f, 1.00f, 1f);
    private static readonly Color IconRemove  = new Color(0.95f, 0.40f, 0.40f, 1f);
    private static readonly Color IconRemoveDevice = new Color(0.95f, 0.55f, 0.30f, 1f);
    private static readonly Color IconTable   = new Color(0.80f, 0.65f, 0.40f, 1f);
    private static readonly Color IconPing    = new Color(0.35f, 0.95f, 0.80f, 1f);
    private static readonly Color IconSendPacket = new Color(0.80f, 0.55f, 0.95f, 1f);

    // Top-bar (Return/Home/Settings/Exit) click-glow tints — pick out each
    // icon's own artwork color (blue arrow, green roof, silver gear, red X).
    private static readonly Color IconReturn   = new Color(0.35f, 0.65f, 0.95f, 1f);
    private static readonly Color IconHome     = new Color(0.40f, 0.85f, 0.55f, 1f);
    private static readonly Color IconSettings = new Color(0.75f, 0.78f, 0.85f, 1f);
    private static readonly Color IconExit     = new Color(0.90f, 0.30f, 0.30f, 1f);

    private enum ButtonKind { Device, Cable, RemoveCable, RemoveDevice, Table, Ping, SendPacket }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        BuildCanvas();
    }

    // ── Public accessors (TutorialManager/Level2Guide/Level3Guide: gray out disallowed buttons, anchor highlights) ──

    public Button GetDeviceButton(DeviceType t) => deviceFx.TryGetValue(t, out var fx) ? fx.GetComponent<Button>() : null;
    public RectTransform GetDeviceButtonRect(DeviceType t) => deviceFx.TryGetValue(t, out var fx) ? fx.transform as RectTransform : null;
    public Button CableButton => cableBtn;
    public RectTransform CableButtonRect => cableBtn != null ? cableBtn.transform as RectTransform : null;
    public Button TableButton => tableBtn;
    public RectTransform TableButtonRect => tableBtn != null ? tableBtn.transform as RectTransform : null;
    public Button PingButton => pingBtn;
    public RectTransform PingButtonRect => pingBtn != null ? pingBtn.transform as RectTransform : null;
    public Button SendPacketButton => sendPacketBtn;
    public Button RemoveCableButton => removeCableBtn;
    public Button RemoveDeviceButton => removeDeviceBtn;

    void BuildCanvas()
    {
        var canvasGo = new GameObject("HUD_Canvas");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);

        canvasGo.AddComponent<GraphicRaycaster>();

        BuildSidebar(canvasGo.transform);
        BuildStatusBar(canvasGo.transform);
        BuildTopBar(canvasGo.transform);
    }

    /// <summary>
    /// Adds a SlidePanel to rt, configured for whichever edge rt is pinned
    /// nearest to, and immediately plays its slide-in — the game HUD's
    /// entrance, mirroring the main menu's own slide-in (see SlidePanel)
    /// but approaching from each panel's own margin instead of always
    /// from the bottom.
    /// </summary>
    static void SlideInFromNearestMargin(RectTransform rt, float distance)
    {
        var slide = rt.gameObject.AddComponent<SlidePanel>();
        slide.Configure(SlidePanel.EdgeFromAnchor(rt), distance);
        slide.SlideIn();
    }

    // ── Left Sidebar ─────────────────────────────────────────────────────────
    // A plain fixed-size vertical list (Unity ScrollRect + VerticalLayoutGroup).
    // Buttons sit at a constant size — SidebarButtonScale, carried over from what
    // used to be the old coverflow carousel's "focused" scale — instead of
    // growing/shrinking as they scroll. ScrollRect.movementType = Clamped stops
    // the list flush against its first/last button, so there's never empty space
    // above PC or below Remove Device.

    const float SidebarWidth       = 300f;  // doubled from the original 150 baseline
    const float ButtonInset        = 0.08f; // fraction of sidebar width trimmed off each side
    const float SidebarButtonWidth = SidebarWidth * (1f - 2f * ButtonInset); // base button width
    const float SidebarButtonScale = 1.18f * 0.8f; // was the carousel's max (focused) scale, now 20% smaller
    const float HeaderZoneHeight   = 74f;
    const float StatusBarHeight    = 32f;
    const string IconResourceFolder = "UIIcons/";

    void BuildSidebar(Transform canvas)
    {
        // Header backdrop
        var panel = new GameObject("SidebarHeader");
        panel.transform.SetParent(canvas, false);
        var bg = panel.AddComponent<Image>();
        bg.color = SidebarBg;

        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0, 1);
        rt.anchorMax        = new Vector2(0, 1);
        rt.pivot            = new Vector2(0, 1);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = new Vector2(SidebarWidth, HeaderZoneHeight);
        SlideInFromNearestMargin(rt, SidebarWidth);

        // Header label
        var headerGo = new GameObject("Header");
        headerGo.transform.SetParent(panel.transform, false);
        var headerTmp = headerGo.AddComponent<TextMeshProUGUI>();
        headerTmp.text      = "DEVICES";
        headerTmp.fontSize  = 20;
        headerTmp.color     = new Color(0.50f, 0.65f, 0.90f, 1f);
        headerTmp.alignment = TextAlignmentOptions.Center;
        headerTmp.fontStyle = FontStyles.Bold;
        var hrt = headerGo.GetComponent<RectTransform>();
        hrt.anchorMin        = new Vector2(0, 1);
        hrt.anchorMax        = new Vector2(1, 1);
        hrt.pivot            = new Vector2(0.5f, 1);
        hrt.anchoredPosition = new Vector2(0, -18);
        hrt.sizeDelta        = new Vector2(0, 28);

        // Separator line
        var sep = new GameObject("Separator");
        sep.transform.SetParent(panel.transform, false);
        var sepImg = sep.AddComponent<Image>();
        sepImg.color = new Color(0.30f, 0.45f, 0.70f, 0.5f);
        var srt = sep.GetComponent<RectTransform>();
        srt.anchorMin        = new Vector2(0.1f, 1);
        srt.anchorMax        = new Vector2(0.9f, 1);
        srt.pivot            = new Vector2(0.5f, 1);
        srt.anchoredPosition = new Vector2(0, -50);
        srt.sizeDelta        = new Vector2(0, 1);

        // Scroll view — everything below the header. No background of its own so
        // it stays visually seamless with the header strip above it.
        var scrollGo = new GameObject("SidebarScrollView", typeof(RectTransform));
        scrollGo.transform.SetParent(canvas, false);
        var scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin = new Vector2(0, 0);
        scrollRt.anchorMax = new Vector2(0, 1);
        scrollRt.pivot     = new Vector2(0, 0.5f);
        scrollRt.offsetMin = new Vector2(0, StatusBarHeight + 8f);
        scrollRt.offsetMax = new Vector2(SidebarWidth, -HeaderZoneHeight);
        SlideInFromNearestMargin(scrollRt, SidebarWidth);

        var viewportGo = new GameObject("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        var vrt = viewportGo.GetComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.offsetMin = Vector2.zero;
        vrt.offsetMax = Vector2.zero;
        viewportGo.AddComponent<RectMask2D>();
        var vImg = viewportGo.AddComponent<Image>();
        vImg.color         = new Color(0, 0, 0, 0f); // invisible; keeps the whole area (incl. gaps) a raycast/scroll target
        vImg.raycastTarget = true;

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewportGo.transform, false);
        var crt = contentGo.GetComponent<RectTransform>();
        crt.anchorMin        = new Vector2(0, 1);
        crt.anchorMax        = new Vector2(1, 1);
        crt.pivot             = new Vector2(0.5f, 1);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta        = Vector2.zero;

        var layout = contentGo.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment          = TextAnchor.UpperCenter;
        layout.childControlWidth       = false;
        layout.childControlHeight      = false;
        layout.childForceExpandWidth   = false;
        layout.childForceExpandHeight  = false;
        layout.spacing                 = 10f;
        layout.padding                 = new RectOffset(0, 0, 10, 10);

        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scrollRect = scrollGo.AddComponent<SmoothScrollRect>();
        scrollRect.content           = crt;
        scrollRect.viewport          = vrt;
        scrollRect.horizontal        = false;
        scrollRect.vertical          = true;
        scrollRect.movementType      = ScrollRect.MovementType.Clamped; // hard stop at both ends — no bounce, no empty space
        scrollRect.scrollSensitivity = 200f;

        // Button entries: (label, click-glow tint, kind, normal icon sprite name, hover/clicked icon sprite name)
        // Sprite names resolve to Assets/Resources/UIIcons/<name>.png (see UIIconImporter for import rules).
        // Each sprite is a complete button graphic (box + icon + baked-in label) — the
        // "_Clicked" variant is the same artwork with its box lit up, reused as the hover state.
        var entries = new (string label, Color glow, ButtonKind kind, string iconSprite, string hoverSprite)[]
        {
            ("PC",     IconPC,     ButtonKind.Device, "PC_Icon",     "PC_Icon_Clicked"),
            ("Switch", IconSwitch, ButtonKind.Device, "Switch_Icon", "Switch_Icon_Clicked"),
            ("Router", IconRouter, ButtonKind.Device, "Router_Icon", "Router_Icon_Clicked"),
            ("Table",  IconTable,  ButtonKind.Table,  "Table",       "Table_Icon_Clicked"),
            ("Cable",  IconCable,  ButtonKind.Cable,  "Cable",       "Cable_Icon_Clicked"),
            ("Ping",   IconPing,   ButtonKind.Ping,   "Ping",        "Ping_Icon_Clicked"),
            ("Send Packet", IconSendPacket, ButtonKind.SendPacket, "SendPackets",     "SendPacket_Icon_Clicked"),
            ("Remove Cable",  IconRemove,       ButtonKind.RemoveCable,  "RemoveCable_Icon",  "RemoveCable_Icon_Clicked"),
            ("Remove Device", IconRemoveDevice, ButtonKind.RemoveDevice, "RemoveDevice_Icon", "RemoveDevice_Icon_Clicked"),
        };

        float buttonWidth = SidebarButtonWidth * SidebarButtonScale;

        foreach (var (label, glow, kind, iconSprite, hoverSprite) in entries)
        {
            var (btn, fx) = CreateSidebarButton(contentGo.transform, label, glow, buttonWidth, iconSprite, hoverSprite);

            switch (kind)
            {
                case ButtonKind.Cable:
                    cableBtn = btn; cableFx = fx;
                    btn.onClick.AddListener(OnCableClicked);
                    break;
                case ButtonKind.RemoveCable:
                    removeCableBtn = btn; removeCableFx = fx;
                    btn.onClick.AddListener(OnRemoveCableClicked);
                    break;
                case ButtonKind.RemoveDevice:
                    removeDeviceBtn = btn; removeDeviceFx = fx;
                    btn.onClick.AddListener(OnRemoveDeviceClicked);
                    break;
                case ButtonKind.Table:
                    tableBtn = btn; tableFx = fx;
                    btn.onClick.AddListener(OnTableClicked);
                    break;
                case ButtonKind.Ping:
                    pingBtn = btn; pingFx = fx;
                    btn.onClick.AddListener(OnPingClicked);
                    break;
                case ButtonKind.SendPacket:
                    sendPacketBtn = btn; sendPacketFx = fx;
                    btn.onClick.AddListener(OnSendPacketClicked);
                    break;
                default:
                    DeviceType dt = System.Enum.Parse<DeviceType>(label);
                    deviceFx[dt] = fx;
                    btn.onClick.AddListener(() => DragDropManager.Instance?.StartDragging(dt));
                    break;
            }
        }
    }

    /// <summary>
    /// Builds a sidebar button whose entire look — box, icon, and label — comes
    /// from a single baked sprite (see UI Assets). The button itself is an empty
    /// container so the glow can render behind the artwork and the artwork can
    /// swap wholesale on hover; height is derived from the sprite's own aspect
    /// ratio so it never gets stretched. Positioning is left entirely to the
    /// parent's VerticalLayoutGroup — this just sets the button's fixed size.
    /// </summary>
    (Button, SidebarIconButtonFX) CreateSidebarButton(Transform parent, string label, Color glowColor,
                                                        float width,
                                                        string iconSpriteName, string hoverSpriteName)
    {
        var iconSprite  = Resources.Load<Sprite>(IconResourceFolder + iconSpriteName);
        var hoverSprite = Resources.Load<Sprite>(IconResourceFolder + hoverSpriteName);

        // No art authored yet for this device (see BuildSidebar's entries list)
        // — previously this silently left iconImg.sprite null,
        // which Unity's Image renders as a blank white box. Draw a procedural
        // placeholder (DeviceIconFallbackBuilder) plus a plate background and
        // baked label instead, so a missing icon still reads as a real button
        // rather than an empty rectangle — same fallback path for any future
        // device added before its art exists.
        bool missingArt = iconSprite == null;
        if (missingArt)
            iconSprite = hoverSprite = DeviceIconFallbackBuilder.GetSprite(glowColor);

        float aspect = !missingArt ? iconSprite.rect.width / iconSprite.rect.height : 1.6f;
        float height = width / aspect;

        // Explicit RectTransform type: this GameObject gets no Graphic component
        // of its own (Button is added later), so nothing would otherwise trigger
        // the usual Transform -> RectTransform upgrade before we need it below.
        var go = new GameObject("Btn_" + label, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        // Fixed size, centered pivot — the VerticalLayoutGroup on Content drives
        // anchoredPosition every layout pass; this is just the button's own size.
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 1f);
        rt.anchorMax        = new Vector2(0.5f, 1f);
        rt.pivot            = new Vector2(0.5f, 1f);
        rt.sizeDelta        = new Vector2(width, height);

        // Glow (soft halo behind the artwork, hidden until clicked)
        var glowGo = new GameObject("Glow");
        glowGo.transform.SetParent(go.transform, false);
        var glowImg = glowGo.AddComponent<Image>();
        glowImg.raycastTarget = false;
        var grt = glowGo.GetComponent<RectTransform>();
        grt.anchorMin        = new Vector2(0.5f, 0.5f);
        grt.anchorMax        = new Vector2(0.5f, 0.5f);
        grt.pivot            = new Vector2(0.5f, 0.5f);
        grt.anchoredPosition = new Vector2(0, height * 0.12f);
        grt.sizeDelta        = new Vector2(width * 0.9f, height * 0.75f);

        // Missing-art fallback only: a light chrome plate standing in for the
        // real PNG's own baked-in box, so the glyph below doesn't float bare
        // on the sidebar's dark background — see the comment above missingArt.
        if (missingArt)
        {
            var plateGo = new GameObject("Plate");
            plateGo.transform.SetParent(go.transform, false);
            var plateImg = plateGo.AddComponent<Image>();
            plateImg.sprite = PlateSpriteBuilder.GetSprite(
                new Color32(210, 212, 218, 255), new Color32(70, 90, 115, 255), new Color32(20, 24, 32, 255),
                borderThickness: 2, chamfer: 10, pixelScale: 3);
            plateImg.type = Image.Type.Sliced;
            var prt = plateGo.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;
        }

        // Icon (the full button graphic; swaps to its "_Clicked" variant on hover)
        var iconGo = new GameObject("Icon");
        iconGo.transform.SetParent(go.transform, false);
        var iconImg = iconGo.AddComponent<Image>();
        iconImg.sprite = iconSprite;
        iconImg.preserveAspect = true;
        var irt = iconGo.GetComponent<RectTransform>();
        irt.anchorMin = Vector2.zero;
        irt.anchorMax = Vector2.one;
        irt.offsetMin = Vector2.zero;
        irt.offsetMax = Vector2.zero;

        // Missing-art fallback only: real icon art bakes its label into the
        // sprite itself (see this method's doc comment) — the placeholder
        // glyph doesn't, so bake one on top here instead, and shrink the
        // glyph up into the remaining space so the two don't overlap. A dark
        // band sits behind the label (matching the real icons' own bottom
        // label bar) since the fallback plate above is light grey — white
        // text directly on that plate was barely legible.
        if (missingArt)
        {
            irt.offsetMin = new Vector2(0f, height * 0.28f);

            var labelBgGo = new GameObject("LabelBg");
            labelBgGo.transform.SetParent(go.transform, false);
            var labelBgImg = labelBgGo.AddComponent<Image>();
            labelBgImg.color = new Color(0.09f, 0.14f, 0.22f, 1f);
            var lbgRt = labelBgGo.GetComponent<RectTransform>();
            lbgRt.anchorMin = new Vector2(0f, 0f);
            lbgRt.anchorMax = new Vector2(1f, 0f);
            lbgRt.pivot     = new Vector2(0.5f, 0f);
            lbgRt.anchoredPosition = Vector2.zero;
            lbgRt.sizeDelta = new Vector2(-4f, height * 0.26f); // inset 2px each side so the outer plate's rounded edges still show

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.text      = label;
            labelTmp.fontSize  = 13;
            labelTmp.color     = new Color(0.90f, 0.94f, 1.00f, 1f);
            labelTmp.alignment = TextAlignmentOptions.Center;
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 0f);
            lrt.pivot     = new Vector2(0.5f, 0f);
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = new Vector2(0f, height * 0.26f);
        }

        var btn = go.AddComponent<Button>();
        btn.transition    = Selectable.Transition.None; // FX component owns all visual feedback
        btn.targetGraphic = iconImg;

        var fx = go.AddComponent<SidebarIconButtonFX>();
        fx.iconImage    = iconImg;
        fx.glowImage    = glowImg;
        fx.normalSprite = iconSprite;
        fx.hoverSprite  = hoverSprite;
        fx.glowColor    = glowColor;
        fx.Init();
        btn.onClick.AddListener(fx.PlayClickPunch);

        return (btn, fx);
    }

    // ── Status Bar ───────────────────────────────────────────────────────────

    // The visible bar/text (the bottom "tips") was removed per request — but
    // statusLabel itself is still built and kept alive, invisible, because
    // Update() below both writes its .text AND drives the sidebar buttons'
    // forced-active glow (cableFx/pingFx/etc.) behind the same
    // "if (statusLabel == null) return;" guard. Killing statusLabel entirely
    // would silently disable that glow tracking too, so the bar is hidden via
    // SetActive(false) instead of not being built at all.
    void BuildStatusBar(Transform canvas)
    {
        var bg = new GameObject("StatusBar");
        bg.transform.SetParent(canvas, false);
        var bgImg  = bg.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.45f);
        var brt = bg.GetComponent<RectTransform>();
        brt.anchorMin        = new Vector2(0, 0);
        brt.anchorMax        = new Vector2(1, 0);
        brt.pivot            = new Vector2(0.5f, 0);
        brt.anchoredPosition = Vector2.zero;
        brt.sizeDelta        = new Vector2(0, StatusBarHeight);

        var labelGo = new GameObject("StatusLabel");
        labelGo.transform.SetParent(bg.transform, false);
        statusLabel           = labelGo.AddComponent<TextMeshProUGUI>();
        statusLabel.text      = "Select a device from the sidebar to place it";
        statusLabel.fontSize  = 14;
        statusLabel.color     = new Color(0.80f, 0.90f, 1.00f, 0.90f);
        statusLabel.alignment = TextAlignmentOptions.Center;

        var rt = labelGo.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(SidebarWidth + 10f, 0);
        rt.offsetMax = Vector2.zero;

        bg.SetActive(false);
    }

    // ── Top Bar (Return / Home / Settings / Exit) ───────────────────────────
    // A small icon-only row, top-center of the screen — each button's whole
    // look (chamfered box + glyph) is a single baked sprite, same convention
    // as the sidebar's device icons (see CreateSidebarButton), just without a
    // baked-in label since these read fine as pure glyphs at this size.

    const float TopBarButtonWidth = 72f;
    const float TopBarSpacing     = 14f;
    const float TopBarMarginTop   = 14f;

    void BuildTopBar(Transform canvas)
    {
        var barGo = new GameObject("TopBar", typeof(RectTransform));
        barGo.transform.SetParent(canvas, false);
        var rt = barGo.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 1f);
        rt.anchorMax        = new Vector2(0.5f, 1f);
        rt.pivot            = new Vector2(0.5f, 1f);
        // Centered on the full screen (x=0 at this 0.5-anchor) per request —
        // previously offset right by half the sidebar's width to center on
        // the play area instead (screen minus the left sidebar).
        rt.anchoredPosition = new Vector2(0f, -TopBarMarginTop);
        SlideInFromNearestMargin(rt, TopBarButtonWidth + 40f);

        var layout = barGo.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment         = TextAnchor.UpperCenter;
        layout.childControlWidth      = false;
        layout.childControlHeight     = false;
        layout.childForceExpandWidth  = false;
        layout.childForceExpandHeight = false;
        layout.spacing                = TopBarSpacing;

        var fitter = barGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        // (label, click-glow tint, icon sprite, hover sprite, click handler)
        var entries = new (string label, Color glow, string iconSprite, string hoverSprite, UnityEngine.Events.UnityAction onClick)[]
        {
            ("Return",   IconReturn,   "Return",   "Return_Hovered",   OnReturnClicked),
            ("Home",     IconHome,     "Home",     "Home_Hovered",     OnHomeClicked),
            ("Settings", IconSettings, "Settings", "Settings_Hovered", OnSettingsClicked),
            ("Exit",     IconExit,     "Exit",     "Exit_Hovered",     OnExitClicked),
        };

        foreach (var (label, glow, iconSprite, hoverSprite, onClick) in entries)
        {
            var (btn, _) = CreateSidebarButton(barGo.transform, label, glow, TopBarButtonWidth, iconSprite, hoverSprite);
            btn.onClick.AddListener(onClick);
        }
    }

    void OnReturnClicked()
    {
        ConfirmDialogManager.Instance?.Show(
            "Restart this level from the beginning?",
            "Restart",
            () =>
            {
                int level = LevelManager.Instance != null ? LevelManager.Instance.CurrentLevel : LevelManager.DevSandboxLevel;
                SaveManager.ClearLevelProgress(level);
                LevelManager.PendingLevelNumber = level;
                LoadingScreenManager.LoadScene(SceneManager.GetActiveScene().name, "RELOADING LEVEL");
            });
    }

    void OnHomeClicked()
    {
        ConfirmDialogManager.Instance?.Show(
            "Return to the main menu?",
            "Return to Menu",
            () =>
            {
                MainMenuBootstrap.OpenLevelSelectOnLoad = true;
                LoadingScreenManager.LoadScene("MainMenu", "LOADING LEVEL SELECT");
            });
    }

    void OnSettingsClicked()
    {
        SettingsPanelManager.Instance?.OpenDialog();
    }

    void OnExitClicked()
    {
        ConfirmDialogManager.Instance?.Show(
            "Quit the game?",
            "Quit",
            () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });
    }

    // ── Events ───────────────────────────────────────────────────────────────

    void OnCableClicked()
    {
        CableManager.Instance?.BeginConnecting();
    }

    void OnRemoveCableClicked()
    {
        CableRemoverManager.Instance?.BeginRemoving();
    }

    void OnRemoveDeviceClicked()
    {
        DeviceRemoverManager.Instance?.BeginRemoving();
    }

    void OnTableClicked()
    {
        TableManager.Instance?.BeginPlacing();
    }

    void OnPingClicked()
    {
        PingToolManager.Instance?.BeginPinging();
    }

    void OnSendPacketClicked()
    {
        PacketSendManager.Instance?.BeginSending();
    }

    void Update()
    {
        if (statusLabel == null) return;

        bool dragging        = DragDropManager.Instance      != null && DragDropManager.Instance.IsDragging;
        bool connecting      = CableManager.Instance         != null && CableManager.Instance.IsConnecting;
        bool removingCable   = CableRemoverManager.Instance  != null && CableRemoverManager.Instance.IsRemoving;
        bool removingDevice  = DeviceRemoverManager.Instance != null && DeviceRemoverManager.Instance.IsRemoving;
        bool placingTable    = TableManager.Instance         != null && TableManager.Instance.IsPlacing;
        bool draggingTable   = TableManager.Instance         != null && TableManager.Instance.IsDragging;
        bool pinging         = PingToolManager.Instance      != null && PingToolManager.Instance.IsPinging;
        bool sendingPacket   = PacketSendManager.Instance    != null && PacketSendManager.Instance.IsSending;

        // While a mode is engaged, keep its button showing the lit "_Clicked" artwork
        // (same visual language as hover) so the active tool stays obviously highlighted.
        pingFx?.SetForcedActive(pinging);
        sendPacketFx?.SetForcedActive(sendingPacket);
        cableFx?.SetForcedActive(connecting);
        removeCableFx?.SetForcedActive(removingCable);
        removeDeviceFx?.SetForcedActive(removingDevice);
        tableFx?.SetForcedActive(placingTable || draggingTable);

        // Same treatment for whichever device button (PC/Switch/Router) started the
        // ghost currently being placed/repositioned.
        DeviceType? draggingType = DragDropManager.Instance?.DraggingDeviceType;
        foreach (var (type, fx) in deviceFx)
            fx.SetForcedActive(draggingType == type);

        if (dragging)
        {
            var ddm = DragDropManager.Instance;
            if (ddm != null && ddm.IsRepositioning)
            {
                // Separated from the pickup itself: the hologram/port-arrow
                // indicator already popped up the instant the device was
                // grabbed (see PickUpPlacedDevice) — this hint follows a
                // moment later (ShowRepositionHint) instead of landing on
                // the same frame, so the two reads as distinct beats.
                statusLabel.text = ddm.ShowRepositionHint
                    ? "Holding Device  —  click again to place it down  |  right-click to cancel"
                    : "";
            }
            else
            {
                statusLabel.text = "Placing Device  —  left-click to drop  (PC/Switch/Router must land on a table)";
            }
        }
        else if (connecting)
            statusLabel.text = "Cable Mode  —  click source device, then destination  |  right-click to cancel";
        else if (removingCable)
            statusLabel.text = "Remove Cable Mode  —  click a cable to delete it  |  right-click to cancel";
        else if (removingDevice)
            statusLabel.text = "Remove Device Mode  —  click a device to delete it  |  right-click to cancel";
        else if (placingTable)
            statusLabel.text = "Table Mode  —  click a corner, then the opposite corner  |  right-click to cancel";
        else if (draggingTable)
            statusLabel.text = "Repositioning Table  —  left-click to drop  |  right-click to cancel";
        else if (pinging)
            statusLabel.text = "Ping Mode  —  click source PC, then destination PC  |  right-click to cancel";
        else if (sendingPacket)
        {
            var psm = PacketSendManager.Instance;
            if (!psm.HasSource)
                statusLabel.text = "Send Packet Mode  —  click source PC, then pick a type  |  right-click to cancel";
            else if (psm.QueuedCount == 0)
                statusLabel.text = "Send Packet Mode  —  click one or more destination PCs  |  right-click to cancel";
            else
                statusLabel.text = $"Send Packet Mode  —  {psm.QueuedCount} destination{(psm.QueuedCount == 1 ? "" : "s")} queued, Enter to send  |  click to add more  |  right-click to cancel";
        }
        else
            statusLabel.text = "Select a device from the sidebar  |  use Cable to connect devices  |  right-click a PC to set its address";
    }
}
