using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

/// <summary>
/// Handles drag-and-drop of network devices from the palette onto the game plane,
/// plus click-to-select/rotate and hold-to-drag for already-placed devices.
///
/// Flow:
///   1. User clicks a palette item → StartDragging() instantiates a clone.
///   2. Clone follows the cursor, resting flush on whatever surface (table or
///      ground) is under it.
///   3. User clicks anywhere on the ground → clone is released;
///      Rigidbody gravity drops it onto the plane.
///
/// Placed devices:
///   - A quick left-click selects/deselects the device (cyan highlight); while
///     selected, holding R and scrolling freely rotates it in place without
///     picking it up.
///   - Holding the left click down for <dragHoldInterval> seconds instead picks
///     the device up and enters the same drag flow as a fresh palette item.
/// </summary>
public class DragDropManager : MonoBehaviour
{
    public static DragDropManager Instance { get; private set; }

    // Fired once per completed user drop (palette placement or repositioning
    // an existing device) — the actual input-driven moment a device is set down.
    // Second parameter is true for a fresh palette placement, false for
    // repositioning an already-placed device — subscribers that count "devices
    // placed" (e.g. TutorialManager) need this to avoid double-counting a drag
    // that just moved something that was already on the field.
    public event System.Action<NetworkDevice, bool> OnDeviceDropped;

    [SerializeField] private float gridCellSize = 1f;
    // Must match GameBootstrap planeScale * 5 (1.5 * 5 = 7.5)
    [SerializeField] private float playAreaHalfExtent = 7.5f;

    [Header("Drag hologram")]
    // Neutral white tint — the grid texture itself now bakes in the blueprint's
    // white line / blue fill colors (see HologramMaterial), so the default tint
    // just controls overall opacity without recoloring them.
    [SerializeField] private Color hologramColor = new Color(1f, 1f, 1f, 0.85f);
    [SerializeField] private Color hologramValidColor = new Color(0.35f, 1f, 0.45f, 0.85f);
    [SerializeField] private Color hologramInvalidColor = new Color(1f, 0.3f, 0.3f, 0.85f);
    // Repositioning an already-placed device uses a solid (opaque) hologram
    // variant tinted blue instead of the see-through palette-drag ghost — the
    // device is already real, so it shouldn't look like it's dissolving.
    [SerializeField] private Color placedDragColor = new Color(0.25f, 0.55f, 1f, 1f);
    // World-space size of one grid square on the hologram's surface pattern.
    [SerializeField] private float hologramGridCellSize = 0.12f;

    [Header("Placed-device selection / drag")]
    // How long (seconds) the left button must be held on a placed device before
    // it's picked up for dragging; releasing sooner is treated as a select click.
    [SerializeField] private float dragHoldInterval = 0.35f;
    // Delay after an already-placed device is picked up before the "click to
    // place it down" status-bar hint appears (see ShowRepositionHint). Keeps
    // the pickup reading as two distinct beats — the hologram/port-arrow
    // indicator pops immediately, the text hint follows a moment later —
    // instead of both landing on the same frame.
    [SerializeField] private float repositionHintDelay = 0.35f;
    [SerializeField] private Color selectionHighlightColor = new Color(0.2f, 0.9f, 1f);
    // Camera dolly-in distance while a device is highlighted — a "static" zoom,
    // not a fly-around — restored the moment the device is deselected.
    [SerializeField] private float highlightZoomDistance = 10f;
    // Pitch matches the main camera's own max-zoom-in angle (groundPitch), nudged
    // a bit steeper so the shot looks slightly down on the device instead of dead-level.
    [SerializeField] private float highlightPitchOffset = 12f;
    // Degrees per scroll notch while free-rotating (hold R + scroll) — a
    // continuous spin rather than the old fixed 90°-per-notch turntable step.
    // Not designer-tunable, so declared as a plain const instead of a
    // serialized field. Quadrupled from the original 90f per user request.
    private const float freeRotateSpeed = 360f;

    private Camera mainCamera;
    private Collider groundCollider;
    private GameObject draggingObject;
    private PaletteItem hoveredItem;
    private bool isDragging;
    private int dragStartFrame = -1; // prevents same-frame drop
    private bool isNewDevice; // true = fresh palette instance (cancel destroys it); false = repositioning (cancel snaps back)
    private Vector3 dragStartPosition;
    private Quaternion dragStartRotation;
    // World-space height (above the object's own base) of its visual center —
    // recomputed per drag so the raycast anchor plane can be raised to that
    // height instead of the ground. Anchoring purely at ground level (y=0)
    // puts the mouse cursor exactly on the object's base, but under the
    // isometric camera's angle a tall model's visible bulk (e.g. a router's
    // antennas) then renders well away from the cursor — raising the plane
    // to the model's center keeps the part players actually look at under
    // the pointer.
    private float dragAnchorHeight;
    // X/Z offset from the mouse's anchor point (see dragAnchorHeight) to the
    // device's position, captured the instant a drag starts. Repositioning an
    // already-placed device would otherwise snap it to be centered exactly on
    // the cursor the moment it's picked up (a visible jump, since the cursor
    // is rarely dead-center on the device you clicked) — carrying this offset
    // through MoveDraggingObject() instead preserves wherever on the device
    // you actually grabbed it, so only mouse movement (not the initial
    // pickup) moves the device. Freshly spawned palette devices have no prior
    // position to preserve, so they keep offset zero (appear under the cursor).
    private Vector3 dragGrabOffset;
    private bool wasShiftHeld;
    private Vector3 shiftAxisAnchor; // position axis-locking is measured from; re-captured each time Shift is (re)pressed

    // Sticky palette placement: once a palette item is picked, placement mode stays
    // active across drops so the player can place several of the same device in a
    // row without returning to the palette each time. Right-click (CancelDrag) is
    // what ends the mode, same gesture as every other toggle-style manager.
    private bool devicePlacementActive;
    private DeviceType placementDeviceType;

    // Renderers currently wearing the hologram material while draggingObject is
    // live, and what to restore them to once it's placed/cancelled.
    private Renderer[] hologramRenderers;
    private Material[] hologramOriginalMaterials;
    private Material[] hologramInstanceMaterials;
    // Colliders disabled for the duration of the hologram phase — a ghost isn't
    // solid, so it shouldn't physically collide with the ground/other devices
    // or get hit by the placement raycast while it's following the cursor.
    private Collider[] hologramColliders;
    // Single direction arrow showing which way the whole device's port cluster
    // faces — spawned alongside the hologram and destroyed with it (see
    // ApplyHologram/RestoreFromHologram), not baked into the permanent model.
    private GameObject portSideArrow;

    // Selection indicator (arrow + true mesh-silhouette outline + ground ring)
    // shown on whatever's currently highlightedDevice — replaces the old flat
    // material-tint highlight (see ApplyHighlightTint). Built once in Start(),
    // not per-selection, same one-owner-per-purpose convention WorldPulseMarker's
    // own callers use (InteractionMode's mutual-exclusion chain already
    // guarantees this never needs to coexist with TableManager's or
    // CableRemoverManager's own instances).
    private SelectionIndicator selectionIndicator;

    private NetworkDevice highlightedDevice;
    private NetworkDevice pendingPickDevice;
    private float pressStartTime;
    // Time.time when an already-placed device was picked up — see
    // ShowRepositionHint / repositionHintDelay.
    private float repositionPickupTime;

    // Lets a straight press-drag-release gesture (e.g. clicking and dragging an
    // in-world palette item, or holding a placed device past dragHoldInterval)
    // complete the same way the click-then-click-again flow does, instead of
    // leaving the ghost stuck in its hologram material forever because Drop()
    // never got a second click. Only armed when the drag actually started from
    // a still-held button (see ArmDragReleaseIfHeld) — a drag started from an
    // already-released click, like a sidebar button whose onClick fires on
    // pointer-up, is deliberately left unarmed so that flow keeps working as
    // click-to-arm/click-to-place.
    private bool dragReleaseArmed;
    private Vector2 dragPressStartScreen;
    // Screen-space mouse movement past which a held-and-released drag counts as
    // a deliberate "drop it here" rather than an incidental tap/jitter.
    private const float dragReleaseThreshold = 24f;

    // Camera state saved the moment the first device gets highlighted, so it can
    // be restored exactly once all highlighting ends (switching between two
    // highlighted devices re-targets without re-saving over the zoomed-in state).
    private bool _cameraFocused;
    private Vector3 _savedLookTarget;
    private float _savedDistance;
    private float _savedPitch;
    private float _savedYaw;

    public bool IsDragging => isDragging;
    public bool HasHighlightedDevice => highlightedDevice != null;
    // The currently selected (highlighted) placed device, if any — read-only,
    // used by TutorialManager to detect the player rotating a device in place
    // (hold R + scroll) without duplicating DragDropManager's own selection logic.
    public NetworkDevice HighlightedDevice => highlightedDevice;
    public float GridCellSize => gridCellSize;
    public float PlayAreaHalfExtent => playAreaHalfExtent;

    // Type of whatever's currently under the cursor — a fresh palette ghost or an
    // already-placed device being repositioned. Read from the live dragged object
    // rather than placementDeviceType, since PickUpPlacedDevice (repositioning)
    // never touches that field and would otherwise report a stale/wrong type.
    // Used by NetworkUIManager to keep the matching sidebar button lit while
    // device-placement mode is active.
    public DeviceType? DraggingDeviceType =>
        isDragging && draggingObject != null
            ? draggingObject.GetComponent<NetworkDevice>()?.deviceType
            : null;

    // True while an already-placed device is being repositioned (as opposed
    // to a fresh palette placement) — used by NetworkUIManager to pick the
    // right status-bar wording.
    public bool IsRepositioning => isDragging && !isNewDevice;

    // True once a repositioned device has been held for repositionHintDelay
    // seconds — i.e. it's time to show the "click to place it down" hint.
    // False for the brief instant right after pickup, so the hologram/
    // port-arrow indicator reads as popping up on its own first.
    public bool ShowRepositionHint =>
        IsRepositioning && Time.time - repositionPickupTime >= repositionHintDelay;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        mainCamera = Camera.main;
        groundCollider = GameObject.Find("GamePlane")?.GetComponent<Collider>();

        selectionIndicator = new GameObject("_DeviceSelectionIndicator").AddComponent<SelectionIndicator>();
        selectionIndicator.SetColor(selectionHighlightColor);
    }

    void Update()
    {
        if (CLIPanelManager.Instance != null && CLIPanelManager.Instance.IsOpen) return;
        if (SettingsPanelManager.Instance != null && SettingsPanelManager.Instance.IsOpen) return;

        if (isDragging)
        {
            MoveDraggingObject();
            HandleFreeRotation(draggingObject);

            // wasPressedThisFrame = "click to place" — also skip the frame drag started
            // (sidebar onClick fires on release; PickUpPlacedDevice fires on press;
            //  in both cases the same-frame guard prevents an accidental immediate drop)
            // IsPointerOverUI guard: without it, re-clicking the sidebar button that started
            // this drag drops the device under the button and the button's onClick immediately
            // starts a new drag, duplicating devices on top of each other on every click.
            if (Time.frameCount != dragStartFrame && Mouse.current.leftButton.wasPressedThisFrame && !IsPointerOverUI())
            {
                // Clicking on an already-placed device instead ends the current
                // placement/reposition and hands the click straight to the normal
                // select / hold-to-drag gesture (see HandlePlacedDevicePress),
                // rather than dropping a duplicate copy on top of it. This is what
                // lets the player go straight from "still holding a ghost" to
                // "selecting a placed device" without an explicit cancel step.
                NetworkDevice clickedPlaced = RaycastForDevice();
                if (clickedPlaced != null)
                {
                    CancelDrag();
                    pendingPickDevice = clickedPlaced;
                    pressStartTime = Time.time;
                }
                else
                {
                    Drop();
                }
            }

            // Completes a press-drag-release gesture the same way a second click
            // would — see dragReleaseArmed. Ignored once the ghost has already
            // been handed off to the "click again to drop" branch above (that
            // branch either drops or re-arms pendingPickDevice, and either way
            // isDragging may already be false by the time this runs this frame).
            if (isDragging && dragReleaseArmed && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                dragReleaseArmed = false;
                float moved = Vector2.Distance(dragPressStartScreen, Mouse.current.position.ReadValue());
                if (moved >= dragReleaseThreshold && !IsPointerOverUI()) Drop();
            }

            if (Mouse.current.rightButton.wasPressedThisFrame)
                CancelDrag();
        }
        else if ((CableManager.Instance == null || !CableManager.Instance.IsConnecting) &&
                 (CableRemoverManager.Instance == null || !CableRemoverManager.Instance.IsRemoving) &&
                 (DeviceRemoverManager.Instance == null || !DeviceRemoverManager.Instance.IsRemoving) &&
                 (TableRemoverManager.Instance == null || !TableRemoverManager.Instance.IsRemoving) &&
                 (TableManager.Instance == null || !TableManager.Instance.IsPlacing) &&
                 (TableManager.Instance == null || !TableManager.Instance.IsDragging) &&
                 (PingToolManager.Instance == null || !PingToolManager.Instance.IsPinging) &&
                 (PacketSendManager.Instance == null || !PacketSendManager.Instance.IsSending) &&
                 (TutorialManager.Instance == null || !TutorialManager.Instance.IsCameraOnlyStep))
        {
            HandlePaletteHover();
            HandlePlacedDevicePress();

            if (highlightedDevice != null)
                HandleFreeRotation(highlightedDevice.gameObject);

            if (Mouse.current.rightButton.wasPressedThisFrame && !IsPointerOverUI())
                TryOpenDeviceConfig();
        }
    }

    static bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    // ── Palette hover highlight ──────────────────────────────────────────────

    void HandlePaletteHover()
    {
        PaletteItem hit = RaycastForPaletteItem();

        if (hit != hoveredItem)
        {
            hoveredItem?.SetHover(false);
            hoveredItem = hit;
            hoveredItem?.SetHover(true);
        }
    }

    bool TryPickFromPalette()
    {
        PaletteItem item = RaycastForPaletteItem();
        if (item == null) return false;
        StartDragging(item.deviceType);
        return true;
    }

    // ── Placed-device select (click) vs pick-up (hold) ──────────────────────

    void HandlePlacedDevicePress()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame && !IsPointerOverUI())
        {
            if (TryPickFromPalette()) return;

            NetworkDevice device = RaycastForDevice();
            if (device != null)
            {
                pendingPickDevice = device;
                pressStartTime = Time.time;
            }
            else
            {
                SetHighlighted(null);
            }
        }

        if (pendingPickDevice == null) return;

        if (!Mouse.current.leftButton.isPressed)
        {
            // Released before the hold threshold — a click, not a hold: select/deselect.
            NetworkDevice clicked = pendingPickDevice;
            pendingPickDevice = null;
            SetHighlighted(highlightedDevice == clicked ? null : clicked);
        }
        else if (Time.time - pressStartTime >= dragHoldInterval)
        {
            NetworkDevice device = pendingPickDevice;
            pendingPickDevice = null;
            PickUpPlacedDevice(device.gameObject);
        }
    }

    // Right-click opens whichever config surface fits the device: the CLI for
    // routers, the IP address popup for PCs. Switches have no config in this scope.
    void TryOpenDeviceConfig()
    {
        // Neither popup is ever taught by the tutorial — without this guard,
        // right-clicking a placed PC mid-tutorial (e.g. during the "rotate the
        // device" step, where nothing else claims right-click) pops the address
        // config screen open unannounced. The tutorial's own right-click-to-cancel
        // moments (canceling a drag/cable/table placement) are untouched — those
        // are handled by each mode's own Cancel(), not this method.
        if (TutorialManager.Instance != null && TutorialManager.Instance.IsActive) return;

        NetworkDevice device = RaycastForDevice();
        if (device == null) return;

        if (device.deviceType == DeviceType.Router)
        {
            CLIPanelManager.Instance?.Open(device);
        }
        else if (device.deviceType == DeviceType.PC)
        {
            Vector2 screenPos = Mouse.current.position.ReadValue();
            DeviceAddressPopup.Instance?.Show(device, screenPos);
        }
    }

    // Arms dragReleaseArmed only if the button that led to this drag is still
    // physically held — true for a direct press-and-drag (in-world palette
    // item, or holding a placed device past dragHoldInterval), false for a
    // drag started from a click that already finished (e.g. a sidebar
    // button's onClick, which fires on pointer-up).
    void ArmDragReleaseIfHeld()
    {
        dragReleaseArmed = Mouse.current.leftButton.isPressed;
        if (dragReleaseArmed) dragPressStartScreen = Mouse.current.position.ReadValue();
    }

    void PickUpPlacedDevice(GameObject go)
    {
        if (isDragging) return;

        InteractionMode.CancelOthers(this);
        SetHighlighted(null);

        draggingObject = go;
        isNewDevice = false;
        dragStartPosition = go.transform.position;
        dragStartRotation = go.transform.rotation;

        var rb = go.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        isDragging = true;
        dragStartFrame = Time.frameCount;
        repositionPickupTime = Time.time;
        wasShiftHeld = false;
        dragAnchorHeight = ComputeAnchorHeight(draggingObject);
        dragGrabOffset = TryGetAnchorPoint(out Vector3 startAnchor)
            ? new Vector3(dragStartPosition.x - startAnchor.x, 0f, dragStartPosition.z - startAnchor.z)
            : Vector3.zero;
        ApplyHologram(draggingObject);
        // Only for repositioning an already-placed device, not a fresh palette
        // ghost (see StartDragging) — a ghost isn't "the device" yet, and
        // already gets its own ApplyHologram grid + port-side arrow feedback.
        // Built after ApplyHologram so its outline shells aren't among the
        // renderers that call swaps onto the hologram ghost material.
        selectionIndicator?.ShowDragging(draggingObject.transform);
        ArmDragReleaseIfHeld();
        MoveDraggingObject();
    }

    // Half the dragged object's own visible height above its base — see
    // dragAnchorHeight. Y-axis-only rotation (the only kind devices ever get)
    // doesn't change this, so it's safe to compute once per pickup.
    static float ComputeAnchorHeight(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return 0f;

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);

        return Mathf.Max(0f, b.center.y - go.transform.position.y);
    }

    PaletteItem RaycastForPaletteItem()
    {
        Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit, 200f))
            return hit.collider.GetComponentInParent<PaletteItem>();
        return null;
    }

    NetworkDevice RaycastForDevice()
    {
        Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f)) return null;

        var device = hit.collider.GetComponentInParent<NetworkDevice>();
        if (device == null || device.isPaletteItem) return null;
        return device;
    }

    // ── Selection highlight ──────────────────────────────────────────────────

    void SetHighlighted(NetworkDevice device)
    {
        if (device == highlightedDevice) return;

        ApplyHighlightTint(highlightedDevice, false);
        highlightedDevice = device;
        ApplyHighlightTint(highlightedDevice, true);
        UpdateCameraFocus();
    }

    void UpdateCameraFocus()
    {
        if (IsometricCamera.Instance == null) return;

        if (highlightedDevice != null)
        {
            if (!_cameraFocused)
            {
                _savedLookTarget = IsometricCamera.Instance.TargetLookTarget;
                _savedDistance   = IsometricCamera.Instance.TargetDistance;
                _savedPitch      = IsometricCamera.Instance.TargetPitch;
                _savedYaw        = IsometricCamera.Instance.TargetYaw;
                _cameraFocused   = true;
            }
            // Pitch: the camera's own max-zoom-in angle, plus a bit extra elevation —
            // yaw stays whatever the player already had, this is a dolly-in, not a fly-around.
            float focusPitch = IsometricCamera.Instance.GroundPitch + highlightPitchOffset;
            IsometricCamera.Instance.FocusOn(highlightedDevice.transform.position, highlightZoomDistance, focusPitch, _savedYaw);
        }
        else if (_cameraFocused)
        {
            IsometricCamera.Instance.FocusOn(_savedLookTarget, _savedDistance, _savedPitch, _savedYaw);
            _cameraFocused = false;
        }
    }

    void ApplyHighlightTint(NetworkDevice device, bool on)
    {
        if (device == null || selectionIndicator == null) return;

        if (!on)
        {
            selectionIndicator.Hide();
            return;
        }

        // No material tinting anymore, so a subnet-tinted device (see
        // DeviceAddress) is never touched by selection at all now — the old
        // "restore the tint rather than clear to null" special case is gone
        // because there's nothing left here to clear.
        var (groundCenter, footprintXZ) = ComputeGroundFootprint(device);
        selectionIndicator.ShowOnMesh(device.transform, device.VisualTopPoint(), groundCenter, footprintXZ);
    }

    // World-space footprint of a device's own combined renderer bounds —
    // center at its base (bounds.min.y, so it sits correctly whether the
    // device rests on the ground or on a table) plus its XZ size, for
    // SelectionIndicator's ground ring.
    static (Vector3 center, Vector2 footprintXZ) ComputeGroundFootprint(NetworkDevice device)
    {
        var renderers = device.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return (device.transform.position, Vector2.one * 0.5f);

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);

        return (new Vector3(b.center.x, b.min.y, b.center.z), new Vector2(b.size.x, b.size.z));
    }

    // Called by InteractionMode when another interaction mode takes over, so a
    // stale selection highlight doesn't linger once the device is no longer
    // actually selectable/rotatable.
    public void ClearHighlight() => SetHighlighted(null);

    // Called by InteractionMode when a different mode/tool takes over, so a
    // pending sticky-placement ghost (or an in-progress reposition) doesn't
    // linger once something else claims input — same idea as ClearHighlight,
    // just for the active drag instead of the selection.
    public void CancelDragging() => CancelDrag();

    // ── Drag lifecycle ───────────────────────────────────────────────────────

    public void StartDragging(DeviceType type)
    {
        if (TutorialManager.Instance != null && !TutorialManager.Instance.IsDeviceAllowed(type)) return;
        if (Level4Guide.Instance != null && !Level4Guide.Instance.IsDeviceAllowed(type)) return;

        if (isDragging)
        {
            // Already placing/repositioning something. Clicking the same sticky
            // device type again is a no-op (it's already the live ghost); clicking
            // a different device switches straight to it instead of being ignored
            // — CancelDrag() drops the stale ghost (or snaps a reposition back)
            // before the fresh drag below starts.
            if (isNewDevice && placementDeviceType == type) return;
            CancelDrag();
        }

        // Sticky placement mode: whoever asked for this device (sidebar button or
        // world palette click) stays "live" across drops — see Drop() — so every
        // entry point that spawns a fresh ghost needs to arm it here, not just the
        // one that happened to be wired up first.
        placementDeviceType = type;
        devicePlacementActive = true;

        InteractionMode.CancelOthers(this);
        SetHighlighted(null);

        GameObject template = DeviceModelBuilder.BuildTemplate(type);
        draggingObject = Object.Instantiate(template);
        Object.Destroy(template);

        draggingObject.SetActive(true);
        draggingObject.name = type.ToString();

        var nd = draggingObject.GetComponent<NetworkDevice>();
        if (nd != null) nd.isPaletteItem = false;

        // Kinematic while dragging; gravity activates on drop
        var rb = draggingObject.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        isNewDevice = true;
        isDragging = true;
        dragStartFrame = Time.frameCount;
        wasShiftHeld = false;
        dragAnchorHeight = ComputeAnchorHeight(draggingObject);
        dragGrabOffset = Vector3.zero; // fresh device — no prior position to preserve, so it appears under the cursor
        ApplyHologram(draggingObject);
        ArmDragReleaseIfHeld();
        MoveDraggingObject();
    }

    // Raycasts the mouse against the actual (bounded) ground collider, and
    // every live table's own BoxCollider (ground level up to its tabletop —
    // see DeviceModelBuilder.BuildTable), taking whichever hit is closest to
    // the camera. This is what actually fixes the "jumps at a certain
    // position... at the closest camera zoom" bug reproduced by dragging a
    // device to a table's edge:
    //
    // Two earlier versions both tried to get the drag-anchor's surface
    // height from Table.ClearanceAt — an XZ-only lookup, not a raycast — fed
    // either the raw cursor hit or (in a second attempt) the device's own
    // last-frame position. ClearanceAt is a hard step (0 off a table,
    // topSurfaceY on one, no ramp), so whichever point got fed in, right at
    // the edge that point's true surface could disagree with what the ACTUAL
    // corrected point's surface turns out to be — the correction then built
    // itself around the wrong height. The last-frame-position attempt made
    // this worse, not better: a stale "on-table" assumption bled into the
    // correction for however many frames it took the seed to catch up,
    // visibly detaching the object from the cursor rather than just
    // flickering at the boundary.
    //
    // Raycasting the table's real collider instead of guessing removes the
    // guess entirely — whatever surface the ray actually hits at a given
    // screen position IS the correct height for that exact position, by
    // construction, every single time it's evaluated. There's no separate
    // lookup left to disagree with the raycast.
    bool TryRaycastSurface(Vector2 screenPos, out RaycastHit hit)
    {
        Ray ray = mainCamera.ScreenPointToRay(screenPos);
        hit = default;
        bool found = false;
        float closestDist = float.MaxValue;

        if (groundCollider != null && groundCollider.Raycast(ray, out RaycastHit groundHit, 200f))
        {
            hit = groundHit;
            closestDist = groundHit.distance;
            found = true;
        }

        var tables = Table.AllTables;
        for (int i = 0; i < tables.Count; i++)
        {
            var col = tables[i] != null ? tables[i].GetComponent<Collider>() : null;
            if (col != null && col.Raycast(ray, out RaycastHit tableHit, 200f) && tableHit.distance < closestDist)
            {
                hit = tableHit;
                closestDist = tableHit.distance;
                found = true;
            }
        }

        return found;
    }

    // Returns the surface point used to drive dragging, corrected so the
    // object's visual center (dragAnchorHeight above its base) — not its
    // invisible surface-contact point — tracks the cursor.
    //
    // An earlier version got this by re-intersecting the mouse ray against a
    // plane raised to dragAnchorHeight directly instead of the surface.
    // That's numerically unstable: how far a ray's surface-hit shifts when
    // the intersection plane is raised depends on that ray's own grazing
    // angle (height ÷ tan(angle-below-horizontal)), which varies a lot
    // across the screen under this isometric camera — shallower rays
    // (pointing toward the top of the screen, or whenever zoomed in close so
    // the overall pitch is shallow) shift far more than steep ones. The
    // dragged object would visibly move faster than the mouse
    // ("overextending") whenever the cursor was over a shallow-angle part of
    // the view.
    //
    // Raycasting only ever against real, bounded colliders (see
    // TryRaycastSurface — always well-conditioned) avoids that: hit the
    // surface at the raw cursor position, see where the lifted center would
    // land on screen, and re-hit the surface from a screen position nudged
    // by that discrepancy. One correction pass is enough since the
    // surface-to-lifted-center screen relationship barely changes between
    // two nearby points, and — unlike the ClearanceAt-based attempts above —
    // there's no separate height guess left that the two passes could
    // disagree about.
    bool TryGetAnchorPoint(out Vector3 anchorPoint)
    {
        Vector2 mouseScreen = Mouse.current.position.ReadValue();
        if (!TryRaycastSurface(mouseScreen, out RaycastHit hit))
        {
            anchorPoint = default;
            return false;
        }

        if (dragAnchorHeight <= 0.0001f)
        {
            anchorPoint = hit.point;
            return true;
        }

        Vector3 centerAtHit = hit.point + Vector3.up * dragAnchorHeight;
        Vector2 centerScreen = mainCamera.WorldToScreenPoint(centerAtHit);
        Vector2 correctedScreen = mouseScreen + (mouseScreen - centerScreen);

        anchorPoint = TryRaycastSurface(correctedScreen, out RaycastHit correctedHit) ? correctedHit.point : hit.point;
        return true;
    }

    void MoveDraggingObject()
    {
        if (draggingObject == null) return;

        if (TryGetAnchorPoint(out Vector3 anchorPoint))
        {
            // Carry the grab offset captured at pickup so the device moves
            // by the same amount the mouse does, rather than snapping to be
            // centered on the cursor (see dragGrabOffset).
            Vector3 targetPoint = anchorPoint + dragGrabOffset;

            // Free placement is the default; holding Ctrl switches to grid
            // snapping for precise alignment, and letting go returns to free.
            bool snapToGrid = Keyboard.current != null &&
                (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);
            Vector3 placed = snapToGrid ? SnapToGrid(targetPoint) : ClampToPlayArea(targetPoint);

            // Holding Shift locks movement to whichever of X/Z has moved further
            // from the point Shift was pressed, for straight horizontal/vertical runs.
            bool shiftHeld = Keyboard.current != null &&
                (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
            if (shiftHeld && !wasShiftHeld) shiftAxisAnchor = draggingObject.transform.position;
            wasShiftHeld = shiftHeld;
            if (shiftHeld)
            {
                float deltaX = Mathf.Abs(placed.x - shiftAxisAnchor.x);
                float deltaZ = Mathf.Abs(placed.z - shiftAxisAnchor.z);
                if (deltaX >= deltaZ) placed.z = shiftAxisAnchor.z;
                else placed.x = shiftAxisAnchor.x;
            }

            // Rests flush on whatever surface is under the cursor — a table's
            // top surface if one's there, otherwise the ground plane.
            placed.y = Table.ClearanceAt(placed);
            draggingObject.transform.position = placed;
        }

        UpdatePlacementTint();
    }

    // ── Hologram ghost ───────────────────────────────────────────────────────
    // The dragging object wears the shared HologramMaterial grid-lined ghost
    // instead of the real device materials — swapped in on pickup and
    // restored the moment it's placed/cancelled, so the actual device only
    // ever looks solid once it's actually sitting in the world.

    // Swaps every renderer under go onto its own hologram material instance
    // (grid density matched to that part's own world scale), remembering the
    // original shared material so RestoreFromHologram() can put it back.
    void ApplyHologram(GameObject go)
    {
        hologramRenderers = go.GetComponentsInChildren<Renderer>();
        hologramOriginalMaterials = new Material[hologramRenderers.Length];
        hologramInstanceMaterials = new Material[hologramRenderers.Length];

        // Fresh palette placements get the see-through ghost; repositioning an
        // already-placed device gets the opaque variant instead.
        Color tint = isNewDevice ? hologramColor : placedDragColor;

        for (int i = 0; i < hologramRenderers.Length; i++)
        {
            var r = hologramRenderers[i];
            hologramOriginalMaterials[i] = r.sharedMaterial;

            var instance = HologramMaterial.CreateInstance(r.transform.lossyScale, hologramGridCellSize, isNewDevice, tint);
            hologramInstanceMaterials[i] = instance;
            r.sharedMaterial = instance;
        }

        hologramColliders = go.GetComponentsInChildren<Collider>();
        foreach (var c in hologramColliders)
            c.enabled = false;

        // Added after the renderer swap above so it keeps its own bright
        // material instead of being caught by the hologram tint loop.
        var nd = go.GetComponent<NetworkDevice>();
        if (nd != null)
            portSideArrow = DeviceModelBuilder.CreatePortSideArrow(nd, nd.visualRoot);
    }

    void ApplyHologramTint(Color tint)
    {
        if (hologramInstanceMaterials == null) return;
        foreach (var mat in hologramInstanceMaterials)
            HologramMaterial.ApplyTint(mat, tint);
    }

    // Puts every renderer's original material back and destroys the temporary
    // hologram instances — called right before a drag ends, one way or another.
    void RestoreFromHologram()
    {
        if (hologramRenderers == null) return;

        for (int i = 0; i < hologramRenderers.Length; i++)
        {
            if (hologramRenderers[i] != null)
                hologramRenderers[i].sharedMaterial = hologramOriginalMaterials[i];
            if (hologramInstanceMaterials[i] != null)
                Object.Destroy(hologramInstanceMaterials[i]);
        }

        if (hologramColliders != null)
            foreach (var c in hologramColliders)
                if (c != null) c.enabled = true;

        if (portSideArrow != null) Object.Destroy(portSideArrow);
        portSideArrow = null;

        hologramRenderers = null;
        hologramOriginalMaterials = null;
        hologramInstanceMaterials = null;
        hologramColliders = null;
    }

    // ── Placement validity feedback ─────────────────────────────────────────

    void UpdatePlacementTint()
    {
        if (draggingObject == null) return;
        var nd = draggingObject.GetComponent<NetworkDevice>();
        bool onValidTable = nd == null || !nd.requiresTable || Table.AnyContains(draggingObject.transform.position);

        Color tint;
        if (!onValidTable || WouldOverlapDevice(draggingObject))
            tint = hologramInvalidColor;
        else if (nd != null && nd.requiresTable)
            tint = hologramValidColor;
        else
            tint = isNewDevice ? hologramColor : placedDragColor;

        // Same breathing alpha as TableManager's placement preview, via the
        // one shared implementation (see HologramMaterial.Pulse) — so every
        // hologram in the game reads as the same "living" material.
        tint.a = HologramMaterial.Pulse();
        ApplyHologramTint(tint);
    }

    // True if go's box footprint, at its current position/rotation, would clip
    // into any other already-placed device. The ghost's own collider is disabled
    // for the whole hologram phase (see ApplyHologram), so it never shows up in
    // its own overlap query — only real, solid devices ever count as a clash.
    static bool WouldOverlapDevice(GameObject go)
    {
        var col = go.GetComponent<BoxCollider>();
        if (col == null) return false;

        Vector3 halfExtents = Vector3.Scale(col.size, go.transform.lossyScale) * 0.5f;
        Vector3 center = go.transform.TransformPoint(col.center);

        foreach (var hit in Physics.OverlapBox(center, halfExtents, go.transform.rotation))
        {
            var device = hit.GetComponentInParent<NetworkDevice>();
            if (device != null && !device.isPaletteItem && device.gameObject != go)
                return true;
        }
        return false;
    }

    // Free (unstepped) turntable rotation, usable both on the object currently
    // being dragged and on a selected-but-not-dragging placed device. Scroll
    // alone now always drives camera zoom (see IsometricCamera), so rotation
    // requires holding R to disambiguate the two. Unity's +Y rotation is
    // clockwise viewed from above, so scroll-up (CCW) maps to negative degrees
    // and scroll-down (CW) to positive, matching the old step-rotate mapping.
    void HandleFreeRotation(GameObject target)
    {
        if (target == null) return;
        if (Keyboard.current == null || !Keyboard.current.rKey.isPressed) return;

        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Approximately(scroll, 0f)) return;

        // New Input System reports ~120 units per scroll notch; normalize to 1 notch = 1 step.
        float degrees = -(scroll / 120f) * freeRotateSpeed;
        target.transform.Rotate(Vector3.up, degrees, Space.World);
    }

    Vector3 SnapToGrid(Vector3 pos)
    {
        float x = Mathf.Round(pos.x / gridCellSize) * gridCellSize;
        float z = Mathf.Round(pos.z / gridCellSize) * gridCellSize;
        x = Mathf.Clamp(x, -playAreaHalfExtent, playAreaHalfExtent);
        z = Mathf.Clamp(z, -playAreaHalfExtent, playAreaHalfExtent);
        return new Vector3(x, pos.y, z);
    }

    Vector3 ClampToPlayArea(Vector3 pos)
    {
        pos.x = Mathf.Clamp(pos.x, -playAreaHalfExtent, playAreaHalfExtent);
        pos.z = Mathf.Clamp(pos.z, -playAreaHalfExtent, playAreaHalfExtent);
        return pos;
    }

    void Drop()
    {
        if (draggingObject == null) return;

        var nd = draggingObject.GetComponent<NetworkDevice>();
        if (nd != null && nd.requiresTable && !Table.AnyContains(draggingObject.transform.position))
            return; // invalid spot — keep dragging; red tint already shows why
        if (WouldOverlapDevice(draggingObject))
            return; // would clip into another device — keep dragging; red tint already shows why

        RestoreFromHologram();
        selectionIndicator?.Hide(); // no-op if this was a fresh placement, which never showed it

        var rb = draggingObject.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = false; // gravity takes it down

        if (isNewDevice)
        {
            // Pronounced squeeze-bounce fires the instant the reveal's final
            // (solidify) phase starts, so the "landing" pop and the blueprint
            // actually turning solid read as one simultaneous beat.
            ConstructionRevealEffect.Play(draggingObject,
                onFinalPhaseStart: nd != null ? () => PacketDeviceEffect.Play(nd, Color.white, PacketDeviceEffect.PlacementIntensity) : null);
        }
        else if (nd != null)
        {
            // Repositioning an already-placed device skips the reveal but still
            // gets the same pronounced pop as soon as it's dropped.
            PacketDeviceEffect.Play(nd, Color.white, PacketDeviceEffect.PlacementIntensity);
        }

        if (nd != null) OnDeviceDropped?.Invoke(nd, isNewDevice);

        // Sticky placement mode: a fresh palette device that just landed spawns
        // its replacement ghost immediately instead of ending the drag, so the
        // player can keep placing the same device without re-clicking the palette.
        bool continuePlacement = isNewDevice && devicePlacementActive;
        DeviceType typeToContinue = placementDeviceType;

        draggingObject = null;
        isDragging = false;

        if (continuePlacement)
            StartDragging(typeToContinue);
    }

    // Right-click while dragging: discard a fresh palette instance, or snap a
    // repositioned device back to where it was picked up from. Also the one
    // gesture that ends sticky palette placement mode.
    void CancelDrag()
    {
        if (draggingObject == null) return;

        RestoreFromHologram();
        selectionIndicator?.Hide();
        devicePlacementActive = false;
        dragReleaseArmed = false;

        if (isNewDevice)
        {
            Object.Destroy(draggingObject);
        }
        else
        {
            draggingObject.transform.SetPositionAndRotation(dragStartPosition, dragStartRotation);
            var rb = draggingObject.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;
        }

        draggingObject = null;
        isDragging = false;
    }
}
