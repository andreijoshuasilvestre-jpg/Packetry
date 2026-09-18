using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// Manages cable connections between placed network devices.
///
/// Usage:
///   1. Call BeginConnecting() (wired to the "Cable" UI button).
///   2. Click a source device → it is highlighted.
///   3. Click a different target device → cable is drawn; mode exits.
///   Right-click or BeginConnecting() again cancels the current operation.
/// </summary>
public class CableManager : MonoBehaviour
{
    public static CableManager Instance { get; private set; }

    [SerializeField] private float cableWidth = 0.06f;
    [SerializeField] private Color cableColor  = new Color(0.55f, 0.55f, 0.55f);

    private bool isConnecting;
    private NetworkDevice sourceDevice;
    private int sourcePort;
    private LineRenderer previewLr;
    private Camera mainCamera;
    private readonly List<Cable> cables = new List<Cable>();

    // Whichever device currently has its free ports lit up while the
    // interface picker is open for it (see ShowPicker/ClearPortsHighlight) —
    // matches WorldPulseMarker's default gold, the same "interact here"
    // language already used for tutorial click-zone markers.
    private NetworkDevice portsHighlightedDevice;
    static readonly Color PortHighlightColor = new Color(1f, 0.85f, 0.3f, 1f);

    public bool IsConnecting => isConnecting;
    public IReadOnlyList<Cable> Cables => cables;

    // Exposed so TutorialManager can poll exactly where the player is inside
    // a single connect-a-cable operation (button clicked -> source device
    // clicked -> source port picked -> target device clicked -> target port
    // picked) and show one tip per sub-step instead of a single "do all of
    // this" blob. Both go null the instant Cancel() runs, whether that's a
    // completed cable or an explicit right-click cancel — see ClearPortsHighlight/Cancel.
    public NetworkDevice SourceDevice => sourceDevice;
    public NetworkDevice PickerOpenForDevice => portsHighlightedDevice;

    // Fired once per successfully created cable — mirrors DragDropManager.OnDeviceDropped.
    // Used by TutorialManager to detect "player connected a cable" without polling.
    public event System.Action<Cable> OnCableCreated;

    // Fired once per removed cable — mirrors OnCableCreated. Used by
    // AutoSaveManager so removing a cable re-saves the level's topology too,
    // not just creating one.
    public event System.Action<Cable> OnCableRemoved;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        mainCamera = Camera.main;
        BuildPreviewLine();
        EnsurePickerExists();
    }

    static void EnsurePickerExists()
    {
        if (InterfacePickerPopup.Instance == null)
        {
            var go = new GameObject("InterfacePickerPopup");
            go.AddComponent<InterfacePickerPopup>();
        }
    }

    void BuildPreviewLine()
    {
        var go = new GameObject("_CablePreview");
        go.transform.SetParent(transform);
        previewLr = go.AddComponent<LineRenderer>();
        previewLr.positionCount   = Cable.Segments + 1;
        previewLr.startWidth      = cableWidth;
        previewLr.endWidth        = cableWidth;
        previewLr.material        = MakeLineMaterial(new Color(1f, 0.85f, 0.1f));
        previewLr.startColor      = new Color(1f, 0.85f, 0.1f, 0.7f);
        previewLr.endColor        = new Color(1f, 0.85f, 0.1f, 0.7f);
        previewLr.useWorldSpace   = true;
        previewLr.textureMode     = LineTextureMode.Tile;
        previewLr.numCornerVertices = 5;
        previewLr.numCapVertices    = 5;
        previewLr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        previewLr.enabled = false;
    }

    void Update()
    {
        if (!isConnecting) return;
        if (CLIPanelManager.Instance != null && CLIPanelManager.Instance.IsOpen) return;
        if (SettingsPanelManager.Instance != null && SettingsPanelManager.Instance.IsOpen) return;

        bool pickerOpen = InterfacePickerPopup.Instance != null && InterfacePickerPopup.Instance.IsOpen;
        // Self-healing safety net: InterfacePickerPopup can also dismiss
        // itself on a left-click outside its own rect (see its Update()),
        // which never routes back through this class — without this, that
        // path would leave the ports highlight stuck on since neither
        // ShowPicker's callback nor Cancel() would ever run to clear it.
        if (!pickerOpen && portsHighlightedDevice != null) ClearPortsHighlight();

        // While the picker is open, let clicks go to its UI buttons instead
        // of the 3D scene.
        if (pickerOpen)
        {
            if (Mouse.current.rightButton.wasPressedThisFrame)
                Cancel();
            return;
        }

        UpdatePreview();

        if (Mouse.current.leftButton.wasPressedThisFrame)
            TrySelect();

        if (Mouse.current.rightButton.wasPressedThisFrame)
            Cancel();
    }

    // Height the loose end of the preview cable floats at.
    const float PreviewHeight = 0.6f;

    static readonly Plane s_groundPlane = new(Vector3.up, Vector3.zero);

    // Ground-plane raycast used twice below — see UpdatePreview for why.
    bool RaycastGround(Vector2 screenPos, out Vector3 point)
    {
        Ray ray = mainCamera.ScreenPointToRay(screenPos);
        if (s_groundPlane.Raycast(ray, out float dist)) { point = ray.GetPoint(dist); return true; }
        point = default;
        return false;
    }

    void UpdatePreview()
    {
        if (sourceDevice == null) return;

        // Intersecting a plane raised straight to PreviewHeight (instead of
        // the ground) is numerically unstable: how far that shifts a ray's
        // hit depends on the ray's own grazing angle, which varies sharply
        // across the screen under this isometric camera, so the endpoint
        // would visibly outrun the cursor toward shallower parts of the view
        // (see DragDropManager.TryGetAnchorPoint for the same issue with
        // dragged devices). Instead, hit the ground plane (always
        // well-conditioned) at the cursor, see where lifting that point to
        // PreviewHeight would land on screen, and re-hit the ground from a
        // screen position nudged by that discrepancy.
        Vector2 mouseScreen = Mouse.current.position.ReadValue();
        if (!RaycastGround(mouseScreen, out Vector3 groundPoint)) return;

        Vector2 liftedScreen = mainCamera.WorldToScreenPoint(groundPoint + Vector3.up * PreviewHeight);
        Vector2 correctedScreen = mouseScreen + (mouseScreen - liftedScreen);
        if (!RaycastGround(correctedScreen, out Vector3 correctedGround)) correctedGround = groundPoint;

        previewLr.enabled = true;
        Vector3 a = sourceDevice.GetPortWorldPoint(sourcePort);
        Vector3 b = correctedGround + Vector3.up * PreviewHeight;
        Cable.FillCurvePoints(previewLr, a, b);
    }

    void TrySelect()
    {
        Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f)) return;

        var device = hit.collider.GetComponentInParent<NetworkDevice>();
        if (device == null || device.isPaletteItem) return;

        Vector2 screenPos = Mouse.current.position.ReadValue();

        if (sourceDevice == null)
        {
            ShowPicker(device, screenPos, idx =>
            {
                sourceDevice = device;
                sourcePort   = idx;
                HighlightDevice(device, true);
                CableConnectEffect.Play(device); // source device pops immediately on port pick; target waits for the cable draw (see CreateCable)
            });
        }
        else if (device != sourceDevice)
        {
            NetworkDevice targetDevice = device;
            ShowPicker(device, screenPos, idx =>
            {
                CreateCable(sourceDevice, sourcePort, targetDevice, idx);
                Cancel(); // Cancel handles the source highlight reset
            });
        }
    }

    // Wraps InterfacePickerPopup.Show so every call site also lights up the
    // device's free ports for the duration — see NetworkDevice.HighlightFreePorts
    // and portsHighlightedDevice above for why a player picking "eth0" from a
    // plain text list otherwise has no idea where that physically is.
    void ShowPicker(NetworkDevice device, Vector2 screenPos, System.Action<int> onPicked)
    {
        ClearPortsHighlight();
        portsHighlightedDevice = device;
        device.HighlightFreePorts(true, PortHighlightColor);

        InterfacePickerPopup.Instance.Show(device, screenPos, idx =>
        {
            ClearPortsHighlight();
            onPicked(idx);
        });
    }

    void ClearPortsHighlight()
    {
        if (portsHighlightedDevice != null) portsHighlightedDevice.HighlightFreePorts(false, default);
        portsHighlightedDevice = null;
    }

    /// <summary>Creates and wires a cable between two device ports. Public so level-setup
    /// code (Level2SceneSetup, Level7SceneSetup, SceneStateCapture.Restore) can pre-wire
    /// devices the same way the click-driven TrySelect() flow does, instead of duplicating
    /// this body.</summary>
    public Cable CreateCable(NetworkDevice a, int portA, NetworkDevice b, int portB)
    {
        var go = new GameObject($"Cable_{a.deviceName}_{b.deviceName}");
        var cable = go.AddComponent<Cable>(); // [RequireComponent] auto-adds LineRenderer
        cable.Initialize(a, portA, b, portB, MakeLineMaterial(cableColor), cableWidth, cableColor);
        cables.Add(cable);

        a.ConnectPort(portA, b, cable);
        b.ConnectPort(portB, a, cable);

        // a is always the origin — whichever device was clicked first. b's
        // connect pop (glow + bounce + sparks, see CableConnectEffect) waits
        // for the cable's own draw animation to finish arriving at b, rather
        // than firing the instant the cable object is created.
        ConstructionRevealEffect.PlayForCable(cable, onComplete: () => CableConnectEffect.Play(b));
        OnCableCreated?.Invoke(cable);
        return cable;
    }

    /// <summary>Adds an already-built, already-connected Cable to the tracked list
    /// without re-running CreateCable's construction/connect-effect side effects —
    /// for a cable that already exists as a live GameObject when it appears (e.g.
    /// TemplateTopologyLoader's additively-loaded level-template scene), so
    /// SceneStateCapture and CableRemoverManager see it same as any other cable.</summary>
    public void RegisterExistingCable(Cable cable)
    {
        if (cable != null && !cables.Contains(cable)) cables.Add(cable);
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void BeginConnecting()
    {
        if (isConnecting) { Cancel(); return; } // toggle off

        InteractionMode.CancelOthers(this);

        isConnecting = true;
        sourceDevice = null;
        previewLr.enabled = false;
    }

    /// <summary>Removes a cable, freeing both endpoint ports. Used by CableRemoverManager.</summary>
    public void RemoveCable(Cable cable)
    {
        if (cable == null) return;
        cables.Remove(cable);
        cable.Disconnect();
        OnCableRemoved?.Invoke(cable);
        Destroy(cable.gameObject);
    }

    public void CancelConnecting() => Cancel();

    void Cancel()
    {
        if (sourceDevice != null) HighlightDevice(sourceDevice, false);
        sourceDevice = null;
        isConnecting = false;
        previewLr.enabled = false;
        if (InterfacePickerPopup.Instance != null) InterfacePickerPopup.Instance.Hide();
        // isConnecting is about to go false, so Update()'s own safety net
        // (which only runs while isConnecting) will never fire to catch this —
        // must clear it here explicitly.
        ClearPortsHighlight();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    static Material MakeLineMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Sprites/Default");
        var mat = new Material(shader) { color = color, mainTexture = Cable.GetCableTexture() };
        // Particles/Unlit multiplies the base map by vertex color when in
        // "Multiply" color mode (0) — that's what makes the per-cable tint
        // (LineRenderer start/end color) combine with the shared texture.
        if (mat.HasProperty("_ColorMode")) mat.SetFloat("_ColorMode", 0f);
        return mat;
    }

    static MaterialPropertyBlock s_propBlock;

    static void HighlightDevice(NetworkDevice device, bool on)
    {
        if (!on)
        {
            // See DragDropManager.ApplyHighlightTint — a subnet-tinted device must
            // restore its tint rather than clear to null.
            var addr = device.GetComponent<DeviceAddress>();
            if (addr != null && addr.HasAddress) { addr.ApplyTint(); return; }

            foreach (var r in device.GetComponentsInChildren<Renderer>())
                r.SetPropertyBlock(null);
            return;
        }

        s_propBlock ??= new MaterialPropertyBlock();
        foreach (var r in device.GetComponentsInChildren<Renderer>())
        {
            string prop = r.sharedMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
            Color orig = r.sharedMaterial.GetColor(prop);
            s_propBlock.Clear();
            s_propBlock.SetColor(prop, Color.Lerp(orig, Color.cyan, 0.45f));
            r.SetPropertyBlock(s_propBlock);
        }
    }
}
