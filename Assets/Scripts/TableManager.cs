using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

/// <summary>
/// Table placement mode: click one grid corner, then click the opposite corner
/// to size a rectangular table between them. A table's footprint is fixed once
/// built (no resize afterward) — position and dimensions are fully determined
/// by the two clicks, so the finished table is built directly at the resolved
/// position.
///
/// Usage:
///   1. Call BeginPlacing() (wired to the "Table" UI button).
///   2. Click the ground → first corner is fixed; a pulsing holographic
///      preview volume follows the cursor, snapped to the grid, until the
///      second click.
///   3. Click again → the table is built between the two corners; mode exits.
///   Right-click or BeginPlacing() again cancels the current operation.
///
/// Already-placed tables: a quick left-click selects/deselects (cyan
/// highlight, same as DragDropManager's placed devices); holding the click
/// down for <dragHoldInterval> seconds instead picks the table up and drags
/// it (grid-snapped, no rotation — a table has no facing) to a new spot.
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

    // Renderers wearing the reposition hologram while draggingTable is live,
    // and what to restore them to once it's dropped/cancelled — same bookkeeping
    // shape as DragDropManager's hologramRenderers/hologramOriginalMaterials.
    private Renderer[] dragHologramRenderers;
    private Material[] dragHologramOriginalMaterials;
    private Material[] dragHologramInstanceMaterials;
    private bool dragValid;

    public bool IsPlacing => isPlacing;
    public bool IsDragging => isDragging;

    // Fired once per freshly-built table (not on reposition drags), passing the
    // new Table — mirrors DragDropManager.OnDeviceDropped / CableManager.OnCableCreated.
    // Used by TutorialManager to detect "player placed a table" and to read its
    // footprint for positioning device-placement guide markers on top of it.
    public event System.Action<Table> OnTablePlaced;

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
        if (CLIPanelManager.Instance != null && CLIPanelManager.Instance.IsOpen) return;
        if (SettingsPanelManager.Instance != null && SettingsPanelManager.Instance.IsOpen) return;

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

    // ── Reposition drag ──────────────────────────────────────────────────────

    void PickUpPlacedTable(Table table)
    {
        if (isDragging) return;

        InteractionMode.CancelOthers(this);
        SetHighlighted(null);

        draggingTable = table;
        dragStartPosition = table.transform.position;
        isDragging = true;
        dragValid = true;
        ApplyDragHologram(table);
        // Built after ApplyDragHologram so its outline shells aren't among
        // the renderers that call swaps onto the hologram ghost material —
        // same ordering DragDropManager.PickUpPlacedDevice relies on.
        selectionIndicator?.ShowDragging(table.transform);
    }

    void MoveDraggingTable()
    {
        if (draggingTable == null || !RaycastGround(out Vector3 point)) return;

        draggingTable.transform.position = new Vector3(point.x, draggingTable.transform.position.y, point.z);
        draggingTable.SyncFinalCenter();

        dragValid = !WouldOverlapOtherTable(draggingTable);
        UpdateDragHologramTint();
    }

    void DropTable()
    {
        if (draggingTable == null) return;

        if (!dragValid) return; // would overlap another table — keep dragging; red tint already shows why

        Table table = draggingTable;
        draggingTable = null;
        isDragging = false;
        RestoreDragHologram();
        selectionIndicator?.Hide();

        // Just a reposition, not a fresh placement — no reveal sweep, but still
        // gets the same pronounced landing pop devices get when repositioned.
        PacketDeviceEffect.Play(table.gameObject, table.transform, Color.white, PacketDeviceEffect.PlacementIntensity);
    }

    void CancelDragTable()
    {
        if (draggingTable == null) return;

        draggingTable.transform.position = dragStartPosition;
        draggingTable.SyncFinalCenter();
        RestoreDragHologram();
        selectionIndicator?.Hide();
        draggingTable = null;
        isDragging = false;
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

    bool RaycastGround(out Vector3 point)
    {
        Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        var plane = new Plane(Vector3.up, Vector3.zero);
        if (plane.Raycast(ray, out float dist))
        {
            point = SnapToGrid(ray.GetPoint(dist));
            return true;
        }
        point = default;
        return false;
    }

    static Vector3 SnapToGrid(Vector3 pos)
    {
        var ddm = DragDropManager.Instance;
        float cell = ddm != null ? ddm.GridCellSize : 1f;
        float half = ddm != null ? ddm.PlayAreaHalfExtent : 7.5f;
        float x = Mathf.Clamp(Mathf.Round(pos.x / cell) * cell, -half, half);
        float z = Mathf.Clamp(Mathf.Round(pos.z / cell) * cell, -half, half);
        return new Vector3(x, 0f, z);
    }

    void UpdatePreview()
    {
        if (!RaycastGround(out Vector3 current)) return;

        Vector3 a = hasOrigin ? originPoint : current;
        var (center, width, length) = ResolveRect(a, current);

        previewOverlapInvalid = Table.WouldOverlap(center, width, length);

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

    static (Vector3 center, float width, float length) ResolveRect(Vector3 a, Vector3 b)
    {
        float cell = DragDropManager.Instance != null ? DragDropManager.Instance.GridCellSize : 1f;
        float minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x);
        float minZ = Mathf.Min(a.z, b.z), maxZ = Mathf.Max(a.z, b.z);
        float width  = Mathf.Max(maxX - minX, cell);
        float length = Mathf.Max(maxZ - minZ, cell);
        Vector3 center = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);
        return (center, width, length);
    }

    void SetOrigin()
    {
        if (!RaycastGround(out Vector3 point)) return;
        originPoint = point;
        hasOrigin = true;
    }

    void FinishPlacement()
    {
        if (!RaycastGround(out Vector3 point)) return;

        var (center, width, length) = ResolveRect(originPoint, point);

        // Would overlap an already-placed table — refuse to build, same as
        // DropTable() refusing a reposition drop while dragValid is false.
        // Stay in placement mode with the origin still fixed so the red
        // preview keeps tracking the cursor and the player can just pick a
        // different second corner.
        if (Table.WouldOverlap(center, width, length)) return;

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
            onFinalPhaseStart: () => PacketDeviceEffect.Play(table, table.transform, Color.white, PacketDeviceEffect.PlacementIntensity));

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
