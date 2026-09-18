using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Packet-Tracer-style popup listing a device's free interfaces.
/// Self-contained: owns its own screen-space overlay canvas.
///
/// Same colors as before (bgColor/rowColor/rowHoverColor, untouched), just
/// drawn as PlateSpriteBuilder chamfered plates instead of flat Image.color
/// rectangles — the same "pixelated" treatment as CLIPanelManager/
/// DeviceAddressPopup — with PixeloidMono SDF for the row labels instead of
/// TMP's default font. Row hover swaps between two pre-baked plate sprites
/// (see BuildPanel) rather than Button's ColorTint, matching
/// CLIPanelManager.BuildPaletteChip's sprite-swap-on-EventTrigger pattern.
/// </summary>
public class InterfacePickerPopup : MonoBehaviour
{
    public static InterfacePickerPopup Instance { get; private set; }

    public bool IsOpen => _panelRoot != null && _panelRoot.activeSelf;

    const string FontResourcePath = "Fonts/PixeloidMono SDF";
    TMP_FontAsset _font;

    private GameObject    _panelRoot;
    private RectTransform _panelRt;
    private Action<int>   _onPicked;
    private int           _shownFrame = -1;

    [SerializeField] private Vector2 rowSize      = new Vector2(170, 28);
    [SerializeField] private Color   bgColor       = new Color(0.08f, 0.10f, 0.15f, 0.97f);
    [SerializeField] private Color   rowColor      = new Color(0.15f, 0.18f, 0.24f, 1f);
    [SerializeField] private Color   rowHoverColor = new Color(0.22f, 0.30f, 0.42f, 1f);

    // Thin near-black accent PlateSpriteBuilder draws between border and fill
    // on every plate — the same trim every other chamfered panel in the game
    // uses (CLIPanelManager, DeviceAddressPopup), not a new theme color.
    static readonly Color trimColor = new Color(0.02f, 0.03f, 0.05f, 1f);

    // Pre-baked once in BuildPanel — rows just swap between these two on
    // hover rather than re-generating a sprite per row.
    Sprite _panelSprite;
    Sprite _rowNormalSprite;
    Sprite _rowHoverSprite;

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
        if (!IsOpen) return;
        if (Mouse.current == null) return;

        // The click that opened us this frame is still "wasPressedThisFrame" —
        // ignore it so we don't immediately dismiss ourselves before the user
        // gets a chance to click a row.
        if (Time.frameCount == _shownFrame) return;

        // Click outside the popup dismisses it without a pick.
        if (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            if (!RectTransformUtility.RectangleContainsScreenPoint(_panelRt, mousePos, null))
                Hide();
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void Show(NetworkDevice device, Vector2 screenPos, Action<int> onPicked)
    {
        _onPicked = onPicked;

        // Destroy() is deferred to end-of-frame, so _panelRt.childCount still
        // includes these old rows right after this loop — AddRow below must
        // NOT use childCount to compute row position, or every Show() after
        // the first stacks new rows below the old (soon-to-be-gone) ones.
        foreach (Transform child in _panelRt)
            Destroy(child.gameObject);

        var free = device.GetFreePorts();
        int nextRow = 0;
        if (free.Count == 0)
        {
            AddRow("No available interfaces", -1, false, nextRow++);
        }
        else
        {
            foreach (int idx in free)
                AddRow($"eth{idx}", idx, true, nextRow++);
        }

        int rowCount = Mathf.Max(1, free.Count);
        _panelRt.sizeDelta = new Vector2(rowSize.x, rowSize.y * rowCount);
        _panelRt.position  = ClampToScreen(screenPos, _panelRt.sizeDelta);
        _shownFrame        = Time.frameCount;
        _panelRoot.SetActive(true);
    }

    // Keeps the popup fully on-screen even when opened near a viewport edge,
    // accounting for the canvas's CanvasScaler scale factor.
    Vector2 ClampToScreen(Vector2 screenPos, Vector2 sizeDelta)
    {
        float scale = _panelRoot.transform.parent.GetComponent<Canvas>().scaleFactor;
        Vector2 scaledSize = sizeDelta * scale;

        float x = Mathf.Clamp(screenPos.x, 0f, Mathf.Max(0f, Screen.width - scaledSize.x));
        float y = Mathf.Clamp(screenPos.y, scaledSize.y, Screen.height);
        return new Vector2(x, y);
    }

    public void Hide()
    {
        if (_panelRoot != null) _panelRoot.SetActive(false);
        _onPicked = null;
    }

    // ── Row construction ─────────────────────────────────────────────────────

    void AddRow(string label, int index, bool clickable, int rowIndex)
    {
        var rowGo = MakeRect("Row", _panelRt);
        var img   = rowGo.AddComponent<Image>();
        img.sprite = _rowNormalSprite;
        img.type   = Image.Type.Sliced;
        var rt = rowGo.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0, 1);
        rt.anchorMax        = new Vector2(0, 1);
        rt.pivot             = new Vector2(0, 1);
        rt.sizeDelta        = rowSize;
        rt.anchoredPosition = new Vector2(0, -rowIndex * rowSize.y);

        var labelTmp = MakeTMP("Label", rowGo.transform);
        labelTmp.font      = _font;
        labelTmp.text      = label;
        labelTmp.fontSize  = 13;
        labelTmp.color     = clickable ? Color.white : new Color(0.55f, 0.55f, 0.55f);
        labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
        labelTmp.margin    = new Vector4(10, 0, 0, 0);
        StretchToFill(labelTmp.GetComponent<RectTransform>());

        if (clickable)
        {
            var btn = rowGo.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition    = Selectable.Transition.None; // sprite swap below owns hover visuals
            btn.onClick.AddListener(() => Pick(index));

            AddTrigger(rowGo, EventTriggerType.PointerEnter, _ => img.sprite = _rowHoverSprite);
            AddTrigger(rowGo, EventTriggerType.PointerExit,  _ => img.sprite = _rowNormalSprite);
        }
    }

    static void AddTrigger(GameObject go, EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> callback)
    {
        var trigger = go.GetComponent<EventTrigger>();
        if (trigger == null) trigger = go.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(callback);
        trigger.triggers.Add(entry);
    }

    void Pick(int index)
    {
        var callback = _onPicked;
        Hide();
        callback?.Invoke(index);
    }

    // ── UI construction ───────────────────────────────────────────────────────

    void BuildPanel()
    {
        var canvasGo = new GameObject("InterfacePicker_Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);
        canvasGo.AddComponent<GraphicRaycaster>();

        // Outer shell — same bgColor as before, now a chamfered plate (border
        // borrows rowHoverColor, already part of this popup's own palette,
        // rather than introducing a new hue) instead of a flat rectangle.
        _panelSprite = PlateSpriteBuilder.GetSprite(bgColor, rowHoverColor, trimColor,
            borderThickness: 2, chamfer: 4, pixelScale: 4);
        // Row plates: bgColor doubles as the frame between rows and the panel
        // it sits on, same "reuse what's already in the palette" approach.
        _rowNormalSprite = SmallPlate(rowColor, bgColor);
        _rowHoverSprite  = SmallPlate(rowHoverColor, bgColor);

        _panelRoot = MakeRect("Picker_Panel", canvasGo.transform);
        var panelImg = _panelRoot.AddComponent<Image>();
        panelImg.sprite = _panelSprite;
        panelImg.type   = Image.Type.Sliced;
        _panelRt       = _panelRoot.GetComponent<RectTransform>();
        _panelRt.anchorMin = new Vector2(0f, 1f);
        _panelRt.anchorMax = new Vector2(0f, 1f);
        _panelRt.pivot     = new Vector2(0f, 1f);
    }

    // Small chamfered plate for rows — thin border, short diagonal, same
    // treatment as CLIPanelManager.SmallPlate/DeviceAddressPopup.SmallPlate so
    // every small control across every panel in the game shares one look.
    static Sprite SmallPlate(Color fill, Color border) =>
        PlateSpriteBuilder.GetSprite(fill, border, trimColor, borderThickness: 1, chamfer: 2, pixelScale: 2);

    static GameObject MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    static TextMeshProUGUI MakeTMP(string name, Transform parent)
    {
        var go = MakeRect(name, parent);
        return go.AddComponent<TextMeshProUGUI>();
    }

    static void StretchToFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
