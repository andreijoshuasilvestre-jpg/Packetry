using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>The four tools folded into NetworkUIManager's single devices/cable
/// sidebar button — see BuildDevicesCableCompound.</summary>
public enum CompoundToolKind { PC, Switch, Router, Cable }

/// <summary>The three tools folded into NetworkUIManager's single remove
/// sidebar button — see BuildRemoveCompound.</summary>
public enum RemoveToolKind { Cable, Device, Table }

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
    private Button tableBtn;
    private SidebarIconButtonFX tableFx;
    private Button pingBtn;
    private SidebarIconButtonFX pingFx;
    private Button sendPacketBtn;
    private SidebarIconButtonFX sendPacketFx;

    // Run Network isn't a sidebar entry — it's a floating "▶ Run" pill button
    // (same chamfered-plate look as CLIPanelManager's own command-builder Run
    // button), only shown while Packet Assign Mode is active. See
    // BuildRunNetworkButton.
    private GameObject runNetworkGo;
    private Button runNetworkBtn;
    private Image runNetworkImg;

    // Devices/Cable and Remove are each a single sidebar entry that flips
    // through several tools via right-click flyout — see CompoundSidebarGroup
    // and BuildDevicesCableCompound/BuildRemoveCompound.
    private RectTransform hudCanvasRect;
    private CompoundSidebarGroup<CompoundToolKind> devicesGroup;
    private CompoundSidebarGroup<RemoveToolKind> removeGroup;

    // Roots that carry this HUD's entrance SlidePanels (either one on the
    // root itself, like the sidebar header plate, or one per staggered
    // child button — see StaggerSlideInChildren) — kept so SlideOutHud can
    // find and reverse every one of them for the restart/exit-to-menu
    // transitions (see LoadingScreenManager.PreloadSceneCorner callers).
    private RectTransform sidebarHeaderRt;
    private RectTransform sidebarContentRt;
    private RectTransform topBarRt;

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
    private static readonly Color IconRemoveTable  = new Color(0.90f, 0.62f, 0.30f, 1f);
    private static readonly Color IconTable   = new Color(0.80f, 0.65f, 0.40f, 1f);
    private static readonly Color IconPing    = new Color(0.35f, 0.95f, 0.80f, 1f);
    private static readonly Color IconSendPacket = new Color(0.80f, 0.55f, 0.95f, 1f);

    // Run Network button colors — matches CLIPanelManager's own "▶ Run"
    // pill (runFill/runBorder there) so both read as the same "execute"
    // language rather than inventing a new one.
    private static readonly Color RunNetworkFill   = new Color(0.35f, 0.75f, 0.45f, 1f);
    private static readonly Color RunNetworkBorder = new Color(0.15f, 0.45f, 0.22f, 1f);
    private static readonly Color RunNetworkTrim   = new Color(0.02f, 0.03f, 0.05f, 1f);

    // Top-bar (Return/Home/Settings/Exit) click-glow tints — pick out each
    // icon's own artwork color (blue arrow, green roof, silver gear, red X).
    private static readonly Color IconReturn   = new Color(0.35f, 0.65f, 0.95f, 1f);
    private static readonly Color IconHome     = new Color(0.40f, 0.85f, 0.55f, 1f);
    private static readonly Color IconSettings = new Color(0.75f, 0.78f, 0.85f, 1f);
    private static readonly Color IconExit     = new Color(0.90f, 0.30f, 0.30f, 1f);

    private enum ButtonKind { Table, Ping, SendPacket }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // Same pixel font every other game panel (CLIPanelManager, DeviceAddressPopup)
    // already uses — the sidebar header and Run Network button previously fell
    // back to TMP's default LiberationSans SDF, which read as an off-brand
    // font swap against the rest of the game's UI.
    const string PixelFontResourcePath = "Fonts/PixeloidMono SDF";
    TMP_FontAsset pixelFont;

    void Start()
    {
        pixelFont = Resources.Load<TMP_FontAsset>(PixelFontResourcePath);
        BuildCanvas();
    }

    // ── Public accessors (TutorialManager/Level2Guide/Level3Guide: gray out disallowed buttons, anchor highlights) ──

    // PC/Switch/Router/Cable now all live on the same compound button (see
    // BuildDevicesCableCompound) — every one of these accessors resolves to
    // it regardless of which DeviceType is asked for, which is what lets
    // TutorialManager/Level4Guide's existing per-device Highlight targeting
    // keep working unchanged. Same story for Remove Cable/Device/Table below,
    // now folded into the remove compound button (see BuildRemoveCompound).
    public Button GetDeviceButton(DeviceType t) => devicesGroup?.FaceButton;
    public RectTransform GetDeviceButtonRect(DeviceType t) => devicesGroup?.FaceRect;
    public Button CableButton => devicesGroup?.FaceButton;
    public RectTransform CableButtonRect => devicesGroup?.FaceRect;
    public Button TableButton => tableBtn;
    public RectTransform TableButtonRect => tableBtn != null ? tableBtn.transform as RectTransform : null;
    public Button PingButton => pingBtn;
    public RectTransform PingButtonRect => pingBtn != null ? pingBtn.transform as RectTransform : null;
    public Button SendPacketButton => sendPacketBtn;
    public RectTransform SendPacketButtonRect => sendPacketBtn != null ? sendPacketBtn.transform as RectTransform : null;
    public Button RunNetworkButton => runNetworkBtn;
    public RectTransform RunNetworkButtonRect => runNetworkBtn != null ? runNetworkBtn.transform as RectTransform : null;
    public Button RemoveCableButton => removeGroup?.FaceButton;
    public Button RemoveDeviceButton => removeGroup?.FaceButton;
    public Button RemoveTableButton => removeGroup?.FaceButton;
    public RectTransform RemoveButtonRect => removeGroup?.FaceRect;

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
        hudCanvasRect = canvasGo.GetComponent<RectTransform>();

        BuildSidebar(canvasGo.transform);
        BuildStatusBar(canvasGo.transform);
        BuildTopBar(canvasGo.transform);
        BuildRunNetworkButton(canvasGo.transform);

        // Same experimental overlay as the main menu (see CRTOverlay's class
        // doc) — added last so it sits above the sidebar/top bar/status bar.
        CRTOverlay.Build(canvasGo.transform);
    }

    // ── Run Network button ──────────────────────────────────────────────────
    // A floating "▶ Run Network" pill — same chamfered SmallPlate + label
    // construction as CLIPanelManager's own command-builder "▶ Run" button
    // (PlateSpriteBuilder-baked sprite, ButtonClickGlow feedback) rather than
    // another baked-icon sidebar entry, since this isn't a mode toggle like
    // Table/Ping/Send Packet — it's a one-shot "fire what I've assigned"
    // action that only ever matters while Packet Assign Mode is active.
    // Bottom-center, clear of the sidebar. Hidden outside Send Packet mode;
    // dimmed (but visible) whenever there's nothing assigned yet to run.
    static readonly Vector2 RunNetworkSize = new Vector2(220, 40);
    const float RunNetworkBottomMargin = 20f;

    /// <summary>Height (canvas units, up from the screen's bottom edge) of the
    /// bottom-center band this HUD keeps for itself — the Run Network pill,
    /// which stays up for the whole of a run since RunNetwork doesn't leave
    /// Packet Assign Mode. Other bottom-center UI (ActivitySubtitle) stacks
    /// above this instead of copying the pill's layout numbers; every in-game
    /// canvas registers with the same UIScale reference resolution, so the
    /// value carries over between canvases as-is.</summary>
    public static float BottomCenterReservedHeight => RunNetworkBottomMargin + RunNetworkSize.y;

    void BuildRunNetworkButton(Transform canvas)
    {
        runNetworkGo = new GameObject("RunNetworkBtn", typeof(RectTransform));
        runNetworkGo.transform.SetParent(canvas, false);

        // Same chamfered-plate proportions as every other small plate/button
        // in the game (CLIPanelManager.SmallPlate, DeviceAddressPopup.SmallPlate)
        // rather than the chunkier one-off params this used before.
        runNetworkImg = runNetworkGo.AddComponent<Image>();
        runNetworkImg.sprite = PlateSpriteBuilder.GetSprite(RunNetworkFill, RunNetworkBorder, RunNetworkTrim,
            borderThickness: 1, chamfer: 2, pixelScale: 2);
        runNetworkImg.type = Image.Type.Sliced;

        var rt = runNetworkGo.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0f);
        rt.anchorMax        = new Vector2(0.5f, 0f);
        rt.pivot            = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0, RunNetworkBottomMargin);
        rt.sizeDelta        = RunNetworkSize;

        runNetworkBtn = runNetworkGo.AddComponent<Button>();
        runNetworkBtn.targetGraphic = runNetworkImg;
        runNetworkBtn.onClick.AddListener(OnRunNetworkClicked);
        ButtonClickGlow.Attach(runNetworkGo, runNetworkBtn, RunNetworkFill);

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(runNetworkGo.transform, false);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.font      = pixelFont;
        // Plain ASCII, not "▶" — LiberationSans SDF (and its fallbacks) don't
        // have that glyph, so it was rendering as a missing-character box.
        label.text      = "> Run Network";
        label.fontSize  = 18;
        label.fontStyle = FontStyles.Bold;
        label.color     = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;

        runNetworkGo.SetActive(false);
    }

    /// <summary>
    /// Adds a SlidePanel to rt, configured for whichever edge rt is pinned
    /// nearest to, and plays its slide-in once the level's entry sequence
    /// reaches the HUD (GameBootstrap.HudRevealStarted) — the game HUD's
    /// entrance, mirroring the main menu's own slide-in (see SlidePanel) but
    /// approaching from each panel's own margin instead of always from the
    /// bottom. The panel is snapped off-screen (SlidePanel.SnapForSlideIn)
    /// right away and only starts sliding once the HUD stage arrives —
    /// snapping immediately rather than only when the deferred SlideIn itself
    /// runs is what avoids the "sits fully visible, then suddenly pops
    /// off-screen and slides back" glitch SnapForSlideIn's own doc comment
    /// covers in more detail.
    /// </summary>
    void SlideInWithHud(RectTransform rt, float distance)
    {
        var slide = rt.gameObject.AddComponent<SlidePanel>();
        // Duration fixed now, at build time — the Level speed can't change
        // before the HUD stage arrives (Settings opens from the HUD itself).
        slide.Configure(SlidePanel.EdgeFromAnchor(rt), distance, AnimationSpeed.Seconds(AnimationKind.Level, HudSlideInDuration));
        slide.SnapForSlideIn();
        StartCoroutine(SlideInOnHudReveal(slide));
    }

    const float HudSlideInDuration = 0.35f; // SlidePanel's own default, at 1x

    static IEnumerator SlideInOnHudReveal(SlidePanel slide)
    {
        while (!GameBootstrap.HudRevealStarted) yield return null;
        if (slide != null) slide.SlideIn();
    }

    /// <summary>
    /// Slides each active immediate child of a HUD button list in one after
    /// another (small per-child delay) instead of moving the whole list as
    /// one rigid block — the "buttons assembling themselves" entrance used
    /// for the sidebar tool list and top bar (see BuildSidebar/BuildTopBar).
    /// Forces an immediate layout rebuild first so every child's
    /// anchoredPosition is already final before SlidePanel captures it as
    /// its rest position — the owning layout group only repositions children
    /// on its own deferred pass, which would otherwise leave SlidePanel
    /// measuring a stale/default position instead of the real slot.
    /// </summary>
    void StaggerSlideInChildren(RectTransform listRoot, SlideEdge edge, float distance, float staggerDelay = 0.045f)
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(listRoot);

        int i = 0;
        foreach (Transform child in listRoot)
        {
            if (!child.gameObject.activeSelf) continue;
            if (!(child is RectTransform rt)) continue;

            var slide = rt.gameObject.AddComponent<SlidePanel>();
            slide.Configure(edge, distance);

            float delay = i * staggerDelay;
            if (delay <= 0f) slide.SlideIn();
            else StartCoroutine(DelayedSlideIn(slide, delay));
            i++;
        }
    }

    static IEnumerator DelayedSlideIn(SlidePanel slide, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        if (slide != null) slide.SlideIn();
    }

    /// <summary>
    /// Flashes each active immediate child of a HUD button list in one at a
    /// time (see HudButtonRevealFlash: afterimage, then click-glow, then
    /// solid) instead of sliding the whole list in as one block or per-button
    /// — replaces StaggerSlideInChildren for the sidebar tool list and top
    /// bar (see BuildSidebar/BuildTopBar). Buttons never move; they just
    /// flash into existence at their normal laid-out position, so this force-
    /// rebuilds layout up front purely so later reads of the hierarchy are
    /// accurate, not to capture a rest position the way the slide version did.
    /// Nothing flashes until the level's entry sequence reaches the HUD
    /// (GameBootstrap.HudRevealStarted, + HudButtonsDelay so the sidebar
    /// header lands first) — i.e. only once the room AND its starting
    /// topology have finished building in, instead of overlapping them.
    ///
    /// Only reaches active children of listRoot — the devices/cable and
    /// remove sidebar entries are CompoundSidebarGroup flyouts (see
    /// BuildDevicesCableCompound/BuildRemoveCompound), which park every tool
    /// but the current "face" in a separate pool GameObject (not under
    /// listRoot at all) until a right-click flyout reveals them. Those
    /// pooled buttons still start at icon alpha 0 like every other button
    /// (see CreateSidebarButton), but never need the flash treatment
    /// themselves (the flyout has its own pop-in animation) — Compound
    /// SidebarGroup.Build() restores their icon alpha directly as it pools
    /// them, rather than this sweep trying to reach outside its own subtree.
    /// </summary>
    void StaggerRevealFlashChildren(RectTransform listRoot, float staggerDelay = 0.14f)
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(listRoot);
        StartCoroutine(QueueRevealFlash(listRoot, staggerDelay));
    }

    // Which buttons get the flash is decided when the HUD stage arrives, not
    // at construction — TutorialManager/Level2Guide/Level3Guide/Level4Guide's
    // HideUnneededButtons (see their own InitNextFrame) deactivates whatever
    // tools aren't unlocked yet a frame after load, and that has to happen
    // BEFORE the activeInHierarchy check below, or a tool the player hasn't
    // unlocked yet would still flash fully in before disappearing. The HUD
    // stage is seconds after load, so that's always long since done.
    IEnumerator QueueRevealFlash(RectTransform listRoot, float staggerDelay)
    {
        while (!GameBootstrap.HudRevealStarted) yield return null;

        int i = 0;
        foreach (var fx in listRoot.GetComponentsInChildren<SidebarIconButtonFX>(includeInactive: true))
        {
            var icon = fx.transform.Find("Icon")?.GetComponent<Image>();
            if (icon == null) continue;

            if (fx.gameObject.activeInHierarchy)
            {
                float delay = AnimationSpeed.Seconds(AnimationKind.Level, GameBootstrap.HudButtonsDelay + i * staggerDelay);
                StartCoroutine(DelayedRevealFlash(fx, icon, delay));
                i++;
            }
            else
            {
                var c = icon.color;
                icon.color = new Color(c.r, c.g, c.b, 1f);
            }
        }
    }

    static IEnumerator DelayedRevealFlash(SidebarIconButtonFX fx, Image icon, float delay)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        if (fx != null && icon != null) yield return HudButtonRevealFlash.Play(fx, icon);
    }

    /// <summary>
    /// Slides the sidebar header back out and fades every HUD button away —
    /// the reverse of this HUD's own entrance. The first stage of
    /// GameBootstrap.PlayExitSequence's fuller reverse (HUD, then topology,
    /// then walls, then platform) on the Home-button exit to the menu and on
    /// a restart.
    /// </summary>
    public void SlideOutHud(Action onComplete)
    {
        var headerSlide = sidebarHeaderRt != null ? sidebarHeaderRt.GetComponent<SlidePanel>() : null;
        float buttonFadeTotal = PlayButtonExitFade(AnimationSpeed.Seconds(AnimationKind.Level, ExitButtonFadeDuration));

        if (headerSlide == null) { StartCoroutine(InvokeAfter(buttonFadeTotal, onComplete)); return; }

        bool headerDone = false, buttonsDone = false;
        headerSlide.SlideOut(() => { headerDone = true; if (buttonsDone) onComplete?.Invoke(); }, AnimationSpeed.Seconds(AnimationKind.Level, ExitHeaderSlideDuration));
        StartCoroutine(InvokeAfter(buttonFadeTotal, () => { buttonsDone = true; if (headerDone) onComplete?.Invoke(); }));
    }

    static IEnumerator InvokeAfter(float delay, Action action)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        action?.Invoke();
    }

    // ── Button exit fade ─────────────────────────────────────────────────────
    // The reverse of HudButtonRevealFlash's entrance — buttons never had a
    // per-button SlidePanel to reverse once the entrance switched from
    // sliding to the afterimage/glow flash, so exiting used to just leave
    // every button in place until the scene swap yanked it away. A plain
    // fade (not a reversed flash — that flourish reads as an arrival, not a
    // goodbye) staggered in reverse tab order.
    //
    // All of the HUD's exit timings are deliberately much quicker than its
    // entrance — leaving (or restarting) a level is something a player does
    // over and over, so it should get out of the way, not perform.
    const float ExitButtonFadeDuration  = 0.12f;
    const float ExitButtonStagger       = 0.02f;
    const float ExitHeaderSlideDuration = 0.18f; // vs. its 0.35s slide-in

    /// <summary>Fades every active sidebar/top-bar button icon to alpha 0,
    /// staggered. Returns the total time (seconds) until the last one
    /// finishes, so a caller orchestrating a longer sequence
    /// (GameBootstrap.PlayExitSequence) knows how long to wait.</summary>
    public float PlayButtonExitFade(float fadeDuration)
    {
        var allFx = new List<SidebarIconButtonFX>();
        if (sidebarContentRt != null) allFx.AddRange(sidebarContentRt.GetComponentsInChildren<SidebarIconButtonFX>(includeInactive: false));
        if (topBarRt != null) allFx.AddRange(topBarRt.GetComponentsInChildren<SidebarIconButtonFX>(includeInactive: false));

        float stagger = AnimationSpeed.Seconds(AnimationKind.Level, ExitButtonStagger);
        for (int i = 0; i < allFx.Count; i++)
        {
            var icon = allFx[i].transform.Find("Icon")?.GetComponent<Image>();
            if (icon != null) StartCoroutine(FadeIconOut(icon, fadeDuration, i * stagger));
        }

        return allFx.Count > 0 ? (allFx.Count - 1) * stagger + fadeDuration : 0f;
    }

    static IEnumerator FadeIconOut(Image icon, float duration, float delay)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        if (icon == null) yield break;

        Color from = icon.color;
        Color to = new Color(from.r, from.g, from.b, 0f);
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            if (icon == null) yield break;
            icon.color = Color.Lerp(from, to, Mathf.Clamp01(t / duration));
            yield return null;
        }
        icon.color = to;
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
        // First piece of the HUD stage, slightly ahead of the buttons (see
        // GameBootstrap.HudButtonsDelay), not at frame 0 — sliding in
        // immediately meant the "TOOLS" label was already sitting there for
        // the whole blank/platform/wall/topology beat before anything else
        // showed up.
        SlideInWithHud(rt, SidebarWidth);
        sidebarHeaderRt = rt;

        // Header label
        var headerGo = new GameObject("Header");
        headerGo.transform.SetParent(panel.transform, false);
        var headerTmp = headerGo.AddComponent<TextMeshProUGUI>();
        headerTmp.font      = pixelFont;
        headerTmp.text      = "TOOLS";
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
        // Individual buttons stagger themselves in (see the StaggerSlideInChildren
        // call at the end of this method) instead of this whole container sliding
        // in as one block.

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
        layout.spacing                 = 16f; // extra room over the old 10f so SidebarIconButtonFX's hover scale-up doesn't touch neighboring buttons
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
            ("Table",  IconTable,  ButtonKind.Table,  "Table",       "Table_Icon_Clicked"),
            ("Ping",   IconPing,   ButtonKind.Ping,   "Ping",        "Ping_Icon_Clicked"),
            ("Send Packet", IconSendPacket, ButtonKind.SendPacket, "SendPackets",     "SendPacket_Icon_Clicked"),
        };

        float buttonWidth = SidebarButtonWidth * SidebarButtonScale;

        BuildDevicesCableCompound(contentGo.transform, buttonWidth);

        foreach (var (label, glow, kind, iconSprite, hoverSprite) in entries)
        {
            var (btn, fx) = CreateSidebarButton(contentGo.transform, label, glow, buttonWidth, iconSprite, hoverSprite);

            switch (kind)
            {
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
            }
        }

        BuildRemoveCompound(contentGo.transform, buttonWidth);

        // Commented out as part of the level-entry transition rework — was
        // the per-button slide-in; replaced by StaggerRevealFlashChildren's
        // afterimage-then-glow-then-solid flash below (see HudButtonRevealFlash).
        // StaggerSlideInChildren(crt, SlideEdge.Left, SidebarWidth);
        StaggerRevealFlashChildren(crt);
        sidebarContentRt = crt;
    }

    // ── Devices + Cable / Remove compound buttons ────────────────────────────
    // Each folds several sidebar entries into one CompoundSidebarGroup (see
    // that class): a single button shows whichever tool is currently "the
    // face" — left-click uses it immediately — and right-click reveals the
    // others as a horizontal flyout past the sidebar's right edge. Both
    // groups are built the same way: create every tool's real button via the
    // usual CreateSidebarButton, wire its own domain action, then hand it to
    // the group with Register(); Build() parks everything but the initial
    // face in an inactive pool until the flyout borrows it.

    void BuildDevicesCableCompound(Transform listParent, float buttonWidth)
    {
        devicesGroup = new CompoundSidebarGroup<CompoundToolKind>(this, hudCanvasRect);

        var art = new (CompoundToolKind kind, string label, Color glow, string iconSprite, string hoverSprite)[]
        {
            (CompoundToolKind.PC,     "PC",     IconPC,     "PC_Icon",     "PC_Icon_Clicked"),
            (CompoundToolKind.Switch, "Switch", IconSwitch, "Switch_Icon", "Switch_Icon_Clicked"),
            (CompoundToolKind.Router, "Router", IconRouter, "Router_Icon", "Router_Icon_Clicked"),
            (CompoundToolKind.Cable,  "Cable",  IconCable,  "Cable",       "Cable_Icon_Clicked"),
        };

        foreach (var (kind, label, glow, iconSprite, hoverSprite) in art)
        {
            var (btn, fx) = CreateSidebarButton(listParent, label, glow, buttonWidth, iconSprite, hoverSprite);

            if (kind == CompoundToolKind.Cable)
                btn.onClick.AddListener(OnCableClicked);
            else
            {
                DeviceType dt = kind == CompoundToolKind.PC ? DeviceType.PC
                              : kind == CompoundToolKind.Switch ? DeviceType.Switch
                              : DeviceType.Router;
                // Press-and-hold, not onClick: the device ghost spawns the
                // moment the button goes down and the player drags it out onto
                // the field, dropping it on release (see DragDropManager.Update).
                // Face swap happens here too, since a release out on the field
                // never reaches the button's own onClick → SelectFace.
                CompoundToolKind k = kind;
                btn.gameObject.AddComponent<LeftPressHandler>().onLeftPress = () =>
                {
                    DragDropManager.Instance?.StartDragging(dt);
                    devicesGroup?.SelectFace(k);
                };
            }

            devicesGroup.Register(kind, btn, fx);
        }

        devicesGroup.Build(listParent, CompoundToolKind.PC);
    }

    void BuildRemoveCompound(Transform listParent, float buttonWidth)
    {
        removeGroup = new CompoundSidebarGroup<RemoveToolKind>(this, hudCanvasRect);

        var art = new (RemoveToolKind kind, string label, Color glow, string iconSprite, string hoverSprite)[]
        {
            (RemoveToolKind.Cable,  "Remove Cable",  IconRemove,       "RemoveCable_Icon",  "RemoveCable_Icon_Clicked"),
            (RemoveToolKind.Device, "Remove Device", IconRemoveDevice, "RemoveDevice_Icon", "RemoveDevice_Icon_Clicked"),
            (RemoveToolKind.Table,  "Remove Table",  IconRemoveTable,  "RemoveTable",       "RemoveTable_Hovered"),
        };

        foreach (var (kind, label, glow, iconSprite, hoverSprite) in art)
        {
            var (btn, fx) = CreateSidebarButton(listParent, label, glow, buttonWidth, iconSprite, hoverSprite);

            switch (kind)
            {
                case RemoveToolKind.Cable:  btn.onClick.AddListener(OnRemoveCableClicked);  break;
                case RemoveToolKind.Device: btn.onClick.AddListener(OnRemoveDeviceClicked); break;
                case RemoveToolKind.Table:  btn.onClick.AddListener(OnRemoveTableClicked);  break;
            }

            removeGroup.Register(kind, btn, fx);
        }

        removeGroup.Build(listParent, RemoveToolKind.Cable);
    }

    /// <summary>Called by TutorialManager/Level4Guide: forcedTool == a value forces
    /// the devices/cable compound button's face to that tool and disables its
    /// flyout (only one tool is allowed this step); null fully dims/disables the
    /// button instead (the step doesn't involve it at all). Never leaves the
    /// flyout reachable while a guided step's callout might be pointing at this
    /// same button — see CompoundSidebarGroup's class comment.</summary>
    public void SetCompoundToolLock(CompoundToolKind? forcedTool)
    {
        devicesGroup.SetLock(forcedTool);
        SidebarButtonLock.SetLocked(devicesGroup.FaceButton, !forcedTool.HasValue);
    }

    /// <summary>Called by TutorialManager for the one step that teaches the
    /// right-click flyout itself (TutorialStep.TeachDeviceFlyout): unlike
    /// SetCompoundToolLock, this leaves the button fully free — any of the
    /// four tools reachable, flyout included — instead of forcing one.</summary>
    public void SetCompoundToolUnrestricted()
    {
        devicesGroup.SetUnrestricted();
        SidebarButtonLock.SetLocked(devicesGroup.FaceButton, false);
    }

    /// <summary>Called by TutorialManager: forcedTool == a value forces the
    /// remove compound button's face to that tool and disables its flyout;
    /// null dims/disables the button instead. Mirrors SetCompoundToolLock —
    /// see its doc for why the flyout is disabled rather than left open.</summary>
    public void SetRemoveToolLock(RemoveToolKind? forcedTool)
    {
        removeGroup.SetLock(forcedTool);
        SidebarButtonLock.SetLocked(removeGroup.FaceButton, !forcedTool.HasValue);
    }

    /// <summary>Called by TutorialManager for the steps that teach the remove
    /// button's own right-click flyout (TutorialStep.TeachRemoveFlyout):
    /// mirrors SetCompoundToolUnrestricted — leaves the button fully free
    /// (Cable/Device/Table all reachable, flyout included) instead of forcing
    /// one.</summary>
    public void SetRemoveToolUnrestricted()
    {
        removeGroup.SetUnrestricted();
        SidebarButtonLock.SetLocked(removeGroup.FaceButton, false);
    }

    /// <summary>True while the devices/cable compound button's right-click
    /// flyout is open AND settled (its pop-in animation has finished — see
    /// CompoundSidebarGroup.FlyoutSettled) — polled every frame by
    /// TutorialManager's FlyoutRevealGate to know when it's safe to retarget
    /// its highlight from the face button onto a specific flyout icon
    /// without corrupting that icon's scale (see TutorialStep.
    /// FlyoutRevealGate). Deliberately NOT just "is it open" — attaching a
    /// continuous scale-pulse highlight while an item is still mid pop-in
    /// would capture its in-progress near-zero scale and freeze it there.</summary>
    public bool IsDevicesFlyoutOpen => devicesGroup != null && devicesGroup.FlyoutSettled;

    /// <summary>Same as IsDevicesFlyoutOpen, for the remove compound button.</summary>
    public bool IsRemoveFlyoutOpen => removeGroup != null && removeGroup.FlyoutSettled;

    /// <summary>The RectTransform of one specific tool folded into the
    /// devices/cable compound button — stable regardless of whether that tool
    /// is currently the visible face or pooled/parked in the flyout. Used by
    /// TutorialManager.FlyoutItemTarget to highlight a specific flyout icon
    /// once the flyout is open (see GetDeviceButtonRect/CableButtonRect,
    /// which instead always resolve to whatever the CURRENT face is).</summary>
    public RectTransform GetCompoundToolRect(CompoundToolKind kind) => devicesGroup?.GetToolRect(kind);

    /// <summary>Same as GetCompoundToolRect, for the remove compound button.</summary>
    public RectTransform GetRemoveToolRect(RemoveToolKind kind) => removeGroup?.GetToolRect(kind);

    /// <summary>Which tool the devices/cable compound button currently shows
    /// (null before it's built).</summary>
    public CompoundToolKind? DevicesFace => devicesGroup?.Face;

    /// <summary>Same as DevicesFace, for the remove compound button.</summary>
    public RemoveToolKind? RemoveFace => removeGroup?.Face;

    /// <summary>The arrow tab beside the devices/cable compound button that
    /// opens its flyout (see FlyoutArrowTab). Only on screen while the flyout
    /// is available, i.e. not while a guided step has locked the button.</summary>
    public RectTransform DevicesFlyoutArrowRect => devicesGroup?.ArrowRect;

    /// <summary>Same as DevicesFlyoutArrowRect, for the remove compound button.</summary>
    public RectTransform RemoveFlyoutArrowRect => removeGroup?.ArrowRect;

    /// <summary>Disables both compound buttons' flyouts (tucking their arrow
    /// tabs away) without dimming or locking the buttons themselves. For a
    /// guide's HideUnneededButtons, which runs before the HUD reveal: its
    /// real per-step locks only start once the intro finishes, so without
    /// this an arrow would pop in with its button and then vanish again a
    /// moment later.</summary>
    public void DisableCompoundFlyouts()
    {
        devicesGroup?.SetLock(null);
        removeGroup?.SetLock(null);
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

        // Real icon PNGs bake in a transparent margin around the plate art (and
        // the label bar beneath it is narrower than the plate above) — trim to
        // the actual visible content's bounding box so glow FX below hugs the
        // button instead of the full padded canvas. Missing-art fallback fills
        // its whole rect on purpose (see missingArt below), so it gets no trim.
        Rect content = !missingArt ? SpriteContentBounds.Get(iconSprite) : new Rect(0f, 0f, 1f, 1f);

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

        // Counts as covering its spot only while actually shown (a flyout
        // item while its flyout is open, a button SidebarButtonLock hasn't
        // hidden), so the tutorial callout steps around whatever's really there.
        OccupiesScreen.Add(go, OccupantLayer.Hud);

        // Glow (soft halo behind the artwork, hidden until clicked) — baked to
        // trace the icon's own alpha silhouette pixel-for-pixel (see
        // SpriteSilhouetteGlow), not just a rectangle approximating its bounds,
        // so it hugs the button's actual edges (chamfered corners, the
        // narrower label stem beneath the plate, etc.) instead of floating
        // past them. Falls back to a rounded-rect ring trimmed to the visible
        // content's bounding box (the old approximation) if the icon's texture
        // isn't readable (missing-art placeholder, or an unreadable import).
        Sprite silhouetteGlow = !missingArt ? SpriteSilhouetteGlow.Get(iconSprite) : null;

        var glowGo = new GameObject("Glow");
        glowGo.transform.SetParent(go.transform, false);
        var glowImg = glowGo.AddComponent<Image>();
        glowImg.raycastTarget = false;
        var grt = glowGo.GetComponent<RectTransform>();
        if (silhouetteGlow != null)
        {
            // Baked at the icon's own resolution with the outward bleed already
            // included — stretch it to exactly the same rect as the icon on
            // top of it so only that bleed peeks out past the icon's edges.
            grt.anchorMin = Vector2.zero;
            grt.anchorMax = Vector2.one;
            grt.offsetMin = Vector2.zero;
            grt.offsetMax = Vector2.zero;
        }
        else
        {
            grt.anchorMin        = new Vector2(0.5f, 0.5f);
            grt.anchorMax        = new Vector2(0.5f, 0.5f);
            grt.pivot            = new Vector2(0.5f, 0.5f);
            grt.anchoredPosition = new Vector2(
                (content.x + content.width * 0.5f - 0.5f) * width,
                (content.y + content.height * 0.5f - 0.5f) * height);
            grt.sizeDelta        = new Vector2(width * content.width * 1.08f, height * content.height * 1.08f);
        }

        // Glow core (tight, hot-white flash layered under/inside the halo
        // above — see SidebarIconButtonFX.ClickPunchRoutine)
        var glowCoreGo = new GameObject("GlowCore");
        glowCoreGo.transform.SetParent(go.transform, false);
        var glowCoreImg = glowCoreGo.AddComponent<Image>();
        glowCoreImg.raycastTarget = false;
        var gcrt = glowCoreGo.GetComponent<RectTransform>();
        gcrt.anchorMin        = new Vector2(0.5f, 0.5f);
        gcrt.anchorMax        = new Vector2(0.5f, 0.5f);
        gcrt.pivot            = new Vector2(0.5f, 0.5f);
        gcrt.anchoredPosition = new Vector2(
            (content.x + content.width * 0.5f - 0.5f) * width,
            (content.y + content.height * 0.5f - 0.5f) * height + height * 0.12f);
        gcrt.sizeDelta        = new Vector2(width * content.width * 0.4f, height * content.height * 0.35f);

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
        // Starts invisible — NetworkUIManager's StaggerRevealFlashChildren
        // (see HudButtonRevealFlash) fades each button in one at a time via
        // this alpha instead of it being visible from frame one.
        iconImg.color = new Color(1f, 1f, 1f, 0f);
        var irt = iconGo.GetComponent<RectTransform>();
        irt.anchorMin = Vector2.zero;
        irt.anchorMax = Vector2.one;
        irt.offsetMin = Vector2.zero;
        irt.offsetMax = Vector2.zero;

        // Icon glow — a duplicate of the same artwork, additively blended and
        // flashed on click (see SidebarIconButtonFX.ClickPunchRoutine), so the
        // PNG itself looks like it's glowing instead of only the halo behind
        // it. Must be a later sibling than Icon so it draws on top — an
        // additive layer sitting *behind* an opaque icon would be fully
        // covered and never show.
        var iconGlowGo = new GameObject("IconGlow");
        iconGlowGo.transform.SetParent(go.transform, false);
        var iconGlowImg = iconGlowGo.AddComponent<Image>();
        iconGlowImg.sprite = iconSprite;
        iconGlowImg.preserveAspect = true;
        iconGlowImg.raycastTarget = false;
        var iglrt = iconGlowGo.GetComponent<RectTransform>();
        iglrt.anchorMin = Vector2.zero;
        iglrt.anchorMax = Vector2.one;
        iglrt.offsetMin = Vector2.zero;
        iglrt.offsetMax = Vector2.zero;

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
            iglrt.offsetMin = irt.offsetMin;

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
        fx.iconImage     = iconImg;
        fx.iconGlowImage = iconGlowImg;
        fx.glowImage     = glowImg;
        fx.glowSilhouette = silhouetteGlow;
        fx.glowCoreImage = glowCoreImg;
        fx.normalSprite  = iconSprite;
        fx.hoverSprite   = hoverSprite;
        fx.glowColor     = glowColor;
        fx.Init();
        btn.onClick.AddListener(fx.PlayClickPunch);

        return (btn, fx);
    }

    // ── Status Bar ───────────────────────────────────────────────────────────

    // The visible bar/text (the bottom "tips") was removed per request — but
    // statusLabel itself is still built and kept alive, invisible, because
    // Update() below both writes its .text AND drives the sidebar buttons'
    // forced-active glow (devicesGroup/removeGroup/pingFx/etc.) behind the same
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
    const float TopBarSpacing     = 18f; // extra room over the old 14f so SidebarIconButtonFX's hover scale-up doesn't touch neighboring buttons
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
        // Individual buttons stagger themselves in (see the StaggerSlideInChildren
        // call at the end of this method) instead of this whole row sliding in as
        // one block.

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

        // Commented out as part of the level-entry transition rework — see
        // the matching comment in BuildSidebar above.
        // StaggerSlideInChildren(rt, SlideEdge.Top, TopBarButtonWidth + 40f);
        StaggerRevealFlashChildren(rt);
        topBarRt = rt;
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
                // The same exit as Home, played while the reload preloads,
                // with the level's background carried through the swap
                // instead of a fade.
                LoadingScreenManager.RestartLevel();
            });
    }

    void OnHomeClicked()
    {
        ConfirmDialogManager.Instance?.Show(
            "Return to the main menu?",
            "Return to Menu",
            LoadingScreenManager.ExitToLevelSelect);
    }

    void OnSettingsClicked()
    {
        SettingsPanelManager.Instance?.OpenDialog();
    }

    void OnExitClicked()
    {
        ConfirmDialogManager.Instance?.ShowQuitPrompt();
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

    void OnRemoveTableClicked()
    {
        TableRemoverManager.Instance?.BeginRemoving();
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

    void OnRunNetworkClicked()
    {
        PacketSendManager.Instance?.RunNetwork();
    }

    void LateUpdate()
    {
        // After SmoothScrollRect's own LateUpdate (base ScrollRect + its glide
        // override both run in LateUpdate) so a flyout tracks this frame's
        // final, post-scroll button position instead of lagging one frame
        // behind while the sidebar glides from a mouse-wheel scroll.
        devicesGroup?.LateTick();
        removeGroup?.LateTick();
    }

    void Update()
    {
        if (statusLabel == null) return;

        bool dragging        = DragDropManager.Instance      != null && DragDropManager.Instance.IsDragging;
        bool connecting      = CableManager.Instance         != null && CableManager.Instance.IsConnecting;
        bool removingCable   = CableRemoverManager.Instance  != null && CableRemoverManager.Instance.IsRemoving;
        bool removingDevice  = DeviceRemoverManager.Instance != null && DeviceRemoverManager.Instance.IsRemoving;
        bool removingTable   = TableRemoverManager.Instance  != null && TableRemoverManager.Instance.IsRemoving;
        bool placingTable    = TableManager.Instance         != null && TableManager.Instance.IsPlacing;
        bool draggingTable   = TableManager.Instance         != null && TableManager.Instance.IsDragging;
        bool resizingTable   = TableManager.Instance         != null && TableManager.Instance.IsResizing;
        bool pinging         = PingToolManager.Instance      != null && PingToolManager.Instance.IsPinging;
        bool sendingPacket   = PacketSendManager.Instance    != null && PacketSendManager.Instance.IsSending;

        GameCursor.Refresh(
            connecting, removingCable, removingDevice, removingTable,
            placingTable || draggingTable || resizingTable, pinging, sendingPacket,
            dragging, DragDropManager.Instance?.DraggingDeviceType);

        // While a mode is engaged, keep its button showing the lit "_Clicked" artwork
        // (same visual language as hover) so the active tool stays obviously highlighted.
        pingFx?.SetForcedActive(pinging);
        sendPacketFx?.SetForcedActive(sendingPacket);
        tableFx?.SetForcedActive(placingTable || draggingTable || resizingTable);

        // The floating Run Network button only exists at all while Packet
        // Assign Mode is active, and stays dimmed (but visible, so it still
        // reads as "the thing you'll eventually press") until there's at
        // least one pairing — committed or still being picked — to fire.
        if (runNetworkGo != null)
        {
            runNetworkGo.SetActive(sendingPacket);
            if (sendingPacket)
            {
                bool hasPendingWork = PacketSendManager.Instance != null && PacketSendManager.Instance.HasPendingWork;
                runNetworkBtn.interactable = hasPendingWork;
                Color c = runNetworkImg.color;
                c.a = hasPendingWork ? 1f : 0.4f;
                runNetworkImg.color = c;
            }
        }

        // Same treatment for the two compound buttons — lit up while any of
        // their folded-in tools is active, whichever the current face happens
        // to be (the face always matches whatever tool was just clicked, so
        // this never needs to check which one specifically).
        devicesGroup?.SetFaceLit(dragging || connecting);
        removeGroup?.SetFaceLit(removingCable || removingDevice || removingTable);

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
                    ? "Holding Device  —  release to place it down  |  right-click to cancel"
                    : "";
            }
            else if (ddm != null && !ddm.IsOverField)
            {
                // Ghost still hidden — not dragged off its button yet, or
                // carried back over the sidebar — so a release here cancels.
                statusLabel.text = "Placing Device  —  drag it out onto a table  |  release here to cancel";
            }
            else
            {
                statusLabel.text = "Placing Device  —  release to drop  (PC/Switch/Router must land on a table)  |  right-click to cancel";
            }
        }
        else if (connecting)
            statusLabel.text = "Cable Mode  —  click source device, then destination  |  right-click to cancel";
        else if (removingCable)
            statusLabel.text = "Remove Cable Mode  —  click a cable to delete it  |  right-click to cancel";
        else if (removingDevice)
            statusLabel.text = "Remove Device Mode  —  click a device to delete it  |  right-click to cancel";
        else if (removingTable)
            statusLabel.text = "Remove Table Mode  —  click a table to delete it  |  right-click to cancel";
        else if (placingTable)
            // Says which of the two separate clicks is next — the first
            // corner, or (once it's fixed) the opposite one — so a player who
            // expects a drag sees the table is sized click by click.
            statusLabel.text = TableManager.Instance.HasOrigin
                ? "Table Mode  —  first corner set: move the mouse, then click again for the opposite corner  (hold Ctrl to snap)  |  right-click to cancel"
                : "Table Mode  —  click once to set the first corner  (hold Ctrl to snap to grid)  |  right-click to cancel";
        else if (draggingTable)
            statusLabel.text = "Repositioning Table  —  left-click to drop  (hold Ctrl to snap to grid)  |  right-click to cancel";
        else if (resizingTable)
        {
            Vector2 size = TableManager.Instance.ResizeSize;
            statusLabel.text = TableManager.Instance.ResizeFits
                ? $"Resizing Table  —  {size.x:0.0} × {size.y:0.0}  —  release to keep this size  (hold Ctrl to snap)  |  right-click to cancel"
                : $"Resizing Table  —  {size.x:0.0} × {size.y:0.0}  —  overlaps something, release to keep the last size that fit  |  right-click to cancel";
        }
        else if (pinging)
            statusLabel.text = "Ping Mode  —  click source PC, then destination PC  |  right-click to cancel";
        else if (sendingPacket)
        {
            var psm = PacketSendManager.Instance;
            if (!psm.HasSource)
                statusLabel.text = psm.AssignmentCount == 0
                    ? "Send Packet Mode  —  click a PC, then pick a type  |  right-click a PC to edit/clear it"
                    : $"Send Packet Mode  —  {psm.AssignmentCount} pairing{(psm.AssignmentCount == 1 ? "" : "s")} ready  |  click a PC to add another  |  Run Network to send";
            else if (psm.QueuedCount == 0)
                statusLabel.text = "Send Packet Mode  —  click one or more destination PCs  |  right-click to edit/clear";
            else
                statusLabel.text = $"Send Packet Mode  —  {psm.QueuedCount} destination{(psm.QueuedCount == 1 ? "" : "s")} queued, Enter to commit  |  click to add more  |  Run Network to send";
        }
        else if (TableManager.Instance != null && TableManager.Instance.HasSelection)
            statusLabel.text = "Table selected  —  drag a corner or edge handle to resize it  |  hold the click on the table to move it";
        else
            statusLabel.text = "Select a device from the sidebar  |  use Cable to connect devices  |  right-click a PC to set its address";
    }
}
