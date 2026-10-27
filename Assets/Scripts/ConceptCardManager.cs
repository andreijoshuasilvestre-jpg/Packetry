using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Full-screen, player-paced explainer modal for the handful of networking
/// ideas that don't reduce to a single "click this next" action (default
/// gateways, VLSM, ...) — distinct from TutorialUIManager's non-blocking
/// per-step callout, which stays reserved for actionable instructions. A
/// guide script (e.g. Level4Guide) calls Show() with a title, a short
/// sequence of beats the player pages through with Next/Got it, and an
/// optional one-time diagram; the onDismissed callback is where the guide
/// resumes its own stage flow once the player clicks through to the end.
///
/// Styled identically to LevelCompletePanel: same dim backdrop + chamfered
/// PlateSpriteBuilder plate, PixeloidMono SDF font, and BlankButton/
/// BlankButton_Hover + MenuButtonFX button — so this reads as the same UI
/// language as every other overlay instead of a bespoke one. Each beat types
/// itself in like the tutorial callout does (see TextTypewriter) — read aloud
/// when narration is on (see NarrationManager) — and the card breathes very
/// slightly while it's up.
/// </summary>
public class ConceptCardManager : MonoBehaviour
{
    public static ConceptCardManager Instance { get; private set; }

    /// <summary>True while a card is up — the rest of the game is locked until
    /// it's clicked through (see AnnotationLock; the dim backdrop already
    /// blocks every UI click below it).</summary>
    public bool IsOpen => dimRoot != null && dimRoot.activeSelf;

    public class ConceptCard
    {
        public string Title;
        public string[] Beats;
        // Invoked once per Show() call to build a diagram into a fixed-size
        // slot between the title and body text — persists across beat
        // navigation rather than being rebuilt per-beat, since every card
        // authored so far uses one diagram for the whole concept. Null = no
        // diagram; the slot just stays hidden.
        public Action<Transform> BuildDiagram;
    }

    const string FontResourcePath = "Fonts/PixeloidMono SDF";
    const string SpriteFolder     = "MainMenuUI/";

    // Matches LevelCompletePanel/TutorialUIManager's shared plate palette —
    // same dark-navy fill, steel-blue chamfered border, near-black trim, so
    // this reads as the same family of overlay.
    static readonly Color32 PanelFill   = new Color32(14, 18, 28, 240);
    static readonly Color32 PanelBorder = new Color32(41, 87, 140, 255);
    static readonly Color32 PanelTrim   = new Color32(5, 8, 13, 255);
    const int PanelBorderThickness = 4;
    const int PanelChamfer         = 22;
    const int PanelPixelScale      = 4;

    // Bigger than LevelCompletePanel's 560x380 — these cards carry a diagram
    // plus multi-beat body text, LevelCompletePanel just a couple of lines.
    static readonly Vector2 PanelSize = new Vector2(680f, 480f);

    // Same pace as the tutorial callout's typing (TutorialUIManager), so the
    // two kinds of annotation read at one speed.
    const float TypingCharsPerSecond = 90f;

    // Idle breathing while the card is up — the same kind of scale bob the
    // tutorial callout has, but far smaller and slower: this is a big
    // full-attention panel, and anything more than a hint of movement on it
    // reads as wobbling rather than alive.
    const float BreathePeriod = 3f;     // seconds per full up/down cycle
    const float BreatheAmount = 0.008f; // +/- scale delta around 1.0

    TMP_FontAsset font;
    Sprite actionNormalSprite;
    Sprite actionHoverSprite;

    GameObject dimRoot;
    RectTransform panelRt;
    Coroutine typingRoutine;
    float shownAt;
    TextMeshProUGUI titleLabel;
    TextMeshProUGUI bodyLabel;
    TextMeshProUGUI progressLabel;
    RectTransform diagramSlot;
    TextMeshProUGUI nextButtonLabel;

    ConceptCard activeCard;
    int beatIndex;
    Action onDismissed;

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

        BuildPanel();
        dimRoot.SetActive(false);
    }

    /// <summary>Opens the card at its first beat. onDismissedCallback fires once
    /// the player clicks through the last beat's "Got it" — that's the caller's
    /// cue to resume whatever it paused to show this.</summary>
    public void Show(ConceptCard card, Action onDismissedCallback = null)
    {
        activeCard  = card;
        onDismissed = onDismissedCallback;
        beatIndex   = 0;

        ClearDiagram();
        bool hasDiagram = card.BuildDiagram != null;
        diagramSlot.gameObject.SetActive(hasDiagram);
        if (hasDiagram) card.BuildDiagram(diagramSlot);

        titleLabel.text = card.Title;
        dimRoot.SetActive(true);
        shownAt = Time.unscaledTime;
        AnnotationLock.DropTextFocus();
        AudioManager.Instance?.PlaySfx(SfxId.PanelPrompt); // no ZoomPanel here to play it
        ShowBeat();
    }

    /// <summary>Force-closes the card without invoking onDismissed — for a level
    /// that needs to tear down mid-explanation (e.g. Skip Tutorial equivalents),
    /// not part of the normal click-through flow.</summary>
    public void Hide()
    {
        StopTyping();
        NarrationManager.Instance?.Stop(this);
        dimRoot.SetActive(false);
        if (panelRt != null) panelRt.localScale = Vector3.one;
        ClearDiagram();
        activeCard  = null;
        onDismissed = null;
    }

    void Update()
    {
        if (!IsOpen || panelRt == null) return;
        // Starts at exactly 1 (sin 0) the moment the card opens.
        float s = 1f + Mathf.Sin((Time.unscaledTime - shownAt) * (2f * Mathf.PI / BreathePeriod)) * BreatheAmount;
        panelRt.localScale = new Vector3(s, s, 1f);
    }

    void ShowBeat()
    {
        bodyLabel.text = activeCard.Beats[beatIndex];
        bool last = beatIndex == activeCard.Beats.Length - 1;
        nextButtonLabel.text = last ? "Got it" : "Next";

        bool multiBeat = activeCard.Beats.Length > 1;
        progressLabel.gameObject.SetActive(multiBeat);
        if (multiBeat) progressLabel.text = $"{beatIndex + 1} / {activeCard.Beats.Length}";

        StopTyping();
        NarrationManager.Instance?.Speak(this, bodyLabel.text);
        typingRoutine = StartCoroutine(TypeBeat());
    }

    IEnumerator TypeBeat()
    {
        yield return TextTypewriter.Reveal(bodyLabel, TypingCharsPerSecond, speaker: this);
        typingRoutine = null;
    }

    void StopTyping()
    {
        if (typingRoutine == null) return;
        StopCoroutine(typingRoutine);
        typingRoutine = null;
        TextTypewriter.Finish(bodyLabel);
    }

    void OnNextClicked()
    {
        // A click while the beat is still typing just finishes it — the next
        // click moves on — so clicking quickly never skips a beat unread.
        if (typingRoutine != null)
        {
            StopTyping();
            return;
        }

        beatIndex++;
        if (beatIndex >= activeCard.Beats.Length)
        {
            var callback = onDismissed;
            Hide();
            callback?.Invoke();
            return;
        }
        ShowBeat();
    }

    void ClearDiagram()
    {
        if (diagramSlot == null) return;
        for (int i = diagramSlot.childCount - 1; i >= 0; i--)
            Destroy(diagramSlot.GetChild(i).gameObject);
    }

    // ── UI construction ──────────────────────────────────────────────────────

    void BuildPanel()
    {
        var canvasGo = new GameObject("ConceptCard_Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30; // above TutorialUIManager (22) and CLIPanelManager (20), below LevelCompletePanel (50)

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);
        canvasGo.AddComponent<GraphicRaycaster>();

        var dimGo = new GameObject("Dim");
        dimRoot = dimGo;
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
        var bg = panelGo.AddComponent<Image>();
        bg.sprite = PlateSpriteBuilder.GetSprite(PanelFill, PanelBorder, PanelTrim,
            PanelBorderThickness, PanelChamfer, PanelPixelScale);
        bg.type = Image.Type.Sliced;
        panelRt = panelGo.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot     = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = PanelSize;

        titleLabel = MakeLabel(panelGo.transform, "Title", 30, FontStyles.Bold, TextAlignmentOptions.Center,
            new Vector2(0f, -28f), new Vector2(600f, 44f));
        titleLabel.color = Color.white;

        var diagramGo = new GameObject("DiagramSlot", typeof(RectTransform));
        diagramGo.transform.SetParent(panelGo.transform, false);
        diagramSlot = diagramGo.GetComponent<RectTransform>();
        diagramSlot.anchorMin        = new Vector2(0.5f, 1f);
        diagramSlot.anchorMax        = new Vector2(0.5f, 1f);
        diagramSlot.pivot            = new Vector2(0.5f, 1f);
        diagramSlot.anchoredPosition = new Vector2(0f, -86f);
        diagramSlot.sizeDelta        = new Vector2(600f, 150f);

        bodyLabel = MakeLabel(panelGo.transform, "Body", 20, FontStyles.Normal, TextAlignmentOptions.TopLeft,
            new Vector2(0f, -252f), new Vector2(600f, 142f));
        bodyLabel.color = new Color(0.85f, 0.90f, 0.97f);
        bodyLabel.lineSpacing = 4f;
        // A long beat (several sentences, or one with an inlined IP/subnet
        // recap — see Level4Guide.GatewayRecapBeat) could overflow this fixed
        // box far enough to visually spill under the Next button below it,
        // which then reads as "the Next button is covering some text" since
        // the button renders on top. Shrink-to-fit instead of ever
        // overflowing; Truncate is just a hard backstop in case some future
        // beat is long enough to still not fit even at the smallest size.
        bodyLabel.enableAutoSizing = true;
        bodyLabel.fontSizeMin = 13f;
        bodyLabel.fontSizeMax = 20f;
        bodyLabel.overflowMode = TextOverflowModes.Truncate;

        progressLabel = MakeLabel(panelGo.transform, "Progress", 15, FontStyles.Normal, TextAlignmentOptions.MidlineLeft,
            Vector2.zero, new Vector2(80f, 24f));
        progressLabel.color = new Color(0.55f, 0.60f, 0.68f);
        var progRt = progressLabel.GetComponent<RectTransform>();
        progRt.anchorMin        = new Vector2(0f, 0f);
        progRt.anchorMax        = new Vector2(0f, 0f);
        progRt.pivot            = new Vector2(0f, 0f);
        progRt.anchoredPosition = new Vector2(28f, 32f);

        MakeButton(panelGo.transform, GameTheme.ElectricBlue, new Vector2(0f, 32f), "Next", OnNextClicked, out nextButtonLabel);

        // Speaking indicator, bottom-right — level with the Next button, the
        // mirror of the progress label on the left.
        var waveRt = NarrationWaveform.Create(panelGo.transform, this, GameTheme.Info).Rect;
        waveRt.anchorMin        = new Vector2(1f, 0f);
        waveRt.anchorMax        = new Vector2(1f, 0f);
        waveRt.pivot            = new Vector2(1f, 0f);
        waveRt.anchoredPosition = new Vector2(-28f, 38f);
        waveRt.sizeDelta        = new Vector2(170f, 40f);

        // Windows-style draggable title bar — same chrome every floating
        // annotation in the game wears (see WindowChromeBuilder) so the
        // player can drag this card out of the way instead of it sitting
        // fixed over whatever it's explaining. No close button: Next/Got it
        // is already this card's one dismissal path, and onDismissed only
        // ever fires through that click-through flow.
        WindowChromeBuilder.Build(panelGo.transform, panelRt, canvas, font,
            "Network Concept", PanelSize.x, GameTheme.Info, showCloseButton: false);
    }

    // Same Glow + Visual(Background+Label) composition and MenuButtonFX
    // hover/click behavior as TutorialUIManager/LevelCompletePanel's own
    // button builders — the "Next"/"Got it" button reads as the same button,
    // art and feedback included, not a flat placeholder rectangle.
    void MakeButton(Transform parent, Color glowColor, Vector2 anchoredPos, string text,
                     UnityEngine.Events.UnityAction onClick, out TextMeshProUGUI label)
    {
        Vector2 size = new Vector2(220f, 52f);

        var btnGo = new GameObject("NextButton", typeof(RectTransform));
        btnGo.transform.SetParent(parent, false);
        var btnRt = btnGo.GetComponent<RectTransform>();
        btnRt.anchorMin        = new Vector2(0.5f, 0f);
        btnRt.anchorMax        = new Vector2(0.5f, 0f);
        btnRt.pivot            = new Vector2(0.5f, 0f);
        btnRt.anchoredPosition = anchoredPos;
        btnRt.sizeDelta        = size;

        var glowGo = new GameObject("Glow", typeof(RectTransform));
        glowGo.transform.SetParent(btnGo.transform, false);
        var glowImg = glowGo.AddComponent<Image>();
        glowImg.raycastTarget = false;
        var grt = glowGo.GetComponent<RectTransform>();
        grt.anchorMin        = new Vector2(0.5f, 0.5f);
        grt.anchorMax        = new Vector2(0.5f, 0.5f);
        grt.pivot            = new Vector2(0.5f, 0.5f);
        grt.anchoredPosition = Vector2.zero;
        grt.sizeDelta        = new Vector2(size.x * 1.15f, size.y * 1.6f);

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
    }

    TextMeshProUGUI MakeLabel(Transform parent, string name, int fontSize, FontStyles style, TextAlignmentOptions align,
                               Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font      = font;
        tmp.fontSize  = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = align;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 1f);
        rt.anchorMax        = new Vector2(0.5f, 1f);
        rt.pivot            = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = size;
        return tmp;
    }

    // ── Shared diagram-node helpers ──────────────────────────────────────────
    // Small building blocks any BuildDiagram delegate can compose into a
    // diagram (see ConceptCard.BuildDiagram) — an icon node with a caption
    // underneath, a plain chamfered-plate node for a concept that has no
    // dedicated icon, and a connecting arrow. Originally written for
    // Level4Guide's gateway diagram; hoisted here once Level11Challenge's
    // VLSM diagram needed the same pieces, so both — and any future concept
    // card — build diagrams out of one shared, already-proven vocabulary
    // instead of each re-implementing its own node/arrow construction.
    // Positions are anchored center (0.5, 0.5) relative to whatever parent
    // is passed in (typically a ConceptCard's diagram slot), matching how
    // ConceptCardManager itself lays out that slot.

    public static void BuildIconNode(Transform parent, TMP_FontAsset font, Sprite icon, string caption, Vector2 anchoredPos)
    {
        var node = new GameObject("Node", typeof(RectTransform));
        node.transform.SetParent(parent, false);
        var nodeRt = node.GetComponent<RectTransform>();
        nodeRt.anchorMin        = new Vector2(0.5f, 0.5f);
        nodeRt.anchorMax        = new Vector2(0.5f, 0.5f);
        nodeRt.pivot            = new Vector2(0.5f, 0.5f);
        nodeRt.anchoredPosition = anchoredPos;
        nodeRt.sizeDelta        = new Vector2(140f, 100f);

        var iconGo = new GameObject("Icon", typeof(RectTransform));
        iconGo.transform.SetParent(node.transform, false);
        var iconImg = iconGo.AddComponent<Image>();
        iconImg.sprite = icon;
        iconImg.preserveAspect = true;
        var iconRt = iconGo.GetComponent<RectTransform>();
        iconRt.anchorMin        = new Vector2(0.5f, 1f);
        iconRt.anchorMax        = new Vector2(0.5f, 1f);
        iconRt.pivot            = new Vector2(0.5f, 1f);
        iconRt.anchoredPosition = Vector2.zero;
        iconRt.sizeDelta        = new Vector2(56f, 56f);

        var captionGo = new GameObject("Caption", typeof(RectTransform));
        captionGo.transform.SetParent(node.transform, false);
        var captionTmp = captionGo.AddComponent<TextMeshProUGUI>();
        captionTmp.font      = font;
        captionTmp.fontSize  = 14;
        captionTmp.alignment = TextAlignmentOptions.Top;
        captionTmp.color     = new Color(0.85f, 0.90f, 0.97f);
        captionTmp.text      = caption;
        var captionRt = captionGo.GetComponent<RectTransform>();
        captionRt.anchorMin        = new Vector2(0.5f, 0f);
        captionRt.anchorMax        = new Vector2(0.5f, 0f);
        captionRt.pivot            = new Vector2(0.5f, 0f);
        captionRt.anchoredPosition = Vector2.zero;
        captionRt.sizeDelta        = new Vector2(140f, 40f);
    }

    // A node with no dedicated icon asset — a small chamfered plate
    // (PlateSpriteBuilder, same look as every other UI panel in the game)
    // reads as "a place/thing," not just a floating label. nodeSize defaults
    // to the gateway diagram's original size; Level11Challenge's VLSM
    // diagram passes a wider one to fit longer captions.
    public static void BuildPlateNode(Transform parent, TMP_FontAsset font, string caption, Vector2 anchoredPos, Vector2? nodeSize = null)
    {
        Vector2 size = nodeSize ?? new Vector2(130f, 64f);

        var node = new GameObject("Node", typeof(RectTransform));
        node.transform.SetParent(parent, false);
        var nodeRt = node.GetComponent<RectTransform>();
        nodeRt.anchorMin        = new Vector2(0.5f, 0.5f);
        nodeRt.anchorMax        = new Vector2(0.5f, 0.5f);
        nodeRt.pivot            = new Vector2(0.5f, 0.5f);
        nodeRt.anchoredPosition = anchoredPos;
        nodeRt.sizeDelta        = size;

        var plateImg = node.AddComponent<Image>();
        plateImg.sprite = PlateSpriteBuilder.GetSprite(
            new Color32(24, 30, 44, 255), new Color32(70, 110, 150, 255), new Color32(5, 8, 13, 255), 2, 8, 4);
        plateImg.type = Image.Type.Sliced;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(node.transform, false);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.font      = font;
        label.fontSize  = 14;
        label.alignment = TextAlignmentOptions.Center;
        label.color     = new Color(0.85f, 0.90f, 0.97f);
        label.text      = caption;
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
    }

    public static void BuildArrow(Transform parent, TMP_FontAsset font, Vector2 anchoredPos)
    {
        var arrowGo = new GameObject("Arrow", typeof(RectTransform));
        arrowGo.transform.SetParent(parent, false);
        var arrowTmp = arrowGo.AddComponent<TextMeshProUGUI>();
        arrowTmp.font      = font;
        arrowTmp.fontSize  = 26;
        arrowTmp.alignment = TextAlignmentOptions.Center;
        arrowTmp.color     = GameTheme.ElectricBlue;
        arrowTmp.text      = "→";
        var arrowRt = arrowGo.GetComponent<RectTransform>();
        arrowRt.anchorMin        = new Vector2(0.5f, 0.5f);
        arrowRt.anchorMax        = new Vector2(0.5f, 0.5f);
        arrowRt.pivot            = new Vector2(0.5f, 0.5f);
        arrowRt.anchoredPosition = anchoredPos;
        arrowRt.sizeDelta        = new Vector2(60f, 40f);
    }
}
