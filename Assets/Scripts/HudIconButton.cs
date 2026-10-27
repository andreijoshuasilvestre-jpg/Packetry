using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The square blue chamfered icon button the on-screen control clusters
/// share (ViewControlsHud's turn/reset/2D buttons, DeviceRotateHud's rotate
/// buttons), plus the bits of canvas plumbing both build with — one place for
/// the look, so the clusters can't drift apart.
/// </summary>
public static class HudIconButton
{
    public const string FontResourcePath = "Fonts/PixeloidMono SDF";
    public const int    SortingOrder     = 11;  // just above the HUD canvas (10)
    public const float  Size             = 54f;
    public const float  KeycapOverhang   = 7f;  // how far a keycap badge pokes above its button

    public static readonly Color Fill       = new Color(0.12f, 0.30f, 0.56f, 1f);
    public static readonly Color Border     = new Color(0.30f, 0.56f, 0.88f, 1f);
    public static readonly Color HoverFill  = new Color(0.20f, 0.42f, 0.74f, 1f);
    public static readonly Color OnFill     = new Color(0.16f, 0.52f, 0.38f, 1f); // a toggle that's switched on
    public static readonly Color OnBorder   = new Color(0.36f, 0.86f, 0.55f, 1f);
    public static readonly Color Trim       = new Color(0.02f, 0.03f, 0.05f, 1f);
    public static readonly Color LabelFill  = new Color(0.08f, 0.10f, 0.16f, 1f); // keycaps and caption plates
    public static readonly Color LabelBorder = new Color(0.62f, 0.70f, 0.82f, 1f);

    public static Sprite Plate(Color fill, Color border) =>
        PlateSpriteBuilder.GetSprite(fill, border, Trim, borderThickness: 2, chamfer: 3, pixelScale: 3);

    public static Sprite LabelPlate() =>
        PlateSpriteBuilder.GetSprite(LabelFill, LabelBorder, Trim, borderThickness: 1, chamfer: 1, pixelScale: 2);

    /// <summary>An overlay canvas for a control cluster, registered with UIScale.</summary>
    public static Canvas CreateCanvas(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    /// <summary>
    /// One button: a glyph (or a short text label) on the blue plate, an
    /// optional keycap badge in its top-left corner naming its keyboard
    /// shortcut, a hover plate swap, the shared click glow, and an
    /// explain-on-hover card.
    /// </summary>
    public static Button Create(Transform parent, string name, TMP_FontAsset font, Sprite glyph, string text,
        string keycap, UnityEngine.Events.UnityAction onClick, System.Func<TooltipContent> tooltip, out Image plate)
    {
        var go = NewRect(name, parent);
        ((RectTransform)go.transform).sizeDelta = new Vector2(Size, Size);
        plate = go.AddComponent<Image>();
        Sprite normal = Plate(Fill, Border), hover = Plate(HoverFill, Border);
        plate.sprite = normal;
        plate.type = Image.Type.Sliced;
        go.AddComponent<CanvasGroup>(); // see SetUsable
        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = plate;
        btn.onClick.AddListener(onClick);
        ButtonClickGlow.Attach(go, btn, Border);

        var trigger = go.AddComponent<EventTrigger>();
        Image p = plate;
        AddTrigger(trigger, EventTriggerType.PointerEnter, () => { if (btn.interactable) p.sprite = hover; });
        AddTrigger(trigger, EventTriggerType.PointerExit,  () => p.sprite = normal);

        if (glyph != null)
        {
            var icon = NewRect("Icon", go.transform).AddComponent<Image>();
            icon.sprite = glyph;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var irt = icon.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(Size - 14f, Size - 14f);
        }

        if (text != null)
        {
            var label = NewText("Label", go.transform, font, 19f, Color.white);
            label.text = text;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform);
        }

        if (keycap != null)
        {
            var cap = NewRect("Keycap", go.transform);
            var capImg = cap.AddComponent<Image>();
            capImg.sprite = LabelPlate();
            capImg.type = Image.Type.Sliced;
            capImg.raycastTarget = false;
            var crt = capImg.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0f, 1f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.anchoredPosition = new Vector2(6f, -4f);
            crt.sizeDelta = new Vector2(20f, 20f);
            var capLabel = NewText("Key", cap.transform, font, 12f, Color.white);
            capLabel.text = keycap;
            capLabel.fontStyle = FontStyles.Bold;
            capLabel.alignment = TextAlignmentOptions.Center;
            Stretch(capLabel.rectTransform);
        }

        HoverTooltip.Attach(go, tooltip, TooltipSide.Above, delay: 0.3f);
        return btn;
    }

    /// <summary>Greys a button out (or back in) through its own CanvasGroup
    /// rather than each Graphic's alpha — ButtonClickGlow's glow layer sits
    /// at alpha 0 until clicked.</summary>
    public static void SetUsable(Button b, bool usable)
    {
        if (b.interactable == usable) return;
        b.interactable = usable;
        b.GetComponent<CanvasGroup>().alpha = usable ? 1f : 0.35f;
    }

    public static void AddTrigger(EventTrigger trigger, EventTriggerType type, System.Action action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
    }

    public static GameObject NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        ((RectTransform)go.transform).sizeDelta = Vector2.zero; // a fresh RectTransform defaults to 100x100
        return go;
    }

    public static TextMeshProUGUI NewText(string name, Transform parent, TMP_FontAsset font, float size, Color color)
    {
        var tmp = NewRect(name, parent).AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = false;
        return tmp;
    }

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
