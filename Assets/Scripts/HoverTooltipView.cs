using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The one tooltip card, on its own overlay canvas above every other game
/// canvas (CLI 20, guides 21-22, popups 30). Built lazily the first time
/// anything is hovered, so scenes that never attach a tooltip pay nothing.
/// Same navy chamfered plate + PixeloidMono as the tutorial callout, so it
/// reads as the game's own "explanation" voice.
/// </summary>
public class HoverTooltipView : MonoBehaviour
{
    public static HoverTooltipView Instance { get; private set; }

    const string FontResourcePath = "Fonts/PixeloidMono SDF";
    // Above every panel that can carry a hover target, Settings (65) and its
    // color picker (70) included — a card only shows while its target is
    // hovered, and a target under a modal's dim can't be, so nothing it
    // explains is ever hidden behind it. Below only the loading screen (1000).
    const int    SortingOrder     = 80;
    const float  CardWidth        = 320f;
    const float  TargetGap        = 10f; // canvas units between the card and its target
    const float  ScreenMargin     = 12f; // canvas units kept clear of the screen edge
    const float  FadeDuration     = 0.12f;
    // How long after a card hides the next one still opens instantly.
    const float  WarmWindow       = 0.5f;
    const float  WarmDelay        = 0.05f;

    static readonly Color CardFill    = new Color(0.06f, 0.08f, 0.13f, 0.97f);
    static readonly Color CardBorder  = new Color(0.16f, 0.34f, 0.55f, 1f);
    static readonly Color Trim        = new Color(0.02f, 0.03f, 0.05f, 1f);
    static readonly Color TitleColor  = Color.white;
    static readonly Color BodyColor   = new Color(0.80f, 0.86f, 0.95f, 1f);
    static readonly Color FooterColor = new Color(0.55f, 0.66f, 0.80f, 1f);
    static readonly Color Terminal    = new Color(0.35f, 0.86f, 0.47f, 1f);
    static readonly Color InsetFill   = new Color(0.02f, 0.02f, 0.02f, 1f);
    static readonly Color InsetBorder = new Color(0.20f, 0.40f, 0.25f, 1f);

    Canvas          canvas;
    RectTransform   cardRt;
    CanvasGroup     cardGroup;
    TextMeshProUGUI titleText, subtitleText, bodyText, exampleText, footerText, tagText;
    GameObject      tagGo, exampleGo;
    Image           tagImg;
    LayoutElement   tagLayout;

    HoverTooltipTrigger hovered;   // pointer is over this one (shown or pending)
    HoverTooltipTrigger shownFor;  // card currently showing for this one
    float showAt;
    float lastHiddenAt = -10f;
    float fadeT;
    bool  suppressed;              // a click hid the card — stay hidden until the pointer moves to another target

    public static HoverTooltipView GetOrCreate()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("HoverTooltip");
        return go.AddComponent<HoverTooltipView>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
        cardRt.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Hover tracking ───────────────────────────────────────────────────────

    internal void Enter(HoverTooltipTrigger trigger)
    {
        if (trigger == null || trigger.Content == null) return;
        if (trigger != hovered) suppressed = false;
        hovered = trigger;
        bool warm = Time.unscaledTime - lastHiddenAt <= WarmWindow || shownFor != null;
        showAt = Time.unscaledTime + (warm ? WarmDelay : trigger.Delay);
        if (shownFor != null && shownFor != trigger) HideNow(warm: true);
    }

    internal void Exit(HoverTooltipTrigger trigger)
    {
        if (trigger != hovered) return;
        hovered = null;
        if (shownFor == trigger) HideNow(warm: true);
    }

    public void HideNow(bool warm)
    {
        if (shownFor != null) lastHiddenAt = warm ? Time.unscaledTime : -10f;
        shownFor = null;
        if (cardRt != null) cardRt.gameObject.SetActive(false);
    }

    void Update()
    {
        // Any click — on the target itself (pressing a button, starting a
        // drag) or anywhere else — means the player has moved on from reading.
        var mouse = Mouse.current;
        bool pressed = mouse != null && (mouse.leftButton.wasPressedThisFrame ||
                                         mouse.rightButton.wasPressedThisFrame ||
                                         mouse.middleButton.wasPressedThisFrame);
        if (pressed && (hovered != null || shownFor != null))
        {
            suppressed = true;
            HideNow(warm: false);
        }

        // Destroyed (Unity null — ReferenceEquals sees past it) or switched
        // off from under the pointer.
        if (hovered == null || !hovered.isActiveAndEnabled) hovered = null;
        if (!ReferenceEquals(shownFor, null) && (shownFor == null || !shownFor.isActiveAndEnabled)) HideNow(warm: false);

        // Nothing opens mid-drag (a command chip carried over its neighbors).
        bool buttonHeld = mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed);
        if (hovered != null && ReferenceEquals(shownFor, null) && !suppressed && !buttonHeld &&
            Time.unscaledTime >= showAt)
            Show(hovered);

        if (shownFor != null)
        {
            Place(shownFor); // targets can move under the pointer (the CLI panel's idle bob)
            if (fadeT < 1f)
            {
                fadeT = Mathf.Clamp01(fadeT + Time.unscaledDeltaTime / FadeDuration);
                cardGroup.alpha = fadeT;
            }
        }
    }

    // ── Showing ──────────────────────────────────────────────────────────────

    void Show(HoverTooltipTrigger trigger)
    {
        TooltipContent c;
        try { c = trigger.Content?.Invoke(); }
        catch (Exception e) { Debug.LogException(e); c = null; }
        if (c == null || string.IsNullOrEmpty(c.Title)) { hovered = null; return; }

        titleText.text = c.Title;

        bool hasTag = !string.IsNullOrEmpty(c.Tag);
        tagGo.SetActive(hasTag);
        if (hasTag)
        {
            tagText.text  = c.Tag;
            tagText.color = Color.Lerp(c.TagColor, Color.white, 0.35f);
            tagImg.sprite = PlateSpriteBuilder.GetSprite(Color.Lerp(c.TagColor, Color.black, 0.6f), c.TagColor, Trim,
                borderThickness: 1, chamfer: 2, pixelScale: 2);
            tagLayout.preferredWidth = tagText.GetPreferredValues(c.Tag).x + 14f;
        }

        SetOptional(subtitleText, c.Subtitle);
        SetOptional(bodyText, c.Body);
        SetOptional(footerText, c.Footer);
        exampleGo.SetActive(!string.IsNullOrEmpty(c.Example));
        exampleText.text = c.Example ?? "";

        shownFor = trigger;
        cardRt.gameObject.SetActive(true);
        cardRt.SetAsLastSibling();
        LayoutRebuilder.ForceRebuildLayoutImmediate(cardRt);
        Place(trigger);

        fadeT = 0f;
        cardGroup.alpha = 0f;
    }

    static void SetOptional(TextMeshProUGUI tmp, string text)
    {
        bool has = !string.IsNullOrEmpty(text);
        tmp.gameObject.SetActive(has);
        if (has) tmp.text = text;
    }

    // Everything in screen pixels: an overlay canvas's world space IS screen
    // space, and the target may live on any other overlay canvas.
    void Place(HoverTooltipTrigger trigger)
    {
        var targetRt = trigger.transform as RectTransform;
        if (targetRt == null) return;

        float scale = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        Rect target = ScreenOccupancy.ScreenRectOf(targetRt);
        Vector2 size = cardRt.rect.size * scale;
        float gap = TargetGap * scale, margin = ScreenMargin * scale;
        var screen = new Rect(margin, margin, Screen.width - margin * 2f, Screen.height - margin * 2f);

        Vector2 pos = Corner(trigger.Side, target, size, gap);
        if (!screen.Contains(pos) || !screen.Contains(pos + size))
        {
            Vector2 flipped = Corner(Opposite(trigger.Side), target, size, gap);
            if (screen.Contains(flipped) && screen.Contains(flipped + size)) pos = flipped;
        }

        pos.x = Mathf.Clamp(pos.x, screen.xMin, Mathf.Max(screen.xMin, screen.xMax - size.x));
        pos.y = Mathf.Clamp(pos.y, screen.yMin, Mathf.Max(screen.yMin, screen.yMax - size.y));
        cardRt.position = new Vector3(pos.x, pos.y, 0f); // pivot is bottom-left
    }

    // Bottom-left corner of the card when opened on `side` of `target`. A
    // side card lines its top up with the target's top and hangs down (so a
    // card for the first item in a list doesn't climb over whatever's above
    // the list); an above/below card is centered on the target.
    static Vector2 Corner(TooltipSide side, Rect target, Vector2 size, float gap)
    {
        switch (side)
        {
            case TooltipSide.Left:  return new Vector2(target.xMin - gap - size.x, target.yMax - size.y);
            case TooltipSide.Above: return new Vector2(target.center.x - size.x * 0.5f, target.yMax + gap);
            case TooltipSide.Below: return new Vector2(target.center.x - size.x * 0.5f, target.yMin - gap - size.y);
            default:                return new Vector2(target.xMax + gap, target.yMax - size.y);
        }
    }

    static TooltipSide Opposite(TooltipSide side) => side switch
    {
        TooltipSide.Left  => TooltipSide.Right,
        TooltipSide.Right => TooltipSide.Left,
        TooltipSide.Above => TooltipSide.Below,
        _                 => TooltipSide.Above,
    };

    // ── Construction ─────────────────────────────────────────────────────────

    void Build()
    {
        var font = Resources.Load<TMP_FontAsset>(FontResourcePath);

        var canvasGo = new GameObject("Tooltip_Canvas");
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);
        // No GraphicRaycaster: the card must never catch the pointer, or it
        // would steal hover from the very target it's explaining.

        var card = NewRect("Card", canvasGo.transform);
        cardRt = card.GetComponent<RectTransform>();
        cardRt.anchorMin = cardRt.anchorMax = Vector2.zero;
        cardRt.pivot     = Vector2.zero;
        cardRt.sizeDelta = new Vector2(CardWidth, 0f);
        var bg = card.AddComponent<Image>();
        bg.sprite = PlateSpriteBuilder.GetSprite(CardFill, CardBorder, Trim, borderThickness: 2, chamfer: 5, pixelScale: 3);
        bg.type = Image.Type.Sliced;
        bg.raycastTarget = false;
        cardGroup = card.AddComponent<CanvasGroup>();
        cardGroup.blocksRaycasts = false;
        cardGroup.interactable   = false;

        var layout = card.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 12, 12);
        layout.spacing = 6f;
        layout.childControlWidth      = true;
        layout.childControlHeight     = true;
        layout.childForceExpandWidth  = true;
        layout.childForceExpandHeight = false;
        var fitter = card.AddComponent<ContentSizeFitter>();
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // Title row: title text + optional category tag pill.
        var header = NewRect("Header", card.transform);
        var hl = header.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 8f;
        hl.childAlignment         = TextAnchor.MiddleLeft;
        hl.childControlWidth      = true;
        hl.childControlHeight     = true;
        hl.childForceExpandWidth  = false;
        hl.childForceExpandHeight = false;

        titleText = NewText("Title", header.transform, font, 15f, TitleColor);
        titleText.fontStyle = FontStyles.Bold;
        titleText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        tagGo = NewRect("Tag", header.transform);
        tagImg = tagGo.AddComponent<Image>();
        tagImg.type = Image.Type.Sliced;
        tagImg.raycastTarget = false;
        tagLayout = tagGo.AddComponent<LayoutElement>();
        tagLayout.preferredHeight = tagLayout.minHeight = 20f;
        tagLayout.flexibleWidth = 0f;
        tagText = NewText("Label", tagGo.transform, font, 10f, Color.white);
        tagText.alignment = TextAlignmentOptions.Center;
        tagText.enableWordWrapping = false;
        Stretch(tagText.rectTransform);

        subtitleText = NewText("Subtitle", card.transform, font, 12.5f, Terminal);
        bodyText     = NewText("Body", card.transform, font, 12f, BodyColor);
        bodyText.lineSpacing = 6f;

        exampleGo = NewRect("Example", card.transform);
        var exImg = exampleGo.AddComponent<Image>();
        exImg.sprite = PlateSpriteBuilder.GetSprite(InsetFill, InsetBorder, Trim, borderThickness: 1, chamfer: 2, pixelScale: 2);
        exImg.type = Image.Type.Sliced;
        exImg.raycastTarget = false;
        var exLayout = exampleGo.AddComponent<VerticalLayoutGroup>();
        exLayout.padding = new RectOffset(8, 8, 6, 6);
        exLayout.childControlWidth      = true;
        exLayout.childControlHeight     = true;
        exLayout.childForceExpandWidth  = true;
        exLayout.childForceExpandHeight = false;
        exampleText = NewText("Text", exampleGo.transform, font, 11.5f, Terminal);

        footerText = NewText("Footer", card.transform, font, 11f, FooterColor);
        footerText.fontStyle = FontStyles.Italic;
    }

    static GameObject NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = Vector2.zero; // a fresh RectTransform defaults to 100x100
        return go;
    }

    static TextMeshProUGUI NewText(string name, Transform parent, TMP_FontAsset font, float size, Color color)
    {
        var tmp = NewRect(name, parent).AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.enableWordWrapping = true;
        tmp.raycastTarget = false;
        return tmp;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
