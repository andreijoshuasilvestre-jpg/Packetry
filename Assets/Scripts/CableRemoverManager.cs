using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Cable removal mode: click an existing cable to delete it and free both endpoint ports.
///
/// Usage:
///   1. Call BeginRemoving() (wired to the "Remove" UI button).
///   2. Hovering a cable highlights it red.
///   3. Click it → the cable is deleted and both ports are freed; mode exits.
///   Right-click or BeginRemoving() again cancels the current operation.
/// </summary>
public class CableRemoverManager : MonoBehaviour
{
    public static CableRemoverManager Instance { get; private set; }

    [SerializeField] private float pickPixelDistance = 14f; // screen-space pick threshold
    [SerializeField] private Color highlightColor = new Color(1f, 0.25f, 0.2f);

    private bool isRemoving;
    private Camera mainCamera;
    private Cable hoveredCable;

    // Same arrow + outline SelectionIndicator DragDropManager/TableManager use
    // for a clicked placed device/table — just no ground ring (see
    // SelectionIndicator.ShowOnCable: a cable strung between two devices has
    // no footprint to ring), and red instead of cyan since this is a "target
    // for deletion" hover, not a selection.
    private SelectionIndicator selectionIndicator;

    public bool IsRemoving => isRemoving;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        mainCamera = Camera.main;

        selectionIndicator = new GameObject("_CableRemoveSelectionIndicator").AddComponent<SelectionIndicator>();
        selectionIndicator.SetColor(highlightColor);
    }

    void Update()
    {
        if (!isRemoving) return;
        if (CLIPanelManager.Instance != null && CLIPanelManager.Instance.IsOpen) return;
        if (InteractionMode.IsSuspended) return;

        UpdateHover();

        if (Mouse.current.leftButton.wasPressedThisFrame && hoveredCable != null)
            RemoveHovered();

        if (Mouse.current.rightButton.wasPressedThisFrame)
            Cancel();
    }

    void UpdateHover()
    {
        // Destroyed-but-still-referenced hover — see DeviceRemoverManager.UpdateHover.
        if (!ReferenceEquals(hoveredCable, null) && hoveredCable == null)
        {
            selectionIndicator?.Hide();
            hoveredCable = null;
        }

        // Over the HUD the click belongs to that UI, not to a cable that
        // happens to run behind it on screen — see DeviceRemoverManager.UpdateHover.
        Cable next = null;
        if (!IsPointerOverUI())
        {
            Cable closest = FindClosestCable(out float dist);
            next = (dist <= pickPixelDistance) ? closest : null;
        }

        if (next == hoveredCable) return;

        SetHighlight(hoveredCable, false);
        hoveredCable = next;
        SetHighlight(hoveredCable, true);
    }

    static bool IsPointerOverUI() =>
        EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    Cable FindClosestCable(out float bestDist)
    {
        bestDist = float.MaxValue;
        Cable best = null;
        if (CableManager.Instance == null) return null;

        Vector2 mouseScreen = Mouse.current.position.ReadValue();

        foreach (var cable in CableManager.Instance.Cables)
        {
            if (cable == null) continue;
            float dist = DistanceToCable(mouseScreen, cable);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = cable;
            }
        }
        return best;
    }

    float DistanceToCable(Vector2 mouseScreen, Cable cable)
    {
        var lr = cable.GetComponent<LineRenderer>();
        if (lr == null || lr.positionCount < 2) return float.MaxValue;

        float closest = float.MaxValue;
        Vector2 prev = mainCamera.WorldToScreenPoint(lr.GetPosition(0));
        for (int i = 1; i < lr.positionCount; i++)
        {
            Vector2 curr = mainCamera.WorldToScreenPoint(lr.GetPosition(i));
            float d = DistancePointToSegment(mouseScreen, prev, curr);
            if (d < closest) closest = d;
            prev = curr;
        }
        return closest;
    }

    static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 0.0001f) return Vector2.Distance(p, a);

        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
        Vector2 proj = a + ab * t;
        return Vector2.Distance(p, proj);
    }

    void SetHighlight(Cable cable, bool on)
    {
        if (selectionIndicator == null) return;
        if (!on || cable == null) { selectionIndicator.Hide(); return; }
        selectionIndicator.ShowOnCable(cable);
    }

    void RemoveHovered()
    {
        var cable = hoveredCable;
        hoveredCable = null;
        selectionIndicator?.Hide(); // about to be destroyed — don't leave the outline tracking it
        if (cable != null) AudioManager.Instance?.PlaySfx(SfxId.Remove);
        CableManager.Instance?.RemoveCable(cable);
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void BeginRemoving()
    {
        if (isRemoving) { Cancel(); return; } // toggle off

        InteractionMode.CancelOthers(this);

        isRemoving = true;
        hoveredCable = null;
    }

    public void CancelRemoving() => Cancel();

    void Cancel()
    {
        SetHighlight(hoveredCable, false);
        hoveredCable = null;
        isRemoving = false;
    }
}
