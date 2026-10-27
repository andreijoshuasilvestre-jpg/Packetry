using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Table removal mode: click a placed table to delete it. Mirrors
/// DeviceRemoverManager/CableRemoverManager exactly, just retargeted at Table
/// — with one deliberate difference: any devices sitting on the table are
/// removed along with it (a requiresTable device has nowhere else to live,
/// so "keep the table, drop the devices" would just strand them). Those
/// devices are outlined in the hover highlight too, so it's clear before you
/// click that they're going with it, and a confirm dialog gates the actual
/// deletion since it's no longer a single-object, trivially-undoable action.
///
/// Usage:
///   1. Call BeginRemoving() (wired to the "Remove Table" UI button).
///   2. Hovering a placed table shows the same arrow+outline+ring
///      SelectionIndicator TableManager uses for a click-selected table (see
///      TableManager.ApplyHighlightTint), just red instead of cyan — a
///      "target for deletion" hover, not a selection — extended to also
///      outline every device currently sitting on the table.
///   3. Click it → ConfirmDialogManager asks to confirm; confirming deletes
///      the table and every device on it. Mode stays active for the next one.
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
        if (InteractionMode.IsSuspended) return;
        if (ConfirmDialogManager.Instance != null && ConfirmDialogManager.Instance.IsOpen) return; // frozen mid-confirm — keep the hovered table/highlight as-is

        UpdateHover();

        if (Mouse.current.leftButton.wasPressedThisFrame && hoveredTable != null)
            RemoveHovered();

        if (Mouse.current.rightButton.wasPressedThisFrame)
            Cancel();
    }

    void UpdateHover()
    {
        // A destroyed-but-still-referenced hoveredTable (removed through some
        // path other than this class's own DoRemove, e.g. a future external
        // reset) would otherwise never get caught below: Unity's == treats a
        // destroyed object as equal to a fresh raycast miss (null), so
        // `next == hoveredTable` would read "still the same hover, nothing to
        // do" and skip ever hiding its indicator — leaving the arrow/ring
        // stuck floating at the table's last position forever. Force it
        // through the hide/clear path first, using ReferenceEquals to catch
        // the "non-null reference, but Unity says destroyed" case the
        // overloaded == above can't distinguish from a genuine null.
        if (!ReferenceEquals(hoveredTable, null) && hoveredTable == null)
        {
            selectionIndicator?.Hide();
            hoveredTable = null;
        }

        // Over the HUD the click belongs to that UI, not to a table behind
        // it on screen — see DeviceRemoverManager.UpdateHover.
        Table next = IsPointerOverUI() ? null : RaycastForTable();
        if (next == hoveredTable) return;

        SetHighlight(hoveredTable, false);
        hoveredTable = next;
        SetHighlight(hoveredTable, true);
    }

    static bool IsPointerOverUI() =>
        EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

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

        var devices = DevicesOnTable(table);
        var roots = new Transform[devices.Count + 1];
        roots[0] = table.transform;
        for (int i = 0; i < devices.Count; i++) roots[i + 1] = devices[i].transform;

        selectionIndicator.ShowOnMesh(roots, VisualTopPoint(table), groundCenter, new Vector2(table.width, table.length));
    }

    // Every placed, non-palette device currently sitting inside the table's
    // footprint — these are the ones that go with it if the table is removed
    // (see DoRemove) and are outlined alongside it in the hover highlight.
    static List<NetworkDevice> DevicesOnTable(Table table)
    {
        var result = new List<NetworkDevice>();
        foreach (var device in Object.FindObjectsByType<NetworkDevice>(FindObjectsInactive.Exclude))
        {
            if (device.isPaletteItem) continue;
            if (table.ContainsXZ(device.transform.position)) result.Add(device);
        }
        return result;
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

    // Doesn't delete anything itself — gates the actual removal behind a
    // ConfirmDialogManager prompt, since this now takes any devices on the
    // table with it (no longer a single, trivially-undoable deletion). The
    // hovered table/highlight stay frozen while the dialog is up (see the
    // ConfirmDialogManager.IsOpen check in Update()).
    void RemoveHovered()
    {
        var table = hoveredTable;
        if (table == null) return;

        var devices = DevicesOnTable(table);
        string message = devices.Count > 0
            ? $"Remove this table? {devices.Count} device{(devices.Count == 1 ? "" : "s")} on it will also be removed."
            : "Remove this table?";

        if (ConfirmDialogManager.Instance == null) { DoRemove(table, devices); return; }
        ConfirmDialogManager.Instance.Show(message, "Remove", () => DoRemove(table, devices));
    }

    void DoRemove(Table table, List<NetworkDevice> devices)
    {
        hoveredTable = null;
        if (table == null) return;
        selectionIndicator?.Hide(); // about to be destroyed — don't leave the outline tracking it
        AudioManager.Instance?.PlaySfx(SfxId.Remove); // once for the table and everything on it

        // Routed through DeviceRemoverManager.RemoveDevice (not a bare
        // Destroy) so each one still fires OnDeviceRemoved — the event
        // AutoSaveManager/PacketSendManager/TutorialManager already listen
        // to for their own cleanup/re-save.
        foreach (var device in devices)
        {
            if (device == null) continue;
            if (DeviceRemoverManager.Instance != null) DeviceRemoverManager.Instance.RemoveDevice(device);
            else Destroy(device.gameObject);
        }

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
