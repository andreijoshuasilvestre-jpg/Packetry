using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Hovering the mouse over a connected port shows that port's cable in the
/// shared hologram material (see HologramMaterial, Cable.SetHologramPreview)
/// — a quick "which wire is this?" trace without opening the CLI or picker.
///
/// Ports have no collider of their own (DeviceModelBuilder.Part strips them,
/// leaving only each device's single root BoxCollider for picking — see
/// CableManager.TrySelect for the same pattern), so hover is resolved by
/// raycasting the device as a whole and then finding whichever connected
/// port's own world attach point (NetworkDevice.GetPortWorldPoint) the hit
/// landed closest to, within portHoverRadius.
/// </summary>
public class PortHologramHoverManager : MonoBehaviour
{
    public static PortHologramHoverManager Instance { get; private set; }

    [SerializeField] private float portHoverRadius = 0.16f;

    private Camera mainCamera;
    private Cable hoveredCable;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start() => mainCamera = Camera.main;

    void Update()
    {
        if (!CanHover())
        {
            ClearHover();
            return;
        }

        Cable cable = RaycastHoveredCable();
        if (cable == hoveredCable) return;

        ClearHover();
        hoveredCable = cable;
        if (hoveredCable != null) hoveredCable.SetHologramPreview(true);
    }

    // Mirrors the mutual-exclusion checks other passive/active interaction
    // modes gate on (see DragDropManager's own chain) — a hologram trace is
    // just visual noise while a panel, drag, or another click-driven mode
    // already owns the mouse.
    bool CanHover()
    {
        if (mainCamera == null || Mouse.current == null) return false;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;
        if (CLIPanelManager.Instance != null && CLIPanelManager.Instance.IsOpen) return false;
        if (InteractionMode.IsSuspended) return false;
        if (DragDropManager.Instance != null && DragDropManager.Instance.IsDragging) return false;
        if (CableManager.Instance != null && CableManager.Instance.IsConnecting) return false;
        if (CableRemoverManager.Instance != null && CableRemoverManager.Instance.IsRemoving) return false;
        if (DeviceRemoverManager.Instance != null && DeviceRemoverManager.Instance.IsRemoving) return false;
        if (TableRemoverManager.Instance != null && TableRemoverManager.Instance.IsRemoving) return false;
        return true;
    }

    Cable RaycastHoveredCable()
    {
        Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f)) return null;

        var device = hit.collider.GetComponentInParent<NetworkDevice>();
        if (device == null || device.isPaletteItem) return null;

        Cable best = null;
        float bestDist = portHoverRadius;
        for (int i = 0; i < device.PortCount; i++)
        {
            Cable cable = device.GetPortCable(i);
            if (cable == null) continue;

            float dist = Vector3.Distance(hit.point, device.GetPortWorldPoint(i));
            if (dist < bestDist) { bestDist = dist; best = cable; }
        }
        return best;
    }

    void ClearHover()
    {
        if (hoveredCable != null) hoveredCable.SetHologramPreview(false);
        hoveredCable = null;
    }
}
