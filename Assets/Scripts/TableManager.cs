using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

/// <summary>
/// Table placement mode: click one corner, then click the opposite corner
/// to size a rectangular table between them — position and dimensions are
/// fully determined by the two clicks, so the finished table is built directly
/// at the resolved position.
///
/// Placement is free-form (not tied to the world grid), same as
/// DragDropManager's device drag; holding Ctrl snaps corners to the grid for
/// precise alignment, and letting go returns to free.
///
/// Usage:
///   1. Call BeginPlacing() (wired to the "Table" UI button).
///   2. Click the ground → first corner is fixed; a pulsing holographic
///      preview volume follows the cursor until the second click.
///   3. Click again → the table is built between the two corners; mode exits.
///   Right-click or BeginPlacing() again cancels the current operation.
///
/// Already-placed tables: a quick left-click selects/deselects (cyan
/// highlight, same as DragDropManager's placed devices); holding the click
/// down for <dragHoldInterval> seconds instead picks the table up and drags
/// it (free-form, Ctrl to snap; no rotation — a table has no facing) to a new spot.
///
/// A selected table also shows resize handles on its corners and edge
/// midpoints: press one and drag to move that side (a corner moves both sides
/// it touches), release to keep the new size. The opposite side stays put,
/// the table never shrinks past the devices sitting on it, and a size that
/// would overlap another table shows red and falls back to the last size that
/// fit. Ctrl snaps the dragged side to the grid; right-click cancels.
/// </summary>
public class TableManager : MonoBehaviour
{
    public static TableManager Instance { get; private set; }

    // Neutral white tint — HologramMaterial's grid texture bakes in the actual
    // line/fill colors (see previewLineColor below), so this just controls
    // overall opacity without recoloring them (same convention as
    // DragDropManager's drag-ghost hologramColor).
    [SerializeField] private Color previewColor = new Color(1f, 1f, 1f, 0.85f);
    [SerializeField] private float previewGridCellSize = 0.12f; // matches DragDropManager.hologramGridCellSize
    // A table's flat, low-profile footprint read poorly against the ground
    // even after switching the old white-line/blue-fill default to a
    // transparent yellow — it just blended into the plane. Now baked as a
    // mostly-opaque solid yellow fill (so the table's actual shape is
    // unmistakable) with a brighter, near-white-yellow, fully-opaque line
    // color layered on top so the grid still reads as a distinct "glowing"
    // accent against the duller fill, instead of both fading into one
    // translucent wash.
    [SerializeField] private Color previewLineColor = new Color(1f, 1f, 0.82f, 1f);
    [SerializeField] private Color previewFillColor = new Color(1f, 0.78f, 0.12f, 0.92f);
    // Swapped in for previewColor while the two-click rectangle would overlap
    // an already-placed table — same red used everywhere else in the game for
    // "invalid spot" (DragDropManager.hologramInvalidColor, this class's own
    // dragInvalidColor below), so a fresh-placement overlap reads the same as
    // a reposition-drag overlap.
    [SerializeField] private Color previewInvalidColor = new Color(1f, 0.3f, 0.3f, 0.85f);

    [Header("Placed-table select / drag")]
    // Same gesture and defaults as DragDropManager's placed-device select/drag:
    // a quick click selects/deselects, holding past this interval picks the
    // table up for repositioning instead.
    [SerializeField] private float dragHoldInterval = 0.35f;
    [SerializeField] private Color selectionHighlightColor = new Color(0.2f, 0.9f, 1f); // matches DragDropManager's

    // Repositioning an already-placed table now wears the same opaque
    // hologram ghost DragDropManager gives a repositioned device, tinted red
    // the moment the new spot would overlap another table — the table itself
    // used to just slide around with no feedback at all about where it was
    // about to land or whether that spot was even valid, unlike every other
    // placement flow in the game (see DragDropManager.UpdatePlacementTint).
    [SerializeField] private Color dragValidColor = new Color(0.25f, 0.55f, 1f, 1f); // matches DragDropManager.placedDragColor
    [SerializeField] private Color dragInvalidColor = new Color(1f, 0.3f, 0.3f, 0.85f); // matches DragDropManager.hologramInvalidColor
    [SerializeField] private float dragGridCellSize = 0.12f; // matches DragDropManager.hologramGridCellSize

    private bool isPlacing;
    private bool hasOrigin;
    private Vector3 originPoint;
    // Set each frame by UpdatePreview() — whether the rectangle currently
    // being sized would overlap an already-placed table. FinishPlacement()
    // refuses to build while this is true, same as DropTable() refusing a
    // reposition drop while dragValid is false.
    private bool previewOverlapInvalid;
    private Camera mainCamera;
    private GameObject previewGo;
    private Transform previewTop;
    private Transform[] previewLegs;
    private Material previewTopMat;
    private Material[] previewLegMats;

    // Same SelectionIndicator (arrow + true mesh-silhouette outline + ground
    // ring) DragDropManager uses for placed devices, owned separately here —
    // InteractionMode's mutual-exclusion chain guarantees the two never need
    // to be visible at once.
    private SelectionIndicator selectionIndicator;
    private Table highlightedTable;
    private Table pendingPickTable;
    private float pressStartTime;

    private bool isDragging;
    private Table draggingTable;
    private Vector3 dragStartPosition;
    // Table center minus the tabletop point under the cursor at pickup, so the
    // table moves by the same amount the mouse does instead of jumping to
    // center itself on the cursor — same idea as DragDropManager.dragGrabOffset.
    private Vector3 dragGrabOffset;

    // Devices resting on the table being dragged, and their positions at the
    // moment the drag started — carried along by the same delta the table
    // itself moves so a repositioned table takes its devices with it instead
    // of leaving them stranded at the old spot. Restored exactly (not just
    // delta-reversed) on a cancelled drag to avoid any float drift.
    private readonly List<NetworkDevice> draggingTableDevices = new List<NetworkDevice>();
    private readonly List<Vector3> draggingTableDeviceStartPositions = new List<Vector3>();

    // Renderers wearing the reposition hologram while draggingTable is live,
    // and what to restore them to once it's dropped/cancelled — same bookkeeping
    // shape as DragDropManager's hologramRenderers/hologramOriginalMaterials.
    private Renderer[] dragHologramRenderers;
    private Material[] dragHologramOriginalMaterials;
    private Material[] dragHologramInstanceMaterials;
    private bool dragValid;

    [Header("Resize handles")]
    // Sized as a share of the camera's distance, so they stay grabbable at any
    // zoom, but never more than a quarter of the table's shorter side.
    [SerializeField] private float handleSizePerDistance = 0.014f;
    [SerializeField] private float handlePickRadius = 18f; // pixels (at 1080p) around a handle that count as on it
    [SerializeField] private Color handleColor = new Color(0.2f, 0.9f, 1f); // matches selectionHighlightColor
    [SerializeField] private Color handleHoverColor = Color.white;

    // Which sides of the footprint each handle moves: -1/+1 along X (width)
    // and Z (length), 0 = that axis stays. Four corners, then four edges.
    static readonly Vector2Int[] HandleSides =
    {
        new Vector2Int(-1, -1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(1, 1),
        new Vector2Int(0, -1), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(1, 0),
    };
    const float HandleLift = 0.03f; // above the tabletop, clear of z-fighting with it

    private Transform handleRoot;
    private Transform[] handles;
    private Renderer[] handleRenderers;
    private MaterialPropertyBlock handleBlock;
    private string handleColorProp;

    private bool isResizing;
    private Table resizingTable;
    private int activeHandle = -1;
    private Vector3 resizeGrabOffset;
    private Vector3 resizeStartCenter, lastValidCenter;
    private float resizeStartWidth, resizeStartLength, lastValidWidth, lastValidLength;
    // How far the devices on the table being resized reach (XZ, from their
    // renderers) — the dragged side stops there instead of sliding under them.
    private bool resizeHasDevices;
    private Vector2 resizeDevicesMin, resizeDevicesMax;
    private readonly List<NetworkDevice> resizingTableDevices = new List<NetworkDevice>();

    public bool IsPlacing => isPlacing;
    public bool IsDragging => isDragging;
    public bool IsResizing => isResizing;
    /// <summary>A placed table is selected (see SetHighlighted) — the status bar
    /// says it can be resized or moved.</summary>
    public bool HasSelection => highlightedTable != null && !isDragging && !isResizing;
    /// <summary>The live size while resizing (width, length).</summary>
    public Vector2 ResizeSize => resizingTable != null ? new Vector2(resizingTable.width, resizingTable.length) : Vector2.zero;
    /// <summary>Whether the size being dragged out right now fits — false while
    /// it would overlap another table (the hologram shows red).</summary>
    public bool ResizeFits => dragValid;
    /// <summary>The cursor is on one of the selected table's resize handles —
    /// DragDropManager leaves that press alone, even with a device under it.</summary>
    public bool IsPointerOverResizeHandle => HandleUnderCursor() >= 0;
    /// <summary>A table's resize handles are on screen (selected and ready, or
    /// mid-resize) — TutorialManager's resize step waits on it.</summary>
    public bool ShowsResizeHandles => HandleTable() != null;
    // True between the two placement clicks — the first corner is fixed and
    // the next click sets the opposite one. Read by the status bar and
    // TutorialManager's table step to tell the player which click is next.
    public bool HasOrigin => isPlacing && hasOrigin;
    // The fixed first corner (ground level, grid-snapped if Ctrl was held) —
    // meaningful only while HasOrigin is true.
    public Vector3 OriginPoint => originPoint;

    // Fired once per freshly-built table (not on reposition drags), passing the
    // new Table — mirrors DragDropManager.OnDeviceDropped / CableManager.OnCableCreated.
    // Used by TutorialManager to detect "player placed a table" and to read its
    // footprint for positioning device-placement guide markers on top of it.
    public event System.Action<Table> OnTablePlaced;

    // Fired once per finished resize that actually changed the footprint,
    // with the footprint it had before (center, width, length) — what
    // UndoManager puts back. AutoSaveManager saves on it.
    public event System.Action<Table, Vector3, float, float> OnTableResized;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        mainCamera = Camera.main;
        BuildPreview();

        selectionIndicator = new GameObject("_TableSelectionIndicator").AddComponent<SelectionIndicator>();
        selectionIndicator.SetColor(selectionHighlightColor);

        BuildHandles();
    }

    void BuildPreview()
    {
        // Mirrors DeviceModelBuilder.BuildTable's actual shape (a thin tabletop
        // slab + 4 legs) instead of a single solid cube, and wears the same
        // grid-lined HologramMaterial ghost as DragDropManager's drag preview,
        // so every placement hologram in the game reads as one consistent style.
        previewGo = new GameObject("_TablePlacementPreview");
        previewGo.transform.SetParent(transform);

        previewTop = CreatePreviewPart("_PreviewTop", out previewTopMat);
        previewLegs = new Transform[4];
        previewLegMats = new Material[4];
        for (int i = 0; i < 4; i++)
            previewLegs[i] = CreatePreviewPart($"_PreviewLeg{i}", out previewLegMats[i]);

        previewGo.SetActive(false);
    }

    Transform CreatePreviewPart(string partName, out Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = partName;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(previewGo.transform, false);

        mat = HologramMaterial.CreateInstance(Vector3.one, previewGridCellSize, transparent: true, previewColor, previewLineColor, previewFillColor);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go.transform;
    }

    void Update()
    {
        // A resize is press-and-hold, like a device drag: an overlay taking
        // over mid-gesture puts the table back rather than leaving it hanging
        // on a release that lands on the overlay's button.
        bool blocked = (CLIPanelManager.Instance != null && CLIPanelManager.Instance.IsOpen) || InteractionMode.IsSuspended;
        if (blocked)
        {
            if (isResizing) CancelResize(reselect: false);
            return;
        }

        if (isResizing)
        {
            UpdateResize();
            return;
        }

        if (isPlacing)
        {
            UpdatePreview();
            UpdatePulse();

            if (Mouse.current.leftButton.wasPressedThisFrame && !IsPointerOverUI())
            {
                if (!hasOrigin) SetOrigin();
                else FinishPlacement();
            }

            if (Mouse.current.rightButton.wasPressedThisFrame)
                Cancel();
            return;
        }

        if (isDragging)
        {
            // The table in hand was destroyed out from under the drag — end
            // it now (see CancelDragTable) instead of waiting forever on a
            // drop that has nothing left to drop.
            if (draggingTable == null) { CancelDragTable(); return; }

            MoveDraggingTable();

            if (Mouse.current.leftButton.wasPressedThisFrame && !IsPointerOverUI())
                DropTable();

            if (Mouse.current.rightButton.wasPressedThisFrame)
                CancelDragTable();
            return;
        }

        // Not placing or dragging: allow click-select / hold-to-pick-up on an
        // already-placed table (same gesture as DragDropManager's placed
        // devices), but only while no other exclusive interaction mode owns input.
        if ((DragDropManager.Instance == null || !DragDropManager.Instance.IsDragging) &&
            (CableManager.Instance == null || !CableManager.Instance.IsConnecting) &&
            (CableRemoverManager.Instance == null || !CableRemoverManager.Instance.IsRemoving) &&
            (DeviceRemoverManager.Instance == null || !DeviceRemoverManager.Instance.IsRemoving) &&
            (TableRemoverManager.Instance == null || !TableRemoverManager.Instance.IsRemoving) &&
            (PingToolManager.Instance == null || !PingToolManager.Instance.IsPinging) &&
            (PacketSendManager.Instance == null || !PacketSendManager.Instance.IsSending))
        {
            HandlePlacedTablePress();
        }
    }

    static bool IsPointerOverUI() =>
        EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    // ── Placed-table select (click) vs pick-up (hold) ───────────────────────
    // Mirrors DragDropManager.HandlePlacedDevicePress exactly, just retargeted
    // at Table instead of NetworkDevice.

    void HandlePlacedTablePress()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame && !IsPointerOverUI())
        {
            int handle = HandleUnderCursor();
            if (handle >= 0)
            {
                BeginResize(highlightedTable, handle);
                return;
            }

            Table table = RaycastForTable();
            if (table != null)
            {
                pendingPickTable = table;
                pressStartTime = Time.time;
            }
            else
            {
                SetHighlighted(null);
            }
        }

        if (pendingPickTable == null) return;

        if (!Mouse.current.leftButton.isPressed)
        {
            // Released before the hold threshold — a click, not a hold: select/deselect.
            Table clicked = pendingPickTable;
            pendingPickTable = null;
            SetHighlighted(highlightedTable == clicked ? null : clicked);
        }
        else if (Time.time - pressStartTime >= dragHoldInterval)
        {
            Table table = pendingPickTable;
            pendingPickTable = null;
            PickUpPlacedTable(table);
        }
    }

    Table RaycastForTable()
    {
        Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f)) return null;
        return hit.collider.GetComponentInParent<Table>();
    }

    void SetHighlighted(Table table)
    {
        if (table == highlightedTable) return;

        ApplyHighlightTint(highlightedTable, false);
        highlightedTable = table;
        ApplyHighlightTint(highlightedTable, true);
    }

    void ApplyHighlightTint(Table table, bool on)
    {
        if (table == null || selectionIndicator == null) return;

        if (!on)
        {
            selectionIndicator.Hide();
            return;
        }

        // Outlines the tabletop slab and all 4 legs individually (one shell
        // per renderer — see SelectionIndicator), so the outline traces the
        // table's real composite shape instead of one box around the whole
        // footprint. Ground ring uses the table's own known width/length
        // rather than re-deriving it from renderer bounds — that's the
        // authoritative footprint already used for overlap checks elsewhere
        // in this file.
        Vector3 groundCenter = new Vector3(table.FinalCenter.x, table.transform.position.y, table.FinalCenter.z);
        selectionIndicator.ShowOnMesh(table.transform, VisualTopPoint(table), groundCenter, new Vector2(table.width, table.length));
    }

    // Table has no VisualTopPoint() of its own (unlike NetworkDevice) — same
    // combined-renderer-bounds approach, just local to this file since only
    // the selection arrow needs it.
    static Vector3 VisualTopPoint(Table table)
    {
        var renderers = table.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return table.transform.position;

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);

        return new Vector3(table.transform.position.x, b.max.y, table.transform.position.z);
    }

    // Called by InteractionMode when another interaction mode takes over, so a
    // stale selection highlight doesn't linger once the table is no longer
    // actually selectable/draggable.
    public void ClearHighlight() => SetHighlighted(null);

    // ── Resize handles ───────────────────────────────────────────────────────

    void BuildHandles()
    {
        handleRoot = new GameObject("_TableResizeHandles").transform;
        handleRoot.SetParent(transform, false);

        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        var material = new Material(shader);
        handleColorProp = material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
        handleBlock = new MaterialPropertyBlock();

        handles = new Transform[HandleSides.Length];
        handleRenderers = new Renderer[HandleSides.Length];
        for (int i = 0; i < HandleSides.Length; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"_ResizeHandle{i}";
            // Picked in screen space (see HandleUnderCursor), never by raycast —
            // a collider here would get in the way of every other click.
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(handleRoot, false);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            handles[i] = go.transform;
            handleRenderers[i] = r;
        }
        handleRoot.gameObject.SetActive(false);
    }

    // The table whose handles are showing: the one being resized, else the
    // selected one (not while it's being moved, or a new one is being placed,
    // or it's still building in — BeginResize would refuse the grab).
    Table HandleTable()
    {
        if (isResizing) return resizingTable;
        if (isDragging || isPlacing || highlightedTable == null || highlightedTable.IsBuilding) return null;
        if (highlightedTable.GetComponent<ConstructionRevealEffect>() != null) return null;
        return highlightedTable;
    }

    static Vector3 HandlePoint(Table table, int i) => HandlePosition(table, HandleSides[i]);

    /// <summary>Where the handle that moves these sides sits: -1/0/+1 along X
    /// (width) and Z (length), so (1, 0) is the +X edge's handle.</summary>
    public static Vector3 HandlePosition(Table table, Vector2Int side)
    {
        Vector3 c = table.FinalCenter;
        return new Vector3(c.x + side.x * table.width * 0.5f, table.topSurfaceY + HandleLift, c.z + side.y * table.length * 0.5f);
    }

    // Nearest handle within handlePickRadius of the cursor, on screen — or -1.
    int HandleUnderCursor()
    {
        Table table = HandleTable();
        if (table == null || mainCamera == null || Mouse.current == null) return -1;

        Vector2 mouse = Mouse.current.position.ReadValue();
        float best = handlePickRadius * Screen.height / 1080f;
        int found = -1;
        for (int i = 0; i < HandleSides.Length; i++)
        {
            Vector3 sp = mainCamera.WorldToScreenPoint(HandlePoint(table, i));
            if (sp.z <= 0f) continue;
            float d = Vector2.Distance(mouse, sp);
            if (d <= best) { best = d; found = i; }
        }
        return found;
    }

    // After Update, so handles land where this frame's resize left the table.
    void LateUpdate()
    {
        if (handleRoot == null) return;
        Table table = HandleTable();
        bool show = table != null;
        if (handleRoot.gameObject.activeSelf != show) handleRoot.gameObject.SetActive(show);
        if (!show || mainCamera == null) return;

        int lit = isResizing ? activeHandle : HandleUnderCursor();
        float maxSize = Mathf.Min(table.width, table.length) * 0.25f;
        for (int i = 0; i < HandleSides.Length; i++)
        {
            Vector3 p = HandlePoint(table, i);
            float size = Mathf.Min(Vector3.Distance(mainCamera.transform.position, p) * handleSizePerDistance, maxSize);
            if (i == lit) size *= 1.3f;

            // A corner handle is a square; an edge handle a bar along its edge.
            Vector2Int side = HandleSides[i];
            float sx = side.x == 0 ? size * 2f : size;
            float sz = side.y == 0 ? size * 2f : size;
            handles[i].SetPositionAndRotation(p, Quaternion.identity); // tables never rotate
            handles[i].localScale = new Vector3(sx, size * 0.3f, sz);

            Color c = i == lit ? handleHoverColor : handleColor;
            handleBlock.SetColor(handleColorProp, c * HologramMaterial.GlowMultiplier);
            handleRenderers[i].SetPropertyBlock(handleBlock);
        }
    }

    // ── Resize ───────────────────────────────────────────────────────────────

    void BeginResize(Table table, int handle)
    {
        // Still growing in, or mid blueprint reveal (which owns its materials)
        // — let it finish first.
        if (table == null || table.IsBuilding || table.GetComponent<ConstructionRevealEffect>() != null) return;
        if (!RaycastPlane(table.topSurfaceY, out Vector3 cursor)) return;

        InteractionMode.CancelOthers(this);
        SetHighlighted(null);

        resizingTable     = table;
        activeHandle      = handle;
        resizeStartCenter = lastValidCenter = table.FinalCenter;
        resizeStartWidth  = lastValidWidth  = table.width;
        resizeStartLength = lastValidLength = table.length;

        // Cursor-to-handle offset, so the side doesn't jump to the cursor.
        Vector3 handlePoint = HandlePoint(table, handle);
        resizeGrabOffset = new Vector3(handlePoint.x - cursor.x, 0f, handlePoint.z - cursor.z);

        CollectResizeDevices(table);
        isResizing = true;
        dragValid = true;
        ApplyDragHologram(table);
        // After ApplyDragHologram — see PickUpPlacedTable.
        selectionIndicator?.ShowDragging(table.transform);
    }

    void CollectResizeDevices(Table table)
    {
        resizingTableDevices.Clear();
        resizeHasDevices = false;
        foreach (var device in Object.FindObjectsByType<NetworkDevice>(FindObjectsInactive.Exclude))
        {
            if (device.isPaletteItem || !table.ContainsXZ(device.transform.position)) continue;
            resizingTableDevices.Add(device);

            foreach (var r in device.GetComponentsInChildren<Renderer>())
            {
                Bounds b = r.bounds;
                Vector2 min = new Vector2(b.min.x, b.min.z), max = new Vector2(b.max.x, b.max.z);
                if (!resizeHasDevices) { resizeDevicesMin = min; resizeDevicesMax = max; resizeHasDevices = true; }
                else { resizeDevicesMin = Vector2.Min(resizeDevicesMin, min); resizeDevicesMax = Vector2.Max(resizeDevicesMax, max); }
            }
        }
    }

    void UpdateResize()
    {
        if (resizingTable == null) { CancelResize(reselect: false); return; }
        if (Mouse.current.rightButton.wasPressedThisFrame) { CancelResize(reselect: true); return; }
        // isPressed, not wasReleasedThisFrame — same reasoning as DragDropManager's drag.
        if (!Mouse.current.leftButton.isPressed) { FinishResize(); return; }

        if (!RaycastPlane(resizingTable.topSurfaceY, out Vector3 cursor)) return;
        Vector3 target = cursor + resizeGrabOffset;
        if (SnapHeld()) target = RoundToGrid(target);
        target = ClampToPlayArea(target);

        Vector2Int side = HandleSides[activeHandle];
        ResolveResizeAxis(side.x, resizeStartCenter.x, resizeStartWidth, target.x,
            resizeDevicesMin.x, resizeDevicesMax.x, out float centerX, out float width);
        ResolveResizeAxis(side.y, resizeStartCenter.z, resizeStartLength, target.z,
            resizeDevicesMin.y, resizeDevicesMax.y, out float centerZ, out float length);
        var center = new Vector3(centerX, 0f, centerZ);

        ApplyFootprint(resizingTable, center, width, length);

        dragValid = !Table.WouldOverlap(center, width, length, resizingTable) &&
                    !Table.WouldCoverDevice(center, width, length, resizingTableDevices);
        if (dragValid)
        {
            lastValidCenter = center;
            lastValidWidth  = width;
            lastValidLength = length;
        }
        UpdateDragHologramTint();
    }

    // One axis of a resize. side 0 leaves it as it started; otherwise the
    // opposite edge stays fixed and the dragged one follows target — but never
    // closer than MinTableSide to the fixed edge, and never back past the
    // devices on the table (only as far as the table's starting edge, so a
    // device already hanging off it doesn't make the table grow on its own).
    void ResolveResizeAxis(int side, float startCenter, float startSize, float target,
        float devicesMin, float devicesMax, out float center, out float size)
    {
        if (side == 0) { center = startCenter; size = startSize; return; }

        float fixedEdge = startCenter - side * startSize * 0.5f;
        float startEdge = startCenter + side * startSize * 0.5f;
        float moving = target;
        if (side > 0)
        {
            moving = Mathf.Max(moving, fixedEdge + MinTableSide);
            if (resizeHasDevices) moving = Mathf.Max(moving, Mathf.Min(devicesMax, startEdge));
        }
        else
        {
            moving = Mathf.Min(moving, fixedEdge - MinTableSide);
            if (resizeHasDevices) moving = Mathf.Min(moving, Mathf.Max(devicesMin, startEdge));
        }

        center = (fixedEdge + moving) * 0.5f;
        size   = Mathf.Abs(moving - fixedEdge);
    }

    // Re-fits the table's geometry and moves it to the new center; devices on
    // it aren't its children, so they stay exactly where they are.
    void ApplyFootprint(Table table, Vector3 center, float width, float length)
    {
        DeviceModelBuilder.ResizeTable(table, width, length);
        table.transform.position = new Vector3(center.x, table.transform.position.y, center.z);
        table.SyncFinalCenter();

        // The hologram's grid follows each part's new size, like the placement
        // preview's does (see UpdatePreview).
        if (dragHologramRenderers == null) return;
        for (int i = 0; i < dragHologramRenderers.Length; i++)
            if (dragHologramRenderers[i] != null)
                HologramMaterial.UpdateGridTexture(dragHologramInstanceMaterials[i], dragHologramRenderers[i].transform.lossyScale, dragGridCellSize);
    }

    void FinishResize()
    {
        Table table = resizingTable;
        if (!dragValid)
        {
            // Released on a size that overlaps something — keep the last one that fit.
            ApplyFootprint(table, lastValidCenter, lastValidWidth, lastValidLength);
            AudioManager.Instance?.PlaySfx(SfxId.Refusal);
        }

        EndResize();

        bool changed = !Mathf.Approximately(table.width, resizeStartWidth) ||
                       !Mathf.Approximately(table.length, resizeStartLength) ||
                       (table.FinalCenter - resizeStartCenter).sqrMagnitude > 1e-6f;
        if (changed)
        {
            PacketDeviceEffect.Play(table.gameObject, table.transform, Color.white, PacketDeviceEffect.PlacementIntensity, AnimationKind.Build);
            AudioManager.Instance?.PlaySfx(SfxId.Place);
            OnTableResized?.Invoke(table, resizeStartCenter, resizeStartWidth, resizeStartLength);
        }

        SetHighlighted(table); // still selected, outline and handles now at the new size
    }

    // Puts the starting size back. reselect: a right-click cancel keeps the
    // table selected; another mode taking over (InteractionMode) doesn't.
    void CancelResize(bool reselect)
    {
        Table table = resizingTable;
        if (table != null) ApplyFootprint(table, resizeStartCenter, resizeStartWidth, resizeStartLength);
        EndResize();
        if (reselect && table != null) SetHighlighted(table);
    }

    void EndResize()
    {
        RestoreDragHologram();
        selectionIndicator?.Hide();
        resizingTableDevices.Clear();
        resizingTable = null;
        activeHandle = -1;
        isResizing = false;
    }

    // InteractionMode's hook — see CancelDragging.
    public void CancelResizing() => CancelResize(reselect: false);

    // ── Reposition drag ──────────────────────────────────────────────────────

    void PickUpPlacedTable(Table table)
    {
        if (isDragging) return;

        InteractionMode.CancelOthers(this);
        SetHighlighted(null);

        draggingTable = table;
        dragStartPosition = table.transform.position;
        dragGrabOffset = RaycastPlane(table.topSurfaceY, out Vector3 grabPoint)
            ? new Vector3(dragStartPosition.x - grabPoint.x, 0f, dragStartPosition.z - grabPoint.z)
            : Vector3.zero;
        isDragging = true;
        dragValid = true;
        CollectDevicesOnTable(table);
        ApplyDragHologram(table);
        // Built after ApplyDragHologram so its outline shells aren't among
        // the renderers that call swaps onto the hologram ghost material —
        // same ordering DragDropManager.PickUpPlacedDevice relies on.
        selectionIndicator?.ShowDragging(table.transform);
    }

    void MoveDraggingTable()
    {
        // Raycast against the tabletop's own height rather than the ground, so
        // the spot the player grabbed stays under the cursor instead of
        // drifting with the parallax between the two planes.
        if (draggingTable == null || !RaycastPlane(draggingTable.topSurfaceY, out Vector3 point)) return;

        Vector3 target = point + dragGrabOffset;
        if (SnapHeld())
        {
            // Snaps the table's corner, not its center, so its edges land on
            // grid lines — same as a Ctrl-snapped two-click placement.
            Vector3 half = new Vector3(draggingTable.width * 0.5f, 0f, draggingTable.length * 0.5f);
            target = RoundToGrid(target - half) + half;
        }
        target = ClampToPlayArea(target);

        draggingTable.transform.position = new Vector3(target.x, draggingTable.transform.position.y, target.z);
        draggingTable.SyncFinalCenter();

        // Carry every device that was riding this table along by the same
        // total delta, re-derived from the fixed drag-start positions each
        // frame (rather than accumulated frame-to-frame) so it can't drift.
        Vector3 tableDelta = draggingTable.transform.position - dragStartPosition;
        for (int i = 0; i < draggingTableDevices.Count; i++)
            if (draggingTableDevices[i] != null)
                draggingTableDevices[i].transform.position = draggingTableDeviceStartPositions[i] + tableDelta;

        dragValid = !WouldOverlapOtherTable(draggingTable) &&
            !Table.WouldCoverDevice(draggingTable.transform.position, draggingTable.width, draggingTable.length, draggingTableDevices);
        UpdateDragHologramTint();
    }

    void DropTable()
    {
        if (draggingTable == null) return;

        if (!dragValid)
        {
            AudioManager.Instance?.PlaySfx(SfxId.Refusal);
            return; // would overlap another table (or a stray device) — keep dragging; red tint already shows why
        }

        Table table = draggingTable;
        draggingTable = null;
        isDragging = false;
        RestoreDragHologram();
        selectionIndicator?.Hide();
        draggingTableDevices.Clear();
        draggingTableDeviceStartPositions.Clear();

        // Just a reposition, not a fresh placement — no reveal sweep, but still
        // gets the same pronounced landing pop (and sound) devices get when repositioned.
        PacketDeviceEffect.Play(table.gameObject, table.transform, Color.white, PacketDeviceEffect.PlacementIntensity, AnimationKind.Build);
        AudioManager.Instance?.PlaySfx(SfxId.Place);
    }

    void CancelDragTable()
    {
        // Keyed on isDragging, not draggingTable — see DragDropManager.
        // CancelDrag: bailing out on a destroyed table used to leave
        // isDragging stuck on, locking out every table and device click.
        if (!isDragging && draggingTable == null) return;

        if (draggingTable != null)
        {
            draggingTable.transform.position = dragStartPosition;
            draggingTable.SyncFinalCenter();
        }

        // Snap carried devices back to their exact pre-drag positions rather
        // than relying on the (now zero) delta, so there's no residual drift.
        for (int i = 0; i < draggingTableDevices.Count; i++)
            if (draggingTableDevices[i] != null)
                draggingTableDevices[i].transform.position = draggingTableDeviceStartPositions[i];
        draggingTableDevices.Clear();
        draggingTableDeviceStartPositions.Clear();

        RestoreDragHologram();
        selectionIndicator?.Hide();
        draggingTable = null;
        isDragging = false;
    }

    // Snapshots every device currently sitting in `table`'s footprint, along
    // with its position, right as the drag begins — see draggingTableDevices.
    void CollectDevicesOnTable(Table table)
    {
        draggingTableDevices.Clear();
        draggingTableDeviceStartPositions.Clear();
        foreach (var device in Object.FindObjectsByType<NetworkDevice>(FindObjectsInactive.Exclude))
        {
            if (device.isPaletteItem || !table.ContainsXZ(device.transform.position)) continue;
            draggingTableDevices.Add(device);
            draggingTableDeviceStartPositions.Add(device.transform.position);
        }
    }

    // ── Reposition hologram indicator ────────────────────────────────────────
    // Swaps the real table's own renderers onto the shared HologramMaterial
    // ghost (opaque, same convention as DragDropManager's repositioned-device
    // ghost) so a table drag shows exactly where it's about to land and
    // whether that spot is valid, instead of the table just sliding around
    // with no feedback at all.

    void ApplyDragHologram(Table table)
    {
        dragHologramRenderers = table.GetComponentsInChildren<Renderer>();
        dragHologramOriginalMaterials = new Material[dragHologramRenderers.Length];
        dragHologramInstanceMaterials = new Material[dragHologramRenderers.Length];

        for (int i = 0; i < dragHologramRenderers.Length; i++)
        {
            var r = dragHologramRenderers[i];
            dragHologramOriginalMaterials[i] = r.sharedMaterial;

            var instance = HologramMaterial.CreateInstance(r.transform.lossyScale, dragGridCellSize, transparent: false, dragValidColor);
            dragHologramInstanceMaterials[i] = instance;
            r.sharedMaterial = instance;
        }
    }

    void UpdateDragHologramTint()
    {
        if (dragHologramInstanceMaterials == null) return;

        Color tint = dragValid ? dragValidColor : dragInvalidColor;
        tint.a = HologramMaterial.Pulse();
        foreach (var mat in dragHologramInstanceMaterials)
            HologramMaterial.ApplyTint(mat, tint);
    }

    void RestoreDragHologram()
    {
        if (dragHologramRenderers == null) return;

        for (int i = 0; i < dragHologramRenderers.Length; i++)
        {
            if (dragHologramRenderers[i] != null)
                dragHologramRenderers[i].sharedMaterial = dragHologramOriginalMaterials[i];
            if (dragHologramInstanceMaterials[i] != null)
                Object.Destroy(dragHologramInstanceMaterials[i]);
        }

        dragHologramRenderers = null;
        dragHologramOriginalMaterials = null;
        dragHologramInstanceMaterials = null;
    }

    // True if table's footprint at its current (live-dragged) position would
    // overlap any other placed table's footprint. The dragged table is
    // excluded so it never "overlaps" itself. See Table.WouldOverlap.
    static bool WouldOverlapOtherTable(Table table) =>
        Table.WouldOverlap(table.transform.position, table.width, table.length, table);

    // Right-click while dragging cancels externally too (e.g. InteractionMode
    // switching to another mode mid-drag) — same idea as DragDropManager's
    // CancelDrag, just reachable from outside for the mutual-exclusion hook.
    public void CancelDragging() => CancelDragTable();

    // Raw (unsnapped) cursor point on the horizontal plane at height y,
    // returned with y = 0 since callers only ever use its XZ.
    bool RaycastPlane(float y, out Vector3 point)
    {
        Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        var plane = new Plane(Vector3.up, new Vector3(0f, y, 0f));
        if (plane.Raycast(ray, out float dist))
        {
            Vector3 p = ray.GetPoint(dist);
            point = new Vector3(p.x, 0f, p.z);
            return true;
        }
        point = default;
        return false;
    }

    // The ground point under the cursor for a two-click placement corner —
    // free by default, grid-snapped while Ctrl is held, always kept inside
    // the play area.
    bool RaycastCorner(out Vector3 point)
    {
        if (!RaycastPlane(0f, out point)) return false;
        if (SnapHeld()) point = RoundToGrid(point);
        point = ClampToPlayArea(point);
        return true;
    }

    // Same modifier DragDropManager uses for its device drag.
    static bool SnapHeld() => Keyboard.current != null &&
        (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);

    static Vector3 RoundToGrid(Vector3 pos)
    {
        float cell = DragDropManager.Instance != null ? DragDropManager.Instance.GridCellSize : 1f;
        return new Vector3(Mathf.Round(pos.x / cell) * cell, pos.y, Mathf.Round(pos.z / cell) * cell);
    }

    static Vector3 ClampToPlayArea(Vector3 pos)
    {
        float half = DragDropManager.Instance != null ? DragDropManager.Instance.PlayAreaHalfExtent : 9f;
        return new Vector3(Mathf.Clamp(pos.x, -half, half), pos.y, Mathf.Clamp(pos.z, -half, half));
    }

    void UpdatePreview()
    {
        if (!RaycastCorner(out Vector3 current)) return;

        Vector3 a = hasOrigin ? originPoint : current;
        var (center, width, length) = ResolveRect(a, current);

        previewOverlapInvalid = Table.WouldOverlap(center, width, length) || Table.WouldCoverDevice(center, width, length);

        // Volumetric hologram shaped like the real table (slab + 4 legs, see
        // DeviceModelBuilder.BuildTable) rather than a solid block, so it reads
        // as a preview of the actual object instead of a generic blue cube.
        previewGo.SetActive(true);
        previewGo.transform.position = center;

        float topThickness = DeviceModelBuilder.TableTopThickness;
        float legThickness = DeviceModelBuilder.TableLegThickness;
        float legInset     = DeviceModelBuilder.TableLegInset;
        float tableHeight  = DeviceModelBuilder.TableHeight;

        previewTop.localPosition = new Vector3(0f, tableHeight, 0f);
        previewTop.localScale    = new Vector3(width, topThickness, length);

        float legHeight = tableHeight - topThickness * 0.5f;
        float hx = Mathf.Max(width * 0.5f - legInset, legThickness);
        float hz = Mathf.Max(length * 0.5f - legInset, legThickness);
        Vector3[] legOffsets =
        {
            new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz),
            new Vector3(-hx, 0f,  hz), new Vector3(hx, 0f,  hz),
        };
        for (int i = 0; i < 4; i++)
        {
            previewLegs[i].localPosition = new Vector3(legOffsets[i].x, legHeight * 0.5f, legOffsets[i].z);
            previewLegs[i].localScale    = new Vector3(legThickness, legHeight, legThickness);
        }

        // Grid density follows each part's current world scale (same as the
        // device drag ghost), so the resize gesture keeps a constant
        // world-space cell size instead of stretching one baked texture.
        HologramMaterial.UpdateGridTexture(previewTopMat, previewTop.lossyScale, previewGridCellSize, previewLineColor, previewFillColor);
        for (int i = 0; i < 4; i++)
            HologramMaterial.UpdateGridTexture(previewLegMats[i], previewLegs[i].lossyScale, previewGridCellSize, previewLineColor, previewFillColor);
    }

    // Breathing alpha + glow boost via the shared HologramMaterial helpers
    // (see Pulse/ApplyTint) instead of this class's own sine wave, so the
    // table preview reads as the same "living" hologram material as
    // DragDropManager's drag ghost rather than inventing its own look.
    void UpdatePulse()
    {
        Color c = previewOverlapInvalid ? previewInvalidColor : previewColor;
        c.a = HologramMaterial.Pulse();

        HologramMaterial.ApplyTint(previewTopMat, c);
        foreach (var mat in previewLegMats) HologramMaterial.ApplyTint(mat, c);
    }

    // Smallest side a table can have (world units) — a thinner drag is
    // widened to this, centered on the dragged span (see ResolveRect).
    const float MinTableSide = 1f;

    // Both corners are free-form, so center and size vary continuously with
    // the cursor — including while it crosses back over the first corner,
    // where the MinTableSide floor just slides the footprint across instead
    // of flipping it from one side to the other. (With grid-snapped corners
    // that floor made the preview jump half a cell every time the cursor
    // crossed the first corner's cell, which read as jitter.)
    static (Vector3 center, float width, float length) ResolveRect(Vector3 a, Vector3 b)
    {
        float minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x);
        float minZ = Mathf.Min(a.z, b.z), maxZ = Mathf.Max(a.z, b.z);
        float width  = Mathf.Max(maxX - minX, MinTableSide);
        float length = Mathf.Max(maxZ - minZ, MinTableSide);
        Vector3 center = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);
        return (center, width, length);
    }

    void SetOrigin()
    {
        if (!RaycastCorner(out Vector3 point)) return;
        originPoint = point;
        hasOrigin = true;
    }

    // Floor below which a resolved footprint reads as "the player didn't
    // really pick a second corner" rather than a deliberate small table —
    // most commonly hit by double-clicking near the first corner instead of
    // making two distinct clicks (see FinishPlacement's guard below). 1.5
    // rather than exactly MinTableSide so a near-miss second click still
    // gets caught, not just an exact repeat click.
    const float MinMeaningfulDimension = 1.5f;

    void FinishPlacement()
    {
        if (!RaycastCorner(out Vector3 point)) return;

        var (center, width, length) = ResolveRect(originPoint, point);

        // Guards against the classic "double-clicked instead of clicking two
        // distinct corners" mistake: both clicks land on (or right next to)
        // the same spot, ResolveRect's own Mathf.Max floor silently clamps
        // the result to MinTableSide, and a table a fraction of a device's
        // size gets built with no warning. Refuse instead, stay in placement
        // mode with the origin still fixed (same as the overlap guard below)
        // so the player can just click a properly distant second corner, and
        // surface why nothing happened — the silent floor previously gave no
        // feedback at all.
        if (width < MinMeaningfulDimension && length < MinMeaningfulDimension)
        {
            AnnotationWarningManager.Instance?.Show("Table Too Small",
                "That second click landed on (or right next to) your first one, so the table would be too small to hold anything.\n\nClick a corner further away to size it — two separate clicks, not a double-click.",
                NotifyKind.Warning);
            return;
        }

        // Would overlap an already-placed table, or cover a device (e.g. one
        // left stranded on the ground by a removed table — see
        // TableRemoverManager) — refuse to build, same as DropTable()
        // refusing a reposition drop while dragValid is false. Stay in
        // placement mode with the origin still fixed so the red preview
        // keeps tracking the cursor and the player can just pick a different
        // second corner.
        if (Table.WouldOverlap(center, width, length) || Table.WouldCoverDevice(center, width, length))
        {
            AudioManager.Instance?.PlaySfx(SfxId.Refusal);
            return;
        }

        AudioManager.Instance?.PlaySfx(SfxId.Place);
        GameObject table = DeviceModelBuilder.BuildTable(width, length);
        table.transform.position = center;
        Table tableComponent = table.GetComponent<Table>();
        tableComponent.PlayBuildAnimation(originPoint);

        // boundsOverride is required here: PlayBuildAnimation above immediately
        // (synchronously) squashes the table's X/Z scale down to near-zero to
        // start its axis-grow — and ConstructionRevealEffect's coroutine would
        // run on that same frame, before the grow animation restores it. If it
        // measured the live (still-squashed) renderer bounds, the reveal's
        // distance-based mask would be normalized against a near-zero box and
        // the entire object would jump from blueprint to solid almost
        // instantly, reading as "no fade". Deriving the true final bounds from
        // the table's own known dimensions instead sidesteps that race
        // entirely, regardless of how the axis-grow is currently animating.
        float tableTop = 2f * tableComponent.topSurfaceY - DeviceModelBuilder.TableHeight;
        Bounds tableBounds = new Bounds(
            new Vector3(center.x, tableTop * 0.5f, center.z),
            new Vector3(width, tableTop, length));

        // Shorter phases than devices — the axis-grow already fixes final size fast.
        // originHint keeps the blueprint sweep starting from the same clicked
        // corner the axis-grow animates from, instead of an unrelated corner.
        // holdDuration is padded a bit past the default so the final (solidify)
        // phase — and the squeeze-bounce fired alongside it below — doesn't
        // start until PlayBuildAnimation's own axis-grow (~0.32s: two 0.16s
        // axis animations back to back) has finished with the table's
        // transform. Both effects scale transform.localScale, so overlapping
        // them would have the squeeze-bounce fight the axis-grow for control
        // of the same property mid-animation.
        ConstructionRevealEffect.Play(table, blueprintPhaseDuration: 0.22f, holdDuration: 0.15f,
            solidPhaseDuration: 0.18f, originHint: originPoint, boundsOverride: tableBounds,
            onFinalPhaseStart: () => PacketDeviceEffect.Play(table, table.transform, Color.white, PacketDeviceEffect.PlacementIntensity, AnimationKind.Build));

        OnTablePlaced?.Invoke(tableComponent);
        Cancel(); // two-click gesture complete — exit mode like CableManager does after one cable
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void BeginPlacing()
    {
        if (isPlacing) { Cancel(); return; } // toggle off
        if (isDragging) CancelDragTable(); // switching straight from repositioning into a fresh placement

        InteractionMode.CancelOthers(this);

        isPlacing = true;
        hasOrigin = false;
    }

    public void CancelPlacing() => Cancel();

    void Cancel()
    {
        isPlacing = false;
        hasOrigin = false;
        if (previewGo != null) previewGo.SetActive(false);
    }
}
