using UnityEngine;
using UnityEngine.EventSystems;
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
    private Camera mainCamera;
    private readonly List<Cable> cables = new List<Cable>();

    // ── Hologram preview ──────────────────────────────────────────────────────
    // One shared LineRenderer, in the same hologram material as the drag ghost
    // (HologramMaterial), covers the whole connect flow: source port -> mouse
    // (while hunting for a target device), source port -> hovered picker row
    // (while choosing which port), and hovered picker row -> mouse (while
    // choosing the very first/source port). Only one of those is ever live at
    // once (mutually exclusive by isConnecting/pickerOpen/sourceDevice state),
    // so a single line/material serves all of them instead of the old
    // separate plain-yellow "drag toward target" line plus this hologram one.
    [Header("Port hover hologram preview")]
    [SerializeField] private Color hologramPreviewTint = new Color(0.45f, 0.85f, 1f, 0.9f);
    [SerializeField] private float hologramGridCellSize = 0.35f;

    private LineRenderer hologramPreviewLr;
    private Material     hologramPreviewMat;
    private int           hoveredPortIndex = -1;

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
        BuildHologramPreviewLine();
        EnsurePickerExists();
        InterfacePickerPopup.Instance.PortHovered   += idx => hoveredPortIndex = idx;
        InterfacePickerPopup.Instance.PortUnhovered += () => hoveredPortIndex = -1;
    }

    static void EnsurePickerExists()
    {
        if (InterfacePickerPopup.Instance == null)
        {
            var go = new GameObject("InterfacePickerPopup");
            go.AddComponent<InterfacePickerPopup>();
        }
    }

    void BuildHologramPreviewLine()
    {
        var go = new GameObject("_CableHologramPreview");
        go.transform.SetParent(transform);
        hologramPreviewLr = go.AddComponent<LineRenderer>();
        hologramPreviewLr.positionCount      = Cable.Segments + 1;
        hologramPreviewLr.startWidth         = cableWidth;
        hologramPreviewLr.endWidth           = cableWidth;
        hologramPreviewMat = HologramMaterial.CreateInstance(Vector3.one, hologramGridCellSize, true, hologramPreviewTint);
        hologramPreviewLr.material           = hologramPreviewMat;
        hologramPreviewLr.startColor         = Color.white;
        hologramPreviewLr.endColor           = Color.white;
        hologramPreviewLr.useWorldSpace      = true;
        hologramPreviewLr.textureMode        = LineTextureMode.Tile;
        hologramPreviewLr.numCornerVertices  = 5;
        hologramPreviewLr.numCapVertices     = 5;
        hologramPreviewLr.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.Off;
        hologramPreviewLr.enabled            = false;
    }

    void Update()
    {
        if (!isConnecting) { hologramPreviewLr.enabled = false; return; }
        if (CLIPanelManager.Instance != null && CLIPanelManager.Instance.IsOpen) { hologramPreviewLr.enabled = false; return; }
        if (InteractionMode.IsSuspended) { hologramPreviewLr.enabled = false; return; }

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
            UpdateHologramPreview();
            if (Mouse.current.rightButton.wasPressedThisFrame)
                Cancel();
            return;
        }

        UpdatePreview();

        // A click on the HUD belongs to that UI, not to a device sitting
        // behind it on screen — same guard as DragDropManager/PingToolManager.
        if (Mouse.current.leftButton.wasPressedThisFrame && !IsPointerOverUI())
            TrySelect();

        if (Mouse.current.rightButton.wasPressedThisFrame)
            Cancel();
    }

    // Traces the exact cable the currently-hovered picker row would produce:
    // while picking the SOURCE port (no target yet), from that port out to
    // wherever the mouse is right now — same shape UpdatePreview draws once a
    // source is actually committed, just a step earlier. While picking the
    // TARGET port (source already fixed), from the fixed source port straight
    // to the hovered port — the real final shape, no mouse involved.
    void UpdateHologramPreview()
    {
        if (hoveredPortIndex < 0 || portsHighlightedDevice == null)
        {
            hologramPreviewLr.enabled = false;
            return;
        }

        Vector3 a, b;
        if (sourceDevice == null)
        {
            a = portsHighlightedDevice.GetPortWorldPoint(hoveredPortIndex);
            if (!TryGetMouseAnchorPoint(out b)) { hologramPreviewLr.enabled = false; return; }
        }
        else
        {
            a = sourceDevice.GetPortWorldPoint(sourcePort);
            b = portsHighlightedDevice.GetPortWorldPoint(hoveredPortIndex);
        }

        DrawHologramLine(a, b, sourceDevice, portsHighlightedDevice);
    }

    // Height the loose end of the preview cable floats at.
    const float PreviewHeight = 0.6f;

    static readonly Plane s_groundPlane = new(Vector3.up, Vector3.zero);

    // Ground-plane raycast used twice below — see TryGetMouseAnchorPoint for why.
    bool RaycastGround(Vector2 screenPos, out Vector3 point)
    {
        Ray ray = mainCamera.ScreenPointToRay(screenPos);
        if (s_groundPlane.Raycast(ray, out float dist)) { point = ray.GetPoint(dist); return true; }
        point = default;
        return false;
    }

    // Where the loose end of a preview cable should float, tracking the mouse
    // cursor at PreviewHeight above the ground.
    //
    // Intersecting a plane raised straight to PreviewHeight (instead of the
    // ground) is numerically unstable: how far that shifts a ray's hit
    // depends on the ray's own grazing angle, which varies sharply across the
    // screen under this isometric camera, so the endpoint would visibly
    // outrun the cursor toward shallower parts of the view (see
    // DragDropManager.TryGetAnchorPoint for the same issue with dragged
    // devices). Instead, hit the ground plane (always well-conditioned) at
    // the cursor, see where lifting that point to PreviewHeight would land on
    // screen, and re-hit the ground from a screen position nudged by that
    // discrepancy.
    bool TryGetMouseAnchorPoint(out Vector3 point)
    {
        Vector2 mouseScreen = Mouse.current.position.ReadValue();
        if (!RaycastGround(mouseScreen, out Vector3 groundPoint)) { point = default; return false; }

        Vector2 liftedScreen = mainCamera.WorldToScreenPoint(groundPoint + Vector3.up * PreviewHeight);
        Vector2 correctedScreen = mouseScreen + (mouseScreen - liftedScreen);
        if (!RaycastGround(correctedScreen, out Vector3 correctedGround)) correctedGround = groundPoint;

        point = correctedGround + Vector3.up * PreviewHeight;
        return true;
    }

    void UpdatePreview()
    {
        if (sourceDevice == null) { hologramPreviewLr.enabled = false; return; }
        if (!TryGetMouseAnchorPoint(out Vector3 b)) { hologramPreviewLr.enabled = false; return; }

        Vector3 a = sourceDevice.GetPortWorldPoint(sourcePort);
        DrawHologramLine(a, b, sourceDevice, null);
    }

    // Shared draw step for every phase of the connect flow's preview line —
    // fills the curve and refreshes the hologram's breathing pulse + grid
    // texture (sized to the current span) exactly like Cable's own per-frame
    // hologram update. excludeA/excludeB are the endpoint device(s) already
    // known at preview time (see Cable.FillCurvePoints) so the preview line
    // doesn't try to route around the very device it's plugged into.
    void DrawHologramLine(Vector3 a, Vector3 b, NetworkDevice excludeA, NetworkDevice excludeB)
    {
        hologramPreviewLr.enabled = true;
        Cable.FillCurvePoints(hologramPreviewLr, a, b, excludeA, excludeB);

        Color tint = hologramPreviewTint;
        tint.a = HologramMaterial.Pulse();
        HologramMaterial.ApplyTint(hologramPreviewMat, tint);
        HologramMaterial.UpdateGridTexture(hologramPreviewMat, new Vector3(Vector3.Distance(a, b), 1f, 1f), hologramGridCellSize);
    }

    static bool IsPointerOverUI() =>
        EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

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
                // Here, not in CreateCable — UndoManager also calls that, and
                // undo stays silent. The draw's sound starts now; Place is the
                // "plugged in" beat once the cable actually reaches the target.
                AudioManager.Instance?.PlaySfx(SfxId.CableConnecting);
                CreateCable(sourceDevice, sourcePort, targetDevice, idx,
                    onConnected: () => AudioManager.Instance?.PlaySfx(SfxId.Place));
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
        // The picker still opens (showing "No available interfaces"), but
        // there's nothing to plug into — that's a refusal.
        if (device.GetFreePorts().Count == 0) AudioManager.Instance?.PlaySfx(SfxId.Refusal);

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

    /// <summary>Creates and wires a cable between two device ports as a player action —
    /// plays the construction reveal and raises OnCableCreated (autosave, undo, tutorial).
    /// Level content wired at load time uses CreatePrebuiltCable instead. playReveal:
    /// false skips the reveal/connect pop for a caller animating the cable itself
    /// (UndoManager restoring a device's cables via LevelBuildInEffect.PlayForItem) —
    /// the reveal would otherwise drive RevealProgress every frame and override it.
    /// onConnected: fires with b's connect pop, once the cable has drawn all the
    /// way to b (straight away when playReveal is false).</summary>
    public Cable CreateCable(NetworkDevice a, int portA, NetworkDevice b, int portB, bool playReveal = true,
                             System.Action onConnected = null)
    {
        var cable = BuildCable(a, portA, b, portB);

        // a is always the origin — whichever device was clicked first. b's
        // connect pop (glow + bounce + sparks, see CableConnectEffect) waits
        // for the cable's own draw animation to finish arriving at b, rather
        // than firing the instant the cable object is created.
        if (playReveal)
            ConstructionRevealEffect.PlayForCable(cable, onComplete: () => { CableConnectEffect.Play(b); onConnected?.Invoke(); });
        else
            onConnected?.Invoke();
        OnCableCreated?.Invoke(cable);
        SubnetVisualizer.RefreshAll();
        return cable;
    }

    /// <summary>Same wiring as CreateCable, for a level's starting topology
    /// (SceneStateCapture.Restore, Level2SceneSetup, Level7SceneSetup). No
    /// construction reveal or connect pop — that animation drives the cable's
    /// RevealProgress every frame and would override LevelBuildInEffect's hide,
    /// drawing the cable in before the room has even assembled — and no
    /// OnCableCreated, since loading a level isn't the player building anything
    /// (nothing to autosave, undo, or advance a tutorial step on).</summary>
    public Cable CreatePrebuiltCable(NetworkDevice a, int portA, NetworkDevice b, int portB)
    {
        var cable = BuildCable(a, portA, b, portB);
        SubnetVisualizer.RefreshAll();
        return cable;
    }

    Cable BuildCable(NetworkDevice a, int portA, NetworkDevice b, int portB)
    {
        var go = new GameObject($"Cable_{a.deviceName}_{b.deviceName}");
        var cable = go.AddComponent<Cable>(); // [RequireComponent] auto-adds LineRenderer
        cable.Initialize(a, portA, b, portB, MakeLineMaterial(cableColor), cableWidth, cableColor);
        cables.Add(cable);

        a.ConnectPort(portA, b, cable);
        b.ConnectPort(portB, a, cable);
        return cable;
    }

    /// <summary>Adds an already-built, already-connected Cable to the tracked list
    /// without re-running CreateCable's construction/connect-effect side effects —
    /// for a cable that already exists as a live GameObject when it appears (e.g.
    /// TemplateTopologyLoader's additively-loaded level-template scene), so
    /// SceneStateCapture and CableRemoverManager see it same as any other cable.</summary>
    public void RegisterExistingCable(Cable cable)
    {
        if (cable == null || cables.Contains(cable)) return;
        cables.Add(cable);
        SubnetVisualizer.RefreshAll();
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void BeginConnecting()
    {
        if (isConnecting) { Cancel(); return; } // toggle off

        InteractionMode.CancelOthers(this);

        isConnecting = true;
        sourceDevice = null;
        hologramPreviewLr.enabled = false;
    }

    /// <summary>Removes a cable, freeing both endpoint ports. Used by CableRemoverManager.</summary>
    public void RemoveCable(Cable cable)
    {
        if (cable == null) return;
        cables.Remove(cable);
        cable.Disconnect();
        OnCableRemoved?.Invoke(cable);
        Destroy(cable.gameObject);
        SubnetVisualizer.RefreshAll();
    }

    public void CancelConnecting() => Cancel();

    void Cancel()
    {
        if (sourceDevice != null) HighlightDevice(sourceDevice, false);
        sourceDevice = null;
        isConnecting = false;
        hologramPreviewLr.enabled = false;
        hoveredPortIndex = -1;
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
            // Same guards as PacketSendManager.HighlightDevice — a renderer
            // with no material, or one with neither color property, is
            // skipped rather than throwing mid-click.
            if (r.sharedMaterial == null) continue;
            bool hasBase = r.sharedMaterial.HasProperty("_BaseColor");
            if (!hasBase && !r.sharedMaterial.HasProperty("_Color")) continue;
            string prop = hasBase ? "_BaseColor" : "_Color";
            Color orig = r.sharedMaterial.GetColor(prop);
            s_propBlock.Clear();
            s_propBlock.SetColor(prop, Color.Lerp(orig, Color.cyan, 0.45f));
            r.SetPropertyBlock(s_propBlock);
        }
    }
}
