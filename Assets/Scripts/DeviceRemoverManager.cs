using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Device removal mode: click a placed device to delete it. Any cables attached
/// to it self-destroy on their next Update() once the device reference goes null
/// (see Cable.cs), so no manual cable cleanup is needed here.
///
/// Usage:
///   1. Call BeginRemoving() (wired to the "Remove Device" UI button).
///   2. Hovering a placed device shows the same arrow+outline+ring
///      SelectionIndicator DragDropManager uses for a click-selected device
///      (see DragDropManager.ApplyHighlightTint), just red instead of cyan —
///      a "target for deletion" hover, not a selection. Mirrors
///      CableRemoverManager's own red SelectionIndicator.
///   3. Click it → the device is deleted; mode exits.
///   Right-click or BeginRemoving() again cancels the current operation.
/// </summary>
public class DeviceRemoverManager : MonoBehaviour
{
    public static DeviceRemoverManager Instance { get; private set; }

    [SerializeField] private Color highlightColor = new Color(1f, 0.25f, 0.2f);

    private bool isRemoving;
    private Camera mainCamera;
    private NetworkDevice hoveredDevice;
    private SelectionIndicator selectionIndicator;

    public bool IsRemoving => isRemoving;

    // Fired once per removed device — mirrors CableManager.OnCableRemoved.
    // Used by AutoSaveManager so removing a device re-saves the level's
    // topology too, not just placing/cabling one.
    public event System.Action<NetworkDevice> OnDeviceRemoved;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        mainCamera = Camera.main;

        selectionIndicator = new GameObject("_DeviceRemoveSelectionIndicator").AddComponent<SelectionIndicator>();
        selectionIndicator.SetColor(highlightColor);
    }

    void Update()
    {
        if (!isRemoving) return;
        if (CLIPanelManager.Instance != null && CLIPanelManager.Instance.IsOpen) return;
        if (SettingsPanelManager.Instance != null && SettingsPanelManager.Instance.IsOpen) return;

        UpdateHover();

        if (Mouse.current.leftButton.wasPressedThisFrame && hoveredDevice != null)
            RemoveHovered();

        if (Mouse.current.rightButton.wasPressedThisFrame)
            Cancel();
    }

    void UpdateHover()
    {
        NetworkDevice next = RaycastForDevice();
        if (next == hoveredDevice) return;

        SetHighlight(hoveredDevice, false);
        hoveredDevice = next;
        SetHighlight(hoveredDevice, true);
    }

    NetworkDevice RaycastForDevice()
    {
        Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f)) return null;

        var device = hit.collider.GetComponentInParent<NetworkDevice>();
        if (device == null || device.isPaletteItem) return null;
        return device;
    }

    void SetHighlight(NetworkDevice device, bool on)
    {
        if (selectionIndicator == null) return;
        if (!on || device == null) { selectionIndicator.Hide(); return; }

        var (groundCenter, footprintXZ) = ComputeGroundFootprint(device);
        selectionIndicator.ShowOnMesh(device.transform, device.VisualTopPoint(), groundCenter, footprintXZ);
    }

    // Mirrors DragDropManager.ComputeGroundFootprint exactly — same reasoning
    // (device's own combined renderer bounds, base-centered) for the ring.
    static (Vector3 center, Vector2 footprintXZ) ComputeGroundFootprint(NetworkDevice device)
    {
        var renderers = device.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return (device.transform.position, Vector2.one * 0.5f);

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);

        return (new Vector3(b.center.x, b.min.y, b.center.z), new Vector2(b.size.x, b.size.z));
    }

    void RemoveHovered()
    {
        var device = hoveredDevice;
        hoveredDevice = null;
        if (device == null) return;
        selectionIndicator?.Hide(); // about to be destroyed — don't leave the outline tracking it
        OnDeviceRemoved?.Invoke(device);
        Destroy(device.gameObject);
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void BeginRemoving()
    {
        if (isRemoving) { Cancel(); return; } // toggle off

        InteractionMode.CancelOthers(this);

        isRemoving = true;
        hoveredDevice = null;
    }

    public void CancelRemoving() => Cancel();

    void Cancel()
    {
        SetHighlight(hoveredDevice, false);
        hoveredDevice = null;
        isRemoving = false;
    }
}
