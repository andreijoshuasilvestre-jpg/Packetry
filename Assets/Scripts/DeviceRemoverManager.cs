using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Device removal mode: click a placed device to delete it. Any cables attached
/// to it self-destroy on their next Update() once the device reference goes null
/// (see Cable.cs), so no manual cable cleanup is needed here.
///
/// Usage:
///   1. Call BeginRemoving() (wired to the "Remove Device" UI button).
///   2. Hovering a placed device highlights it red.
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
    private MaterialPropertyBlock propBlock;

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
        if (device == null) return;
        propBlock ??= new MaterialPropertyBlock();
        foreach (var r in device.GetComponentsInChildren<Renderer>())
        {
            if (on)
            {
                string prop = r.sharedMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                Color orig = r.sharedMaterial.GetColor(prop);
                propBlock.Clear();
                propBlock.SetColor(prop, Color.Lerp(orig, highlightColor, 0.55f));
                r.SetPropertyBlock(propBlock);
            }
            else
            {
                r.SetPropertyBlock(null);
            }
        }
    }

    void RemoveHovered()
    {
        var device = hoveredDevice;
        hoveredDevice = null;
        if (device == null) return;
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
