using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Tiny popup letting the player pick from a caller-supplied list of labeled
/// actions, shown from PacketSendManager either after clicking a free PC to
/// start it as a new sender (rows: HTTP Request / File Transfer) or after
/// right-clicking an already-assigned PC to edit it (rows vary — see
/// PacketSendManager.OpenContextPopup). An optional header line renders above
/// the rows when supplied.
///
/// Styled as InterfacePickerPopup's sibling — same navy palette, same
/// PlateSpriteBuilder chamfered plates, PixeloidMono SDF labels, sprite-swap
/// hover, ButtonClickGlow and PanelRevealFX open/close — so the two context
/// popups that float over devices read as one family. On top of that, a row
/// can carry an icon (tinted by its accent), an accent color (hover border +
/// click glow), a checkmark for the option currently in effect, and a red
/// "destructive" treatment for rows that clear something. The popup still
/// doesn't know what a row means — the caller decides all of that per row.
/// </summary>
public class PacketTypePickerPopup : MonoBehaviour
{
    public static PacketTypePickerPopup Instance { get; private set; }

    /// <summary>One clickable row. Only Label/OnClick are required.</summary>
    public struct Row
    {
        public string Label;
        public Action OnClick;
        public Sprite Icon;       // drawn white-on-transparent, tinted by Accent (or the danger red)
        public Color? Accent;     // icon tint, hover border, click glow — falls back to rowHoverColor
        public bool   Selected;   // the option currently in effect — outlined in Accent + checkmark
        public bool   Destructive; // clears/removes something — red icon/label/hover
    }

    public bool IsOpen => _panelRoot != null && _panelRoot.activeSelf;

    const string FontResourcePath = "Fonts/PixeloidMono SDF";
    TMP_FontAsset _font;

    private GameObject    _panelRoot;
    private RectTransform _panelRt;
    // Sits between the canvas and _panelRoot, carrying a RectMask2D — Show()
    // animates THIS rect's height open (see PanelRevealFX), while
    // _panelRoot/_panelRt keep a fixed size/position inside it, same split
    // InterfacePickerPopup/CLIPanelManager use for their own reveal.
    private GameObject    _panelWrapper;
    private Canvas        _canvas;
    private int           _shownFrame = -1;
    // True from Hide() until the close animation finishes — the rows are
    // still on screen (and still raycast-able) while they shrink away, so a
    // fast double-click must not fire a second pick.
    private bool          _closing;

    [SerializeField] private Vector2 rowSize       = new Vector2(190, 28);
    [SerializeField] private Color   bgColor       = new Color(0.08f, 0.10f, 0.15f, 0.97f);
    [SerializeField] private Color   rowColor      = new Color(0.15f, 0.18f, 0.24f, 1f);
    [SerializeField] private Color   rowHoverColor = new Color(0.22f, 0.30f, 0.42f, 1f);
    [SerializeField] private Color   infoColor     = new Color(0.60f, 0.68f, 0.78f, 1f);

    // Thin near-black accent PlateSpriteBuilder draws between border and fill
    // on every plate — the same trim every other chamfered panel in the game
    // uses (CLIPanelManager, DeviceAddressPopup, InterfacePickerPopup).
    static readonly Color trimColor = new Color(0.02f, 0.03f, 0.05f, 1f);

    // Destructive rows: GameTheme's danger red for the icon/hover border, a
    // dark red fill on hover, and a label nudged toward red so the row reads
    // as "removes something" before the player even hovers it.
    static readonly Color dangerHoverFill = new Color(0.30f, 0.10f, 0.12f, 1f);
    static readonly Color dangerLabel     = Color.Lerp(Color.white, GameTheme.Danger, 0.45f);

    const float Pad         = 8f;  // shell border ring (borderThickness 2 × pixelScale 4) — rows sit inside it
    const float RowGap      = 2f;
    const float HeaderH     = 20f;
    const float IconSize    = 16f;
    const float IconInset   = 9f;
    const float CheckSize   = 12f;
    const float LabelInset  = 10f;

    Sprite _panelSprite;
    Sprite _rowNormalSprite;
    Sprite _checkSprite;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        _font = Resources.Load<TMP_FontAsset>(FontResourcePath);
        BuildPanel();
        _panelRoot.SetActive(false);
    }

    void Update()
    {
        if (!IsOpen || _closing || Mouse.current == null) return;

        // The click that opened us this frame is still "wasPressedThisFrame" —
        // ignore it so we don't immediately dismiss ourselves before the user
        // gets a chance to click a row.
        if (Time.frameCount == _shownFrame) return;
        if (InteractionMode.IsSuspended) return; // a click on an overlay above us isn't "outside"

        if (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            if (!RectTransformUtility.RectangleContainsScreenPoint(_panelRt, mousePos, null))
                Hide();
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Shows one row per entry, top to bottom, plus an optional
    /// header line above them when header is non-null. Each row's OnClick
    /// already knows what it should do (commit a type, clear an assignment,
    /// etc.) — this popup doesn't know or care what a row means, it just
    /// renders and dispatches.</summary>
    public void Show(Vector2 screenPos, string header, List<Row> rows)
    {
        // Destroy() is deferred to end-of-frame, so row placement below uses
        // its own running index rather than childCount (see InterfacePickerPopup.Show).
        foreach (Transform child in _panelRt)
            Destroy(child.gameObject);

        // Rows are rowSize.x wide unless the header needs more — it names
        // the PC's address, so its length varies and it must never clip.
        float width = rowSize.x;
        float y = -Pad;
        if (!string.IsNullOrEmpty(header))
        {
            width = Mathf.Max(width, AddHeader(header, y));
            y -= HeaderH;
        }
        for (int i = 0; i < rows.Count; i++)
        {
            AddRow(rows[i], y, width);
            y -= rowSize.y + RowGap;
        }

        float fullWidth  = width + Pad * 2f;
        float fullHeight = -y - RowGap + Pad;
        // _panelRt keeps a FIXED size/position from here on — top edge glued to
        // the wrapper's own top edge (wrapper is left-CENTER pivoted, so its
        // local +fullHeight/2 is its top) — only the wrapper's height animates.
        _panelRt.sizeDelta        = new Vector2(fullWidth, fullHeight);
        _panelRt.anchoredPosition = new Vector2(0f, fullHeight * 0.5f);

        var wrapperRt = _panelWrapper.GetComponent<RectTransform>();
        wrapperRt.sizeDelta = new Vector2(fullWidth, fullHeight);
        Vector2 topLeft = ClampToScreen(screenPos, wrapperRt.sizeDelta);
        wrapperRt.position = new Vector3(topLeft.x, topLeft.y - fullHeight * 0.5f * _canvas.scaleFactor, wrapperRt.position.z);

        _shownFrame = Time.frameCount;
        _closing    = false;
        _panelRoot.SetActive(true);

        PanelRevealFX.Play(_panelWrapper, fullHeight, rowHoverColor,
            _canvas.transform as RectTransform, _canvas.worldCamera);
    }

    public void Hide()
    {
        if (_panelRoot == null || !_panelRoot.activeSelf || _closing) return;
        _closing = true;
        PanelRevealFX.PlayClose(_panelWrapper, _panelRt.sizeDelta.y, rowHoverColor,
            _canvas.transform as RectTransform, _canvas.worldCamera,
            () => { _closing = false; if (_panelRoot != null) _panelRoot.SetActive(false); });
    }

    // Keeps the popup fully on-screen even when opened near a viewport edge,
    // accounting for the canvas's CanvasScaler scale factor.
    Vector2 ClampToScreen(Vector2 screenPos, Vector2 sizeDelta)
    {
        float scale = _canvas.scaleFactor;
        Vector2 scaledSize = sizeDelta * scale;

        float x = Mathf.Clamp(screenPos.x, 0f, Mathf.Max(0f, Screen.width - scaledSize.x));
        float y = Mathf.Clamp(screenPos.y, scaledSize.y, Screen.height);
        return new Vector2(x, y);
    }

    // ── Row construction ─────────────────────────────────────────────────────

    // Returns the width the header needs, so Show() can widen the rows to match.
    float AddHeader(string text, float y)
    {
        var lineGo = MakeRect("Header", _panelRt);

        var tmp = MakeTMP("Label", lineGo.transform);
        tmp.text      = text;
        tmp.fontSize  = 11;
        tmp.color     = infoColor;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.margin    = new Vector4(LabelInset - 4f, 0, LabelInset - 4f, 2);
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        StretchToFill(tmp.rectTransform);

        float width = Mathf.Max(rowSize.x, Mathf.Ceil(tmp.GetPreferredValues(text).x) + tmp.margin.x + tmp.margin.z);
        PlaceTopLeft(lineGo.GetComponent<RectTransform>(), new Vector2(Pad, y), new Vector2(width, HeaderH));
        return width;
    }

    void AddRow(Row row, float y, float width)
    {
        Color accent = row.Destructive ? GameTheme.Danger : row.Accent ?? rowHoverColor;

        var rowGo = MakeRect("Row", _panelRt);
        PlaceTopLeft(rowGo.GetComponent<RectTransform>(), new Vector2(Pad, y), new Vector2(width, rowSize.y));

        // A selected row keeps its accent border even at rest, so the option
        // currently in effect is visible before hovering anything.
        Sprite normal = row.Selected ? SmallPlate(rowColor, accent) : _rowNormalSprite;
        Sprite hover  = SmallPlate(row.Destructive ? dangerHoverFill : rowHoverColor, accent);

        var img = rowGo.AddComponent<Image>();
        img.sprite = normal;
        img.type   = Image.Type.Sliced;

        float labelLeft = LabelInset;
        if (row.Icon != null)
        {
            var icon = MakeIcon("Icon", rowGo.transform, row.Icon, accent, IconSize);
            PlaceMidLeft(icon.rectTransform, IconInset, IconSize);
            labelLeft = IconInset + IconSize + 7f;
        }

        float labelRight = LabelInset;
        if (row.Selected)
        {
            var check = MakeIcon("Check", rowGo.transform, _checkSprite, GameTheme.Success, CheckSize);
            var crt = check.rectTransform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 0.5f);
            crt.sizeDelta        = new Vector2(CheckSize, CheckSize);
            crt.anchoredPosition = new Vector2(-IconInset, 0f);
            labelRight = IconInset + CheckSize + 4f;
        }

        var labelTmp = MakeTMP("Label", rowGo.transform);
        labelTmp.text      = row.Label;
        labelTmp.fontSize  = 13;
        labelTmp.color     = row.Destructive ? dangerLabel : Color.white;
        labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
        labelTmp.margin    = new Vector4(labelLeft, 0, labelRight, 0);
        labelTmp.textWrappingMode = TextWrappingModes.NoWrap;
        StretchToFill(labelTmp.rectTransform);

        var btn = rowGo.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.transition    = Selectable.Transition.None; // sprite swap below owns hover visuals
        Action onClick = row.OnClick;
        btn.onClick.AddListener(() => Pick(onClick));
        ButtonClickGlow.Attach(rowGo, btn, accent);

        AddTrigger(rowGo, EventTriggerType.PointerEnter, _ => img.sprite = hover);
        AddTrigger(rowGo, EventTriggerType.PointerExit,  _ => img.sprite = normal);
    }

    static void AddTrigger(GameObject go, EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> callback)
    {
        var trigger = go.GetComponent<EventTrigger>();
        if (trigger == null) trigger = go.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(callback);
        trigger.triggers.Add(entry);
    }

    void Pick(Action onClick)
    {
        if (_closing) return;
        Hide();
        onClick?.Invoke();
    }

    // ── UI construction ───────────────────────────────────────────────────────

    void BuildPanel()
    {
        var canvasGo = new GameObject("PacketTypePicker_Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);
        canvasGo.AddComponent<GraphicRaycaster>();
        _canvas = canvas;

        // Reveal wrapper — see InterfacePickerPopup.BuildPanel: left-CENTER
        // pivoted so growing sizeDelta.y opens the popup symmetrically from
        // its own middle, masked so the rows never squash while it grows.
        _panelWrapper = MakeRect("Picker_Wrapper", canvasGo.transform);
        _panelWrapper.AddComponent<RectMask2D>();
        _panelWrapper.GetComponent<RectTransform>().pivot = new Vector2(0f, 0.5f);

        _panelSprite     = PlateSpriteBuilder.GetSprite(bgColor, rowHoverColor, trimColor,
            borderThickness: 2, chamfer: 4, pixelScale: 4);
        _rowNormalSprite = SmallPlate(rowColor, bgColor);
        _checkSprite     = CheckmarkIconBuilder.GetSprite(Color.white);

        _panelRoot = MakeRect("Picker_Panel", _panelWrapper.transform);
        OccupiesScreen.Add(_panelRoot, OccupantLayer.Panel); // tutorial callout/markers step around it while open
        var panelImg = _panelRoot.AddComponent<Image>();
        panelImg.sprite = _panelSprite;
        panelImg.type   = Image.Type.Sliced;
        _panelRt = _panelRoot.GetComponent<RectTransform>();
        // Anchored to the wrapper's own (never-moving) center pivot, not its
        // animating top edge — same reasoning as InterfacePickerPopup.BuildPanel.
        _panelRt.anchorMin = new Vector2(0f, 0.5f);
        _panelRt.anchorMax = new Vector2(0f, 0.5f);
        _panelRt.pivot     = new Vector2(0f, 1f);
    }

    // Small chamfered plate for rows — same treatment as
    // InterfacePickerPopup/CLIPanelManager/DeviceAddressPopup.SmallPlate so
    // every small control across every panel in the game shares one look.
    static Sprite SmallPlate(Color fill, Color border) =>
        PlateSpriteBuilder.GetSprite(fill, border, trimColor, borderThickness: 1, chamfer: 2, pixelScale: 2);

    static void PlaceTopLeft(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin        = new Vector2(0, 1);
        rt.anchorMax        = new Vector2(0, 1);
        rt.pivot            = new Vector2(0, 1);
        rt.sizeDelta        = size;
        rt.anchoredPosition = new Vector2(pos.x, pos.y);
    }

    static void PlaceMidLeft(RectTransform rt, float x, float size)
    {
        rt.anchorMin        = new Vector2(0, 0.5f);
        rt.anchorMax        = new Vector2(0, 0.5f);
        rt.pivot            = new Vector2(0, 0.5f);
        rt.sizeDelta        = new Vector2(size, size);
        rt.anchoredPosition = new Vector2(x, 0f);
    }

    static Image MakeIcon(string name, Transform parent, Sprite sprite, Color tint, float size)
    {
        var go  = MakeRect(name, parent);
        var img = go.AddComponent<Image>();
        img.sprite         = sprite;
        img.color          = tint;
        img.preserveAspect = true;
        img.raycastTarget  = false;
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(size, size);
        return img;
    }

    static GameObject MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    TextMeshProUGUI MakeTMP(string name, Transform parent)
    {
        var go  = MakeRect(name, parent);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (_font != null) tmp.font = _font;
        tmp.raycastTarget = false;
        return tmp;
    }

    static void StretchToFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
