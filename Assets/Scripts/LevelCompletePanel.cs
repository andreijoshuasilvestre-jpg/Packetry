using System;
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

    // Clears NetworkUIManager's StatusBar (bottom, full-width, 32px) with
    // room to spare, and sits in the one HUD corner nothing else claims —
    // TaskUIManager owns top-right, NetworkUIManager's sidebar owns left.
    const float FloatingBackBottomMargin = 44f;

    static readonly Color StarFilledColor = GameTheme.Warning;
    static readonly Color StarEmptyColor  = new Color(0.35f, 0.36f, 0.40f, 1f);

    TMP_FontAsset font;
    Sprite actionNormalSprite;
    Sprite actionHoverSprite;
    Sprite starFilledSprite;
    Sprite starEmptySprite;

    GameObject dimRoot;
    GameObject panelRoot;
    TextMeshProUGUI titleLabel;
    TextMeshProUGUI bodyLabel;
    TextMeshProUGUI continueButtonLabel;
    GameObject floatingBackButtonGo;
    GameObject starsRow;
    Image[] starImages;
    Action onContinue;

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
    public void Show(string title, string body, string continueButtonText, Action onContinueClicked, int stars = -1)
    {
        titleLabel.text          = title;
        bodyLabel.text           = body;
        continueButtonLabel.text = continueButtonText;
        onContinue                = onContinueClicked;
        SetStars(stars);

        // Reset any "View Topology" peek state left over from a previous
        // call — Show() always starts a fresh completion flow.
        floatingBackButtonGo.SetActive(false);
        dimRoot.SetActive(true);
    }

    void SetStars(int stars)
    {
        starsRow.SetActive(stars >= 0);
        if (stars < 0) return;

        for (int i = 0; i < starImages.Length; i++)
            starImages[i].sprite = i < stars ? starFilledSprite : starEmptySprite;
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

    // Lets the player dismiss the modal and look at the network they just
    // built without losing the way back — the floating button picks up the
    // exact same "go to Level Select" action the modal's own button has.
    void OnViewTopologyClicked()
    {
        Hide();
        floatingBackButtonGo.SetActive(true);
    }

    void OnContinueClicked()
    {
        floatingBackButtonGo.SetActive(false);
        var callback = onContinue;
        onContinue = null;
        Hide();
        callback?.Invoke();
    }

    // ── UI construction ──────────────────────────────────────────────────────

    void BuildPanel()
    {
        var canvasGo = new GameObject("LevelComplete_Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50; // above every other in-scene canvas, including TutorialUIManager's

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

        BuildStarsRow(panelRoot.transform);

        // Stacked top-down by hand (stars bottom -44 -> title -56..-100 ->
        // body -108..-276), each with a small gap from the one above, ending
        // well clear of the button row's top edge (~-102 at this panel
        // height) — the previous offsets here (title -50, body +10) were
        // never recomputed when the stars row was added and actually
        // overlapped (body's own anchored Y put it partly *above* the
        // panel's top edge).
        titleLabel = MakeLabel(panelRoot.transform, "Title", 32, FontStyles.Bold, new Vector2(0f, -56f), new Vector2(480f, 44f));
        titleLabel.color = Color.white;

        bodyLabel = MakeLabel(panelRoot.transform, "Body", 20, FontStyles.Normal, new Vector2(0f, -108f), new Vector2(480f, 148f));
        bodyLabel.color = new Color(0.85f, 0.90f, 0.97f);

        // Two buttons side by side — "View Topology" (secondary, dismiss-only)
        // on the left, the primary Level Select action on the right.
        MakeButton(panelRoot.transform, "ViewTopologyButton", GameTheme.Info,
            new Vector2(-135f, 36f), "View Topology", OnViewTopologyClicked, out _);

        MakeButton(panelRoot.transform, "ContinueButton", GameTheme.ElectricBlue,
            new Vector2(135f, 36f), "Continue", OnContinueClicked, out continueButtonLabel);

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
        rowRt.anchoredPosition = new Vector2(0f, -14f);
        rowRt.sizeDelta        = new Vector2(120f, 28f);

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
        layout.spacing               = 8f;

        starImages = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            var go = new GameObject($"Star{i}");
            go.transform.SetParent(starsRow.transform, false);
            var img = go.AddComponent<Image>();
            img.sprite = starEmptySprite;
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth  = 28f;
            le.preferredHeight = 28f;
            starImages[i] = img;
        }
    }

    // Small pill button, bottom-right of the whole screen (clear of
    // TaskUIManager's top-right panel and NetworkUIManager's status bar),
    // that only appears once "View Topology" has dismissed the modal —
    // reuses the exact same Continue action so there's always a way back
    // to Level Select.
    void BuildFloatingBackButton(Transform canvasParent)
    {
        floatingBackButtonGo = MakeButton(canvasParent, "FloatingBackButton", GameTheme.ElectricBlue,
            new Vector2(-20f, FloatingBackBottomMargin), "Back to Level Select", OnContinueClicked, out _,
            size: new Vector2(220f, 48f), anchor: new Vector2(1f, 0f));

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
                           out TextMeshProUGUI label, Vector2? size = null, Vector2? anchor = null)
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
