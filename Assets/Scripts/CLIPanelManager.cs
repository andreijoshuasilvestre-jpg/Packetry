using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using TMPro;

public class CLIPanelManager : MonoBehaviour
{
    public static CLIPanelManager Instance { get; private set; }

    const string FontResourcePath = "Fonts/PixeloidMono SDF";
    TMP_FontAsset _font;

    public bool IsOpen => _panelRoot != null && _panelRoot.activeSelf;

    /// <summary>Whichever device the panel is currently open for, or null when closed.
    /// Exposed for guide/tutorial code (see Level3Guide) that needs to confirm the
    /// player opened the CLI on a *specific* device, not just that some panel is open.</summary>
    public NetworkDevice CurrentDevice => IsOpen ? _currentDevice : null;

    /// <summary>Fired from Close() with whichever device the panel was open for.
    /// AutoSaveManager listens so a router's interface config (only ever mutated
    /// through CLI commands, not one of AutoSaveManager's other tracked events)
    /// actually gets persisted once the player is done editing it.</summary>
    public event System.Action<NetworkDevice> OnPanelClosed;

    private GameObject    _panelRoot;
    private TextMeshProUGUI _titleLabel;
    private TextMeshProUGUI _outputText;
    private TextMeshProUGUI _promptLabel;
    private TMP_InputField  _inputField;
    private ScrollRect      _outputScrollRect;
    private NetworkDevice   _currentDevice;

    private Canvas _screenCanvas;
    private Camera _mainCamera;
    private Cable  _highlightedCable;
    private bool   _cameraFocused;
    private Vector3 _savedLookTarget;
    private float   _savedDistance;
    private float   _savedPitch;
    private float   _savedYaw;
    private Vector3 _panelBasePos; // world anchor point (device position + side/height offset), without the idle float bob

    // One RouterCLI instance per placed router, persisted between opens
    private readonly Dictionary<NetworkDevice, RouterCLI> _routerSessions
        = new Dictionary<NetworkDevice, RouterCLI>();

    // ── Command builder (drag-and-drop) state ───────────────────────────────────
    private Transform  _paletteListParent;
    private Transform  _queueListParent;
    private ScrollRect _queueScrollRect;
    private TextMeshProUGUI _queueEmptyLabel;
    private GameObject _trayDropZoneGo;
    private GameObject _dragGhost;
    private readonly Dictionary<GameObject, (string name, string desc)> _paletteChipSource
        = new Dictionary<GameObject, (string name, string desc)>();
    private readonly Dictionary<string, GameObject> _paletteChipsByName
        = new Dictionary<string, GameObject>();
    private readonly List<QueuedCommand> _queue = new List<QueuedCommand>();

    // ── Guide/tutorial outline highlights (see PulsingOutline) ──────────────────
    // Two independent pulsing outlines: one points at whichever command the
    // player should click/drag or type next (a palette chip, or the input
    // field itself as a fallback), the other frames the prompt label so a
    // guide step can call out "notice the prompt changed" separately from
    // "here's what to do next". Both are built once and reparented onto their
    // targets rather than being destroyed/recreated, so they survive
    // RefreshModulePalette() rebuilding the chips out from under them.
    private PulsingOutline _commandOutline;
    private PulsingOutline _promptOutline;
    private bool   _hasCommandHighlight;
    private string _highlightedCommandName; // null = highlight the input field itself

    [Header("Panel")]
    // Narrower, comic-panel-style callout rather than a full dashboard — the
    // command builder sidebar (builderPanelWidth, below) attaches to its right
    // edge separately and isn't affected by this.
    [SerializeField] private Vector2 panelSize     = new Vector2(520, 460);

    [Header("World Focus (Router only)")]
    [SerializeField] private float focusDistance          = 7.5f;
    // Pitch matches the main camera's own max-zoom-in angle (same treatment as
    // DragDropManager's highlighted-device zoom) rather than a fixed hand-tuned
    // angle — yaw is likewise whatever direction the player was already facing,
    // not forced, so the panel reads correctly regardless of camera rotation.
    [SerializeField] private float focusPitchOffset       = 12f;
    // Panel sits to the side of the router (screen-space "right" of it, from
    // whatever angle the camera currently is) rather than directly overhead, so
    // the device model itself never overlaps/obscures it. Sized generously
    // because the command builder sidebar bulges further right past panelSize's
    // own edge — the offset has to clear the *combined* footprint, not just the terminal.
    [SerializeField] private float worldPanelSideOffset   = 4.5f;
    [SerializeField] private float worldPanelHeightOffset = 1.5f;
    [Header("Float")]
    [SerializeField] private float floatBobAmplitude      = 0.15f; // gentle idle hover while the panel is open
    [SerializeField] private float floatBobSpeed          = 1.2f;
    // The panel is a flat screen-space overlay that tracks the world anchor's
    // projected screen position — it can never clip through 3D geometry (there's
    // no depth-tested quad to intersect), and clamping this margin in from the
    // screen edge keeps it fully readable however the offset math above lands.
    [SerializeField] private float screenClampMargin      = 24f;

    [Header("Plate Colors")]
    // Every panel/chip/button in this file is a PlateSpriteBuilder chamfered
    // plate (fill + border + trim) — never a flat Image.color — matching the
    // grey/black/green terminal look established for this panel (see
    // ConfirmDialogManager for the same pattern). "Trim" is the thin near-
    // black accent line PlateSpriteBuilder draws between border and fill on
    // every plate, shared across all of them.
    [SerializeField] private Color trimColor       = new Color(0.02f, 0.03f, 0.05f, 1f);
    [SerializeField] private Color panelFill       = new Color(0.70f, 0.70f, 0.71f, 1f); // silver outer shell
    [SerializeField] private Color panelBorder     = new Color(0.27f, 0.27f, 0.29f, 1f); // dark grey
    [SerializeField] private Color blackFill       = new Color(0.02f, 0.02f, 0.02f, 1f); // output area / queue zone
    [SerializeField] private Color blackBorder     = new Color(0.27f, 0.27f, 0.29f, 1f);
    [SerializeField] private Color sidebarFill     = new Color(0.055f, 0.07f, 0.11f, 1f); // navy
    [SerializeField] private Color sidebarBorder   = new Color(0.14f, 0.32f, 0.50f, 1f);  // blue
    [SerializeField] private Color chipFill        = new Color(0.70f, 0.70f, 0.71f, 1f);
    [SerializeField] private Color chipBorder      = new Color(0.14f, 0.32f, 0.50f, 1f);
    [SerializeField] private Color chipHoverFill   = new Color(0.55f, 0.75f, 1.00f, 1f);
    [SerializeField] private Color chipHoverBorder = new Color(0.20f, 0.45f, 0.70f, 1f);
    [SerializeField] private Color runFill         = new Color(0.35f, 0.75f, 0.45f, 1f);
    [SerializeField] private Color runBorder       = new Color(0.15f, 0.45f, 0.22f, 1f);
    [SerializeField] private Color clearFill       = new Color(0.75f, 0.40f, 0.38f, 1f);
    [SerializeField] private Color clearBorder     = new Color(0.55f, 0.15f, 0.13f, 1f);
    [SerializeField] private Color paramFieldFill   = new Color(0.03f, 0.04f, 0.03f, 1f);
    [SerializeField] private Color paramFieldBorder = new Color(0.20f, 0.40f, 0.25f, 1f); // dim green

    [Header("Header")]
    [SerializeField] private float   titleFontSize  = 15f;
    [SerializeField] private Color   titleTextColor = new Color(0.12f, 0.13f, 0.15f, 1f); // dark — reads on the silver plate
    [SerializeField] private Color   closeColor     = new Color(0.75f, 0.20f, 0.18f, 1f);

    [Header("Output")]
    [SerializeField] private Color   textColor      = new Color(0.35f, 0.86f, 0.47f, 1f); // terminal green
    [SerializeField] private float   outputFontSize = 13f;

    [Header("Input")]
    [SerializeField] private Color   inputBgColor     = new Color(0.04f, 0.055f, 0.08f, 1f);
    [SerializeField] private Color   placeholderColor = new Color(0.22f, 0.37f, 0.27f, 1f); // dim green
    [SerializeField] private float   inputFontSize    = 14f;

    [Header("Command Builder")]
    [SerializeField] private float   builderPanelWidth = 330f;
    // Gap between the CLI panel's right edge and the command builder sidebar
    // (see BuildCommandBuilderPanel) — pulled out as a shared constant because
    // the centering math below (SidebarBulge/ComboCenterOffsetX) has to agree
    // with it exactly, not just the sidebar's own anchoredPosition.
    const float SidebarGap = 12f;

    // How much wider the sidebar makes the combined CLI+sidebar block than the
    // CLI panel alone — used to re-center the *combo*, not just the panel, so
    // the sidebar's bulge to the right doesn't read as an off-center layout.
    float SidebarBulge => builderPanelWidth + SidebarGap;
    float ComboCenterOffsetX => -SidebarBulge * 0.5f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        _mainCamera = Camera.main;
        _font = Resources.Load<TMP_FontAsset>(FontResourcePath);
        BuildPanel();
        _panelRoot.SetActive(false);
    }

    void Update()
    {
        if (!IsOpen) return;

        if (_currentDevice != null && _currentDevice.deviceType == DeviceType.Router)
            UpdateFloatingPanel();

        if (Keyboard.current == null) return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Close();
            return;
        }

        if (Keyboard.current.tabKey.wasPressedThisFrame && _inputField.isFocused)
            HandleTabComplete();
    }

    // Tracks the world anchor's projected screen position every frame (plus a
    // small idle sine bob, so it still reads as hovering) and clamps it inside
    // the screen — the panel itself is a flat overlay UI element, not a 3D quad,
    // so it can never clip through the router or anything else in the scene.
    void UpdateFloatingPanel()
    {
        if (_mainCamera == null) return;

        float bob = Mathf.Sin(Time.time * floatBobSpeed) * floatBobAmplitude;
        Vector3 worldPos = _panelBasePos + Vector3.up * bob;

        Vector3 screenPoint = _mainCamera.WorldToScreenPoint(worldPos);
        if (screenPoint.z <= 0f) return; // behind the camera — hold last position rather than snap somewhere wrong

        var canvasRt = (RectTransform)_screenCanvas.transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screenPoint, null, out Vector2 local);

        // Clamp against the *combined* CLI+sidebar footprint (not just the CLI
        // panel) so the sidebar can never clip off-screen, then re-derive the
        // panel's own anchored position from the clamped combo center — see
        // ComboCenterOffsetX/SidebarBulge for why the panel and the combo don't
        // share the same center.
        Vector2 halfCanvas    = canvasRt.rect.size * 0.5f;
        float comboHalfWidth  = (panelSize.x + SidebarBulge) * 0.5f;
        float maxComboX       = Mathf.Max(0f, halfCanvas.x - comboHalfWidth - screenClampMargin);
        float maxY            = Mathf.Max(0f, halfCanvas.y - panelSize.y * 0.5f - screenClampMargin);

        float comboCenterX = Mathf.Clamp(local.x, -maxComboX, maxComboX);
        local.x = comboCenterX + ComboCenterOffsetX;
        local.y = Mathf.Clamp(local.y, -maxY, maxY);

        _panelRoot.GetComponent<RectTransform>().anchoredPosition = local;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void Open(NetworkDevice device)
    {
        ClearHighlights(); // a stale guide highlight from a previous session shouldn't linger
        _currentDevice = device;
        _titleLabel.text = $"CLI  —  {device.deviceName}  [{device.deviceType}]";

        string banner;
        if (device.deviceType == DeviceType.Router)
        {
            RouterCLI cli = GetOrCreateSession(device);
            SetPrompt(cli.Prompt);
            banner = cli.Banner;

            // Float the panel beside the router (not overhead, so the device model
            // never overlaps/obscures it) and focus the camera on it — same "static
            // zoom" treatment as a highlighted device (DragDropManager): dolly in at
            // the camera's own max-zoom-in angle, keeping whatever yaw the player
            // already had instead of forcing a fixed facing.
            float focusYaw   = 0f;
            float focusPitch = focusPitchOffset;

            if (IsometricCamera.Instance != null)
            {
                _savedLookTarget = IsometricCamera.Instance.TargetLookTarget;
                _savedDistance   = IsometricCamera.Instance.TargetDistance;
                _savedPitch      = IsometricCamera.Instance.TargetPitch;
                _savedYaw        = IsometricCamera.Instance.TargetYaw;

                focusYaw   = _savedYaw; // keep whatever direction the player was already facing
                focusPitch = IsometricCamera.Instance.GroundPitch + focusPitchOffset;
            }

            // "Beside" is relative to the current view, not a fixed world axis — the
            // camera's own screen-space right, so the panel lands to the side the
            // player is actually looking from at any of the Q/E rotation steps.
            Vector3 viewRight = Quaternion.Euler(0f, focusYaw, 0f) * Vector3.right;
            _panelBasePos = device.transform.position
                          + viewRight * worldPanelSideOffset
                          + Vector3.up * worldPanelHeightOffset;

            // The panel itself is a plain screen-space overlay (like the non-router
            // case below) — UpdateFloatingPanel() tracks _panelBasePos's projected
            // screen position every frame, so reposition once now too or it'd flash
            // at whatever anchoredPosition was last left at for one frame.
            _panelRoot.transform.SetParent(_screenCanvas.transform, false);

            if (IsometricCamera.Instance != null)
            {
                // Look straight at the panel's position (not the device's base) so it
                // lands centered on screen regardless of the side offset above —
                // guarantees it's never cropped by the camera's view.
                IsometricCamera.Instance.FocusOn(_panelBasePos, focusDistance, focusPitch, focusYaw);
                _cameraFocused = true;
            }

            UpdateFloatingPanel();
        }
        else
        {
            SetPrompt(">");
            banner = $"Connected to {device.deviceName}.";

            // Original centered screen-space overlay behavior — no camera change.
            // Offset by ComboCenterOffsetX so the panel+sidebar combo is what's
            // actually centered on screen (see BuildPanel).
            _panelRoot.transform.SetParent(_screenCanvas.transform, false);
            _panelRoot.GetComponent<RectTransform>().anchoredPosition = new Vector2(ComboCenterOffsetX, 0f);
        }

        _outputText.text = banner;
        _panelRoot.SetActive(true);
        _inputField.text = string.Empty;
        _inputField.ActivateInputField();
        ClearQueue();
        RefreshModulePalette();
        UpdateCableHighlight();
    }

    public void Close()
    {
        NetworkDevice closedDevice = _currentDevice;

        _panelRoot.SetActive(false);
        _currentDevice = null;
        ClearQueue();
        ClearHighlights();

        if (_dragGhost != null)
        {
            Destroy(_dragGhost);
            _dragGhost = null;
        }

        if (_highlightedCable != null)
        {
            _highlightedCable.SetHighlight(false);
            _highlightedCable = null;
        }

        if (_cameraFocused && IsometricCamera.Instance != null)
        {
            IsometricCamera.Instance.FocusOn(_savedLookTarget, _savedDistance, _savedPitch, _savedYaw);
            _cameraFocused = false;
        }

        if (closedDevice != null) OnPanelClosed?.Invoke(closedDevice);
    }

    // ── Input handler ─────────────────────────────────────────────────────────

    void OnInputSubmit(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        string trimmed = text.Trim();

        _inputField.text = string.Empty;
        _inputField.ActivateInputField();

        if (_currentDevice == null) return;

        if (_currentDevice.deviceType == DeviceType.Router)
        {
            RouterCLI cli = GetOrCreateSession(_currentDevice);
            if (!ExecuteRouterLine(cli, trimmed)) return;
            RefreshModulePalette();
            UpdateCableHighlight();
        }
        else
        {
            // Other device types: echo only (expand later)
            AppendOutput($"> {trimmed}");
            AppendOutput("% CLI not implemented for this device type.");
        }
    }

    // ── Command builder (drag-and-drop) ─────────────────────────────────────────

    /// <summary>
    /// Echoes and executes one router command line, mirroring what typing +
    /// Enter does. Returns false if the session closed the panel as a result
    /// (caller should stop, e.g. abort a running queue).
    /// </summary>
    bool ExecuteRouterLine(RouterCLI cli, string line)
    {
        AppendOutput($"{cli.Prompt} {line}");
        string result = cli.Execute(line);

        if (result == "__CLOSE__")
        {
            Close();
            return false;
        }

        if (result != null)
            AppendOutput(result);

        SetPrompt(cli.Prompt);
        return true;
    }

    void AddQueuedCommand(string name)
    {
        var qc = new QueuedCommand
        {
            Segments = CLICommandTemplate.Parse(name)
        };
        qc.RowGo = BuildQueueRow(qc);
        _queue.Add(qc);
        RefreshQueueEmptyHint();
    }

    void RemoveQueuedCommand(QueuedCommand qc)
    {
        _queue.Remove(qc);
        if (qc.RowGo != null) Destroy(qc.RowGo);
        RefreshQueueEmptyHint();
    }

    void ClearQueue()
    {
        foreach (var qc in _queue)
            if (qc.RowGo != null) Destroy(qc.RowGo);
        _queue.Clear();
        RefreshQueueEmptyHint();
    }

    void RefreshQueueEmptyHint()
    {
        if (_queueEmptyLabel != null)
            _queueEmptyLabel.gameObject.SetActive(_queue.Count == 0);
    }

    void RunQueue()
    {
        if (_currentDevice == null || _currentDevice.deviceType != DeviceType.Router) return;
        if (_queue.Count == 0) return;

        RouterCLI cli = GetOrCreateSession(_currentDevice);

        // Snapshot: ClearQueue() (called below, or by Close() mid-loop) mutates the live list.
        var toRun = new List<QueuedCommand>(_queue);

        foreach (var qc in toRun)
        {
            string line = qc.Compose();
            if (!ExecuteRouterLine(cli, line)) return; // panel closed mid-batch
        }

        ClearQueue();
        RefreshModulePalette();
        UpdateCableHighlight();
    }

    // ── Tab-complete ──────────────────────────────────────────────────────────

    void HandleTabComplete()
    {
        if (_currentDevice == null || _currentDevice.deviceType != DeviceType.Router) return;

        RouterCLI cli  = GetOrCreateSession(_currentDevice);
        string    text = _inputField.text;

        int    spaceIdx   = text.IndexOf(' ');
        string firstToken = spaceIdx >= 0 ? text.Substring(0, spaceIdx) : text;
        if (firstToken.Length == 0) return;

        List<string> matches = cli.MatchPrefix(firstToken);

        if (matches.Count == 1)
        {
            string completed = spaceIdx >= 0 ? matches[0] + text.Substring(spaceIdx) : matches[0] + " ";
            _inputField.text          = completed;
            _inputField.caretPosition = completed.Length;
        }
        else if (matches.Count > 1)
        {
            AppendOutput(string.Join("  ", matches));
        }
    }

    /// <summary>
    /// Rebuilds the draggable module palette to match whichever RouterCLI mode
    /// the session is currently in (mirrors the old static help-text list, but
    /// each command is now its own draggable chip). Called on Open(), after a
    /// typed command, and after a queued batch finishes running.
    /// </summary>
    void RefreshModulePalette()
    {
        if (_paletteListParent == null) return;

        // Park the command outline back on its home parent first — it can't stay
        // attached to a chip that's about to be Destroy()'d below.
        _commandOutline?.Attach(null);

        foreach (Transform child in _paletteListParent)
            Destroy(child.gameObject);
        _paletteChipSource.Clear();
        _paletteChipsByName.Clear();

        bool isRouter = _currentDevice != null && _currentDevice.deviceType == DeviceType.Router;
        if (!isRouter)
        {
            var msg = MakeTMP("EmptyHint", _paletteListParent);
            msg.text      = "No CLI commands\navailable for this\ndevice type.";
            msg.fontSize  = outputFontSize;
            msg.color     = textColor;
            msg.alignment = TextAlignmentOptions.TopLeft;
            var le = msg.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 60;
            ApplyCommandHighlight();
            return;
        }

        RouterCLI cli = GetOrCreateSession(_currentDevice);
        foreach (var (name, desc) in cli.CurrentCommands)
            BuildPaletteChip(name, desc);

        ApplyCommandHighlight();
    }

    void BuildPaletteChip(string name, string desc)
    {
        var chipGo = MakeRect("ModuleChip", _paletteListParent);
        var img = chipGo.AddComponent<Image>();
        Sprite chipNormal = SmallPlate(chipFill, chipBorder);
        Sprite chipHover  = SmallPlate(chipHoverFill, chipHoverBorder);
        img.sprite = chipNormal;
        img.type   = Image.Type.Sliced;
        var le = chipGo.AddComponent<LayoutElement>();
        le.minHeight = 42;
        le.preferredHeight = 42;

        // Dark text — chip fill is the light silver plate, same as the panel.
        var nameTmp      = MakeTMP("Name", chipGo.transform);
        nameTmp.text      = name;
        nameTmp.fontSize  = 12f;
        nameTmp.color     = titleTextColor;
        nameTmp.fontStyle = FontStyles.Bold;
        nameTmp.alignment = TextAlignmentOptions.TopLeft;
        nameTmp.enableWordWrapping = false;
        nameTmp.overflowMode = TextOverflowModes.Ellipsis;
        var nrt = nameTmp.GetComponent<RectTransform>();
        nrt.anchorMin = new Vector2(0, 0.5f);
        nrt.anchorMax = new Vector2(1, 1);
        nrt.offsetMin = new Vector2(8, 0);
        nrt.offsetMax = new Vector2(-8, -3);

        var descTmp      = MakeTMP("Desc", chipGo.transform);
        descTmp.text      = desc;
        descTmp.fontSize  = 9.5f;
        descTmp.color     = new Color(titleTextColor.r, titleTextColor.g, titleTextColor.b, 0.65f);
        descTmp.alignment = TextAlignmentOptions.TopLeft;
        descTmp.enableWordWrapping = false;
        descTmp.overflowMode = TextOverflowModes.Ellipsis;
        var drt = descTmp.GetComponent<RectTransform>();
        drt.anchorMin = new Vector2(0, 0);
        drt.anchorMax = new Vector2(1, 0.5f);
        drt.offsetMin = new Vector2(8, 2);
        drt.offsetMax = new Vector2(-8, 0);

        _paletteChipSource[chipGo] = (name, desc);
        _paletteChipsByName[name]  = chipGo;

        AddTrigger(chipGo, EventTriggerType.PointerEnter, _ => img.sprite = chipHover);
        AddTrigger(chipGo, EventTriggerType.PointerExit,  _ => img.sprite = chipNormal);
        AddTrigger(chipGo, EventTriggerType.BeginDrag, data => BeginDragModule((PointerEventData)data, name));
        AddTrigger(chipGo, EventTriggerType.Drag,      data => DragModule((PointerEventData)data));
        AddTrigger(chipGo, EventTriggerType.EndDrag,   data => EndDragModule((PointerEventData)data));
    }

    GameObject BuildQueueRow(QueuedCommand qc)
    {
        // No background of its own — rows sit directly on the black queue-zone
        // plate, matching the mockup's plain "> command" terminal-line look.
        var rowGo = MakeRect("QueueRow", _queueListParent);
        var le = rowGo.AddComponent<LayoutElement>();
        le.minHeight = 38;
        le.preferredHeight = 38;

        var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(6, 4, 4, 4);
        // Spacing is handled per-segment below (see spacer insertion), not by a
        // flat group-wide gap — a Glued segment (e.g. the "<N>" in "eth<N>")
        // must sit flush against its neighbor exactly like Compose() composes
        // it, while a non-glued segment still wants a visible gap.
        hlg.spacing = 0;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = true;
        hlg.childControlWidth     = true;
        hlg.childControlHeight    = true;
        hlg.childAlignment = TextAnchor.MiddleLeft;

        bool firstSegment = true;
        foreach (var seg in qc.Segments)
        {
            if (!seg.Glued && !firstSegment)
            {
                var spacerGo = MakeRect("Spacer", rowGo.transform);
                var sle = spacerGo.AddComponent<LayoutElement>();
                sle.preferredWidth = 5f;
                sle.minWidth       = 5f;
            }
            firstSegment = false;

            if (!seg.IsParam)
            {
                if (seg.Text.Length == 0) continue;
                var lbl      = MakeTMP("Lit", rowGo.transform);
                lbl.text      = seg.Text;
                lbl.fontSize  = 11f;
                lbl.color     = textColor;
                lbl.alignment = TextAlignmentOptions.MidlineLeft;
                lbl.enableWordWrapping = false;
                lbl.overflowMode = TextOverflowModes.Overflow;
                var lle = lbl.gameObject.AddComponent<LayoutElement>();
                // TMP's own preferred width (computed from the actual font/size once
                // .text is set) rather than a hand-tuned char-count estimate — the
                // estimate undershot real glyph widths for this font and let long
                // words like "interface" visually bleed into/merge with their
                // neighbor segment.
                lle.preferredWidth = lbl.preferredWidth + 4f;
            }
            else
            {
                bool isInterfaceIndex = seg.Text == "N";
                var fieldGo = MakeRect("Param", rowGo.transform);
                var fieldImg = fieldGo.AddComponent<Image>();
                fieldImg.sprite = SmallPlate(paramFieldFill, paramFieldBorder);
                fieldImg.type   = Image.Type.Sliced;
                var field = fieldGo.AddComponent<TMP_InputField>();
                var fle = fieldGo.AddComponent<LayoutElement>();
                fle.preferredWidth  = isInterfaceIndex ? 48f : 76f;
                fle.preferredHeight = 28f;

                var fieldTextGo = MakeRect("Text", fieldGo.transform);
                var fieldTmp    = fieldTextGo.AddComponent<TextMeshProUGUI>();
                fieldTmp.font      = _font;
                fieldTmp.fontSize  = 11f;
                fieldTmp.color     = textColor;
                fieldTmp.alignment = TextAlignmentOptions.MidlineLeft;
                fieldTmp.margin    = new Vector4(4, 0, 4, 0);
                StretchToFill(fieldTextGo.GetComponent<RectTransform>());

                var placeholderGo = MakeRect("Placeholder", fieldGo.transform);
                var placeholderTmp = placeholderGo.AddComponent<TextMeshProUGUI>();
                placeholderTmp.font      = _font;
                placeholderTmp.text      = isInterfaceIndex ? "0" : $"<{seg.Text}>";
                placeholderTmp.fontSize  = 11f;
                placeholderTmp.color     = placeholderColor;
                placeholderTmp.fontStyle = FontStyles.Italic;
                placeholderTmp.alignment = TextAlignmentOptions.MidlineLeft;
                placeholderTmp.margin    = new Vector4(4, 0, 4, 0);
                StretchToFill(placeholderGo.GetComponent<RectTransform>());

                field.targetGraphic = fieldImg;
                field.textViewport  = fieldGo.GetComponent<RectTransform>();
                field.textComponent = fieldTmp;
                field.placeholder   = placeholderTmp;
                if (isInterfaceIndex)
                {
                    // Not ContentType.IntegerNumber: that content type switches TMP_InputField
                    // to a numeric-only TouchScreenKeyboardType, which the InputSystemUIInputModule
                    // (New Input System) fails to route physical digit keystrokes through on desktop —
                    // the field looks focused but every digit key is silently swallowed. Standard
                    // content type + a manual digit-only filter gets the same restriction without
                    // relying on that broken keyboard-type path.
                    field.contentType    = TMP_InputField.ContentType.Standard;
                    field.characterLimit = 2;
                    field.onValidateInput = (text, charIndex, addedChar) => char.IsDigit(addedChar) ? addedChar : '\0';
                }
                field.navigation = new Navigation { mode = Navigation.Mode.None };

                qc.ParamFields[seg.Text] = field;
            }
        }

        var removeGo = MakeRect("Remove", rowGo.transform);
        var removeImg = removeGo.AddComponent<Image>();
        removeImg.color = new Color(0, 0, 0, 0);
        var removeBtn = removeGo.AddComponent<Button>();
        var rle = removeGo.AddComponent<LayoutElement>();
        rle.preferredWidth = 20f;
        var removeLabel      = MakeTMP("X", removeGo.transform);
        removeLabel.text      = "✕";
        removeLabel.fontSize  = 11f;
        removeLabel.color     = new Color(0.8f, 0.4f, 0.35f);
        removeLabel.alignment = TextAlignmentOptions.Center;
        StretchToFill(removeLabel.GetComponent<RectTransform>());
        removeBtn.onClick.AddListener(() => RemoveQueuedCommand(qc));

        return rowGo;
    }

    // ── Drag-and-drop plumbing ──────────────────────────────────────────────────

    void BeginDragModule(PointerEventData eventData, string commandName)
    {
        _dragGhost = MakeRect("DragGhost", _screenCanvas.transform);
        _dragGhost.transform.SetAsLastSibling();
        var ghostImg = _dragGhost.AddComponent<Image>();
        ghostImg.sprite = SmallPlate(chipHoverFill, chipHoverBorder);
        ghostImg.type   = Image.Type.Sliced;
        var cg = _dragGhost.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.alpha = 0.9f;
        var rt = _dragGhost.GetComponent<RectTransform>();
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(180, 30);

        var label      = MakeTMP("Label", _dragGhost.transform);
        label.text      = commandName;
        label.fontSize  = 12f;
        label.color     = titleTextColor;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        StretchToFill(label.GetComponent<RectTransform>());

        MoveGhostTo(eventData.position);
    }

    void DragModule(PointerEventData eventData)
    {
        if (_dragGhost != null) MoveGhostTo(eventData.position);
    }

    void EndDragModule(PointerEventData eventData)
    {
        if (_dragGhost != null)
        {
            Destroy(_dragGhost);
            _dragGhost = null;
        }
    }

    void MoveGhostTo(Vector2 screenPos)
    {
        var canvasRt = (RectTransform)_screenCanvas.transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screenPos, null, out Vector2 local);
        _dragGhost.GetComponent<RectTransform>().anchoredPosition = local;
    }

    void OnTrayDrop(PointerEventData eventData)
    {
        if (eventData.pointerDrag == null) return;
        if (!_paletteChipSource.TryGetValue(eventData.pointerDrag, out var template)) return;
        AddQueuedCommand(template.name);
    }

    static void AddTrigger(GameObject go, EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> callback)
    {
        var trigger = go.GetComponent<EventTrigger>();
        if (trigger == null) trigger = go.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(callback);
        trigger.triggers.Add(entry);
    }

    void UpdateCableHighlight()
    {
        Cable target = null;
        if (_currentDevice != null && _currentDevice.deviceType == DeviceType.Router)
        {
            RouterCLI cli = GetOrCreateSession(_currentDevice);
            if (cli.CurrentMode == RouterCLI.Mode.InterfaceConfig)
                target = _currentDevice.GetPortCable(cli.CurrentInterfacePort);
        }

        if (target == _highlightedCable) return;

        if (_highlightedCable != null) _highlightedCable.SetHighlight(false);
        if (target != null) target.SetHighlight(true);
        _highlightedCable = target;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Exposed for ReachabilityService, which needs to read a router's interface
    /// ip/mask/up-state without going through the CLI UI. Lazily creating a session for
    /// a never-opened router is safe — it just starts with all interfaces unassigned/down,
    /// matching real IOS defaults.</summary>
    public RouterCLI GetOrCreateSession(NetworkDevice device)
    {
        if (!_routerSessions.TryGetValue(device, out RouterCLI cli))
        {
            cli = new RouterCLI(device);
            _routerSessions[device] = cli;
        }
        return cli;
    }

    void SetPrompt(string prompt)
    {
        if (_promptLabel != null)
            _promptLabel.text = prompt;
    }

    // ── Guide/tutorial outline highlights ────────────────────────────────────

    /// <summary>Outlines the Command Builder chip named commandName (matching one of
    /// RouterCLI.CurrentCommands' exact names, e.g. "enable" or "ip address &lt;ip&gt; &lt;mask&gt;")
    /// with a pulsing border, or the command input field itself if no chip with that
    /// name exists in the current mode's palette (pass null explicitly to always target
    /// the input field, e.g. for a generic "type here" nudge). Survives RefreshModulePalette()
    /// rebuilding the palette — re-applied automatically after every rebuild.</summary>
    public void SetHighlightedCommand(string commandName)
    {
        _hasCommandHighlight   = true;
        _highlightedCommandName = commandName;
        ApplyCommandHighlight();
    }

    /// <summary>Outlines the current mode prompt (e.g. "Router(config)#") so a guide step
    /// can call attention to "the prompt itself just changed" separately from the
    /// command-target outline above.</summary>
    public void HighlightPrompt(bool on)
    {
        if (_promptOutline == null) return;
        if (on) _promptOutline.Show();
        else _promptOutline.Hide();
    }

    /// <summary>Clears both outlines. Called on Open()/Close() so a highlight requested
    /// by a guide step never lingers into a session it wasn't meant for.</summary>
    public void ClearHighlights()
    {
        _hasCommandHighlight   = false;
        _highlightedCommandName = null;
        _commandOutline?.Hide();
        _promptOutline?.Hide();
    }

    // Re-targets _commandOutline at the named chip (falling back to the input
    // field) — called both from SetHighlightedCommand and after every
    // RefreshModulePalette() rebuild, since the chip GameObjects (and thus any
    // RectTransform reference to one) don't survive a mode transition.
    void ApplyCommandHighlight()
    {
        if (_commandOutline == null) return;

        if (!_hasCommandHighlight)
        {
            _commandOutline.Hide();
            return;
        }

        RectTransform target = null;
        if (_highlightedCommandName != null &&
            _paletteChipsByName.TryGetValue(_highlightedCommandName, out GameObject chipGo) &&
            chipGo != null)
            target = chipGo.GetComponent<RectTransform>();

        if (target == null && _inputField != null)
            target = _inputField.GetComponent<RectTransform>();

        _commandOutline.Attach(target);
        _commandOutline.Show();
    }

    void AppendOutput(string line)
    {
        _outputText.text += (_outputText.text.Length > 0 ? "\n" : "") + line;
        StartCoroutine(ScrollToBottom());
    }

    IEnumerator ScrollToBottom()
    {
        yield return new WaitForEndOfFrame();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_outputScrollRect.content);
        _outputScrollRect.verticalNormalizedPosition = 0f;
    }

    // ── UI construction ───────────────────────────────────────────────────────

    void BuildPanel()
    {
        // Canvas
        var canvasGo = new GameObject("CLI_Canvas");
        canvasGo.transform.SetParent(transform, false);
        _screenCanvas = canvasGo.AddComponent<Canvas>();
        _screenCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _screenCanvas.sortingOrder = 20;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIScale.Register(scaler);
        canvasGo.AddComponent<GraphicRaycaster>();

        // Panel root — the silver/grey chamfered outer shell everything else
        // sits on (see BuildHeader: the header itself has no background of its
        // own, so this shows through behind the title/close button).
        _panelRoot = MakeRect("CLI_Panel", canvasGo.transform);
        var panelImg = _panelRoot.AddComponent<Image>();
        panelImg.sprite = PlateSpriteBuilder.GetSprite(panelFill, panelBorder, trimColor,
            borderThickness: 2, chamfer: 5, pixelScale: 6);
        panelImg.type = Image.Type.Sliced;
        var panelRt = _panelRoot.GetComponent<RectTransform>();
        panelRt.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRt.pivot            = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta        = panelSize;
        // Offset so the CLI panel + the command builder sidebar (which bulges
        // out past the panel's own right edge) sit centered as one combined
        // block, not just the CLI panel itself (see ComboCenterOffsetX).
        panelRt.anchoredPosition = new Vector2(ComboCenterOffsetX, 0f);

        BuildHeader();
        BuildOutputArea();
        BuildInputArea();
        BuildCommandBuilderPanel();
        BuildOutlineHighlights();
    }

    // Both outlines start parented directly under _panelRoot — a stable home
    // that's never destroyed/rebuilt (unlike the palette chips or the prompt
    // label's own children), so Attach(null) always has somewhere safe to park.
    void BuildOutlineHighlights()
    {
        var commandGo = new GameObject("CommandOutline", typeof(RectTransform));
        commandGo.transform.SetParent(_panelRoot.transform, false);
        _commandOutline = commandGo.AddComponent<PulsingOutline>();

        var promptGo = new GameObject("PromptOutline", typeof(RectTransform));
        promptGo.transform.SetParent(_panelRoot.transform, false);
        _promptOutline = promptGo.AddComponent<PulsingOutline>();
        _promptOutline.Attach(_promptLabel.GetComponent<RectTransform>());
    }

    void BuildHeader()
    {
        // No background of its own — the silver panelRoot plate shows through
        // behind the title/close button, same as the mockup's "title sits
        // directly on the grey margin" look.
        var header = MakeRect("Header", _panelRoot.transform);
        var hrt = header.GetComponent<RectTransform>();
        hrt.anchorMin        = new Vector2(0, 1);
        hrt.anchorMax        = new Vector2(1, 1);
        hrt.pivot            = new Vector2(0.5f, 1f);
        hrt.anchoredPosition = Vector2.zero;
        hrt.sizeDelta        = new Vector2(0, 42);

        // Title
        _titleLabel           = MakeTMP("TitleText", header.transform);
        _titleLabel.text      = "CLI";
        _titleLabel.fontSize  = titleFontSize;
        _titleLabel.color     = titleTextColor;
        _titleLabel.alignment = TextAlignmentOptions.MidlineLeft;
        var trt = _titleLabel.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(14, 0);
        trt.offsetMax = new Vector2(-50, 0);

        // Close button — small square red plate (same family as the panel
        // plates, just a tiny thin-bordered one — see SmallPlate).
        var closeBtnGo = MakeRect("CloseBtn", header.transform);
        var closeImg = closeBtnGo.AddComponent<Image>();
        closeImg.sprite = SmallPlate(closeColor, clearBorder);
        closeImg.type   = Image.Type.Sliced;
        var btn = closeBtnGo.AddComponent<Button>();
        btn.targetGraphic = closeImg;
        btn.onClick.AddListener(Close);
        var crt = closeBtnGo.GetComponent<RectTransform>();
        crt.anchorMin        = new Vector2(1, 0.5f);
        crt.anchorMax        = new Vector2(1, 0.5f);
        crt.pivot            = new Vector2(1f, 0.5f);
        crt.anchoredPosition = new Vector2(-6, 0);
        crt.sizeDelta        = new Vector2(30, 30); // square
        var xLabel      = MakeTMP("X", closeBtnGo.transform);
        xLabel.text      = "✕";
        xLabel.fontSize  = 14;
        xLabel.color     = Color.white;
        xLabel.alignment = TextAlignmentOptions.Center;
        StretchToFill(xLabel.GetComponent<RectTransform>());
    }

    void BuildOutputArea()
    {
        // Background container — black chamfered plate, nested inside the
        // silver panelRoot plate (matching the mockup's "black terminal inset
        // within the grey shell" look).
        var outputBg = MakeRect("OutputBg", _panelRoot.transform);
        var outputImg = outputBg.AddComponent<Image>();
        outputImg.sprite = PlateSpriteBuilder.GetSprite(blackFill, blackBorder, trimColor,
            borderThickness: 2, chamfer: 5, pixelScale: 3);
        outputImg.type = Image.Type.Sliced;
        var ort = outputBg.GetComponent<RectTransform>();
        ort.anchorMin = Vector2.zero;
        ort.anchorMax = Vector2.one;
        ort.offsetMin = new Vector2(0, 50);
        ort.offsetMax = new Vector2(0, -42);

        // ScrollRect fills the background container
        var scrollGo = MakeRect("OutputScroll", outputBg.transform);
        _outputScrollRect                  = scrollGo.AddComponent<ScrollRect>();
        _outputScrollRect.horizontal       = false;
        _outputScrollRect.vertical         = true;
        _outputScrollRect.scrollSensitivity = 30f;
        _outputScrollRect.movementType     = ScrollRect.MovementType.Clamped;
        StretchToFill(scrollGo.GetComponent<RectTransform>());

        // Viewport
        var viewport = MakeRect("Viewport", scrollGo.transform);
        viewport.AddComponent<RectMask2D>();
        StretchToFill(viewport.GetComponent<RectTransform>());
        _outputScrollRect.viewport = viewport.GetComponent<RectTransform>();

        // Content IS the output text — ContentSizeFitter only reads preferred-size data
        // from components on its own GameObject, not from children, so the TMP text and
        // the fitter must live together here for the content height to actually grow
        // (and the ScrollRect to have anything to scroll to) as lines are appended.
        var content    = MakeRect("Content", viewport.transform);
        var contentRt  = content.GetComponent<RectTransform>();
        contentRt.anchorMin        = new Vector2(0, 1);
        contentRt.anchorMax        = new Vector2(1, 1);
        contentRt.pivot            = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta        = Vector2.zero;

        _outputText                    = content.AddComponent<TextMeshProUGUI>();
        _outputText.font               = _font;
        _outputText.fontSize           = outputFontSize;
        _outputText.color              = textColor;
        _outputText.enableWordWrapping = true;
        _outputText.alignment          = TextAlignmentOptions.TopLeft;
        _outputText.margin             = new Vector4(12, 10, 12, 10);

        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        _outputScrollRect.content = contentRt;

        BuildScrollbar(outputBg.transform, _outputScrollRect);
    }

    /// <summary>Builds a vertical scrollbar and wires it to the given ScrollRect. Shared by the output log, module palette, and command queue.</summary>
    static void BuildScrollbar(Transform parent, ScrollRect scrollRect)
    {
        var barGo  = MakeRect("Scrollbar", parent);
        var barRt  = barGo.GetComponent<RectTransform>();
        barRt.anchorMin = new Vector2(1, 0);
        barRt.anchorMax = new Vector2(1, 1);
        barRt.pivot     = new Vector2(1, 1);
        barRt.offsetMin = new Vector2(-8, 0);
        barRt.offsetMax = Vector2.zero;
        barGo.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
        var scrollbar = barGo.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        var slidingArea = MakeRect("SlidingArea", barGo.transform);
        StretchToFill(slidingArea.GetComponent<RectTransform>());

        var handleGo = MakeRect("Handle", slidingArea.transform);
        var handleImg = handleGo.AddComponent<Image>();
        handleImg.color = new Color(1f, 1f, 1f, 0.25f);
        var handleRt = handleGo.GetComponent<RectTransform>();
        handleRt.anchorMin = Vector2.zero;
        handleRt.anchorMax = Vector2.one;
        handleRt.sizeDelta = Vector2.zero;

        scrollbar.handleRect    = handleRt;
        scrollbar.targetGraphic = handleImg;

        scrollRect.verticalScrollbar          = scrollbar;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        scrollRect.verticalScrollbarSpacing    = -3f;
    }

    void BuildInputArea()
    {
        var inputBg = MakeRect("InputBg", _panelRoot.transform);
        inputBg.AddComponent<Image>().color = inputBgColor;
        var ibrt = inputBg.GetComponent<RectTransform>();
        ibrt.anchorMin        = new Vector2(0, 0);
        ibrt.anchorMax        = new Vector2(1, 0);
        ibrt.pivot            = new Vector2(0.5f, 0f);
        ibrt.anchoredPosition = Vector2.zero;
        ibrt.sizeDelta        = new Vector2(0, 48);

        // Prompt label — wide enough to show "Router(config-if)#"
        var promptGo = MakeRect("Prompt", inputBg.transform);
        _promptLabel           = promptGo.AddComponent<TextMeshProUGUI>();
        _promptLabel.font      = _font;
        _promptLabel.text      = ">";
        _promptLabel.fontSize  = inputFontSize;
        _promptLabel.color     = textColor;
        _promptLabel.alignment = TextAlignmentOptions.MidlineLeft;
        var prt = promptGo.GetComponent<RectTransform>();
        prt.anchorMin        = new Vector2(0, 0);
        prt.anchorMax        = new Vector2(0, 1);
        prt.pivot            = new Vector2(0, 0.5f);
        prt.anchoredPosition = new Vector2(10, 0);
        prt.sizeDelta        = new Vector2(190, 0);

        // Input field — starts after prompt
        var ifGo  = MakeRect("InputField", inputBg.transform);
        var ifImg = ifGo.AddComponent<Image>();
        ifImg.color    = new Color(0, 0, 0, 0);
        _inputField    = ifGo.AddComponent<TMP_InputField>();
        var ifrt = ifGo.GetComponent<RectTransform>();
        ifrt.anchorMin = Vector2.zero;
        ifrt.anchorMax = Vector2.one;
        ifrt.offsetMin = new Vector2(206, 6);
        ifrt.offsetMax = new Vector2(-12, -6);

        // Text Area (required viewport for TMP_InputField)
        var textArea = MakeRect("Text Area", ifGo.transform);
        textArea.AddComponent<RectMask2D>();
        StretchToFill(textArea.GetComponent<RectTransform>());

        // Input text
        var inputTextGo = MakeRect("Text", textArea.transform);
        var inputTmp    = inputTextGo.AddComponent<TextMeshProUGUI>();
        inputTmp.font      = _font;
        inputTmp.fontSize  = inputFontSize;
        inputTmp.color     = textColor;
        inputTmp.alignment = TextAlignmentOptions.MidlineLeft;
        StretchToFill(inputTextGo.GetComponent<RectTransform>());

        // Placeholder
        var phGo  = MakeRect("Placeholder", textArea.transform);
        var phTmp = phGo.AddComponent<TextMeshProUGUI>();
        phTmp.font      = _font;
        phTmp.text      = "Type a command...";
        phTmp.fontSize  = inputFontSize;
        phTmp.color     = placeholderColor;
        phTmp.fontStyle = FontStyles.Italic;
        phTmp.alignment = TextAlignmentOptions.MidlineLeft;
        StretchToFill(phGo.GetComponent<RectTransform>());

        // Wire up TMP_InputField
        _inputField.targetGraphic = ifImg;
        _inputField.textViewport  = textArea.GetComponent<RectTransform>();
        _inputField.textComponent = inputTmp;
        _inputField.placeholder   = phTmp;
        _inputField.onSubmit.AddListener(OnInputSubmit);

        // Tab is used for command completion, not UI focus navigation.
        _inputField.navigation = new Navigation { mode = Navigation.Mode.None };
    }

    /// <summary>
    /// Right-side panel: a draggable module palette on top (source) and a
    /// command-queue drop zone on the bottom (destination), with Run/Clear
    /// buttons. Replaces the old static "Commands" help-text list.
    /// </summary>
    void BuildCommandBuilderPanel()
    {
        var builderRoot = MakeRect("CommandBuilder", _panelRoot.transform);
        var builderImg = builderRoot.AddComponent<Image>();
        builderImg.sprite = PlateSpriteBuilder.GetSprite(sidebarFill, sidebarBorder, trimColor,
            borderThickness: 2, chamfer: 5, pixelScale: 5);
        builderImg.type = Image.Type.Sliced;
        var brt = builderRoot.GetComponent<RectTransform>();
        brt.anchorMin        = new Vector2(1, 0);
        brt.anchorMax        = new Vector2(1, 1);
        brt.pivot            = new Vector2(0, 0f);
        brt.anchoredPosition = new Vector2(SidebarGap, 0);
        brt.sizeDelta        = new Vector2(builderPanelWidth, 0);

        // ── Palette title ──
        var paletteTitle      = MakeTMP("PaletteTitle", builderRoot.transform);
        paletteTitle.text      = "Drag Commands";
        paletteTitle.fontSize  = titleFontSize;
        paletteTitle.color     = Color.white;
        paletteTitle.fontStyle = FontStyles.Bold;
        paletteTitle.alignment = TextAlignmentOptions.TopLeft;
        var ptrt = paletteTitle.GetComponent<RectTransform>();
        ptrt.anchorMin = new Vector2(0, 1);
        ptrt.anchorMax = new Vector2(1, 1);
        ptrt.pivot     = new Vector2(0.5f, 1f);
        ptrt.offsetMin = new Vector2(12, -30);
        ptrt.offsetMax = new Vector2(-12, -8);

        // ── Palette scroll list (draggable source chips), top ~52% ──
        var paletteScrollGo = MakeRect("PaletteScroll", builderRoot.transform);
        var paletteScroll    = paletteScrollGo.AddComponent<ScrollRect>();
        paletteScroll.horizontal        = false;
        paletteScroll.vertical          = true;
        paletteScroll.scrollSensitivity = 30f;
        paletteScroll.movementType      = ScrollRect.MovementType.Clamped;
        var psrt = paletteScrollGo.GetComponent<RectTransform>();
        psrt.anchorMin = new Vector2(0, 0.52f);
        psrt.anchorMax = new Vector2(1, 1);
        psrt.offsetMin = new Vector2(0, 2);
        psrt.offsetMax = new Vector2(0, -30);

        var paletteViewport = MakeRect("Viewport", paletteScrollGo.transform);
        paletteViewport.AddComponent<RectMask2D>();
        StretchToFill(paletteViewport.GetComponent<RectTransform>());
        paletteScroll.viewport = paletteViewport.GetComponent<RectTransform>();

        var paletteContent   = MakeRect("Content", paletteViewport.transform);
        var paletteContentRt = paletteContent.GetComponent<RectTransform>();
        paletteContentRt.anchorMin        = new Vector2(0, 1);
        paletteContentRt.anchorMax        = new Vector2(1, 1);
        paletteContentRt.pivot            = new Vector2(0.5f, 1f);
        paletteContentRt.anchoredPosition = Vector2.zero;
        paletteContentRt.sizeDelta        = Vector2.zero;
        var paletteLayout = paletteContent.AddComponent<VerticalLayoutGroup>();
        paletteLayout.padding = new RectOffset(6, 6, 6, 6);
        paletteLayout.spacing = 6;
        paletteLayout.childForceExpandHeight = false;
        paletteLayout.childForceExpandWidth  = true;
        var paletteFitter = paletteContent.AddComponent<ContentSizeFitter>();
        paletteFitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        paletteFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        paletteScroll.content = paletteContentRt;
        _paletteListParent = paletteContent.transform;

        BuildScrollbar(paletteScrollGo.transform, paletteScroll);

        // ── Queue title ──
        var queueTitle      = MakeTMP("QueueTitle", builderRoot.transform);
        queueTitle.text      = "Command Queue";
        queueTitle.fontSize  = titleFontSize;
        queueTitle.color     = Color.white;
        queueTitle.fontStyle = FontStyles.Bold;
        queueTitle.alignment = TextAlignmentOptions.TopLeft;
        var qtrt = queueTitle.GetComponent<RectTransform>();
        qtrt.anchorMin        = new Vector2(0, 0.52f);
        qtrt.anchorMax        = new Vector2(0, 0.52f);
        qtrt.pivot            = new Vector2(0, 0f);
        qtrt.anchoredPosition = new Vector2(12, 4);
        qtrt.sizeDelta        = new Vector2(builderPanelWidth - 24, 18);

        // ── Queue drop zone + scroll list, bottom ~52% minus the Run/Clear row ──
        var queueScrollGo = MakeRect("QueueScroll", builderRoot.transform);
        _queueScrollRect   = queueScrollGo.AddComponent<ScrollRect>();
        _queueScrollRect.horizontal        = false;
        _queueScrollRect.vertical          = true;
        _queueScrollRect.scrollSensitivity = 30f;
        _queueScrollRect.movementType      = ScrollRect.MovementType.Clamped;
        var qsrt = queueScrollGo.GetComponent<RectTransform>();
        qsrt.anchorMin = new Vector2(0, 0);
        qsrt.anchorMax = new Vector2(1, 0.52f);
        qsrt.offsetMin = new Vector2(0, 46);
        qsrt.offsetMax = new Vector2(0, -24);
        var queueZoneImg = queueScrollGo.AddComponent<Image>();
        queueZoneImg.sprite = PlateSpriteBuilder.GetSprite(blackFill, blackBorder, trimColor,
            borderThickness: 2, chamfer: 5, pixelScale: 3);
        queueZoneImg.type = Image.Type.Sliced;
        _trayDropZoneGo = queueScrollGo;

        var queueViewport = MakeRect("Viewport", queueScrollGo.transform);
        queueViewport.AddComponent<RectMask2D>();
        StretchToFill(queueViewport.GetComponent<RectTransform>());
        _queueScrollRect.viewport = queueViewport.GetComponent<RectTransform>();

        var queueContent   = MakeRect("Content", queueViewport.transform);
        var queueContentRt = queueContent.GetComponent<RectTransform>();
        queueContentRt.anchorMin        = new Vector2(0, 1);
        queueContentRt.anchorMax        = new Vector2(1, 1);
        queueContentRt.pivot            = new Vector2(0.5f, 1f);
        queueContentRt.anchoredPosition = Vector2.zero;
        queueContentRt.sizeDelta        = Vector2.zero;
        var queueLayout = queueContent.AddComponent<VerticalLayoutGroup>();
        queueLayout.padding = new RectOffset(4, 4, 4, 4);
        queueLayout.spacing = 4;
        queueLayout.childForceExpandHeight = false;
        queueLayout.childForceExpandWidth  = true;
        var queueFitter = queueContent.AddComponent<ContentSizeFitter>();
        queueFitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        queueFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        _queueScrollRect.content = queueContentRt;
        _queueListParent = queueContent.transform;

        BuildScrollbar(queueScrollGo.transform, _queueScrollRect);

        _queueEmptyLabel           = MakeTMP("EmptyHint", queueContent.transform);
        _queueEmptyLabel.text      = "Drag commands here...";
        _queueEmptyLabel.fontSize  = 11f;
        _queueEmptyLabel.color     = placeholderColor;
        _queueEmptyLabel.fontStyle = FontStyles.Italic;
        _queueEmptyLabel.alignment = TextAlignmentOptions.TopLeft;
        var qele = _queueEmptyLabel.gameObject.AddComponent<LayoutElement>();
        qele.minHeight = 22;

        // The whole scroll area is the drop target so dropping anywhere over the
        // (possibly empty, possibly full) queue list appends a new row.
        AddTrigger(_trayDropZoneGo, EventTriggerType.Drop, data => OnTrayDrop((PointerEventData)data));

        // ── Run / Clear buttons ──
        float btnWidth = (builderPanelWidth - 24f - 6f) / 2f;

        var runGo = MakeRect("RunBtn", builderRoot.transform);
        var runImg = runGo.AddComponent<Image>();
        runImg.sprite = SmallPlate(runFill, runBorder);
        runImg.type   = Image.Type.Sliced;
        var runBtn = runGo.AddComponent<Button>();
        runBtn.targetGraphic = runImg;
        runBtn.onClick.AddListener(RunQueue);
        var rrt = runGo.GetComponent<RectTransform>();
        rrt.anchorMin        = new Vector2(0, 0);
        rrt.anchorMax        = new Vector2(0, 0);
        rrt.pivot            = new Vector2(0, 0);
        rrt.anchoredPosition = new Vector2(12, 8);
        rrt.sizeDelta        = new Vector2(btnWidth, 30);
        var runLabel      = MakeTMP("Label", runGo.transform);
        runLabel.text      = "▶ Run";
        runLabel.fontSize  = 13f;
        runLabel.color     = Color.white;
        runLabel.alignment = TextAlignmentOptions.Center;
        StretchToFill(runLabel.GetComponent<RectTransform>());

        var clearGo = MakeRect("ClearBtn", builderRoot.transform);
        var clearImg = clearGo.AddComponent<Image>();
        clearImg.sprite = SmallPlate(clearFill, clearBorder);
        clearImg.type   = Image.Type.Sliced;
        var clearBtn = clearGo.AddComponent<Button>();
        clearBtn.targetGraphic = clearImg;
        clearBtn.onClick.AddListener(ClearQueue);
        var crt = clearGo.GetComponent<RectTransform>();
        crt.anchorMin        = new Vector2(0, 0);
        crt.anchorMax        = new Vector2(0, 0);
        crt.pivot            = new Vector2(0, 0);
        crt.anchoredPosition = new Vector2(12 + btnWidth + 6, 8);
        crt.sizeDelta        = new Vector2(btnWidth, 30);
        var clearLabel      = MakeTMP("Label", clearGo.transform);
        clearLabel.text      = "Clear";
        clearLabel.fontSize  = 13f;
        clearLabel.color     = Color.white;
        clearLabel.alignment = TextAlignmentOptions.Center;
        StretchToFill(clearLabel.GetComponent<RectTransform>());
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    static GameObject MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    TextMeshProUGUI MakeTMP(string name, Transform parent)
    {
        var go = MakeRect(name, parent);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = _font;
        return tmp;
    }

    // Small chamfered plate for chips/buttons/the close icon — thin border,
    // short diagonal, tuned to stay safe (border well under half the shortest
    // side) at the tiny real sizes these controls render at. Reusing
    // BlankButton.png's own baked-in 90px border directly would massively
    // overlap at this scale (Unity renders a sliced sprite's border at that
    // many *screen* pixels regardless of the target rect size), so these are
    // generated fresh via PlateSpriteBuilder instead, same as every other
    // plate in this file.
    Sprite SmallPlate(Color fill, Color border) =>
        PlateSpriteBuilder.GetSprite(fill, border, trimColor, borderThickness: 1, chamfer: 2, pixelScale: 2);

    static void StretchToFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>One row in the command queue: a parsed template plus the live input fields the user filled in for its parameters (if any).</summary>
    class QueuedCommand
    {
        public List<CLICommandTemplate.Segment> Segments;
        public readonly Dictionary<string, TMP_InputField> ParamFields = new Dictionary<string, TMP_InputField>();
        public GameObject RowGo;

        public string Compose() => CLICommandTemplate.Compose(Segments, ParamFields);
    }
}
