using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Builds the static level-select shell: panel scaffold, title, back button,
/// carousel frame (background + empty scroll/viewport/content — cards are
/// added later by LevelSelectManager, since they depend on live SaveManager
/// progress), and the description popup scaffold. Pure structure: assigns
/// MenuButtonFX's sprite/color fields but never calls Fx.Init(), never wires
/// Button.onClick — LevelSelectManager does both once this shell lives under
/// a real Canvas (see LevelSelectShellView).
///
/// Called from two places that must never visually diverge: MainMenuUIBaker
/// (Editor tool, bakes the result once into
/// Assets/Resources/MainMenuPrefabs/LevelSelectShell.prefab) and
/// LevelSelectManager's runtime fallback (used only if that prefab hasn't
/// been generated yet, or nothing is pre-placed in the scene).
/// </summary>
public static class LevelSelectShellBuilder
{
    const string SpriteFolder = "MainMenuUI/";
    public const float CardWidth = 820f; // widened again (was 480 -> 560 -> 640) so longer level titles/card labels fit without shrinking as much
    // Horizontal gap between each card edge and the viewport's RectMask2D.
    // Cards scale up on hover (MenuButtonFX.hoverScale 1.06) and on click
    // (its 1.12 bounce pop); without this margin, a card that fills the full
    // viewport width gets its sides clipped whenever it grows. 50 covers the
    // click peak: 820 * 0.12 / 2 = ~49px per side.
    public const int   CardSideInset = 50;
    const float ViewportWidth  = CardWidth + CardSideInset * 2;
    const float ViewportHeight = 560f; // a little taller than the old 520, to give the now-taller (wider-aspect) cards some breathing room
    const float CardSpacing    = 40f;

    static readonly Vector2 DescPanelSize = new Vector2(720f, 440f);
    const float DescIconSize = 56f;
    const float DescButtonWidth  = 220f;
    const float DescButtonHeight = 64f;
    const float DescButtonGapFromCenter = 130f;
    const int MaxDescIcons = 3;

    /// <summary>
    /// Builds the LevelSelect_Panel hierarchy under `canvasParent` (with
    /// LevelSelectManager already attached to the same root) and returns its
    /// view.
    /// </summary>
    public static LevelSelectShellView Build(Transform canvasParent, TMP_FontAsset font, Color textColor,
        Color containerFill, Color containerBorder, Color containerTrim,
        int containerBorderThickness, int containerChamfer, int containerPixelScale)
    {
        var panel = new GameObject("LevelSelect_Panel", typeof(RectTransform));
        panel.transform.SetParent(canvasParent, false);
        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;

        var view = panel.AddComponent<LevelSelectShellView>();
        panel.AddComponent<LevelSelectManager>();
        view.PanelSlide = panel.AddComponent<ZoomPanel>();

        BuildTitle(panelRt, font, textColor);

        view.BackButton = CreateStaticButton(panelRt, "< BACK", 200f, 64f, font, textColor);
        var backRt = view.BackButton.Button.GetComponent<RectTransform>();
        backRt.anchorMin        = new Vector2(0f, 1f);
        backRt.anchorMax        = new Vector2(0f, 1f);
        backRt.pivot            = new Vector2(0f, 1f);
        backRt.anchoredPosition = new Vector2(48f, -48f);

        BuildCarouselBackground(panelRt, containerFill, containerBorder, containerTrim,
            containerBorderThickness, containerChamfer, containerPixelScale);
        BuildCarousel(panelRt, view);
        BuildDescriptionPopup(panelRt, font, textColor, containerFill, containerBorder, containerTrim, view);

        // Starts inactive — LevelSelectManager.Build() also does this once it
        // actually wires the panel up (see its own doc comment), but that
        // only ever runs at real Play-mode Awake(). Without also deactivating
        // it here, the baked prefab itself (and any pre-placed scene
        // instance of it, or a runtime Instantiate() in the brief window
        // before Build() runs) sits active-by-default — visible behind the
        // main menu in the Editor without even pressing Play, or as a
        // one-frame flash at runtime.
        panel.SetActive(false);

        return view;
    }

    static void BuildTitle(RectTransform parent, TMP_FontAsset font, Color textColor)
    {
        var go = new GameObject("Title", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 1f);
        rt.anchorMax        = new Vector2(0.5f, 1f);
        rt.pivot            = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -70f);
        rt.sizeDelta        = new Vector2(900f, 90f);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text          = "SELECT LEVEL";
        tmp.font          = font;
        tmp.fontSize      = 54;
        tmp.color         = textColor;
        tmp.alignment     = TextAlignmentOptions.Center;
        tmp.fontStyle     = FontStyles.Bold;
        tmp.raycastTarget = false;
    }

    static void BuildCarouselBackground(RectTransform parent, Color fill, Color border, Color trim,
        int borderThickness, int chamfer, int pixelScale)
    {
        const float paddingX = 60f;
        const float paddingY = 60f;

        var go = new GameObject("CarouselBackground", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, -20f); // matches Carousel's own anchoredPosition
        rt.sizeDelta        = new Vector2(ViewportWidth + paddingX, ViewportHeight + paddingY);

        var img = go.AddComponent<Image>();
        img.sprite = PlateSpriteBuilder.GetSprite(fill, border, trim, borderThickness, chamfer, pixelScale);
        img.type          = Image.Type.Sliced;
        img.raycastTarget = false;
    }

    static void BuildCarousel(RectTransform parent, LevelSelectShellView view)
    {
        var scrollGo = new GameObject("Carousel", typeof(RectTransform));
        scrollGo.transform.SetParent(parent, false);
        var scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin        = new Vector2(0.5f, 0.5f);
        scrollRt.anchorMax        = new Vector2(0.5f, 0.5f);
        scrollRt.pivot            = new Vector2(0.5f, 0.5f);
        scrollRt.anchoredPosition = new Vector2(0f, -20f);
        scrollRt.sizeDelta        = new Vector2(ViewportWidth, ViewportHeight);

        var scrollRect = scrollGo.AddComponent<SmoothScrollRect>();
        scrollRect.horizontal        = false;
        scrollRect.vertical          = true;
        scrollRect.movementType      = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;

        var viewportGo = new GameObject("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        viewportGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f); // near-invisible; RectMask2D needs a Graphic to clip against
        viewportGo.AddComponent<RectMask2D>();
        var viewportRt = viewportGo.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = Vector2.zero;
        viewportRt.offsetMax = Vector2.zero;
        scrollRect.viewport = viewportRt;

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewportGo.transform, false);
        var contentRt = contentGo.GetComponent<RectTransform>();
        contentRt.anchorMin        = new Vector2(0f, 1f);
        contentRt.anchorMax        = new Vector2(1f, 1f);
        contentRt.pivot            = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta        = Vector2.zero;

        var layout = contentGo.AddComponent<VerticalLayoutGroup>();
        // Top/bottom padding depends on the live card/header layout (see
        // LevelSelectManager.RefreshCards, which recomputes it so the first
        // and last card can reach the viewport's center) — start with a
        // neutral placeholder.
        layout.padding             = new RectOffset(CardSideInset, CardSideInset, 20, 20);
        layout.spacing             = CardSpacing;
        layout.childAlignment      = TextAnchor.UpperCenter;
        layout.childControlWidth   = true;
        layout.childControlHeight  = false;
        layout.childForceExpandWidth  = true;
        layout.childForceExpandHeight = false;

        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        scrollRect.content = contentRt;

        var carousel = scrollGo.AddComponent<LevelCarousel>();
        carousel.Configure(scrollRect);

        view.ContentRt     = contentRt;
        view.ContentLayout = layout;
        view.ScrollRect    = scrollRect;
        view.Carousel      = carousel;
    }

    static void BuildDescriptionPopup(RectTransform parent, TMP_FontAsset font, Color textColor,
        Color fill, Color border, Color trim, LevelSelectShellView view)
    {
        var descDim = new GameObject("DescriptionDim", typeof(RectTransform));
        descDim.transform.SetParent(parent, false);
        var dimImg = descDim.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.6f);
        var dimRt = descDim.GetComponent<RectTransform>();
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = Vector2.zero;
        dimRt.offsetMax = Vector2.zero;

        var popupGo = new GameObject("DescriptionPopup", typeof(RectTransform));
        popupGo.transform.SetParent(descDim.transform, false);
        var popupImg = popupGo.AddComponent<Image>();
        popupImg.sprite = PlateSpriteBuilder.GetSprite(fill, border, trim, borderThickness: 4, chamfer: 22, pixelScale: 6);
        popupImg.type = Image.Type.Sliced;

        var descPanelRt = popupGo.GetComponent<RectTransform>();
        descPanelRt.anchorMin = new Vector2(0.5f, 0.5f);
        descPanelRt.anchorMax = new Vector2(0.5f, 0.5f);
        descPanelRt.pivot     = new Vector2(0.5f, 0.5f);
        descPanelRt.sizeDelta = DescPanelSize;

        var titleGo = new GameObject("Title", typeof(RectTransform));
        titleGo.transform.SetParent(popupGo.transform, false);
        var descTitleTmp = titleGo.AddComponent<TextMeshProUGUI>();
        descTitleTmp.font      = font;
        descTitleTmp.fontSize  = 36;
        descTitleTmp.fontStyle = FontStyles.Bold;
        descTitleTmp.color     = textColor;
        descTitleTmp.alignment = TextAlignmentOptions.Center;
        var trt = titleGo.GetComponent<RectTransform>();
        trt.anchorMin        = new Vector2(0.5f, 1f);
        trt.anchorMax        = new Vector2(0.5f, 1f);
        trt.pivot            = new Vector2(0.5f, 1f);
        trt.anchoredPosition = new Vector2(0f, -32f);
        trt.sizeDelta        = new Vector2(DescPanelSize.x - 80f, 50f);

        var categoryGo = new GameObject("Category", typeof(RectTransform));
        categoryGo.transform.SetParent(popupGo.transform, false);
        var descCategoryTmp = categoryGo.AddComponent<TextMeshProUGUI>();
        descCategoryTmp.font          = font;
        descCategoryTmp.fontSize      = 18;
        descCategoryTmp.fontStyle     = FontStyles.Bold;
        descCategoryTmp.color         = new Color(textColor.r, textColor.g, textColor.b, 0.55f);
        descCategoryTmp.alignment     = TextAlignmentOptions.Center;
        descCategoryTmp.characterSpacing = 3f;
        var catRt = categoryGo.GetComponent<RectTransform>();
        catRt.anchorMin        = new Vector2(0.5f, 1f);
        catRt.anchorMax        = new Vector2(0.5f, 1f);
        catRt.pivot            = new Vector2(0.5f, 1f);
        catRt.anchoredPosition = new Vector2(0f, -84f);
        catRt.sizeDelta        = new Vector2(DescPanelSize.x - 80f, 26f);

        var iconRowGo = new GameObject("IconRow", typeof(RectTransform));
        iconRowGo.transform.SetParent(popupGo.transform, false);
        var rowLayout = iconRowGo.AddComponent<HorizontalLayoutGroup>();
        rowLayout.childAlignment        = TextAnchor.MiddleCenter;
        rowLayout.spacing               = 18f;
        rowLayout.childForceExpandWidth  = false;
        rowLayout.childForceExpandHeight = false;
        var rowRt = iconRowGo.GetComponent<RectTransform>();
        rowRt.anchorMin        = new Vector2(0.5f, 1f);
        rowRt.anchorMax        = new Vector2(0.5f, 1f);
        rowRt.pivot            = new Vector2(0.5f, 1f);
        rowRt.anchoredPosition = new Vector2(0f, -124f);
        rowRt.sizeDelta        = new Vector2(DescPanelSize.x - 80f, DescIconSize);

        var iconSlots = new List<Image>();
        for (int i = 0; i < MaxDescIcons; i++)
        {
            var slotGo = new GameObject("Icon" + i, typeof(RectTransform));
            slotGo.transform.SetParent(iconRowGo.transform, false);
            var slotImg = slotGo.AddComponent<Image>();
            slotImg.preserveAspect = true;
            slotImg.raycastTarget  = false;
            var slotLe = slotGo.AddComponent<LayoutElement>();
            slotLe.preferredWidth  = DescIconSize;
            slotLe.preferredHeight = DescIconSize;
            iconSlots.Add(slotImg);
        }

        var bodyGo = new GameObject("Body", typeof(RectTransform));
        bodyGo.transform.SetParent(popupGo.transform, false);
        var descBodyTmp = bodyGo.AddComponent<TextMeshProUGUI>();
        descBodyTmp.font               = font;
        descBodyTmp.fontSize           = 22;
        // Only ever shrinks for a description longer than today's longest
        // (5 lines at 22 — Levels 7/9/11) — a LevelCatalog edit then gets
        // slightly smaller text instead of spilling into the buttons.
        descBodyTmp.enableAutoSizing   = true;
        descBodyTmp.fontSizeMin        = 16;
        descBodyTmp.fontSizeMax        = 22;
        descBodyTmp.color              = new Color(0.88f, 0.92f, 0.97f, 1f);
        descBodyTmp.alignment          = TextAlignmentOptions.Top;
        descBodyTmp.enableWordWrapping = true;
        // Top-anchored like the rows above it, starting a clear gap below
        // the icon row (-124 - 56 = -180) — it used to be center-anchored,
        // which put its first line at -165, up inside the icons. Ends above
        // the Cancel/Play row (top edge at 440 - 32 - 64 = 344 from the top).
        var bodyRt = bodyGo.GetComponent<RectTransform>();
        bodyRt.anchorMin        = new Vector2(0.5f, 1f);
        bodyRt.anchorMax        = new Vector2(0.5f, 1f);
        bodyRt.pivot            = new Vector2(0.5f, 1f);
        bodyRt.anchoredPosition = new Vector2(0f, -192f);
        bodyRt.sizeDelta        = new Vector2(DescPanelSize.x - 100f, 140f);

        var cancelRefs = CreateStaticButton(popupGo.transform, "CANCEL", DescButtonWidth, DescButtonHeight, font, textColor);
        var cancelRt = cancelRefs.Button.GetComponent<RectTransform>();
        cancelRt.anchorMin        = new Vector2(0.5f, 0f);
        cancelRt.anchorMax        = new Vector2(0.5f, 0f);
        cancelRt.pivot            = new Vector2(0.5f, 0f);
        cancelRt.anchoredPosition = new Vector2(-DescButtonGapFromCenter, 32f);

        var playRefs = CreateStaticButton(popupGo.transform, "PLAY", DescButtonWidth, DescButtonHeight, font, textColor);
        var playRt = playRefs.Button.GetComponent<RectTransform>();
        playRt.anchorMin        = new Vector2(0.5f, 0f);
        playRt.anchorMax        = new Vector2(0.5f, 0f);
        playRt.pivot            = new Vector2(0.5f, 0f);
        playRt.anchoredPosition = new Vector2(DescButtonGapFromCenter, 32f);

        // Its arrival glow (PanelArrivalGlow) is added later, at runtime, by
        // LevelSelectManager.Build — its additive material and generated ring
        // sprite can't survive being baked into the prefab.
        var descZoom = popupGo.AddComponent<ZoomPanel>();

        descDim.SetActive(false);

        view.DescDim         = descDim;
        view.DescZoom        = descZoom;
        view.DescTitleTmp    = descTitleTmp;
        view.DescCategoryTmp = descCategoryTmp;
        view.DescBodyTmp     = descBodyTmp;
        view.DescIconSlots   = iconSlots;
        view.CancelButton    = cancelRefs;
        view.PlayButton      = playRefs;
    }

    /// <summary>
    /// Shared static-button recipe for Back/Cancel/Play — same visual
    /// language as MainMenuShellBuilder's menu buttons. Never locked (that
    /// variant lives in LevelSelectManager.CreateButton, used only for the
    /// dynamic level cards, which depend on live SaveManager state).
    /// </summary>
    public static MenuButtonRefs CreateStaticButton(Transform parent, string label, float width, float height,
                                                      TMP_FontAsset font, Color textColor)
    {
        var go = new GameObject("Btn_" + label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);

        var visualGo = new GameObject("Visual", typeof(RectTransform));
        visualGo.transform.SetParent(go.transform, false);
        var vrt = visualGo.GetComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.offsetMin = Vector2.zero;
        vrt.offsetMax = Vector2.zero;

        // Glow is a child of Visual (not a sibling) — see
        // MainMenuShellBuilder.CreateMenuButton's matching comment for why
        // (hand-editability) and how MenuButtonFX compensates for it.
        var glowGo = new GameObject("Glow", typeof(RectTransform));
        glowGo.transform.SetParent(visualGo.transform, false);
        var glowImg = glowGo.AddComponent<Image>();
        glowImg.raycastTarget = false;
        // Invisible at rest — see MainMenuShellBuilder.CreateMenuButton's
        // matching comment: Fx.Init() reassigns this at runtime, but a
        // pre-placed/baked instance needs a sane default before that ever runs.
        glowImg.color = new Color(0f, 0f, 0f, 0f);
        var grt = glowGo.GetComponent<RectTransform>();
        grt.anchorMin        = new Vector2(0.5f, 0.5f);
        grt.anchorMax        = new Vector2(0.5f, 0.5f);
        grt.pivot            = new Vector2(0.5f, 0.5f);
        grt.anchoredPosition = Vector2.zero;
        grt.sizeDelta        = new Vector2(width * 1.15f, height * 1.6f);

        var normalSprite = Resources.Load<Sprite>(SpriteFolder + "BlankButton");
        var hoverSprite  = Resources.Load<Sprite>(SpriteFolder + "BlankButton_Hover");

        var bgGo = new GameObject("Background", typeof(RectTransform));
        bgGo.transform.SetParent(visualGo.transform, false);
        var bgImg = bgGo.AddComponent<Image>();
        bgImg.sprite = normalSprite;
        bgImg.preserveAspect = true;
        var brt = bgGo.GetComponent<RectTransform>();
        brt.anchorMin = Vector2.zero;
        brt.anchorMax = Vector2.one;
        brt.offsetMin = Vector2.zero;
        brt.offsetMax = Vector2.zero;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(visualGo.transform, false);
        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.text             = label;
        tmp.font             = font;
        tmp.fontSize         = 34;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin      = 14;
        tmp.fontSizeMax      = 34;
        tmp.color            = textColor;
        tmp.alignment        = TextAlignmentOptions.Center;
        tmp.fontStyle        = FontStyles.Bold;
        tmp.raycastTarget    = false;
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(20f, 22f);
        lrt.offsetMax = new Vector2(-20f, -22f);

        var btn = go.AddComponent<Button>();
        btn.transition    = Selectable.Transition.None;
        btn.targetGraphic = bgImg;

        var fx = go.AddComponent<MenuButtonFX>();
        fx.visualRect      = vrt;
        fx.backgroundImage = bgImg;
        fx.glowImage       = glowImg;
        fx.normalSprite    = normalSprite;
        fx.hoverSprite     = hoverSprite;
        fx.glowColor       = GameTheme.ElectricBlue;

        return new MenuButtonRefs { Button = btn, Fx = fx, VisualRect = vrt };
    }
}
