using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Level Complete modal — replaces the old "silently unlock everything in
/// place" behaviour. Shown once a level's completion condition is met (see
/// TutorialManager.CompleteTutorial, Level2Guide, Level3Guide), names what's
/// next, and offers two ways forward:
///   - "View Topology" dismisses the modal (backdrop included) so the player
///     can freely look around the network they just finished — nothing about
///     the scene changes, the modal was just sitting on top of it.
///   - The primary button always sends the player back to the Level Select
///     screen — see MainMenuBootstrap.OpenLevelSelectOnLoad for how that
///     hand-off actually lands on Level Select instead of the main menu root.
/// Choosing "View Topology" doesn't strand the player: a small floating
/// button (same action as the modal's primary button) takes over until
/// they're done looking and ready to head back.
///
/// Built the same self-contained screen-space-overlay singleton way as
/// DeviceAddressPopup. Styled to match TutorialUIManager's annotation
/// callout — same chamfered PlateSpriteBuilder plate (colors are the same
/// dark-navy/steel-blue/near-black trio, just written as Color32 literals
/// here), same PixeloidMono SDF font, and the same BlankButton/BlankButton_
/// Hover + MenuButtonFX glow-and-bounce buttons as the tutorial's "Next"
/// button — rather than flat-color rectangles, so the completion modal reads
/// as the same UI language as the rest of the game's overlays instead of a
/// separate, plainer one. Star rating icons are procedurally drawn
/// (StarIconBuilder) instead of a "★" text glyph — TMP's fallback for a
/// missing glyph in that font rendered as oversized, badly-placed boxes.
/// </summary>
public class LevelCompletePanel : MonoBehaviour
{
    public static LevelCompletePanel Instance { get; private set; }

    const string FontResourcePath = "Fonts/PixeloidMono SDF";
    const string SpriteFolder     = "MainMenuUI/";

    // Matches TutorialUIManager's calloutFill/calloutBorder/calloutTrim (see
    // that class) — same dark-navy plate, steel-blue chamfered border,
    // near-black trim line, just as Color32 literals instead of 0-1 floats.
    static readonly Color32 PanelFill   = new Color32(14, 18, 28, 240);
    static readonly Color32 PanelBorder = new Color32(41, 87, 140, 255);
    static readonly Color32 PanelTrim   = new Color32(5, 8, 13, 255);
    const int PanelBorderThickness = 4;
    const int PanelChamfer         = 22;
    const int PanelPixelScale      = 4;

    // Bottom-center, stacked just above DeviceRotateHud's slot (same baseline
    // and button height as ViewControlsHud) so the two never cover each other
    // when a device gets selected while looking around. The bottom-right
    // corner is ViewControlsHud's, with toasts and the diagram window stacked
    // above it; TaskUIManager owns top-right, NetworkUIManager's sidebar left.
    static float FloatingBackBottom => ViewControlsHud.Margin + HudIconButton.Size + 12f;

    static readonly Color StarFilledColor = StarIconBuilder.EarnedColor;
    static readonly Color StarEmptyColor  = StarIconBuilder.UnearnedColor;

    // Panel entrance/exit — reuses ZoomPanel (already LevelSelectManager's
    // scale-from-0 pop for its own panel) rather than a bespoke coroutine
    // here, so "pop up" / "disappear" mean the exact same animation language
    // across both screens.
    const float StarRevealStartDelay = 0.35f; // roughly ZoomPanel's own duration — first star waits for the panel's own pop to settle before celebrating
    const float StarStaggerGap       = 0.18f; // gap between one star finishing its pop and the next one starting
    const float StarPopDuration      = 0.32f;
    const float StarMinPopScale      = 0.2f;
    const float StarGlowPeak         = 0.85f;
    const float StarGlowPunch        = 1.6f;
    const float StarSize    = StarIconBuilder.DisplaySize; // whole multiple of the star's pixel grid, so every star pixel is an even block
    const float StarSpacing = 8f;
    static readonly Vector2 StarGlowSize = new Vector2(96f, 96f); // ~2.5x the star icon — same "glow bleeds past the shape" ratio MenuButtonFX/PanelRevealFX use

    TMP_FontAsset font;
    Sprite actionNormalSprite;
    Sprite actionHoverSprite;
    Sprite starFilledSprite;
    Sprite starEmptySprite;

    GameObject dimRoot;
    GameObject panelRoot;
    ZoomPanel panelZoom;
    RectTransform sparkParent;
    Camera eventCamera;
    TextMeshProUGUI titleLabel;
    TextMeshProUGUI bodyLabel;
    TextMeshProUGUI continueButtonLabel;
    GameObject floatingBackButtonGo;
    GameObject starsRow;
    StarSlot[] starSlots;
    Coroutine starRevealRoutine;
    Coroutine pendingShow;
    Action onContinue;

    // One star's icon (the sprite that swaps empty->filled) plus its own
    // behind-the-icon glow (see BuildStarsRow) — grouped so PopStar can
    // animate both together without threading two parallel arrays around.
    struct StarSlot
    {
        public RectTransform Rt;
        public Image Icon;
        public Image Glow;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        font               = Resources.Load<TMP_FontAsset>(FontResourcePath);
        actionNormalSprite = Resources.Load<Sprite>(SpriteFolder + "BlankButton");
        actionHoverSprite  = Resources.Load<Sprite>(SpriteFolder + "BlankButton_Hover");
        starFilledSprite   = StarIconBuilder.GetSprite(StarFilledColor);
        starEmptySprite    = StarIconBuilder.GetSprite(StarEmptyColor);

        BuildPanel();
        dimRoot.SetActive(false);
    }

    // stars: 1-3 to show a filled/empty star row (from TaskManager.ComputeStars()
    // at the moment the level completed), or -1 (default) to hide the row
    // entirely — the sentinel keeps this call backward-compatible with any
    // future non-completion caller that has no star rating to report.
    //
    // Holds off while NetworkActivity says packets or pings are still
    // traveling or landing — a level usually completes the instant its last
    // packet is delivered, and popping the modal right then covered the very
    // delivery the player had just earned it with. Shows straight away (same
    // frame) when nothing is in flight. Callers already save progress before
    // calling this, so only the modal itself is deferred.
    public void Show(string title, string body, string continueButtonText, Action onContinueClicked, int stars = -1)
    {
        if (pendingShow != null) StopCoroutine(pendingShow);
        pendingShow = StartCoroutine(ShowWhenNetworkSettles(title, body, continueButtonText, onContinueClicked, stars));
    }

    IEnumerator ShowWhenNetworkSettles(string title, string body, string continueButtonText, Action onContinueClicked, int stars)
    {
        while (NetworkActivity.IsBusy) yield return null;
        pendingShow = null;
        ShowNow(title, body, continueButtonText, onContinueClicked, stars);
    }

    void ShowNow(string title, string body, string continueButtonText, Action onContinueClicked, int stars)
    {
        titleLabel.text          = title;
        bodyLabel.text           = body;
        continueButtonLabel.text = continueButtonText;
        onContinue                = onContinueClicked;

        // Reset any "View Topology" peek state left over from a previous
        // call — Show() always starts a fresh completion flow.
        floatingBackButtonGo.SetActive(false);
        dimRoot.SetActive(true);
        panelZoom.ZoomIn();
        SetStars(stars);
    }

    // Snaps every star to its empty sprite synchronously (so nothing flashes
    // full-bright before its own turn), then staggers the earned ones in one
    // at a time via PopStar — see RevealStarsRoutine.
    void SetStars(int stars)
    {
        starsRow.SetActive(stars >= 0);
        if (starRevealRoutine != null) { StopCoroutine(starRevealRoutine); starRevealRoutine = null; }

        for (int i = 0; i < starSlots.Length; i++)
        {
            starSlots[i].Icon.sprite = starEmptySprite;
            starSlots[i].Icon.rectTransform.localScale = Vector3.one;
            starSlots[i].Glow.color = new Color(StarFilledColor.r, StarFilledColor.g, StarFilledColor.b, 0f);
        }
        if (stars <= 0) return;

        starRevealRoutine = StartCoroutine(RevealStarsRoutine(stars));
    }

    IEnumerator RevealStarsRoutine(int stars)
    {
        yield return new WaitForSecondsRealtime(StarRevealStartDelay);

        for (int i = 0; i < stars && i < starSlots.Length; i++)
        {
            yield return PopStar(starSlots[i]);
            yield return new WaitForSecondsRealtime(StarStaggerGap);
        }
        starRevealRoutine = null;
    }

    // One star's reveal: swap to the filled sprite, punch-scale it in from
    // StarMinPopScale with an ease-out-back overshoot (the same "pop" feel as
    // PingCheckmarkEffect's own icon reveal), and fire a glow flash + UI spark
    // burst in step with it — the exact glow+particle language MenuButtonFX
    // uses for a button click, just replayed per star instead of per press.
    IEnumerator PopStar(StarSlot slot)
    {
        slot.Icon.sprite = starFilledSprite;
        slot.Icon.rectTransform.localScale = Vector3.one * StarMinPopScale;
        AudioManager.Instance?.PlaySfx(SfxId.StarAppear);

        if (sparkParent != null)
            UISparks.EmitBurst(slot.Rt, sparkParent, eventCamera, StarFilledColor);

        float t = 0f;
        while (t < StarPopDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / StarPopDuration);
            slot.Icon.rectTransform.localScale = Vector3.one * Mathf.LerpUnclamped(StarMinPopScale, 1f, EaseOutBack(p));

            float glowFlash = Mathf.Sin(p * Mathf.PI); // 0 -> 1 -> 0, front-loaded flash rather than a glow that tracks the whole pop
            slot.Glow.color = new Color(StarFilledColor.r, StarFilledColor.g, StarFilledColor.b, glowFlash * StarGlowPeak);
            slot.Glow.rectTransform.localScale = Vector3.one * (1f + glowFlash * (StarGlowPunch - 1f));

            yield return null;
        }
        slot.Icon.rectTransform.localScale = Vector3.one;
        slot.Glow.color = new Color(StarFilledColor.r, StarFilledColor.g, StarFilledColor.b, 0f);
        slot.Glow.rectTransform.localScale = Vector3.one;
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    public void Hide()
    {
        // Deactivate the whole modal root (dim backdrop + panel), not just the
        // inner panel — the dim backdrop is a separate full-screen raycast
        // blocker (see BuildPanel) that must come down with it, or it silently
        // eats every click on the entire screen while invisible-ish against
        // the dark background (see TutorialManager's "Next" button repro).
        if (dimRoot != null) dimRoot.SetActive(false);
    }

    // Zooms the panel back out to 0 (mirrors Show()'s zoom-in) before actually
    // hiding the modal root and handing off to onComplete — so both ways the
    // modal can end (View Topology / Continue) read as the panel shrinking
    // away, not just vanishing.
    // The floating back button also lands here after View Topology has
    // already hidden the modal — ZoomOut's coroutine can't start on the
    // inactive panel, so onComplete would silently never fire. Nothing is
    // on screen to animate then anyway, so skip straight to it.
    void HideAnimated(Action onComplete)
    {
        if (!panelRoot.activeInHierarchy)
        {
            Hide();
            onComplete?.Invoke();
            return;
        }

        panelZoom.ZoomOut(() =>
        {
            Hide();
            onComplete?.Invoke();
        });
    }

    // Lets the player dismiss the modal and look at the network they just
    // built without losing the way back — the floating button picks up the
    // exact same "go to Level Select" action the modal's own button has.
    void OnViewTopologyClicked()
    {
        HideAnimated(() => floatingBackButtonGo.SetActive(true));
    }

    void OnContinueClicked()
    {
        floatingBackButtonGo.SetActive(false);
        var callback = onContinue;
        onContinue = null;
        HideAnimated(() => callback?.Invoke());
    }

    // ── UI construction ──────────────────────────────────────────────────────

    void BuildPanel()
    {
        var canvasGo = new GameObject("LevelComplete_Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50; // above every other in-scene canvas, including TutorialUIManager's

        // Same origin/camera UISparks.EmitBurst needs everywhere else it's
        // used (see MenuButtonFX.Init) — worldCamera is null for this Screen
        // Space Overlay canvas, which is exactly what the overlay-mode
        // ScreenPointToLocalPointInRectangle conversion expects.
        sparkParent = canvasGo.transform as RectTransform;
        eventCamera = canvas.worldCamera;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);
        canvasGo.AddComponent<GraphicRaycaster>();

        // Full-screen dim backdrop — blocks input to the scene behind it and
        // reads as a genuine modal, not a corner toast. This (not just the
        // inner Panel below) is what Show()/Hide() toggle, since it's the
        // piece that actually blocks raycasts full-screen.
        var dimGo = new GameObject("Dim");
        dimRoot = dimGo;
        dimGo.transform.SetParent(canvasGo.transform, false);
        var dimImg = dimGo.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.55f);
        var dimRt = dimGo.GetComponent<RectTransform>();
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = Vector2.zero;
        dimRt.offsetMax = Vector2.zero;

        panelRoot = new GameObject("Panel");
        panelRoot.transform.SetParent(dimGo.transform, false);
        var bg = panelRoot.AddComponent<Image>();
        bg.sprite = PlateSpriteBuilder.GetSprite(PanelFill, PanelBorder, PanelTrim,
            PanelBorderThickness, PanelChamfer, PanelPixelScale);
        bg.type = Image.Type.Sliced;
        var rt = panelRoot.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        // Grown from the original 300 to fit the stars row above the title —
        // title/body are top-anchored and buttons are bottom-anchored, so
        // both stay exactly where they were relative to their own edge; the
        // extra height only opens up room between the two, never overlaps.
        // (380, not the original 336: the stars/title/body stack below needs
        // that much room — see the offsets' comment for why 336 overlapped.)
        rt.sizeDelta = new Vector2(560f, 380f);

        // Scale-from-0 pop / shrink-to-0 dismiss (see Show/HideAnimated) —
        // same component LevelSelectManager's own panel already uses.
        panelZoom = panelRoot.AddComponent<ZoomPanel>();
        panelZoom.PromptSfx = SfxId.LevelCompletePrompt;

        BuildStarsRow(panelRoot.transform);

        // Stacked top-down by hand (stars -20..-59 -> title -66..-110 ->
        // body -118..-266), each with a small gap from the one above, ending
        // well clear of the button row's top edge (-292 at this panel
        // height: 52px buttons sitting 36px up from the bottom) — the
        // previous offsets here (title -50, body +10) were never recomputed
        // when the stars row was added and actually overlapped (body's own
        // anchored Y put it partly *above* the panel's top edge). Shifted
        // down 10px when the stars grew from 28px to the pixel-grid-exact 39px.
        titleLabel = MakeLabel(panelRoot.transform, "Title", 32, FontStyles.Bold, new Vector2(0f, -66f), new Vector2(480f, 44f));
        titleLabel.color = Color.white;

        bodyLabel = MakeLabel(panelRoot.transform, "Body", 20, FontStyles.Normal, new Vector2(0f, -118f), new Vector2(480f, 148f));
        bodyLabel.color = new Color(0.85f, 0.90f, 0.97f);

        // Two buttons side by side — "View Topology" (secondary, dismiss-only)
        // on the left, the primary Level Select action on the right.
        MakeButton(panelRoot.transform, "ViewTopologyButton", GameTheme.Info,
            new Vector2(-135f, 36f), "View Topology", OnViewTopologyClicked, out _);

        // Idle pulse on the primary action only — the one button every
        // player needs to notice and press to move on, not the secondary
        // View Topology dismiss.
        MakeButton(panelRoot.transform, "ContinueButton", GameTheme.ElectricBlue,
            new Vector2(135f, 36f), "Continue", OnContinueClicked, out continueButtonLabel,
            idlePulse: true);

        BuildFloatingBackButton(canvasGo.transform);
    }

    // Three star icons above the title — built once here, mutated per-Show()
    // call by SetStars() rather than rebuilt, same as titleLabel/bodyLabel.
    // Procedurally-drawn icons (StarIconBuilder), not a font glyph — see the
    // class comment for why.
    void BuildStarsRow(Transform parent)
    {
        starsRow = new GameObject("StarsRow");
        starsRow.transform.SetParent(parent, false);
        var rowRt = starsRow.AddComponent<RectTransform>();
        rowRt.anchorMin        = new Vector2(0.5f, 1f);
        rowRt.anchorMax        = new Vector2(0.5f, 1f);
        rowRt.pivot            = new Vector2(0.5f, 1f);
        rowRt.anchoredPosition = new Vector2(0f, -20f); // clears the plate's 16px border (4 cells x pixelScale 4)
        rowRt.sizeDelta        = new Vector2(3 * StarSize + 2 * StarSpacing, StarSize);

        var layout = starsRow.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment        = TextAnchor.MiddleCenter;
        // Must be true for LayoutElement.preferredWidth/Height (set below) to
        // actually apply — with these false the group only positions children
        // and leaves each Image's own RectTransform at its default 100x100,
        // which is why the stars first came out oversized.
        layout.childControlWidth     = true;
        layout.childControlHeight    = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.spacing               = StarSpacing;

        starSlots = new StarSlot[3];
        for (int i = 0; i < 3; i++)
        {
            var go = new GameObject($"Star{i}", typeof(RectTransform));
            go.transform.SetParent(starsRow.transform, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth  = StarSize;
            le.preferredHeight = StarSize;

            // Glow built (and thus drawn) BEFORE Icon so it sits behind the
            // star sprite, not on top of it — same ordering MenuButtonFX's
            // Glow/Visual split relies on. Additive + oversized relative to
            // the icon so PopStar's flash reads as light spilling out
            // from the star rather than a bright disc printed over it.
            var glowGo = new GameObject("Glow", typeof(RectTransform));
            glowGo.transform.SetParent(go.transform, false);
            var glowImg = glowGo.AddComponent<Image>();
            glowImg.sprite         = RadialGlowSprite.Get();
            glowImg.material       = RadialGlowSprite.GetAdditiveMaterial();
            glowImg.raycastTarget  = false;
            glowImg.color          = new Color(StarFilledColor.r, StarFilledColor.g, StarFilledColor.b, 0f);
            var grt = glowGo.GetComponent<RectTransform>();
            grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(0.5f, 0.5f);
            grt.sizeDelta = StarGlowSize;

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(go.transform, false);
            var iconImg = iconGo.AddComponent<Image>();
            iconImg.sprite = starEmptySprite;
            var irt = iconGo.GetComponent<RectTransform>();
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = Vector2.zero;
            irt.offsetMax = Vector2.zero;

            starSlots[i] = new StarSlot { Rt = go.GetComponent<RectTransform>(), Icon = iconImg, Glow = glowImg };
        }
    }

    // Small pill button, bottom-center of the whole screen (see
    // FloatingBackBottom for what it stays clear of), that only appears once
    // "View Topology" has dismissed the modal — reuses the exact same
    // Continue action so there's always a way back to Level Select.
    // Registered as a screen occupant so tutorial callouts and the CLI panel
    // place themselves around it like any other HUD control.
    void BuildFloatingBackButton(Transform canvasParent)
    {
        floatingBackButtonGo = MakeButton(canvasParent, "FloatingBackButton", GameTheme.ElectricBlue,
            new Vector2(0f, FloatingBackBottom), "Back to Level Select", OnContinueClicked, out _,
            size: new Vector2(220f, 48f), anchor: new Vector2(0.5f, 0f), idlePulse: true);
        OccupiesScreen.Add(floatingBackButtonGo, OccupantLayer.Hud);

        floatingBackButtonGo.SetActive(false);
    }

    // Shared button builder — same Glow + Visual(Background+Label) composition
    // and MenuButtonFX hover/click behavior as TutorialUIManager.BuildActionButton
    // / LevelSelectManager.CreateButton, so these modal buttons are the same
    // button, art and feedback included, not flat placeholder rectangles.
    // glowColor differentiates primary/secondary actions (the plate art itself
    // isn't tinted) — matches TutorialUIManager's fx.glowColor usage.
    GameObject MakeButton(Transform parent, string name, Color glowColor, Vector2 anchoredPos,
                           string text, UnityEngine.Events.UnityAction onClick,
                           out TextMeshProUGUI label, Vector2? size = null, Vector2? anchor = null,
                           bool idlePulse = false)
    {
        Vector2 btnSize = size ?? new Vector2(250f, 52f);
        Vector2 a       = anchor ?? new Vector2(0.5f, 0f);

        var btnGo = new GameObject(name, typeof(RectTransform));
        btnGo.transform.SetParent(parent, false);
        var btnRt = btnGo.GetComponent<RectTransform>();
        btnRt.anchorMin        = a;
        btnRt.anchorMax        = a;
        btnRt.pivot            = a;
        btnRt.anchoredPosition = anchoredPos;
        btnRt.sizeDelta        = btnSize;

        var glowGo = new GameObject("Glow", typeof(RectTransform));
        glowGo.transform.SetParent(btnGo.transform, false);
        var glowImg = glowGo.AddComponent<Image>();
        glowImg.raycastTarget = false;
        var grt = glowGo.GetComponent<RectTransform>();
        grt.anchorMin        = new Vector2(0.5f, 0.5f);
        grt.anchorMax        = new Vector2(0.5f, 0.5f);
        grt.pivot            = new Vector2(0.5f, 0.5f);
        grt.anchoredPosition = Vector2.zero;
        grt.sizeDelta        = new Vector2(btnSize.x * 1.15f, btnSize.y * 1.6f);

        var visualGo = new GameObject("Visual", typeof(RectTransform));
        visualGo.transform.SetParent(btnGo.transform, false);
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
        label = labelGo.AddComponent<TextMeshProUGUI>();
        label.font      = font;
        label.text      = text;
        label.fontSize  = 18;
        label.color     = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.fontStyle = FontStyles.Bold;
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;

        var btn = btnGo.AddComponent<Button>();
        btn.transition    = Selectable.Transition.None;
        btn.targetGraphic = bgImg;

        var fx = btnGo.AddComponent<MenuButtonFX>();
        fx.visualRect      = vrt;
        fx.backgroundImage = bgImg;
        fx.glowImage       = glowImg;
        fx.normalSprite    = actionNormalSprite;
        fx.hoverSprite     = actionHoverSprite;
        fx.glowColor       = glowColor;
        fx.idlePulse       = idlePulse;
        fx.Init();

        btn.onClick.AddListener(() => fx.PlayClickThen(() => onClick?.Invoke()));

        return btnGo;
    }

    TextMeshProUGUI MakeLabel(Transform parent, string name, int fontSize, FontStyles style,
                               Vector2 anchoredPos, Vector2 size, bool stretch = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font      = font;
        tmp.fontSize  = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;

        var rt = go.GetComponent<RectTransform>();
        if (stretch)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
        else
        {
            rt.anchorMin        = new Vector2(0.5f, 1f);
            rt.anchorMax        = new Vector2(0.5f, 1f);
            rt.pivot            = new Vector2(0.5f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta        = size;
        }
        return tmp;
    }
}
