using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Table removal mode: click a placed table to delete it. Mirrors
/// DeviceRemoverManager/CableRemoverManager exactly, just retargeted at Table
/// (devices left sitting on a removed table are untouched — same "nothing
/// cascades, everything else reacts to null on its own" philosophy Cable
/// already relies on for a removed device).
///
/// Usage:
///   1. Call BeginRemoving() (wired to the "Remove Table" UI button).
///   2. Hovering a placed table shows the same arrow+outline+ring
///      SelectionIndicator TableManager uses for a click-selected table (see
///      TableManager.ApplyHighlightTint), just red instead of cyan — a
///      "target for deletion" hover, not a selection.
///   3. Click it → the table is deleted; mode exits.
///   Right-click or BeginRemoving() again cancels the current operation.
/// </summary>
public class TableRemoverManager : MonoBehaviour
{
    public static TableRemoverManager Instance { get; private set; }

    [SerializeField] private Color highlightColor = new Color(1f, 0.25f, 0.2f);

    private bool isRemoving;
    private Camera mainCamera;
    private Table hoveredTable;
    private SelectionIndicator selectionIndicator;

    public bool IsRemoving => isRemoving;

    // Mirrors DeviceRemoverManager.OnDeviceRemoved / CableManager.OnCableRemoved —
    // used by AutoSaveManager so removing a table re-saves the level's topology too.
    public event System.Action<Table> OnTableRemoved;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        mainCamera = Camera.main;

        selectionIndicator = new GameObject("_TableRemoveSelectionIndicator").AddComponent<SelectionIndicator>();
        selectionIndicator.SetColor(highlightColor);
    }

    void Update()
    {
        if (!isRemoving) return;
        if (CLIPanelManager.Instance != null && CLIPanelManager.Instance.IsOpen) return;
        if (SettingsPanelManager.Instance != null && SettingsPanelManager.Instance.IsOpen) return;

        UpdateHover();

        if (Mouse.current.leftButton.wasPressedThisFrame && hoveredTable != null)
            RemoveHovered();

        if (Mouse.current.rightButton.wasPressedThisFrame)
            Cancel();
    }

    void UpdateHover()
    {
        Table next = RaycastForTable();
        if (next == hoveredTable) return;

        SetHighlight(hoveredTable, false);
        hoveredTable = next;
        SetHighlight(hoveredTable, true);
    }

    Table RaycastForTable()
    {
        Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f)) return null;
        return hit.collider.GetComponentInParent<Table>();
    }

    void SetHighlight(Table table, bool on)
    {
        if (selectionIndicator == null) return;
        if (!on || table == null) { selectionIndicator.Hide(); return; }

        // Mirrors TableManager.ApplyHighlightTint exactly — ring sized to the
        // table's own known width/length rather than re-deriving it from
        // renderer bounds, same authoritative footprint used elsewhere.
        Vector3 groundCenter = new Vector3(table.FinalCenter.x, table.transform.position.y, table.FinalCenter.z);
        selectionIndicator.ShowOnMesh(table.transform, VisualTopPoint(table), groundCenter, new Vector2(table.width, table.length));
    }

    // Table has no VisualTopPoint() of its own (unlike NetworkDevice) — mirrors
    // TableManager's own local copy since only the selection arrow needs it.
    static Vector3 VisualTopPoint(Table table)
    {
        var renderers = table.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return table.transform.position;

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);

        return new Vector3(table.transform.position.x, b.max.y, table.transform.position.z);
    }

    void RemoveHovered()
    {
        var table = hoveredTable;
        hoveredTable = null;
        if (table == null) return;
        selectionIndicator?.Hide(); // about to be destroyed — don't leave the outline tracking it
        OnTableRemoved?.Invoke(table);
        Destroy(table.gameObject);
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void BeginRemoving()
    {
        if (isRemoving) { Cancel(); return; } // toggle off

        InteractionMode.CancelOthers(this);

        isRemoving = true;
        hoveredTable = null;
    }

    public void CancelRemoving() => Cancel();

    void Cancel()
    {
        SetHighlight(hoveredTable, false);
        hoveredTable = null;
        isRemoving = false;
    }
}
