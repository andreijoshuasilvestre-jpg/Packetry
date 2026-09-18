using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Non-blocking annotation callout + player-facing "Skip Tutorial" button for
/// TutorialManager's step sequence. Mirrors the existing NetworkUIManager /
/// TaskUIManager split — TutorialManager owns step state and logic, this
/// class only owns the visual build. Also owns the pulsing sidebar-button
/// beacon (PulsingHighlight) and a small pool of world-space click-zone
/// markers (WorldPulseMarker) that TutorialManager re-targets per step.
///
/// Built in Awake() rather than Start() (unlike most of this project's HUD,
/// which builds in Start()) — TutorialManager's own initialization needs to
/// safely call into this class regardless of which MonoBehaviour's Start()
/// happens to run first, so this must already exist by the time any Start()
/// runs.
/// </summary>
public class TutorialUIManager : MonoBehaviour
{
    public static TutorialUIManager Instance { get; private set; }

    private static readonly Color TextColor  = new Color(0.90f, 0.94f, 1.00f, 1f);
    private static readonly Color SkipBg     = new Color(0.35f, 0.38f, 0.45f, 0.9f);

    // Callout box look — same procedurally-drawn "chamfered plate" style as
    // LevelSelectManager's container box (see PlateSpriteBuilder), just
    // scaled down and kept slightly translucent (alpha < 1 on calloutFill)
    // so it still reads as a floating annotation over live gameplay rather
    // than a fully opaque modal. Serialized (not consts) so it's tunable
    // per-inspector, same rationale as LevelSelectManager's Container Box.
    [Header("Callout Box")]
    [SerializeField] private Color calloutFill           = new Color(0.06f, 0.08f, 0.13f, 0.94f);
    [SerializeField] private Color calloutBorder         = new Color(0.16f, 0.34f, 0.55f, 1f);
    [SerializeField] private Color calloutTrim           = new Color(0.02f, 0.03f, 0.05f, 1f);
    [SerializeField] private int   calloutBorderThickness = 3;
    [SerializeField] private int   calloutChamfer         = 12;
    [SerializeField] private int   calloutPixelScale      = 4;

    // Keycap chip drawn behind any <key>...</key> span in step text (see
    // ApplyKeyChips) — a lighter, slightly blue-tinted rectangle so the exact
    // key to press reads as a distinct "button" against the callout's dark
    // fill, rather than just bold text blending into the rest of the line.
    [SerializeField] private Color keyChipColor = new Color(0.24f, 0.32f, 0.44f, 1f);

    // Sized generously for multi-line, specific "here's exactly what to do"
    // instructions rather than one-line hints — readability was the whole
    // point of the redesign. Height is no longer fixed: MinPanelHeight is
    // just the floor for short steps, and AutoSizeCallout() grows the panel
    // to fit whatever step text actually needs (see below) — a long Text
    // string used to overflow past the reserved button row instead of
    // growing the box (e.g. the "ports" info step), which is the bug this
    // replaced.
    private const float PanelWidth      = 460f;
    private const float MinPanelHeight  = 190f;
    private const float MaxPanelHeight  = 340f; // cap so a runaway string can't push the panel off-screen
    private const float PanelOffsetX    = 330f; // clears NetworkUIManager's 300px-wide sidebar
    private const float ButtonHeight    = 40f;
    private const float TextPaddingX      = 20f; // matches txtRt offsetMin/offsetMax.x magnitude
    private const float TextPaddingTop    = 18f; // matches txtRt offsetMax.y magnitude
    private const float TextPaddingBottom = 16f; // gap between text and the button row above it

    // Pop-in (on Show) + idle bounce (while visible) + typing animation tuning.
    // Pop-in eases outward from PopInStartScale to 1 once, then the idle bounce
    // takes over and loops for as long as the callout stays on screen — both
    // animate panelRect.localScale so they're chained in one coroutine
    // (PopInThenBounce) rather than fighting over the same value.
    private const float PopInDuration        = 0.25f;
    private const float PopInStartScale      = 0.85f;
    private const float BouncePeriod         = 1.6f;   // seconds per full up/down cycle
    private const float BounceAmount         = 0.025f; // +/- scale delta around 1.0
    private const float TypingCharsPerSecond = 45f;

    const string SpriteFolder = "MainMenuUI/";
    const string FontResourcePath = "Fonts/PixeloidMono SDF";

    // Author-facing shorthand for "this is a keyboard key" — step text writes
    // <key>WASD</key> and ApplyKeyChips rewrites it into TMP's <mark>/<b>
    // markup at display time. Kept separate from plain <b>, which the guide
    // scripts already use for UI button names and CLI commands (e.g.
    // <b>Ping</b>, <b>interface eth0</b>) — those shouldn't get a keycap chip.
    static readonly Regex KeyTagPattern = new Regex("<key>(.*?)</key>", RegexOptions.Compiled);

    private RectTransform canvasRect;
    private RectTransform panelRect;
    private TextMeshProUGUI bodyText;
    private RectTransform lastTarget;   // remembered so UpdateText() can reposition after a resize
    private float lastOffsetX;
    private float lastOffsetY;
    private GameObject actionButtonGo;
    private TextMeshProUGUI actionButtonLabel;
    private GameObject skipButtonGo;
    private Coroutine typingRoutine;
    private Coroutine scaleRoutine;
    private PulsingHighlight highlight;
    private WorldPulseMarker markerA;
    private WorldPulseMarker markerB;

    // "Next"/"Continue" action button art — the actual menu-button plate
    // (BlankButton / BlankButton_Hover), same asset CreateMenuButton/
    // LevelSelectManager.CreateButton use, so the tutorial's own action
    // button reads as the same button, not a flat placeholder rectangle.
    Sprite actionNormalSprite;
    Sprite actionHoverSprite;
    TMP_FontAsset font;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        font = Resources.Load<TMP_FontAsset>(FontResourcePath);
        BuildCanvas();

        // Renders nothing of its own — just pulses whatever RectTransform
        // TutorialManager points it at (see PulsingHighlight).
        highlight = gameObject.AddComponent<PulsingHighlight>();

        markerA = BuildWorldMarker("TutorialMarkerA");
        markerB = BuildWorldMarker("TutorialMarkerB");
    }

    static WorldPulseMarker BuildWorldMarker(string name)
    {
        var go = new GameObject(name);
        return go.AddComponent<WorldPulseMarker>();
    }

    void BuildCanvas()
    {
        var canvasGo = new GameObject("Tutorial_Canvas");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 21; // above CLI_Canvas (20, see CLIPanelManager.BuildPanel) so the callout
                                  // stays readable over an open router CLI (e.g. Level4Guide's addressing
                                  // walkthrough) as well as over HUD_Canvas (10)

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);

        canvasGo.AddComponent<GraphicRaycaster>();
        canvasRect = canvasGo.GetComponent<RectTransform>();

        actionNormalSprite = Resources.Load<Sprite>(SpriteFolder + "BlankButton");
        actionHoverSprite  = Resources.Load<Sprite>(SpriteFolder + "BlankButton_Hover");

        BuildCallout(canvasGo.transform);
        BuildSkipButton(canvasGo.transform);

        SetCalloutVisible(false);
        skipButtonGo.SetActive(false);
    }

    // ── Callout bubble ───────────────────────────────────────────────────────

    void BuildCallout(Transform canvas)
    {
        var panel = new GameObject("TutorialCallout");
        panel.transform.SetParent(canvas, false);
        var bg = panel.AddComponent<Image>();
        bg.sprite = PlateSpriteBuilder.GetSprite(calloutFill, calloutBorder, calloutTrim,
            calloutBorderThickness, calloutChamfer, calloutPixelScale);
        bg.type = Image.Type.Sliced;

        panelRect = panel.GetComponent<RectTransform>();
        // Anchored to the screen's vertical center, left-pinned — anchoredPosition.y
        // then lines up directly with RectTransformUtility's canvas-center-relative
        // local Y (see PositionPanel), no extra top/bottom offset math needed.
        panelRect.anchorMin        = new Vector2(0f, 0.5f);
        panelRect.anchorMax        = new Vector2(0f, 0.5f);
        panelRect.pivot            = new Vector2(0f, 0.5f);
        panelRect.anchoredPosition = new Vector2(PanelOffsetX, 0f);
        panelRect.sizeDelta        = new Vector2(PanelWidth, MinPanelHeight);

        // Small diamond "tail" nudging toward the sidebar on the panel's left
        // edge — every highlighted target lives in the left sidebar, so a
        // fixed leftward-pointing tail reads correctly for every step.
        var tailGo = new GameObject("Tail");
        tailGo.transform.SetParent(panel.transform, false);
        var tailImg = tailGo.AddComponent<Image>();
        tailImg.color = calloutFill; // flat-color diamond, tinted to match the plate's interior fill so it blends in
        tailImg.raycastTarget = false;
        var trt = tailGo.GetComponent<RectTransform>();
        trt.anchorMin        = new Vector2(0f, 0.5f);
        trt.anchorMax        = new Vector2(0f, 0.5f);
        trt.pivot             = new Vector2(0.5f, 0.5f);
        trt.anchoredPosition = new Vector2(-10f, 0f);
        trt.sizeDelta        = new Vector2(20f, 20f);
        trt.localRotation    = Quaternion.Euler(0f, 0f, 45f);

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(panel.transform, false);
        bodyText = textGo.AddComponent<TextMeshProUGUI>();
        bodyText.font      = font;
        bodyText.fontSize  = 20;
        bodyText.color     = TextColor;
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.lineSpacing = 4f;
        var txtRt = textGo.GetComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = new Vector2(TextPaddingX, ButtonHeight + TextPaddingBottom);
        txtRt.offsetMax = new Vector2(-TextPaddingX, -TextPaddingTop);

        BuildActionButton(panel.transform);
    }

    // Same Glow + Visual(Background+Label) composition and MenuButtonFX
    // hover/click behavior as LevelSelectManager.CreateButton / MainMenuBootstrap.
    // CreateMenuButton — the tutorial's "Next"/"Continue" button is now the
    // same button, art and feedback included, not a separate flat rectangle.
    void BuildActionButton(Transform panel)
    {
        actionButtonGo = new GameObject("ActionButton", typeof(RectTransform));
        actionButtonGo.transform.SetParent(panel, false);
        var art = actionButtonGo.GetComponent<RectTransform>();
        art.anchorMin        = new Vector2(1f, 0f);
        art.anchorMax        = new Vector2(1f, 0f);
        art.pivot            = new Vector2(1f, 0f);
        art.anchoredPosition = new Vector2(-16f, 14f);
        art.sizeDelta        = new Vector2(130f, ButtonHeight);

        var glowGo = new GameObject("Glow", typeof(RectTransform));
        glowGo.transform.SetParent(actionButtonGo.transform, false);
        var glowImg = glowGo.AddComponent<Image>();
        glowImg.raycastTarget = false;
        var grt = glowGo.GetComponent<RectTransform>();
        grt.anchorMin        = new Vector2(0.5f, 0.5f);
        grt.anchorMax        = new Vector2(0.5f, 0.5f);
        grt.pivot            = new Vector2(0.5f, 0.5f);
        grt.anchoredPosition = Vector2.zero;
        grt.sizeDelta        = new Vector2(130f * 1.15f, ButtonHeight * 1.6f);

        var visualGo = new GameObject("Visual", typeof(RectTransform));
        visualGo.transform.SetParent(actionButtonGo.transform, false);
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
        actionButtonLabel = labelGo.AddComponent<TextMeshProUGUI>();
        actionButtonLabel.font      = font;
        actionButtonLabel.text      = "Next";
        actionButtonLabel.fontSize  = 18;
        actionButtonLabel.color     = Color.white;
        actionButtonLabel.alignment = TextAlignmentOptions.Center;
        actionButtonLabel.fontStyle = FontStyles.Bold;
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;

        var btn = actionButtonGo.AddComponent<Button>();
        btn.transition    = Selectable.Transition.None;
        btn.targetGraphic = bgImg;

        var fx = actionButtonGo.AddComponent<MenuButtonFX>();
        fx.visualRect      = vrt;
        fx.backgroundImage = bgImg;
        fx.glowImage       = glowImg;
        fx.normalSprite    = actionNormalSprite;
        fx.hoverSprite     = actionHoverSprite;
        fx.glowColor       = GameTheme.ElectricBlue;
        fx.Init();

        btn.onClick.AddListener(() => fx.PlayClickThen(() => TutorialManager.Instance?.AdvanceFromButton()));
    }

    // ── Skip button ──────────────────────────────────────────────────────────

    void BuildSkipButton(Transform canvas)
    {
        skipButtonGo = new GameObject("SkipTutorialButton");
        skipButtonGo.transform.SetParent(canvas, false);
        var bg = skipButtonGo.AddComponent<Image>();
        bg.color = SkipBg;
        var rt = skipButtonGo.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(1f, 1f);
        rt.anchorMax        = new Vector2(1f, 1f);
        rt.pivot            = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-20f, -20f);
        rt.sizeDelta        = new Vector2(160f, ButtonHeight);

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(skipButtonGo.transform, false);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.font      = font;
        label.text      = "Skip Tutorial";
        label.fontSize  = 16;
        label.color     = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;

        var btn = skipButtonGo.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(() => TutorialManager.Instance?.SkipTutorial());
    }

    // ── Public API (called by TutorialManager) ──────────────────────────────

    /// <summary>Shows/updates the callout for the current step. target = null means
    /// no sidebar button is being pointed at (welcome/completion/info steps) — the
    /// callout rests at a default center position and the highlight hides.
    /// buttonLabel = null means the step auto-advances (no button shown).</summary>
    /// <summary>offsetX overrides the callout's default horizontal position (PanelOffsetX,
    /// which clears the 300px sidebar) — pass a smaller value for a level whose sidebar
    /// is entirely hidden (see Level3Guide), where the wider default gap just wastes
    /// screen space. Leave null for the normal sidebar-clearing position. offsetY nudges
    /// the callout vertically away from target's own height — used for the one tutorial
    /// step that leaves NetworkUIManager's devices/cable compound button unrestricted
    /// (TutorialStep.TeachDeviceFlyout) so its right-click flyout has room to open
    /// without the callout sitting on top of it (the highlight beacon still attaches to
    /// the real target regardless of this offset).</summary>
    public void Show(string text, RectTransform target, string buttonLabel, float? offsetX = null, float offsetY = 0f)
    {
        skipButtonGo.SetActive(true);
        SetCalloutVisible(true);
        bodyText.text = ApplyKeyChips(text);
        lastTarget   = target;
        lastOffsetX  = offsetX ?? PanelOffsetX;
        lastOffsetY  = offsetY;
        AutoSizeCallout();
        PositionPanel(lastTarget, lastOffsetX, lastOffsetY);
        PlayPopInAndBounce();
        PlayTyping();

        bool showButton = !string.IsNullOrEmpty(buttonLabel);
        actionButtonGo.SetActive(showButton);
        if (showButton) actionButtonLabel.text = buttonLabel;

        if (target != null) { highlight.Attach(target); highlight.Show(); }
        else highlight.Hide();
    }

    /// <summary>Hides everything — callout, highlight, world markers, and the Skip
    /// button. Called once the tutorial ends, whether by finishing the last step
    /// or being skipped.</summary>
    public void Hide()
    {
        SetCalloutVisible(false);
        skipButtonGo.SetActive(false);
        highlight.Hide();
        HideWorldMarkers();

        if (typingRoutine != null) { StopCoroutine(typingRoutine); typingRoutine = null; }
        if (scaleRoutine  != null) { StopCoroutine(scaleRoutine);  scaleRoutine  = null; }
        panelRect.localScale = Vector3.one;
    }

    /// <summary>Swaps just the callout's body text in place — the button and
    /// highlight are left untouched. Used mid-step (e.g. once the player has
    /// actually picked a device up) rather than Show(), which would also
    /// reset those. Still re-runs the panel auto-sizing/positioning, since a
    /// TextAfterGate swap can be longer or shorter than the text it replaces.</summary>
    public void UpdateText(string text)
    {
        bodyText.text = ApplyKeyChips(text);
        AutoSizeCallout();
        PositionPanel(lastTarget, lastOffsetX, lastOffsetY);
        PlayTyping();
    }

    /// <summary>Shows a floating pulse marker above a world position — index 0 or 1,
    /// so up to two can be visible at once (e.g. a table's two corners). Pass
    /// pointingAtDevice when pointing at an already-placed device so the pulse
    /// becomes a translucent echo of that device's own model instead of a
    /// generic circle (see WorldPulseMarker.Show).</summary>
    public void ShowWorldMarker(int index, Vector3 worldPos, NetworkDevice pointingAtDevice = null) =>
        (index == 0 ? markerA : markerB).Show(worldPos, pointingAtDevice);

    public void HideWorldMarkers()
    {
        markerA.Hide();
        markerB.Hide();
    }

    /// <summary>Tints both markers a specific color (e.g. table-corner clicks vs.
    /// device-drop spots reading as visually distinct kinds of target).</summary>
    public void SetMarkerColor(Color color)
    {
        markerA.SetColor(color);
        markerB.SetColor(color);
    }

    /// <summary>Restores both markers to their default color — call before showing
    /// markers for a step that didn't call SetMarkerColor, so a color set for a
    /// previous step (both markers are reused across steps) doesn't linger.</summary>
    public void ResetMarkerColors()
    {
        markerA.ResetColor();
        markerB.ResetColor();
    }

    void SetCalloutVisible(bool visible) => panelRect.gameObject.SetActive(visible);

    /// <summary>Rewrites author-facing &lt;key&gt;X&lt;/key&gt; spans into TMP's
    /// &lt;mark&gt; highlight tag (a background rectangle behind the text) plus
    /// &lt;b&gt;, so a keyboard key reads as a small contrasting "keycap" chip
    /// rather than plain bold text. padding values are percentages of font size,
    /// per TMP's &lt;mark&gt; spec (see TMP_Text's MARK/PADDING handling).</summary>
    string ApplyKeyChips(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string colorHex = ColorUtility.ToHtmlStringRGBA(keyChipColor);
        return KeyTagPattern.Replace(text,
            m => $"<mark=#{colorHex} padding=\"25,25,12,12\"><b>{m.Groups[1].Value}</b></mark>");
    }

    /// <summary>Restarts the callout's scale animation: a one-shot ease-out scale-up
    /// from PopInStartScale to 1 (the popup "arriving"), then chains straight into a
    /// looping, subtle sine-wave scale bounce for as long as the callout stays open.
    /// Both phases animate the same panelRect.localScale, so they're one coroutine
    /// rather than two competing over the value.</summary>
    void PlayPopInAndBounce()
    {
        if (scaleRoutine != null) StopCoroutine(scaleRoutine);
        scaleRoutine = StartCoroutine(PopInThenBounce());
    }

    IEnumerator PopInThenBounce()
    {
        panelRect.localScale = Vector3.one * PopInStartScale;
        float t = 0f;
        while (t < PopInDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = EaseOutCubic(Mathf.Clamp01(t / PopInDuration));
            panelRect.localScale = Vector3.one * Mathf.LerpUnclamped(PopInStartScale, 1f, p);
            yield return null;
        }
        panelRect.localScale = Vector3.one;

        float phase = 0f;
        while (true)
        {
            phase += Time.unscaledDeltaTime;
            float s = 1f + Mathf.Sin(phase * (2f * Mathf.PI / BouncePeriod)) * BounceAmount;
            panelRect.localScale = Vector3.one * s;
            yield return null;
        }
    }

    static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

    /// <summary>Restarts the typewriter reveal of bodyText.text. The full string is
    /// assigned up front (so AutoSizeCallout/layout are computed against the final
    /// text, not a growing substring) and TMP's maxVisibleCharacters is animated from
    /// 0 up to the real character count instead — avoids re-laying-out the mesh every
    /// frame and keeps rich-text tags (if any) from being counted as visible chars.</summary>
    void PlayTyping()
    {
        if (typingRoutine != null) StopCoroutine(typingRoutine);
        typingRoutine = StartCoroutine(TypeText());
    }

    IEnumerator TypeText()
    {
        bodyText.ForceMeshUpdate();
        int totalVisible = bodyText.textInfo.characterCount;
        bodyText.maxVisibleCharacters = 0;
        float shown = 0f;
        while (shown < totalVisible)
        {
            shown += Time.unscaledDeltaTime * TypingCharsPerSecond;
            bodyText.maxVisibleCharacters = Mathf.Clamp(Mathf.FloorToInt(shown), 0, totalVisible);
            yield return null;
        }
        bodyText.maxVisibleCharacters = totalVisible;
        typingRoutine = null;
    }

    /// <summary>Grows (or shrinks back down) the callout to fit bodyText.text at the
    /// fixed PanelWidth, so a long step string pushes the box taller instead of
    /// overflowing past the reserved button row at the bottom — see the class
    /// comment on MinPanelHeight/MaxPanelHeight for the bug this replaced. Call
    /// this any time bodyText.text changes, before PositionPanel (the new height
    /// affects how far the panel can sit off-center).</summary>
    void AutoSizeCallout()
    {
        float textWidth   = PanelWidth - TextPaddingX * 2f;
        float textHeight  = bodyText.GetPreferredValues(bodyText.text, textWidth, 0f).y;
        float neededHeight = textHeight + ButtonHeight + TextPaddingBottom + TextPaddingTop;
        float height = Mathf.Clamp(neededHeight, MinPanelHeight, MaxPanelHeight);
        panelRect.sizeDelta = new Vector2(PanelWidth, height);
    }

    void PositionPanel(RectTransform target, float offsetX, float offsetY = 0f)
    {
        float y = 0f;
        if (target != null)
        {
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, target.position);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out Vector2 local))
                y = local.y;
        }
        y += offsetY;

        float panelHeight = panelRect.sizeDelta.y;
        float maxY = Mathf.Max(0f, canvasRect.rect.height * 0.5f - panelHeight * 0.5f - 20f);
        y = Mathf.Clamp(y, -maxY, maxY);
        panelRect.anchoredPosition = new Vector2(offsetX, y);
    }
}
