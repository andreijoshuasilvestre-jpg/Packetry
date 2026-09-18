using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One entry in the level carousel. Add more entries in
/// LevelSelectManager.DefaultLevelList() (or, once baked, hand-edit
/// Assets/Resources/LevelCatalog.asset in the Inspector — see LevelCatalog)
/// as new levels are built — the carousel, card layout, and scroll range all
/// adapt automatically; nothing else needs to change (see LevelCarousel).
/// </summary>
[Serializable]
public struct LevelEntry
{
    public int      number;
    public string   title;
    public string   sceneName;
    public string   category;
    public string   description;
    public string[] icons; // UIIcons/ resource names shown as a small row on the description popup

    public LevelEntry(int number, string title, string sceneName, string category,
                       string description, params string[] icons)
    {
        this.number      = number;
        this.title       = title;
        this.sceneName   = sceneName;
        this.category    = category;
        this.description = description;
        this.icons       = icons;
    }
}

/// <summary>
/// Owns the level-select screen: a top-to-bottom scrolling carousel of level
/// cards, shown over the main menu canvas when Play is clicked. The static
/// scaffold (panel, title, back button, carousel frame, description popup)
/// is built by LevelSelectShellBuilder — either baked once into
/// Assets/Resources/MainMenuPrefabs/LevelSelectShell.prefab (with this
/// component already attached to its root) or built live as a runtime
/// fallback — and Build() just wires it up: click handlers, MenuButtonFX.Init(),
/// and the actual level cards, which stay genuinely dynamic since they read
/// live SaveManager progress. MainMenuBootstrap creates/resolves one
/// instance and toggles it with Open()/Close(); picking a card loads that
/// level's scene directly.
/// </summary>
[RequireComponent(typeof(LevelSelectShellView))]
public class LevelSelectManager : MonoBehaviour
{
    const string SpriteFolder = "MainMenuUI/";
    const float ViewportHeight = 520f; // matches LevelSelectShellBuilder's ViewportHeight — used to re-center a single card's padding
    const float CardWidth      = 480f;

    TMP_FontAsset menuFont;
    Color textColor = Color.white;
    Action onBack;

    LevelSelectShellView view;
    GameObject panel;
    SlidePanel panelSlide;
    RectTransform contentRt;
    LevelCarousel carousel;
    Sprite normalSprite;
    Sprite hoverSprite;

    // ── Level description popup (see LevelSelectShellBuilder/ShowDescriptionPopup) ──
    GameObject descDim;
    SlidePanel descSlide;
    TextMeshProUGUI descTitleTmp;
    TextMeshProUGUI descCategoryTmp;
    TextMeshProUGUI descBodyTmp;
    List<Image> descIconSlots;
    readonly Dictionary<string, Sprite> iconCache = new Dictionary<string, Sprite>();

    readonly List<LevelEntry> levels = new List<LevelEntry>();

    // Set by Open(unlockAll: true) — the "Skip Tutorial (Dev)" menu button's
    // path (see MainMenuBootstrap.OnSkipTutorialClicked) opens this same
    // screen but with every card shown unlocked, regardless of actual save
    // progress, so testers can jump straight to any level. Never touches
    // SaveManager itself — purely a display/click-gate override here.
    bool forceUnlockAll;

    /// <summary>
    /// Call once after the shell (LevelSelectShellBuilder's output — this
    /// component already lives on its root) is resolved, before Open().
    /// Wires every click handler and MenuButtonFX.Init() against the shell's
    /// pre-built objects, then loads the level list and builds the cards.
    /// </summary>
    public void Build(TMP_FontAsset font, Color buttonTextColor, string firstLevelSceneName, Action onBack)
    {
        menuFont    = font;
        textColor   = buttonTextColor;
        this.onBack = onBack;

        view       = GetComponent<LevelSelectShellView>();
        panel      = gameObject;
        panelSlide = view.PanelSlide;
        contentRt  = view.ContentRt;
        carousel   = view.Carousel;

        normalSprite = Resources.Load<Sprite>(SpriteFolder + "BlankButton");
        hoverSprite  = Resources.Load<Sprite>(SpriteFolder + "BlankButton_Hover");

        WireButton(view.BackButton, ClosePanel);
        WireButton(view.CancelButton, HideDescriptionPopup);
        WireButton(view.PlayButton, () => { if (currentDescLevel.HasValue) OnLevelClicked(currentDescLevel.Value); });

        descDim         = view.DescDim;
        descSlide       = view.DescSlide;
        descTitleTmp    = view.DescTitleTmp;
        descCategoryTmp = view.DescCategoryTmp;
        descBodyTmp     = view.DescBodyTmp;
        descIconSlots   = view.DescIconSlots;

        LoadLevels(firstLevelSceneName);
        RefreshCards();

        panel.SetActive(false);
    }

    // A shell button's Fx.Init()/onClick wiring happens here, once it lives
    // under a real Canvas — see LevelSelectShellBuilder's class doc for why
    // it can't happen at build/bake time.
    static void WireButton(MenuButtonRefs refs, Action onClick)
    {
        if (refs?.Button == null) return;
        refs.Fx?.Init();
        refs.Button.onClick.AddListener(() =>
        {
            if (refs.Fx != null) refs.Fx.PlayClickThen(onClick);
            else onClick?.Invoke();
        });
    }

    void LoadLevels(string firstLevelSceneName)
    {
        levels.Clear();
        var catalog = Resources.Load<LevelCatalog>("LevelCatalog");
        if (catalog != null && catalog.levels != null && catalog.levels.Count > 0)
            levels.AddRange(catalog.levels);
        else
            levels.AddRange(DefaultLevelList(firstLevelSceneName));
    }

    // Hardcoded fallback used only if Assets/Resources/LevelCatalog.asset is
    // missing — also what MainMenuUIBaker seeds that asset with the first
    // time it runs (see MainMenuUIBaker.SeedLevelCatalogIfMissing). Every
    // level reuses the same scene — there's only one gameplay scene
    // (SampleScene). LevelManager.PendingLevelNumber (set in OnLevelClicked
    // below) is what actually distinguishes each once loaded, not sceneName.
    public static List<LevelEntry> DefaultLevelList(string firstLevelSceneName)
    {
        return new List<LevelEntry>
        {
            new LevelEntry(1, "Device Connecting", firstLevelSceneName, "Tutorials",
                "Learn the basics: place a table, add two PCs and a Switch, then cable everything " +
                "together into your first working network.",
                "PC_Icon", "Cable"),
            new LevelEntry(2, "IP Allocation", firstLevelSceneName, "Tutorials",
                "Give two PCs IP addresses on the same subnet and get them talking — your first " +
                "taste of addressing a network.",
                "PC_Icon"),
            new LevelEntry(3, "Router CLI Basics", firstLevelSceneName, "Tutorials",
                "Open a Router's command line and learn to navigate its modes — User EXEC, " +
                "Privileged EXEC, Global Config, and Interface Config — the same way real Cisco " +
                "IOS works.",
                "Router_Icon"),
            new LevelEntry(4, "Routers & Subnetting", firstLevelSceneName, "Tutorials",
                "Bridge two different subnets with a Router: address both PCs, configure each " +
                "router interface, and get a cross-subnet ping working.",
                "Router_Icon", "PC_Icon"),
            new LevelEntry(5, "LAN Challenge", firstLevelSceneName, "Challenges",
                "No hand-holding this time. Build a small LAN — two PCs and a Switch — entirely " +
                "on your own and prove it works with a successful ping.",
                "PC_Icon", "Switch_Icon"),
            new LevelEntry(6, "Two-LAN Challenge", firstLevelSceneName, "Challenges",
                "Scale it up: two switched departments bridged by a Router. Cable, address, and " +
                "configure everything yourself to get a cross-router ping working.",
                "PC_Icon", "Switch_Icon", "Router_Icon"),
            new LevelEntry(7, "Network Troubleshooting", firstLevelSceneName, "Challenges",
                "A pre-built network is broken — an unplugged cable, a misaddressed PC, and a " +
                "shut-down router interface. Find and fix all three faults using the tools you've " +
                "already learned.",
                "Router_Icon", "Ping"),
        };
    }

    /// <summary>Shows the level-select screen. unlockAll forces every card to
    /// render as unlocked/clickable regardless of actual save progress — used
    /// by the "Skip Tutorial (Dev)" menu button for testing (see
    /// MainMenuBootstrap.OnSkipTutorialClicked) — and is re-evaluated on every
    /// call, so a normal Open() afterwards correctly goes back to respecting
    /// real progress.</summary>
    public void Open(bool unlockAll = false)
    {
        forceUnlockAll = unlockAll;
        RefreshCards(); // also picks up any progress made since the screen was last built/shown
        panel.SetActive(true);
        panelSlide.SlideIn();
    }

    // Rebuilds the card list from scratch — cheap at this list size (a handful
    // of levels) and needed both for the initial Build() and every Open(),
    // since forceUnlockAll and completed/locked state can change between
    // shows. Cards are grouped under a section header per distinct category,
    // in the order each category first appears in `levels` — a header is just
    // another (unregistered, so LevelCarousel's focus/scale/snap logic never
    // touches it) child of the same VerticalLayoutGroup, so it takes up real
    // row space and the carousel's scroll range/snap math accounts for it
    // automatically like any card.
    void RefreshCards()
    {
        for (int i = contentRt.childCount - 1; i >= 0; i--)
            Destroy(contentRt.GetChild(i).gameObject);
        carousel.Clear(); // drop the just-destroyed cards' registrations, not just the GameObjects

        // Top/bottom padding depends on the live level count — more than one
        // level keeps a small snug margin; exactly one re-centers it in the
        // viewport (see LevelSelectShellBuilder.BuildCarousel for why this
        // can't be baked as a fixed value).
        if (view.ContentLayout != null)
        {
            const float SnugPadding = 20f;
            float cardHeight = CardHeight();
            int verticalPad = levels.Count > 1
                ? Mathf.RoundToInt(SnugPadding)
                : Mathf.RoundToInt((ViewportHeight - cardHeight) / 2f);
            view.ContentLayout.padding = new RectOffset(0, 0, verticalPad, verticalPad);
        }

        string lastCategory = null;
        foreach (var level in levels)
        {
            if (level.category != lastCategory)
            {
                BuildCategoryHeader(level.category, isFirst: lastCategory == null);
                lastCategory = level.category;
            }
            BuildCard(level);
        }
    }

    // Click FX already played (see CreateButton) by the time this runs. Slides
    // the panel out, then hides it and hands off to onBack (MainMenuBootstrap
    // slides the main menu back in — see ShowMainMenu).
    void ClosePanel()
    {
        panelSlide.SlideOut(() =>
        {
            panel.SetActive(false);
            onBack?.Invoke();
        });
    }

    // ── Level Description Popup ─────────────────────────────────────────────
    // Shown when a card is clicked, instead of loading the level immediately
    // (see BuildCard/ShowDescriptionPopup) — the shell's dim backdrop +
    // chamfered plate (built by LevelSelectShellBuilder) holding the level's
    // title, category, a small row of in-game UIIcons representing its
    // devices/tools, its description, and the Cancel/Play buttons wired in
    // Build() above.

    // Read by the Play button's onClick (wired once in Build(), same as every
    // other menu button's click-FX-wrapped listener) rather than rebinding
    // the listener per open — closing over a mutable field instead keeps
    // MenuButtonFX's click animation intact, which removing/re-adding the
    // Button's own onClick listener would bypass.
    LevelEntry? currentDescLevel;

    void ShowDescriptionPopup(LevelEntry level)
    {
        currentDescLevel = level;

        descTitleTmp.text    = level.title.ToUpperInvariant();
        descCategoryTmp.text = level.category.ToUpperInvariant();
        descBodyTmp.text     = level.description;

        for (int i = 0; i < descIconSlots.Count; i++)
        {
            bool has = level.icons != null && i < level.icons.Length;
            descIconSlots[i].gameObject.SetActive(has);
            if (has) descIconSlots[i].sprite = GetIconSprite(level.icons[i]);
        }

        descDim.SetActive(true);
        descSlide.SlideIn();
    }

    void HideDescriptionPopup()
    {
        descSlide.SlideOut(() => descDim.SetActive(false));
    }

    Sprite GetIconSprite(string name)
    {
        if (!iconCache.TryGetValue(name, out var sprite))
        {
            sprite = Resources.Load<Sprite>("UIIcons/" + name);
            iconCache[name] = sprite;
        }
        return sprite;
    }

    // ── Carousel content ─────────────────────────────────────────────────────

    float CardHeight()
    {
        float aspect = normalSprite != null ? normalSprite.rect.width / normalSprite.rect.height : 3.26f;
        return CardWidth / aspect;
    }

    // Plain left-aligned label, not a card — sits directly in the carousel's
    // VerticalLayoutGroup like any other row. The extra top gap (skipped for
    // the very first header) is baked into the row's own height rather than
    // a separate spacer element, with the label bottom-aligned inside it so
    // the visible text still sits close to the cards below it.
    void BuildCategoryHeader(string label, bool isFirst)
    {
        const float height      = 34f;
        const float extraTopGap = 24f;

        var go = new GameObject("Header_" + label, typeof(RectTransform));
        go.transform.SetParent(contentRt, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, height + (isFirst ? 0f : extraTopGap));

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text            = label.ToUpperInvariant();
        tmp.font            = menuFont;
        tmp.fontSize        = 24;
        tmp.color           = new Color(textColor.r, textColor.g, textColor.b, 0.55f);
        tmp.alignment       = TextAlignmentOptions.BottomLeft;
        tmp.fontStyle       = FontStyles.Bold;
        tmp.characterSpacing = 3f;
        tmp.raycastTarget   = false;
        tmp.margin          = new Vector4(4f, 0f, 4f, 0f); // matches card content's own left/right inset
    }

    void BuildCard(LevelEntry level)
    {
        bool unlocked  = forceUnlockAll || SaveManager.IsLevelUnlocked(level.number);
        bool completed = SaveManager.IsLevelCompleted(level.number);
        string label = unlocked ? level.title.ToUpperInvariant() : level.title.ToUpperInvariant() + " — LOCKED";

        var (btn, rt) = CreateButton(contentRt, label, CardWidth, CardHeight(),
            unlocked ? () => ShowDescriptionPopup(level) : (Action)null, locked: !unlocked);
        btn.interactable = unlocked;

        // A completed level is always unlocked (see SaveManager.
        // IsLevelUnlocked), so this and the locked look above never both apply.
        if (completed) AddCompletedBadge(rt);

        var group = btn.gameObject.AddComponent<CanvasGroup>();
        carousel.Register(rt, group);
    }

    // Small pixel-art checkmark badge, top-right corner of a completed
    // level's card — a square plate (same procedurally-drawn look as every
    // other panel here, see PlateSpriteBuilder) with a blocky checkmark icon
    // (see CheckmarkIconBuilder) centered on top, tinted the same "success"
    // green used elsewhere in the HUD (see GameTheme.Success).
    const float BadgeSize = 44f;

    void AddCompletedBadge(RectTransform card)
    {
        var go = new GameObject("CompletedBadge", typeof(RectTransform));
        go.transform.SetParent(card, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(1f, 1f);
        rt.anchorMax        = new Vector2(1f, 1f);
        rt.pivot            = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-14f, -14f);
        rt.sizeDelta        = new Vector2(BadgeSize, BadgeSize);

        var plateImg = go.AddComponent<Image>();
        plateImg.sprite = PlateSpriteBuilder.GetSprite(
            new Color(0.05f, 0.10f, 0.08f), GameTheme.Success, new Color(0.02f, 0.03f, 0.05f),
            borderThickness: 2, chamfer: 4, pixelScale: 4);
        plateImg.type          = Image.Type.Sliced;
        plateImg.raycastTarget = false;

        var checkGo = new GameObject("Check", typeof(RectTransform));
        checkGo.transform.SetParent(go.transform, false);
        var checkImg = checkGo.AddComponent<Image>();
        checkImg.sprite         = CheckmarkIconBuilder.GetSprite(Color.white);
        checkImg.preserveAspect = true;
        checkImg.raycastTarget  = false;
        var crt = checkGo.GetComponent<RectTransform>();
        crt.anchorMin        = new Vector2(0.5f, 0.5f);
        crt.anchorMax        = new Vector2(0.5f, 0.5f);
        crt.pivot            = new Vector2(0.5f, 0.5f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta        = new Vector2(BadgeSize * 0.6f, BadgeSize * 0.6f);
    }

    // Click FX already played (see CreateButton) by the time this runs. The
    // actual scene swap goes through LoadingScreenManager instead of a bare
    // SceneManager.LoadScene — GameBootstrap's setup cost (device meshes,
    // managers, HUD) is enough to hitch, so it's masked behind a loading
    // screen rather than left as a visible freeze.
    void OnLevelClicked(LevelEntry level)
    {
        LevelManager.PendingLevelNumber = level.number;
        panelSlide.SlideOut(() => LoadingScreenManager.LoadScene(level.sceneName, "LOADING " + level.title.ToUpperInvariant()));
    }

    // ── Shared dynamic card button builder ──────────────────────────────────
    // Stays here (rather than moving into LevelSelectShellBuilder) since it's
    // only ever used for the genuinely dynamic level cards — locked/unlocked/
    // completed state depends on live SaveManager progress, not something a
    // static bake could capture. The scaffold's own static buttons
    // (Back/Cancel/Play) use LevelSelectShellBuilder.CreateStaticButton instead.

    // A locked button is the literal same BlankButton art as every other
    // button — same sprite, same border, same corners — just dimmed with a
    // flat grey multiply tint, the same idea SidebarButtonLock.SetLocked
    // already uses elsewhere in the project for a locked/unavailable button.
    static readonly Color LockedTint = new Color(0.5f, 0.5f, 0.5f, 1f);
    static readonly Color LockedText = new Color(0.68f, 0.68f, 0.72f, 1f);

    (Button, RectTransform) CreateButton(Transform parent, string label, float width, float height,
                                          Action onClick, bool locked = false)
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

        // Locked buttons skip the glow entirely — nothing should ever pulse
        // or draw attention to a card the player can't act on. Glow is a
        // child of Visual (not a sibling) — see MainMenuShellBuilder.
        // CreateMenuButton's matching comment for why (hand-editability) and
        // how MenuButtonFX compensates for it.
        Image glowImg = null;
        if (!locked)
        {
            var glowGo = new GameObject("Glow", typeof(RectTransform));
            glowGo.transform.SetParent(visualGo.transform, false);
            glowImg = glowGo.AddComponent<Image>();
            glowImg.raycastTarget = false;
            var grt = glowGo.GetComponent<RectTransform>();
            grt.anchorMin        = new Vector2(0.5f, 0.5f);
            grt.anchorMax        = new Vector2(0.5f, 0.5f);
            grt.pivot            = new Vector2(0.5f, 0.5f);
            grt.anchoredPosition = Vector2.zero;
            grt.sizeDelta        = new Vector2(width * 1.15f, height * 1.6f);
        }

        var bgGo = new GameObject("Background", typeof(RectTransform));
        bgGo.transform.SetParent(visualGo.transform, false);
        var bgImg = bgGo.AddComponent<Image>();
        var brt = bgGo.GetComponent<RectTransform>();
        brt.anchorMin = Vector2.zero;
        brt.anchorMax = Vector2.one;
        brt.offsetMin = Vector2.zero;
        brt.offsetMax = Vector2.zero;

        bgImg.sprite = normalSprite;
        bgImg.preserveAspect = true;
        if (locked) bgImg.color = LockedTint;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(visualGo.transform, false);
        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.text          = label;
        tmp.font          = menuFont;
        tmp.fontSize      = 34;
        // Auto-shrinks instead of overflowing past the button's edges — the
        // fixed 34 was sized for a bare title ("IP ALLOCATION"), not the
        // longer "TITLE — LOCKED" variant (see BuildCard), which almost
        // always wraps to two lines. TMP's autosize solver fits its own
        // reported line-height metrics inside the rect, but this pixel font's
        // actual glyph ink runs taller than that reported metric — at a max
        // of 34 a wrapped two-line label still visibly overshot the padded
        // rect (see the vertical padding below). Capping locked labels' max
        // well below the unlocked (always single-line) cap, on top of that
        // padding, is what actually keeps two lines clear of the border art.
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin   = 14;
        tmp.fontSizeMax   = locked ? 22 : 34;
        tmp.color         = locked ? LockedText : textColor;
        tmp.alignment     = TextAlignmentOptions.Center;
        tmp.fontStyle     = FontStyles.Bold;
        tmp.raycastTarget = false;
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        // Padding on all four sides so text never sits flush against — or
        // auto-shrinks right up to — the button's own edge.
        lrt.offsetMin = new Vector2(20f, 22f);
        lrt.offsetMax = new Vector2(-20f, -22f);

        var btn = go.AddComponent<Button>();
        btn.transition    = Selectable.Transition.None;
        btn.targetGraphic = bgImg;

        // Locked buttons get no MenuButtonFX at all — not just a disabled
        // one. Button.interactable = false (set by callers) only blocks
        // clicks; MenuButtonFX's hover swap runs off IPointerEnter/Exit
        // directly, which the EventSystem still dispatches to a non-
        // interactable Button's raycastable graphic. Not adding the
        // component is what actually removes the hover feedback.
        if (!locked)
        {
            var fx = go.AddComponent<MenuButtonFX>();
            fx.visualRect      = vrt;
            fx.backgroundImage = bgImg;
            fx.glowImage       = glowImg;
            fx.normalSprite    = normalSprite;
            fx.hoverSprite     = hoverSprite;
            fx.glowColor       = GameTheme.ElectricBlue;
            fx.Init();

            btn.onClick.AddListener(() => fx.PlayClickThen(onClick));
        }

        return (btn, rt);
    }
}
