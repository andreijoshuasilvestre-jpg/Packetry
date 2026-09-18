using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Small popup for assigning a PC's IP address — mirrors InterfacePickerPopup's
/// self-contained screen-space-overlay pattern. Shows the mask in both dotted
/// and /N (CIDR) form side by side, with a stepper that live-converts between
/// them and shows the usable-host count, so the player feels /24 vs /28 as a
/// host-count tradeoff before ever being asked to compute one.
///
/// Styled to match CLIPanelManager's router terminal look rather than a flat
/// panel: silver chamfered outer shell (PlateSpriteBuilder), a black terminal
/// inset for the form fields, dim-green "config line" input styling, and the
/// same PixeloidMono SDF pixel font — so opening a PC's config and a router's
/// CLI read as the same device-config UI family instead of two different UIs.
/// </summary>
public class DeviceAddressPopup : MonoBehaviour
{
    public static DeviceAddressPopup Instance { get; private set; }

    public bool IsOpen => _panelRoot != null && _panelRoot.activeSelf;

    const int MinPrefix = 8;
    const int MaxPrefix = 30;
    const int DefaultPrefix = 24;

    const string FontResourcePath = "Fonts/PixeloidMono SDF";
    TMP_FontAsset _font;

    NetworkDevice _device;
    int _prefix = DefaultPrefix;
    int _shownFrame = -1;

    GameObject      _panelRoot;
    RectTransform   _panelRt;
    TMP_InputField  _ipField;
    TextMeshProUGUI _maskLabel;
    TextMeshProUGUI _cidrLabel;
    TextMeshProUGUI _hostsLabel;

    static readonly Vector2 PanelSize = new Vector2(300, 210);

    // Same plate-color family as CLIPanelManager (silver shell / black inset /
    // dim-green terminal fields) so a PC's popup and a router's CLI panel read
    // as one visual language. "Trim" is the thin near-black accent line
    // PlateSpriteBuilder draws between border and fill on every plate.
    static readonly Color trimColor        = new Color(0.02f, 0.03f, 0.05f, 1f);
    static readonly Color panelFill        = new Color(0.70f, 0.70f, 0.71f, 1f); // silver outer shell
    static readonly Color panelBorder      = new Color(0.27f, 0.27f, 0.29f, 1f); // dark grey
    static readonly Color blackFill        = new Color(0.02f, 0.02f, 0.02f, 1f); // terminal inset
    static readonly Color blackBorder      = new Color(0.27f, 0.27f, 0.29f, 1f);
    static readonly Color titleTextColor   = new Color(0.12f, 0.13f, 0.15f, 1f); // dark — reads on the silver plate
    static readonly Color closeColor       = new Color(0.75f, 0.20f, 0.18f, 1f);
    static readonly Color labelColor       = new Color(0.65f, 0.72f, 0.82f, 1f);
    static readonly Color textColor        = new Color(0.35f, 0.86f, 0.47f, 1f); // terminal green
    static readonly Color placeholderColor = new Color(0.22f, 0.37f, 0.27f, 1f); // dim green
    static readonly Color fieldFill        = new Color(0.03f, 0.04f, 0.03f, 1f);
    static readonly Color fieldBorder      = new Color(0.20f, 0.40f, 0.25f, 1f); // dim green
    static readonly Color chipFill         = new Color(0.70f, 0.70f, 0.71f, 1f);
    static readonly Color chipBorder       = new Color(0.14f, 0.32f, 0.50f, 1f); // blue
    static readonly Color runFill          = new Color(0.35f, 0.75f, 0.45f, 1f); // Apply
    static readonly Color runBorder        = new Color(0.15f, 0.45f, 0.22f, 1f);
    static readonly Color clearFill        = new Color(0.75f, 0.40f, 0.38f, 1f); // Cancel
    static readonly Color clearBorder      = new Color(0.55f, 0.15f, 0.13f, 1f);

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
        if (!IsOpen || Mouse.current == null) return;
        if (Time.frameCount == _shownFrame) return; // ignore the click that opened us

        if (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            if (!RectTransformUtility.RectangleContainsScreenPoint(_panelRt, mousePos, null))
                Hide();
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void Show(NetworkDevice device, Vector2 screenPos)
    {
        _device = device;

        var addr = device.GetComponent<DeviceAddress>();
        _prefix = (addr != null && addr.HasAddress) ? SubnetMath.MaskToPrefixLength(addr.Mask) : DefaultPrefix;
        if (_prefix < MinPrefix || _prefix > MaxPrefix) _prefix = DefaultPrefix;
        _ipField.text = (addr != null && addr.HasAddress) ? addr.Ip : "";

        RefreshMaskDisplay();

        _panelRt.position = ClampToScreen(screenPos, _panelRt.sizeDelta);
        _shownFrame        = Time.frameCount;
        _panelRoot.SetActive(true);
    }

    public void Hide()
    {
        if (_panelRoot != null) _panelRoot.SetActive(false);
        _device = null;
    }

    Vector2 ClampToScreen(Vector2 screenPos, Vector2 sizeDelta)
    {
        float scale = _panelRoot.transform.parent.GetComponent<Canvas>().scaleFactor;
        Vector2 scaledSize = sizeDelta * scale;
        float x = Mathf.Clamp(screenPos.x, 0f, Mathf.Max(0f, Screen.width - scaledSize.x));
        float y = Mathf.Clamp(screenPos.y, scaledSize.y, Screen.height);
        return new Vector2(x, y);
    }

    // ── Behaviour ────────────────────────────────────────────────────────────

    void StepPrefix(int delta)
    {
        _prefix = Mathf.Clamp(_prefix + delta, MinPrefix, MaxPrefix);
        RefreshMaskDisplay();
    }

    void RefreshMaskDisplay()
    {
        string mask = SubnetMath.PrefixLengthToMask(_prefix);
        _maskLabel.text  = mask;
        _cidrLabel.text  = $"/{_prefix}";
        _hostsLabel.text = $"Usable hosts: {SubnetMath.UsableHostCount(_prefix)}";
    }

    void Apply()
    {
        var addr = _device.GetComponent<DeviceAddress>();
        if (addr == null) addr = _device.gameObject.AddComponent<DeviceAddress>();

        string ip = _ipField.text.Trim();
        if (!SubnetMath.IsDottedQuad(ip))
        {
            NotificationManager.Instance?.Notify("Invalid IP address format.", NotifyKind.Danger);
            return;
        }
        addr.SetAddress(ip, SubnetMath.PrefixLengthToMask(_prefix));

        // Mirrors RouterCLI.PushInterfaceState: an addressed interface counts as
        // "up" for Cable's ambient packet-flow cosmetic. PCs have exactly one port.
        _device.SetPortActive(0, true);

        Hide();
    }

    // ── UI construction ──────────────────────────────────────────────────────

    void BuildPanel()
    {
        var canvasGo = new GameObject("DeviceAddress_Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);
        canvasGo.AddComponent<GraphicRaycaster>();

        // Panel root — the silver chamfered outer shell, same plate the router
        // CLI panel uses (see CLIPanelManager.BuildPanel) — everything else
        // (header text, terminal inset, buttons) sits on top of it.
        _panelRoot = MakeRect("AddressPanel", canvasGo.transform);
        var panelImg = _panelRoot.AddComponent<Image>();
        panelImg.sprite = PlateSpriteBuilder.GetSprite(panelFill, panelBorder, trimColor,
            borderThickness: 2, chamfer: 5, pixelScale: 5);
        panelImg.type = Image.Type.Sliced;
        _panelRt = _panelRoot.GetComponent<RectTransform>();
        _panelRt.anchorMin = new Vector2(0f, 1f);
        _panelRt.anchorMax = new Vector2(0f, 1f);
        _panelRt.pivot     = new Vector2(0f, 1f);
        _panelRt.sizeDelta = PanelSize;

        // ── Header: title + close button, sitting directly on the silver
        // shell with no background of its own (matches CLIPanelManager.BuildHeader). ──
        var header = MakeRect("Header", _panelRoot.transform);
        var hrt = header.GetComponent<RectTransform>();
        hrt.anchorMin        = new Vector2(0, 1);
        hrt.anchorMax        = new Vector2(1, 1);
        hrt.pivot            = new Vector2(0.5f, 1f);
        hrt.anchoredPosition = Vector2.zero;
        hrt.sizeDelta        = new Vector2(0, 34);

        var title      = MakeTMP("Title", header.transform);
        title.font      = _font;
        title.text      = "PC Address";
        title.fontSize  = 15;
        title.color     = titleTextColor;
        title.alignment = TextAlignmentOptions.MidlineLeft;
        var trt = title.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(12, 0);
        trt.offsetMax = new Vector2(-34, 0);

        var closeBtnGo = MakeRect("CloseBtn", header.transform);
        var closeImg = closeBtnGo.AddComponent<Image>();
        closeImg.sprite = SmallPlate(closeColor, clearBorder);
        closeImg.type   = Image.Type.Sliced;
        var closeBtn = closeBtnGo.AddComponent<Button>();
        closeBtn.targetGraphic = closeImg;
        closeBtn.onClick.AddListener(Hide);
        var crt = closeBtnGo.GetComponent<RectTransform>();
        crt.anchorMin        = new Vector2(1, 0.5f);
        crt.anchorMax        = new Vector2(1, 0.5f);
        crt.pivot            = new Vector2(1f, 0.5f);
        crt.anchoredPosition = new Vector2(-6, 0);
        crt.sizeDelta        = new Vector2(22, 22);
        var xLabel      = MakeTMP("X", closeBtnGo.transform);
        xLabel.font      = _font;
        xLabel.text      = "x";
        xLabel.fontSize  = 12;
        xLabel.color     = Color.white;
        xLabel.alignment = TextAlignmentOptions.Center;
        StretchToFill(xLabel.GetComponent<RectTransform>());

        // ── Body: black terminal inset holding every form row, same
        // treatment as CLIPanelManager.BuildOutputArea's output plate. ──
        var body = MakeRect("Body", _panelRoot.transform);
        var bodyImg = body.AddComponent<Image>();
        bodyImg.sprite = PlateSpriteBuilder.GetSprite(blackFill, blackBorder, trimColor,
            borderThickness: 2, chamfer: 5, pixelScale: 3);
        bodyImg.type = Image.Type.Sliced;
        var bodyRt = body.GetComponent<RectTransform>();
        bodyRt.anchorMin        = new Vector2(0f, 1f);
        bodyRt.anchorMax        = new Vector2(1f, 1f);
        bodyRt.pivot            = new Vector2(0.5f, 1f);
        bodyRt.anchoredPosition = new Vector2(0, -34f);

        float y = -10f; // running cursor inside the body plate

        var ipLabel = MakeTMP("IpLabel", body.transform);
        ipLabel.font = _font;
        ipLabel.text = "IP address";
        ipLabel.fontSize = 11;
        ipLabel.color = labelColor;
        PlaceRow(ipLabel.GetComponent<RectTransform>(), y, 16);
        y -= 18f;

        _ipField = MakeInputField(body.transform, "192.168.1.10");
        PlaceRow(_ipField.GetComponent<RectTransform>(), y, 26);
        y -= 34f;

        var maskLabelHeader = MakeTMP("MaskHeader", body.transform);
        maskLabelHeader.font = _font;
        maskLabelHeader.text = "Subnet mask";
        maskLabelHeader.fontSize = 11;
        maskLabelHeader.color = labelColor;
        PlaceRow(maskLabelHeader.GetComponent<RectTransform>(), y, 16);
        y -= 20f;

        // Stepper row: [-]  255.255.255.0  /24  [+]
        var stepperRow = MakeRect("StepperRow", body.transform);
        PlaceRow(stepperRow.GetComponent<RectTransform>(), y, 26);

        var minusBtn = MakeStepperButton(stepperRow.transform, "-", new Vector2(0f, 0f), new Vector2(0.15f, 1f));
        minusBtn.onClick.AddListener(() => StepPrefix(-1));

        _maskLabel = MakeTMP("MaskValue", stepperRow.transform);
        _maskLabel.font      = _font;
        _maskLabel.fontSize  = 13;
        _maskLabel.alignment = TextAlignmentOptions.Center;
        _maskLabel.color     = textColor;
        var maskRt = _maskLabel.GetComponent<RectTransform>();
        maskRt.anchorMin = new Vector2(0.17f, 0f);
        maskRt.anchorMax = new Vector2(0.66f, 1f);
        maskRt.offsetMin = Vector2.zero;
        maskRt.offsetMax = Vector2.zero;

        _cidrLabel = MakeTMP("CidrValue", stepperRow.transform);
        _cidrLabel.font       = _font;
        _cidrLabel.fontSize   = 13;
        _cidrLabel.alignment  = TextAlignmentOptions.Center;
        _cidrLabel.color      = GameTheme.Info;
        var cidrRt = _cidrLabel.GetComponent<RectTransform>();
        cidrRt.anchorMin = new Vector2(0.68f, 0f);
        cidrRt.anchorMax = new Vector2(0.85f, 1f);
        cidrRt.offsetMin = Vector2.zero;
        cidrRt.offsetMax = Vector2.zero;

        var plusBtn = MakeStepperButton(stepperRow.transform, "+", new Vector2(0.85f, 0f), new Vector2(1f, 1f));
        plusBtn.onClick.AddListener(() => StepPrefix(1));

        y -= 32f;

        _hostsLabel = MakeTMP("HostsLabel", body.transform);
        _hostsLabel.font  = _font;
        _hostsLabel.fontSize  = 11;
        _hostsLabel.color     = labelColor;
        PlaceRow(_hostsLabel.GetComponent<RectTransform>(), y, 16);
        y -= 20f;

        // -y is the body plate's own top-down cursor; add back the 10px top
        // margin we started from so the plate wraps every row with even
        // padding above and below (see PlaceRow: rows are placed via -24 sizeDelta
        // margin already, this just closes out the plate's vertical extent).
        bodyRt.sizeDelta = new Vector2(0, -y + 10f);

        float footerY = -34f - (-y + 10f) - 10f; // header height + body height + gap

        var applyBtn = MakeTextButton(_panelRoot.transform, "Apply", runFill, runBorder);
        var applyRt = applyBtn.GetComponent<RectTransform>();
        applyRt.anchorMin = new Vector2(0.06f, 1f);
        applyRt.anchorMax = new Vector2(0.5f, 1f);
        applyRt.pivot     = new Vector2(0.5f, 1f);
        applyRt.anchoredPosition = new Vector2(0, footerY);
        applyRt.sizeDelta = new Vector2(-6, 28);
        applyBtn.onClick.AddListener(Apply);

        var cancelBtn = MakeTextButton(_panelRoot.transform, "Cancel", clearFill, clearBorder);
        var cancelRt = cancelBtn.GetComponent<RectTransform>();
        cancelRt.anchorMin = new Vector2(0.5f, 1f);
        cancelRt.anchorMax = new Vector2(0.94f, 1f);
        cancelRt.pivot     = new Vector2(0.5f, 1f);
        cancelRt.anchoredPosition = new Vector2(0, footerY);
        cancelRt.sizeDelta = new Vector2(-6, 28);
        cancelBtn.onClick.AddListener(Hide);

        // footerY brings us to the Apply/Cancel row's own top edge; that row is
        // itself 28 tall and extends further down from there, so the panel
        // must cover that too, or clicks on the buttons register as "outside
        // the popup" (see Update()) and dismiss it before Apply's save logic
        // ever runs.
        _panelRt.sizeDelta = new Vector2(PanelSize.x, -footerY + 28f + 12f);
    }

    static void PlaceRow(RectTransform rt, float y, float height)
    {
        rt.anchorMin        = new Vector2(0f, 1f);
        rt.anchorMax        = new Vector2(1f, 1f);
        rt.pivot            = new Vector2(0.5f, 1f);
        rt.sizeDelta        = new Vector2(-24, height);
        rt.anchoredPosition = new Vector2(0, y);
    }

    Button MakeStepperButton(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject($"Btn_{label}");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = SmallPlate(chipFill, chipBorder);
        img.type   = Image.Type.Sliced;
        var btn = go.AddComponent<Button>();
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var tmp = MakeTMP("Label", go.transform);
        tmp.font      = _font;
        tmp.text      = label;
        tmp.fontSize  = 15;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = titleTextColor;
        StretchToFill(tmp.GetComponent<RectTransform>());

        return btn;
    }

    Button MakeTextButton(Transform parent, string label, Color fill, Color border)
    {
        var go = new GameObject($"Btn_{label}");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = SmallPlate(fill, border);
        img.type   = Image.Type.Sliced;
        var btn = go.AddComponent<Button>();

        var tmp = MakeTMP("Label", go.transform);
        tmp.font      = _font;
        tmp.text      = label;
        tmp.fontSize  = 12;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = Color.white;
        StretchToFill(tmp.GetComponent<RectTransform>());

        return btn;
    }

    TMP_InputField MakeInputField(Transform parent, string placeholder)
    {
        var go = new GameObject("IpField");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = SmallPlate(fieldFill, fieldBorder);
        img.type   = Image.Type.Sliced;
        var field = go.AddComponent<TMP_InputField>();

        var textArea = MakeRect("TextArea", go.transform);
        StretchToFill(textArea.GetComponent<RectTransform>());
        var textAreaRt = textArea.GetComponent<RectTransform>();
        textAreaRt.offsetMin = new Vector2(8, 2);
        textAreaRt.offsetMax = new Vector2(-8, -2);

        var placeholderTmp = MakeTMP("Placeholder", textArea.transform);
        placeholderTmp.font      = _font;
        placeholderTmp.text      = placeholder;
        placeholderTmp.fontSize  = 13;
        placeholderTmp.color     = placeholderColor;
        StretchToFill(placeholderTmp.GetComponent<RectTransform>());

        var textTmp = MakeTMP("Text", textArea.transform);
        textTmp.font     = _font;
        textTmp.fontSize = 13;
        textTmp.color    = textColor;
        StretchToFill(textTmp.GetComponent<RectTransform>());

        field.textViewport   = textArea.GetComponent<RectTransform>();
        field.textComponent  = textTmp;
        field.placeholder    = placeholderTmp;
        field.characterLimit = 15;

        return field;
    }

    // Small chamfered plate for chips/buttons/the close icon — thin border,
    // short diagonal, same treatment as CLIPanelManager.SmallPlate so every
    // small control across both panels shares one look.
    Sprite SmallPlate(Color fill, Color border) =>
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
