using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Tiny popup letting the player pick which packet type to send, shown right
/// after selecting a source device in PacketSendManager. Mirrors
/// InterfacePickerPopup's self-contained screen-space-overlay pattern, just
/// with a fixed pair of options instead of a device's free-port list.
/// </summary>
public class PacketTypePickerPopup : MonoBehaviour
{
    public static PacketTypePickerPopup Instance { get; private set; }

    public bool IsOpen => _panelRoot != null && _panelRoot.activeSelf;

    private GameObject         _panelRoot;
    private RectTransform      _panelRt;
    private Action<PacketType> _onPicked;
    private int                _shownFrame = -1;

    [SerializeField] private Vector2 rowSize      = new Vector2(150, 28);
    [SerializeField] private Color   bgColor       = new Color(0.08f, 0.10f, 0.15f, 0.97f);
    [SerializeField] private Color   rowColor      = new Color(0.15f, 0.18f, 0.24f, 1f);
    [SerializeField] private Color   rowHoverColor = new Color(0.22f, 0.30f, 0.42f, 1f);

    static readonly (string label, PacketType type)[] Options =
    {
        ("HTTP Request",  PacketType.HttpRequest),
        ("File Transfer",  PacketType.FileTransfer),
    };

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        BuildPanel();
        _panelRoot.SetActive(false);
    }

    void Update()
    {
        if (!IsOpen || Mouse.current == null) return;

        // The click that opened us this frame is still "wasPressedThisFrame" —
        // ignore it so we don't immediately dismiss ourselves before the user
        // gets a chance to click a row.
        if (Time.frameCount == _shownFrame) return;

        if (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            if (!RectTransformUtility.RectangleContainsScreenPoint(_panelRt, mousePos, null))
                Hide();
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void Show(Vector2 screenPos, Action<PacketType> onPicked)
    {
        _onPicked = onPicked;

        foreach (Transform child in _panelRt)
            Destroy(child.gameObject);

        for (int i = 0; i < Options.Length; i++)
            AddRow(Options[i].label, Options[i].type, i);

        _panelRt.sizeDelta = new Vector2(rowSize.x, rowSize.y * Options.Length);
        _panelRt.position  = ClampToScreen(screenPos, _panelRt.sizeDelta);
        _shownFrame        = Time.frameCount;
        _panelRoot.SetActive(true);
    }

    public void Hide()
    {
        if (_panelRoot != null) _panelRoot.SetActive(false);
        _onPicked = null;
    }

    Vector2 ClampToScreen(Vector2 screenPos, Vector2 sizeDelta)
    {
        float scale = _panelRoot.transform.parent.GetComponent<Canvas>().scaleFactor;
        Vector2 scaledSize = sizeDelta * scale;

        float x = Mathf.Clamp(screenPos.x, 0f, Mathf.Max(0f, Screen.width - scaledSize.x));
        float y = Mathf.Clamp(screenPos.y, scaledSize.y, Screen.height);
        return new Vector2(x, y);
    }

    // ── Row construction ─────────────────────────────────────────────────────

    void AddRow(string label, PacketType type, int rowIndex)
    {
        var rowGo = MakeRect("Row", _panelRt);
        var img   = rowGo.AddComponent<Image>();
        img.color = rowColor;
        var rt = rowGo.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0, 1);
        rt.anchorMax        = new Vector2(0, 1);
        rt.pivot            = new Vector2(0, 1);
        rt.sizeDelta        = rowSize;
        rt.anchoredPosition = new Vector2(0, -rowIndex * rowSize.y);

        var labelTmp = MakeTMP("Label", rowGo.transform);
        labelTmp.text      = label;
        labelTmp.fontSize  = 13;
        labelTmp.color     = Color.white;
        labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
        labelTmp.margin    = new Vector4(10, 0, 0, 0);
        StretchToFill(labelTmp.GetComponent<RectTransform>());

        var btn = rowGo.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = rowHoverColor;
        colors.pressedColor     = rowHoverColor;
        btn.colors = colors;
        btn.onClick.AddListener(() => Pick(type));
    }

    void Pick(PacketType type)
    {
        var callback = _onPicked;
        Hide();
        callback?.Invoke(type);
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

        _panelRoot = MakeRect("Picker_Panel", canvasGo.transform);
        _panelRoot.AddComponent<Image>().color = bgColor;
        _panelRt       = _panelRoot.GetComponent<RectTransform>();
        _panelRt.anchorMin = new Vector2(0f, 1f);
        _panelRt.anchorMax = new Vector2(0f, 1f);
        _panelRt.pivot     = new Vector2(0f, 1f);
    }

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
