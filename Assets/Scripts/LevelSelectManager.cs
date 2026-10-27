using System;
using System.Collections;
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
    const float ViewportHeight = 560f; // matches LevelSelectShellBuilder's ViewportHeight — fallback if the shell has no ScrollRect reference (see RefreshCards)
    const float HeaderHeight   = 34f;  // category header row (see BuildCategoryHeader)
    const float HeaderTopGap   = 24f;  // extra space above every header but the first

    // Level to center the carousel on the next time Open() runs — 0 for none.
    // Set right before leaving a level (LoadingScreenManager.ExitToLevelSelect)
    // and consumed by Open(), the same "plain static field, consumed on next
    // load" idiom as MainMenuBootstrap.OpenLevelSelectOnLoad. A completed
    // level hands focus on to the one after it (see TakeRestCard), so
    // finishing a level lands with the next one centered — after any new
    // stars/unlock it earned have played out (see Celebrate).
    public static int FocusLevelOnOpen;
    // The carousel's VerticalLayoutGroup stretches every card to the content
    // width minus its CardSideInset padding regardless of this value
    // (childControlWidth/childForceExpandWidth), so this only feeds
    // CardHeight()'s aspect-based height; it has to equal the actual rendered
    // width or the background sprite's preserveAspect scaling no longer
    // matches the card's real size.
    const float CardWidth      = LevelSelectShellBuilder.CardWidth;
    const int   CardSideInset  = LevelSelectShellBuilder.CardSideInset;

    TMP_FontAsset menuFont;
    Color textColor = Color.white;
    Action onBack;
    CanvasGroup backgroundGroup;

    LevelSelectShellView view;
    GameObject panel;
    ZoomPanel panelSlide;
    RectTransform contentRt;
    LevelCarousel carousel;
    Sprite normalSprite;
    Sprite hoverSprite;
    Sprite starEarnedSprite;
    Sprite starUnearnedSprite;
    TextMeshProUGUI starTotalTmp;
    RectTransform starTotalIconRt;
    int shownStarTotal; // what the star total reads right now — behind the save while Celebrate is still filling stars in

    // Where the celebration's spark bursts spawn — same canvas-root parent
    // MenuButtonFX.Init picks for its own sparks.
    RectTransform sparkParent;
    Camera sparkCamera;
    CanvasGroup carouselInputGroup; // blocks card clicks/drags/wheel while Celebrate plays

    // ── Level description popup (see LevelSelectShellBuilder/ShowDescriptionPopup) ──
    GameObject descDim;
    ZoomPanel descZoom;
    PanelArrivalGlow descGlow;
    TextMeshProUGUI descTitleTmp;
    TextMeshProUGUI descCategoryTmp;
    TextMeshProUGUI descBodyTmp;
    List<Image> descIconSlots;
    readonly Dictionary<string, Sprite> iconCache = new Dictionary<string, Sprite>();

    readonly List<LevelEntry> levels = new List<LevelEntry>();
    readonly Dictionary<int, LevelCard> cardsByLevel = new Dictionary<int, LevelCard>();

    // One carousel card's live pieces — kept so Celebrate can restyle a card
    // that's already built (fill its stars, unlock it) instead of rebuilding it.
    class LevelCard
    {
        public LevelEntry level;
        public RectTransform root;
        public Button button;
        public MenuButtonFX fx;          // null on a card built locked — see CreateButton
        public Image background;
        public TextMeshProUGUI label;
        public RectTransform starRow;    // null on a card built locked
        public readonly List<Image> stars = new List<Image>(); // each star's icon, in order; its parent is the slot that pops
    }

    /// <summary>
    /// Call once after the shell (LevelSelectShellBuilder's output — this
    /// component already lives on its root) is resolved, before Open().
    /// Wires every click handler and MenuButtonFX.Init() against the shell's
    /// pre-built objects, then loads the level list and builds the cards.
    /// </summary>
    public void Build(TMP_FontAsset font, Color buttonTextColor, string firstLevelSceneName, CanvasGroup backgroundGroup, Action onBack)
    {
        menuFont         = font;
        textColor        = buttonTextColor;
        this.onBack      = onBack;
        this.backgroundGroup = backgroundGroup;

        view       = GetComponent<LevelSelectShellView>();
        panel      = gameObject;
        panelSlide = view.PanelSlide;
        contentRt  = view.ContentRt;
        carousel   = view.Carousel;

        normalSprite = Resources.Load<Sprite>(SpriteFolder + "BlankButton");
        hoverSprite  = Resources.Load<Sprite>(SpriteFolder + "BlankButton_Hover");
        starEarnedSprite   = StarIconBuilder.GetSprite(StarIconBuilder.EarnedColor);
        starUnearnedSprite = StarIconBuilder.GetEngravedSprite();

        WireButton(view.BackButton, ClosePanel);
        WireButton(view.CancelButton, HideDescriptionPopup);
        WireButton(view.PlayButton, () => { if (currentDescLevel.HasValue) OnLevelClicked(currentDescLevel.Value); });

        descDim         = view.DescDim;
        descZoom        = view.DescZoom != null ? view.DescZoom : UpgradeLegacyDescPopup(descDim);
        descGlow        = PanelArrivalGlow.Attach(descZoom.gameObject, GameTheme.ElectricBlue);
        descTitleTmp    = view.DescTitleTmp;
        descCategoryTmp = view.DescCategoryTmp;
        descBodyTmp     = view.DescBodyTmp;
        descIconSlots   = view.DescIconSlots;

        var canvas = GetComponentInParent<Canvas>(true);
        if (canvas != null)
        {
            sparkParent = canvas.transform as RectTransform;
            sparkCamera = canvas.worldCamera;
        }
        if (view.ScrollRect != null)
        {
            // Not `GetComponent() ?? AddComponent()` — see MainMenuBootstrap.ResolveCanvas.
            carouselInputGroup = view.ScrollRect.GetComponent<CanvasGroup>();
            if (carouselInputGroup == null) carouselInputGroup = view.ScrollRect.gameObject.AddComponent<CanvasGroup>();
        }

        LoadLevels(firstLevelSceneName);
        var starTotalRt = BuildStarTotal();
        SplitHeaderFromZoom(view.BackButton?.Button != null ? (RectTransform)view.BackButton.Button.transform : null,
                            panel.transform.Find("Title") as RectTransform,
                            starTotalRt);
        RefreshCards();

        panel.SetActive(false);
    }

    // ── Header chrome (Back, title, star total) ─────────────────────────────
    // Drops in from above the screen while the rest of the panel zooms in,
    // and lifts back out while it zooms out, instead of scaling along with
    // it — so it can't stay under the root's own ZoomPanel. Everything else
    // (carousel frame + carousel, or anything hand-added to the baked
    // prefab) moves into a full-screen ZoomBody child that takes the zoom
    // over. Done here at runtime rather than in LevelSelectShellBuilder so an
    // already-baked (and possibly hand-edited) LevelSelectShell.prefab picks
    // it up without a re-bake — same idea as UpgradeLegacyDescPopup.

    const float HeaderStagger         = 0.07f; // left to right: Back, title, star total
    const float HeaderSlideInDuration = 0.4f;
    const float HeaderSlideOutDuration = 0.22f; // leaving is quicker than arriving, same as the HUD's own exit
    const float HeaderOffscreenMargin = 40f;   // clears the Back button's glow, which overhangs its own rect

    readonly List<SlidePanel> headerSlides = new List<SlidePanel>();
    Coroutine headerRoutine;

    void SplitHeaderFromZoom(params RectTransform[] header)
    {
        var headerSet = new HashSet<Transform>();
        foreach (var rt in header) if (rt != null) headerSet.Add(rt);

        var bodyGo = new GameObject("ZoomBody", typeof(RectTransform));
        var bodyRt = (RectTransform)bodyGo.transform;
        bodyRt.SetParent(panel.transform, false);
        bodyRt.anchorMin = Vector2.zero;
        bodyRt.anchorMax = Vector2.one;
        bodyRt.offsetMin = Vector2.zero;
        bodyRt.offsetMax = Vector2.zero;

        // The description dim stays out too — it's a full-screen backdrop
        // over everything, never visible while the panel itself zooms.
        var toMove = new List<Transform>();
        foreach (Transform child in panel.transform)
            if (child != bodyRt && !headerSet.Contains(child) && (descDim == null || child != descDim.transform))
                toMove.Add(child);

        // Body takes the first moved child's slot, so draw order is kept.
        if (toMove.Count > 0) bodyRt.SetSiblingIndex(toMove[0].GetSiblingIndex());
        // Same-rect parent, so every child's local anchors/offsets stay valid as-is.
        foreach (var child in toMove) child.SetParent(bodyRt, false);

        Destroy(panelSlide); // the root's own zoom would still scale the header
        panel.transform.localScale = Vector3.one;
        panelSlide = bodyGo.AddComponent<ZoomPanel>();
        view.PanelSlide = panelSlide;

        foreach (var rt in header)
        {
            if (rt == null) continue;
            var slide = rt.gameObject.AddComponent<SlidePanel>();
            slide.Configure(SlideEdge.Top, DistanceAboveFrame(rt), HeaderSlideInDuration);
            // Same streaks the main menu's own logo/buttons leave as they
            // slide (see MainMenuShellBuilder), sized down to match them.
            rt.gameObject.AddComponent<SlideTrail>().Configure(0.45f);
            headerSlides.Add(slide);
        }
    }

    // How far above its resting spot a top-anchored element has to start to
    // sit fully out of frame: the part of it below its pivot, plus however
    // far down from the top edge it rests. Independent of screen size, since
    // it's measured from the top anchor.
    static float DistanceAboveFrame(RectTransform rt) =>
        rt.rect.height * rt.pivot.y - rt.anchoredPosition.y + HeaderOffscreenMargin;

    // Zoom-in and header drop-in play together; onComplete fires once both
    // have landed — the mirror of PlayExit.
    void PlayEnter(Action onComplete = null)
    {
        // All snapped out of frame together up front — see
        // SlidePanel.SnapForSlideIn for why a staggered group needs this.
        foreach (var slide in headerSlides) slide.SnapForSlideIn();
        int pending = 2;
        void Done() { if (--pending == 0) onComplete?.Invoke(); }
        panelSlide.ZoomIn(Done);
        RestartHeaderRoutine(StaggerHeader(slideOut: false, Done));
    }

    // Zoom-out and header lift-off play together; onComplete fires once
    // both have finished.
    void PlayExit(Action onComplete)
    {
        int pending = 2;
        void Done() { if (--pending == 0) onComplete?.Invoke(); }
        panelSlide.ZoomOut(Done);
        RestartHeaderRoutine(StaggerHeader(slideOut: true, Done));
    }

    void RestartHeaderRoutine(IEnumerator routine)
    {
        if (headerRoutine != null) StopCoroutine(headerRoutine);
        headerRoutine = StartCoroutine(routine);
    }

    IEnumerator StaggerHeader(bool slideOut, Action onComplete)
    {
        if (headerSlides.Count == 0) { onComplete?.Invoke(); yield break; }

        for (int i = 0; i < headerSlides.Count; i++)
        {
            // Equal durations, so the last one started is the last to land.
            bool isLast = i == headerSlides.Count - 1;
            Action done = isLast ? onComplete : null;
            if (slideOut) headerSlides[i].SlideOut(done, HeaderSlideOutDuration);
            else headerSlides[i].SlideIn(done);

            if (!isLast) yield return new WaitForSecondsRealtime(HeaderStagger);
        }
        headerRoutine = null;
    }

    // A shell baked (MainMenuUIBaker) before the description popup switched
    // from sliding up to zooming in still carries the old SlidePanel and no
    // DescZoom — swap it in place here instead of requiring a re-bake, which
    // would also wipe any hand edits made to the baked prefabs (same idea as
    // MainMenuCanvasBuilder.UpgradeBackground). The popup plate is the only
    // SlidePanel under descDim.
    static ZoomPanel UpgradeLegacyDescPopup(GameObject descDim)
    {
        var legacySlide = descDim.GetComponentInChildren<SlidePanel>(true);
        var popup = legacySlide.gameObject;
        Destroy(legacySlide);
        return popup.AddComponent<ZoomPanel>();
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
            new LevelEntry(8, "Free Build: Office LAN", firstLevelSceneName, "Challenges",
                "A blank table and no prescribed layout. Place at least three PCs, wire and " +
                "address them however you like, and get every PC talking to every other PC.",
                "PC_Icon", "Switch_Icon"),
            new LevelEntry(9, "Multi-Department Network", firstLevelSceneName, "Challenges",
                "Scale it up to three departments — six PCs, three Switches, and a Router using " +
                "every one of its interfaces. Bigger table, same rules: cable, address, and route " +
                "between all three subnets yourself.",
                "PC_Icon", "Switch_Icon", "Router_Icon"),
            new LevelEntry(10, "Resilience Challenge", firstLevelSceneName, "Challenges",
                "Build a network that keeps working no matter which single cable gets cut. A " +
                "straight chain won't survive — you'll need real redundancy in the backbone.",
                "Switch_Icon", "Cable"),
            new LevelEntry(11, "VLSM: Multi-Site Network", firstLevelSceneName, "Challenges",
                "Three departments, one shared address block. Instead of a fresh subnet each, " +
                "carve 192.168.1.0/24 into three differently-sized pieces that actually fit what " +
                "each department needs.",
                "Router_Icon", "PC_Icon", "Switch_Icon"),
        };
    }

    /// <summary>Shows the level-select screen.</summary>
    public void Open()
    {
        StopCelebration();
        int generation = celebrationGeneration;

        // Diffed before the rebuild, which builds any card with unseen
        // progress in its old look for Celebrate to carry forward.
        var unseen = TakeUnseenProgress();
        RefreshCards(unseen); // also picks up any progress made since the screen was last built/shown
        panel.SetActive(true);

        // After SetActive — the layout pass CenterOn forces only sees active cards.
        var steps     = CelebrationSteps(unseen);
        var restCard  = TakeRestCard();
        var startCard = steps.Count > 0 ? steps[0].card.root : restCard;
        if (startCard != null) carousel.CenterOn(startCard);

        if (steps.Count == 0) { PlayEnter(); return; }

        SetCarouselInputBlocked(true);
        PlayEnter(() =>
        {
            // Skipped if the panel was closed (or reopened) before it finished arriving.
            if (generation == celebrationGeneration)
                celebrationRoutine = StartCoroutine(Celebrate(steps, restCard));
        });
    }

    // The card the carousel comes to rest on for this showing: the level
    // just left, or the one after it if that level's now complete — so
    // finishing a level lands with the next one centered. With no level just
    // left (opened from the main menu's Play, or the level left isn't in the
    // list, e.g. LevelManager.DevSandboxLevel), it's the furthest the player
    // has got instead — see NextUnplayedIndex. Consumes FocusLevelOnOpen.
    RectTransform TakeRestCard()
    {
        int levelNumber = FocusLevelOnOpen;
        FocusLevelOnOpen = 0;

        int index = levels.FindIndex(l => l.number == levelNumber);
        if (index < 0) index = NextUnplayedIndex();
        // A completed level moves focus on to the one after it; an
        // unfinished one (left via the Home button) or the last level in
        // the list stays focused on itself.
        else if (SaveManager.IsLevelCompleted(levelNumber) && index + 1 < levels.Count) index++;

        if (index < 0) return null; // empty level list
        return cardsByLevel.TryGetValue(levels[index].number, out var card) ? card.root : null;
    }

    // The first level the player can play but hasn't finished yet, or the
    // last level once every one's done; -1 for an empty list.
    int NextUnplayedIndex()
    {
        int index = levels.FindIndex(l => SaveManager.IsLevelUnlocked(l.number) && !SaveManager.IsLevelCompleted(l.number));
        return index >= 0 ? index : levels.Count - 1;
    }

    // Rebuilds the card list from scratch — cheap at this list size (a handful
    // of levels) and needed both for the initial Build() and every Open(),
    // since completed/locked state can change between shows. Cards are
    // grouped under a section header per distinct category,
    // in the order each category first appears in `levels` — a header is just
    // another (unregistered, so LevelCarousel's focus/scale/snap logic never
    // touches it) child of the same VerticalLayoutGroup, so it takes up real
    // row space and the carousel's scroll range/snap math accounts for it
    // automatically like any card. `unseen` (see TakeUnseenProgress) holds
    // the old look of any level whose progress Celebrate is about to play.
    void RefreshCards(Dictionary<int, ShownProgress> unseen = null)
    {
        for (int i = contentRt.childCount - 1; i >= 0; i--)
        {
            // Destroy() only happens at end of frame — deactivating first
            // drops the old rows from the layout right away, so Open()'s
            // same-frame layout pass (LevelCarousel.CenterOn) sees only the
            // new ones.
            var child = contentRt.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
        carousel.Clear(); // drop the just-destroyed cards' registrations, not just the GameObjects
        cardsByLevel.Clear();

        // Top/bottom padding sized so the first and last card can each reach
        // the viewport's center — LevelCarousel snaps whichever card is
        // nearest to center, and an end card that can't get there rests
        // off-center (shrunk and faded) at the scroll limit. The top pad also
        // subtracts the category header row (plus the layout spacing after
        // it) sitting above the first card. Can't be baked as a fixed value —
        // see LevelSelectShellBuilder.BuildCarousel.
        if (view.ContentLayout != null)
        {
            const float SnugPadding = 20f;
            var viewport = view.ScrollRect != null ? view.ScrollRect.viewport : null;
            float viewportHeight = viewport != null ? viewport.rect.height : ViewportHeight;
            float centerPad = (viewportHeight - CardHeight()) / 2f;
            float aboveFirstCard = levels.Count > 0 && levels[0].category != null
                ? HeaderHeight + view.ContentLayout.spacing
                : 0f;
            int top    = Mathf.RoundToInt(Mathf.Max(SnugPadding, centerPad - aboveFirstCard));
            int bottom = Mathf.RoundToInt(Mathf.Max(SnugPadding, centerPad));
            view.ContentLayout.padding = new RectOffset(CardSideInset, CardSideInset, top, bottom);
        }

        shownStarTotal = 0;
        string lastCategory = null;
        foreach (var level in levels)
        {
            if (level.category != lastCategory)
            {
                BuildCategoryHeader(level.category, isFirst: lastCategory == null);
                lastCategory = level.category;
            }
            var now = ShownProgress.Of(level.number);
            BuildCard(level, now, unseen != null && unseen.TryGetValue(level.number, out var before) ? before : now);
        }

        RefreshStarTotal();
    }

    // Click FX already played (see CreateButton) by the time this runs. Zooms
    // the panel out (and lifts the header off, see PlayExit), then hides it
    // and hands off to onBack (MainMenuBootstrap slides the main menu back in
    // — see ShowMainMenu). Cuts short any celebration still playing — the
    // next Open() rebuilds every card from the save anyway.
    void ClosePanel()
    {
        StopCelebration();
        PlayExit(() =>
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
        descZoom.ZoomIn(descGlow.Play);
    }

    void HideDescriptionPopup()
    {
        descZoom.ZoomOut(() => descDim.SetActive(false));
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
        var go = new GameObject("Header_" + label, typeof(RectTransform));
        go.transform.SetParent(contentRt, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, HeaderHeight + (isFirst ? 0f : HeaderTopGap));

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

    // `now` is the level's saved progress; `shown` is the look to build it
    // in — the same as `now`, except for a level with progress Celebrate is
    // about to play, which starts out in its old look. A card is always
    // *built* for its current state (FX, glow, star row all present on a
    // now-unlocked card), then dressed in the old look over the top, so
    // Celebrate only ever restyles it — never rebuilds it mid-show.
    void BuildCard(LevelEntry level, ShownProgress now, ShownProgress shown)
    {
        var card = CreateButton(contentRt, level, CardWidth, CardHeight(),
            now.unlocked ? () => ShowDescriptionPopup(level) : (Action)null, locked: !now.unlocked);

        // No separate "completed" mark — a played level shows it through its
        // earned stars.
        int stars = Mathf.Min(shown.stars, now.stars);
        if (now.unlocked) AddStarRow(card, stars);
        shownStarTotal += stars;

        ApplyCardLook(card, locked: !now.unlocked || !shown.unlocked);

        var group = card.root.gameObject.AddComponent<CanvasGroup>();
        carousel.Register(card.root, group);
        cardsByLevel[level.number] = card;
    }

    // ── Star ratings ─────────────────────────────────────────────────────────
    // Each unlocked card shows its level's best rating (SaveManager.GetStars)
    // as a row of earned/unearned stars under its title, and the panel's
    // top-right corner totals them against the maximum the level list can
    // award — both read live progress, so both are rebuilt by RefreshCards.

    // A whole multiple of the star's grid, so every star pixel is an even
    // 4x4 block — a step up from the 3x3 (StarIconBuilder.DisplaySize) the
    // star total and level-complete screen use.
    const float CardStarSize     = StarIconBuilder.GridSize * 4;
    const float CardStarSpacing  = 10f;
    const float CardStarBottom   = 38f; // keeps the holes' bright bottom lip clear of the card art's own bottom border
    const float CardStarTitleGap = 6f;

    // The top-right star total's plate — dark and gold-bordered, since it
    // sits on the dark menu background rather than on a card.
    static Sprite StarPlateSprite() => PlateSpriteBuilder.GetSprite(
        new Color(0.10f, 0.08f, 0.04f), StarIconBuilder.EarnedColor, new Color(0.02f, 0.03f, 0.05f),
        borderThickness: 2, chamfer: 4, pixelScale: 4);

    // A row of all MaxStarsPerLevel slots along the card's bottom edge,
    // straight on the card face — earned ones are gold stars, the rest are
    // star-shaped holes dug into the card (StarIconBuilder.GetEngravedSprite)
    // that an earned star would fill. Parented under the card's Visual (not
    // the card root) so it scales with the title through MenuButtonFX's
    // hover/pulse/click animations instead of drifting against it. Added
    // after Background/Label, so it draws on top. The title moves up above
    // it in StyleLabel.
    void AddStarRow(LevelCard card, int earned)
    {
        var parent = card.root.Find("Visual") ?? card.root;
        int max = SaveManager.MaxStarsPerLevel;
        float rowWidth = max * CardStarSize + (max - 1) * CardStarSpacing;

        var go = new GameObject("StarRow", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0f);
        rt.anchorMax        = new Vector2(0.5f, 0f);
        rt.pivot            = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, CardStarBottom);
        rt.sizeDelta        = new Vector2(rowWidth, CardStarSize);
        card.starRow = rt;

        for (int i = 0; i < max; i++)
        {
            // A bare centered slot holding the icon, so Celebrate's reveal
            // (see FlashIn) can pop it in place and slip a glow in behind it.
            var slotGo = new GameObject("Star" + i, typeof(RectTransform));
            slotGo.transform.SetParent(go.transform, false);
            var srt = slotGo.GetComponent<RectTransform>();
            srt.anchorMin        = new Vector2(0f, 0.5f);
            srt.anchorMax        = new Vector2(0f, 0.5f);
            srt.pivot            = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = new Vector2(i * (CardStarSize + CardStarSpacing) + CardStarSize / 2f, 0f);
            srt.sizeDelta        = new Vector2(CardStarSize, CardStarSize);

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(slotGo.transform, false);
            var irt = iconGo.GetComponent<RectTransform>();
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = Vector2.zero;
            irt.offsetMax = Vector2.zero;
            var img = iconGo.AddComponent<Image>();
            img.sprite         = i < earned ? starEarnedSprite : starUnearnedSprite;
            img.preserveAspect = true;
            img.raycastTarget  = false;
            card.stars.Add(img);
        }
    }

    // Top-right counterpart of the Back button (same 48px corner inset and
    // height). Built here at runtime rather than in LevelSelectShellBuilder
    // because its value is live progress, like the cards — which also means
    // an already-baked LevelSelectShell.prefab picks it up without a re-bake.
    // Sits on StarPlateSprite.
    const float StarTotalWidth    = 200f;
    const float StarTotalHeight   = 64f;
    const float StarTotalIconSize = StarIconBuilder.DisplaySize;

    RectTransform BuildStarTotal()
    {
        var go = new GameObject("StarTotal", typeof(RectTransform));
        go.transform.SetParent(panel.transform, false);
        // Behind the description popup's full-screen dim, not on top of it.
        if (descDim != null && descDim.transform.parent == panel.transform)
            go.transform.SetSiblingIndex(descDim.transform.GetSiblingIndex());
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(1f, 1f);
        rt.anchorMax        = new Vector2(1f, 1f);
        rt.pivot            = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-48f, -48f);
        rt.sizeDelta        = new Vector2(StarTotalWidth, StarTotalHeight);

        var plateImg = go.AddComponent<Image>();
        plateImg.sprite        = StarPlateSprite();
        plateImg.type          = Image.Type.Sliced;
        plateImg.raycastTarget = false;

        var iconGo = new GameObject("Icon", typeof(RectTransform));
        iconGo.transform.SetParent(go.transform, false);
        var iconImg = iconGo.AddComponent<Image>();
        iconImg.sprite         = starEarnedSprite;
        iconImg.preserveAspect = true;
        iconImg.raycastTarget  = false;
        var irt = iconGo.GetComponent<RectTransform>();
        irt.anchorMin        = new Vector2(0f, 0.5f);
        irt.anchorMax        = new Vector2(0f, 0.5f);
        irt.pivot            = new Vector2(0f, 0.5f);
        irt.anchoredPosition = new Vector2(16f, 0f);
        irt.sizeDelta        = new Vector2(StarTotalIconSize, StarTotalIconSize);
        starTotalIconRt = irt;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        starTotalTmp = labelGo.AddComponent<TextMeshProUGUI>();
        starTotalTmp.font             = menuFont;
        starTotalTmp.fontSize         = 30;
        starTotalTmp.enableAutoSizing = true;
        starTotalTmp.fontSizeMin      = 16;
        starTotalTmp.fontSizeMax      = 30;
        starTotalTmp.color            = textColor;
        starTotalTmp.alignment        = TextAlignmentOptions.Center;
        starTotalTmp.fontStyle        = FontStyles.Bold;
        starTotalTmp.raycastTarget    = false;
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(16f + StarTotalIconSize + 6f, 8f);
        lrt.offsetMax = new Vector2(-16f, -8f);
        return rt;
    }

    // Best rating per level, summed — replaying a level for a better rating
    // replaces its old one (see SaveManager.SetStars), so the total can never
    // exceed levels × MaxStarsPerLevel. Reads shownStarTotal (what the cards
    // show, tallied by BuildCard) rather than the save, so it counts up
    // alongside Celebrate's star fill instead of jumping ahead of it.
    void RefreshStarTotal()
    {
        if (starTotalTmp == null) return;
        starTotalTmp.text = $"{shownStarTotal}/{levels.Count * SaveManager.MaxStarsPerLevel}";
        if (starTotalIconRt != null) starTotalIconRt.localScale = Vector3.one; // in case a close cut a PunchScale short
    }

    // Click FX already played (see CreateButton) by the time this runs. The
    // panel zooms out FIRST, then the corner "loading" readout plays over
    // the bare persistent background, then the scene swap itself happens
    // (LoadingScreenManager.ActivateAndFade) — background stays on screen
    // the whole time, so the visible order reads: card clicked → panel
    // gone → plain background with a loading message → level's in. Used to
    // preload behind the readout WHILE the panel was still open and only
    // zoom out once ready — closer to the destination technically, but it
    // meant the panel was still fully up during the loading beat instead of
    // the background having its own quiet moment first.
    void OnLevelClicked(LevelEntry level)
    {
        LevelManager.PendingLevelNumber = level.number;
        // Instant, not HideDescriptionPopup()'s animated zoom-out — that
        // leaves its 60%-black DescriptionDim backdrop fully visible for the
        // whole animation (uncoordinated with the sequence below), which is
        // exactly what was reading as a "blackout" through this transition.
        // HideDescriptionPopup's zoom-out is still right for the Cancel
        // button, which stays on this screen; here we're leaving it for
        // good, so there's no reason for the dim to linger at all.
        descDim.SetActive(false);
        PlayExit(() =>
        {
            LoadingScreenManager.PreloadSceneCorner(level.sceneName, level.title.ToUpperInvariant(), op =>
                LoadingScreenManager.ActivateAndFade(op, backgroundGroup));
        });
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

    // Vertical padding between the label and the card's top/bottom edge —
    // see CreateButton's label padding comment.
    const float LabelInset = 22f;

    // Only builds the card's structure — its locked/live look (tint, label
    // text/size, star row) is StyleLabel/ApplyCardLook's, applied by
    // BuildCard afterward and re-applied by PlayUnlock, so one card can
    // change look in place. `locked` here decides what the card is *built*
    // with: a card that's locked for good gets no glow or MenuButtonFX at
    // all, while a playable card gets both even if it starts out dressed in
    // its old locked look (see BuildCard).
    LevelCard CreateButton(Transform parent, LevelEntry level, float width, float height,
                           Action onClick, bool locked = false)
    {
        var card = new LevelCard { level = level };

        var go = new GameObject("Btn_" + level.title.ToUpperInvariant(), typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);
        card.root = rt;

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
        card.background = bgImg;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(visualGo.transform, false);
        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.font          = menuFont;
        tmp.fontSize      = 34;
        tmp.enableAutoSizing = true; // caps set per look — see StyleLabel
        tmp.fontSizeMin   = 14;
        tmp.alignment     = TextAlignmentOptions.Center;
        tmp.fontStyle     = FontStyles.Bold;
        tmp.raycastTarget = false;
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        // Padding on all four sides so text never sits flush against — or
        // auto-shrinks right up to — the button's own edge. The card art's
        // light face only starts ~50px in from the rect's sides (transparent
        // margin + blue border), so 84 keeps a long single-line title (see
        // StyleLabel's NoWrap) ~30px clear of the border instead of on it.
        // The bottom inset is StyleLabel's too — it moves up over a star row.
        lrt.offsetMin = new Vector2(84f, LabelInset);
        lrt.offsetMax = new Vector2(-84f, -LabelInset);
        card.label = tmp;

        var btn = go.AddComponent<Button>();
        btn.transition    = Selectable.Transition.None;
        btn.targetGraphic = bgImg;
        card.button = btn;

        // Locked buttons get no MenuButtonFX at all — not just a disabled
        // one. Button.interactable = false (set by ApplyCardLook) only blocks
        // clicks; MenuButtonFX's hover swap runs off IPointerEnter/Exit
        // directly, which the EventSystem still dispatches to a non-
        // interactable Button's raycastable graphic. Not adding the
        // component is what actually removes the hover feedback. (A playable
        // card still in its old locked look keeps its FX disabled instead —
        // a disabled behaviour gets no pointer events either.)
        if (!locked)
        {
            var fx = go.AddComponent<MenuButtonFX>();
            fx.visualRect      = vrt;
            fx.backgroundImage = bgImg;
            fx.glowImage       = glowImg;
            fx.normalSprite    = normalSprite;
            fx.hoverSprite     = hoverSprite;
            fx.glowColor       = GameTheme.ElectricBlue;
            // Subtle idle "breathing" scale so playable cards read as alive,
            // not static — amplitude is small enough (see MenuButtonFX) that
            // it never meaningfully moves the card's own clickable edge.
            fx.idlePulse       = true;
            fx.Init();
            card.fx = fx;

            btn.onClick.AddListener(() => fx.PlayClickThen(onClick));
        }

        return card;
    }

    // Locked or live look on an already-built card — the same art either
    // way, just dimmed (see LockedTint). What BuildCard dresses every card
    // in, and what PlayUnlock flips at the moment a card transforms.
    void ApplyCardLook(LevelCard card, bool locked)
    {
        card.button.interactable = !locked;
        if (card.fx != null) card.fx.enabled = !locked;
        card.background.color = locked ? LockedTint : Color.white;
        if (card.starRow != null) card.starRow.gameObject.SetActive(!locked);
        StyleLabel(card, locked);
    }

    void StyleLabel(LevelCard card, bool locked)
    {
        var tmp = card.label;
        string title = card.level.title.ToUpperInvariant();
        tmp.text = locked ? title + " — LOCKED" : title;
        // Auto-shrinks instead of overflowing past the button's edges — the
        // fixed 34 was sized for a bare title ("IP ALLOCATION"), not the
        // longer "TITLE — LOCKED" variant, which almost always wraps to two
        // lines. TMP's autosize solver fits its own reported line-height
        // metrics inside the rect, but this pixel font's actual glyph ink
        // runs taller than that reported metric — at a max of 34 a wrapped
        // two-line label still visibly overshot the padded rect (see
        // CreateButton's label padding). Capping locked labels' max well
        // below the unlocked (always single-line) cap, on top of that
        // padding, is what actually keeps two lines clear of the border art.
        // Unlocked titles never wrap: short ones grow to the 52 cap and long
        // ones ("MULTI-DEPARTMENT NETWORK") auto-shrink until the single line
        // fits the padded rect, so none runs past the button.
        tmp.fontSizeMax      = locked ? 22 : 52;
        tmp.textWrappingMode = locked ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        tmp.color            = locked ? LockedText : textColor;

        // Re-centered in the space above the star row, when one's showing,
        // instead of on the whole card.
        var lrt = tmp.rectTransform;
        bool aboveStars = !locked && card.starRow != null;
        lrt.offsetMin = new Vector2(lrt.offsetMin.x,
            aboveStars ? CardStarBottom + CardStarSize + CardStarTitleGap : LabelInset);
    }

    // ── Progress celebration ────────────────────────────────────────────────
    // Coming back from a level that earned something, the screen plays it
    // out instead of just showing the new state: centered on the level just
    // finished, its new stars drop in one at a time (star-yellow glow +
    // sparks, the star total ticking up); then the carousel glides down to the level that just unlocked, which
    // sits there still greyed out for a moment before bursting into its live
    // look with the same glow-and-spark entrance as the main menu's buttons.
    //
    // Nothing on the gameplay side is involved — every level writes its own
    // completion/stars to SaveManager as before, and this screen just notices
    // the difference against what it showed last time (TakeUnseenProgress).

    const float CelebrateStartDelay   = 0.25f; // beat after the panel lands, before anything moves
    const float StarStagger           = 0.2f;
    const float StarGlowScale         = 2.4f;  // glow size vs. the star — a star needs proportionally more than a wide card (width × 1.15)
    const float PreGlideHold          = 0.35f; // time to take in what just played before scrolling away
    const float GlideDuration         = 0.6f;
    const float LockedHold            = 0.45f; // the "split second" an unlocking card sits centered, still greyed out
    const float UnlockSqueezeDuration = 0.14f;
    const float UnlockTintDuration    = 0.15f;
    const float TotalPunchScale       = 1.35f;
    const float TotalPunchDuration    = 0.18f; // under StarStagger, so each punch finishes before the next star's

    Coroutine celebrationRoutine;
    Coroutine starTotalPunch;
    // Bumped by StopCelebration, so a celebration still waiting on Open()'s
    // entrance can tell it's been cancelled (panel closed/reopened) since.
    int celebrationGeneration;

    // A level's progress, as far as this screen shows it. Completion itself
    // has no mark of its own (the earned stars show a level's been played),
    // so it isn't tracked here.
    struct ShownProgress
    {
        public bool unlocked;
        public int  stars;

        public static ShownProgress Of(int levelNumber) => new ShownProgress
        {
            unlocked = SaveManager.IsLevelUnlocked(levelNumber),
            stars    = SaveManager.GetStars(levelNumber),
        };

        // Only forward progress counts — anything that went backwards (a
        // wiped save) just shows as it now is, with nothing to celebrate.
        public bool IsAheadOf(ShownProgress before) =>
            (unlocked && !before.unlocked) || stars > before.stars;
    }

    // Every level's progress as of the last Open(). Static so it survives the
    // scene reload a trip into a level and back goes through; null until this
    // screen has been shown once this session, so its first showing (the app
    // just launched) never celebrates progress that predates it.
    static Dictionary<int, ShownProgress> lastShown;

    // Levels whose progress has moved forward since the screen was last
    // shown, each mapped to how it looked then — and records what it shows
    // now, so the same progress is never celebrated twice.
    Dictionary<int, ShownProgress> TakeUnseenProgress()
    {
        var unseen = new Dictionary<int, ShownProgress>();
        var shown  = new Dictionary<int, ShownProgress>();
        foreach (var level in levels)
        {
            var now = ShownProgress.Of(level.number);
            shown[level.number] = now;
            if (lastShown != null && lastShown.TryGetValue(level.number, out var before) && now.IsAheadOf(before))
                unseen[level.number] = before;
        }
        lastShown = shown;
        return unseen;
    }

    // One card's unseen progress, in the order Celebrate plays it.
    readonly struct CelebrationStep
    {
        public readonly LevelCard card;
        public readonly ShownProgress before;
        public readonly ShownProgress now;

        public CelebrationStep(LevelCard card, ShownProgress before, ShownProgress now)
        {
            this.card   = card;
            this.before = before;
            this.now    = now;
        }

        public bool Unlocks    => now.unlocked && !before.unlocked;
        public bool GainsStars => now.stars > before.stars;
    }

    List<CelebrationStep> CelebrationSteps(Dictionary<int, ShownProgress> unseen)
    {
        var steps = new List<CelebrationStep>();
        foreach (var level in levels) // list order, so the carousel only ever scrolls one way
            if (unseen.TryGetValue(level.number, out var before) && cardsByLevel.TryGetValue(level.number, out var card))
                steps.Add(new CelebrationStep(card, before, ShownProgress.Of(level.number)));
        return steps;
    }

    // Open() has already centered the first step's card and blocked the
    // carousel's input; ends by gliding to restCard (TakeRestCard) if the
    // last step didn't already land there, then hands input back.
    IEnumerator Celebrate(List<CelebrationStep> steps, RectTransform restCard)
    {
        yield return new WaitForSecondsRealtime(CelebrateStartDelay);

        RectTransform centered = steps[0].card.root;
        foreach (var step in steps)
        {
            if (step.card.root != centered)
            {
                yield return GlideCarouselTo(step.card.root, LockedHold);
                centered = step.card.root;
            }
            if (step.Unlocks)    yield return PlayUnlock(step.card);
            if (step.GainsStars) yield return PlayStarGain(step.card, step.before.stars, step.now.stars);
        }

        if (restCard != null && restCard != centered)
            yield return GlideCarouselTo(restCard, 0f);

        SetCarouselInputBlocked(false);
        celebrationRoutine = null;
    }

    IEnumerator GlideCarouselTo(RectTransform card, float arrivalHold)
    {
        yield return new WaitForSecondsRealtime(PreGlideHold);
        carousel.GlideTo(card, GlideDuration);
        while (carousel.IsGliding) yield return null;
        if (arrivalHold > 0f) yield return new WaitForSecondsRealtime(arrivalHold);
    }

    // The still-locked card draws in, then bursts into its live look with
    // the main menu buttons' own entrance (MenuButtonFX.PlayRevealIn:
    // overshoot pop, blue glow, spark burst), its grey washing out as it goes.
    IEnumerator PlayUnlock(LevelCard card)
    {
        if (card.fx == null)
        {
            ApplyCardLook(card, locked: false);
            AudioManager.Instance?.PlaySfx(SfxId.LevelUnlocked);
            yield break;
        }

        // Draws in to exactly the reveal's start scale, so the pop picks up
        // right where this leaves off.
        yield return ScaleTo(card.fx.visualRect, MenuRevealFlash.StartScale, UnlockSqueezeDuration);

        ApplyCardLook(card, locked: false);
        AudioManager.Instance?.PlaySfx(SfxId.LevelUnlocked);
        StartCoroutine(TintTo(card.background, LockedTint, Color.white, UnlockTintDuration));
        card.fx.PlayRevealIn();
        yield return new WaitForSecondsRealtime(MenuRevealFlash.Duration);
    }

    // Each newly earned star drops into its hole in turn with the same
    // reveal as the main menu's buttons, glowing and sparking star-yellow,
    // and the header's star total ticks up with it.
    IEnumerator PlayStarGain(LevelCard card, int from, int to)
    {
        to = Mathf.Min(to, card.stars.Count);
        for (int i = from; i < to; i++)
        {
            var icon = card.stars[i];
            icon.sprite = starEarnedSprite;
            StartCoroutine(FlashIn((RectTransform)icon.transform.parent, StarIconBuilder.EarnedColor, StarGlowScale));
            AudioManager.Instance?.PlaySfx(SfxId.StarAppear);

            shownStarTotal++;
            RefreshStarTotal();
            AudioManager.Instance?.PlaySfx(SfxId.StarCountUpdate);
            if (starTotalIconRt != null)
            {
                if (starTotalPunch != null) StopCoroutine(starTotalPunch);
                starTotalPunch = StartCoroutine(PunchScale(starTotalIconRt));
            }

            // The last star's flash plays out in full before moving on.
            yield return new WaitForSecondsRealtime(i < to - 1 ? StarStagger : MenuRevealFlash.Duration);
        }
    }

    // Pops `slot` in place with the reveal the main menu's buttons enter
    // with (MenuRevealFlash: overshoot pop, radial glow pulse, spark burst)
    // in `color`. The glow is a temporary first child of the slot, so it
    // draws behind the slot's own graphics and rides its scale the way
    // MenuRevealFlash expects.
    IEnumerator FlashIn(RectTransform slot, Color color, float glowScale)
    {
        var glowGo = new GameObject("RevealGlow", typeof(RectTransform));
        glowGo.transform.SetParent(slot, false);
        glowGo.transform.SetAsFirstSibling();
        var grt = glowGo.GetComponent<RectTransform>();
        grt.anchorMin        = new Vector2(0.5f, 0.5f);
        grt.anchorMax        = new Vector2(0.5f, 0.5f);
        grt.pivot            = new Vector2(0.5f, 0.5f);
        grt.anchoredPosition = Vector2.zero;
        grt.sizeDelta        = slot.rect.size * glowScale;
        var glow = glowGo.AddComponent<Image>();
        glow.sprite        = RadialGlowSprite.Get();
        glow.color         = new Color(color.r, color.g, color.b, 0f);
        glow.raycastTarget = false;

        slot.localScale = Vector3.one * MenuRevealFlash.StartScale;
        yield return MenuRevealFlash.Play(null, slot, glow, color, slot, sparkParent, sparkCamera);
        Destroy(glowGo);
    }

    // Ease-in, so the squeeze gathers speed into the pop that follows it.
    static IEnumerator ScaleTo(RectTransform rt, float target, float duration)
    {
        float start = rt.localScale.x;
        for (float t = 0f; t < duration; )
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / duration);
            rt.localScale = Vector3.one * Mathf.Lerp(start, target, p * p);
            yield return null;
        }
        rt.localScale = Vector3.one * target;
    }

    static IEnumerator TintTo(Graphic graphic, Color from, Color to, float duration)
    {
        for (float t = 0f; t < duration; )
        {
            t += Time.unscaledDeltaTime;
            graphic.color = Color.Lerp(from, to, Mathf.Clamp01(t / duration));
            yield return null;
        }
        graphic.color = to;
    }

    IEnumerator PunchScale(RectTransform rt)
    {
        for (float t = 0f; t < TotalPunchDuration; )
        {
            t += Time.unscaledDeltaTime;
            float arch = Mathf.Sin(Mathf.Clamp01(t / TotalPunchDuration) * Mathf.PI);
            rt.localScale = Vector3.one * (1f + arch * (TotalPunchScale - 1f));
            yield return null;
        }
        rt.localScale = Vector3.one;
        starTotalPunch = null;
    }

    // Leaves the cards wherever the celebration got to — fine, since the
    // panel's either closing or about to be rebuilt by Open() anyway.
    void StopCelebration()
    {
        celebrationGeneration++;
        if (celebrationRoutine != null)
        {
            StopCoroutine(celebrationRoutine);
            celebrationRoutine = null;
        }
        carousel.StopGlide();
        SetCarouselInputBlocked(false);
    }

    // Blocks the carousel's cards, drag and wheel for the celebration's
    // length — the Back button stays live (see ClosePanel).
    void SetCarouselInputBlocked(bool blocked)
    {
        if (carouselInputGroup != null) carouselInputGroup.blocksRaycasts = !blocked;
    }
}
