using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Orbits the camera around a look target: WASD free-pans (works even while
/// placing/dragging a device), middle-mouse-drag pans, Q/E step-rotate the yaw,
/// and scroll-wheel zoom also tilts the pitch — top-down when zoomed out,
/// leveling out toward the plane as you zoom in. Scroll is reserved for zoom;
/// holding R repurposes it for device rotation instead (see DragDropManager).
/// </summary>
public class IsometricCamera : MonoBehaviour
{
    public static IsometricCamera Instance { get; private set; }

    [Header("Initial Pose")]
    private float yawAngle      = 45f;
    // GamePlane's actual center — GameBootstrap.SetupPlayArea() never repositions
    // it off the origin, so this must stay in sync with that for rotation (Q/E)
    // to orbit cleanly around the plane instead of drifting off-center.
    private Vector3 lookTarget  = Vector3.zero;

    [Header("Zoom")]
    private float zoomSpeed     = 100f;
    [SerializeField] private float minDistance   = 8f;
    [SerializeField] private float maxDistance   = 60f;
    // Starting distance on scene load — a bit closer than maxDistance so the
    // opening view isn't as far back as a fully-zoomed-out survey shot.
    [SerializeField] private float startDistance = 50f;
    [SerializeField] private float zoomSmoothing = 10f;

    [Header("Zoom-linked Pitch")]
    // Zooming doesn't just dolly the camera — it tilts it too: fully zoomed out
    // sits at a top-down survey angle, fully zoomed in levels out to roughly the
    // plane's own height, so getting close to a device reads like walking up to it.
    [SerializeField] private float topPitch    = 60f; // pitch at maxDistance (start)
    [SerializeField] private float groundPitch = 15f; // pitch at minDistance

    [Header("Pan")]
    [SerializeField] private float panSpeed      = 0.04f; // world-units per pixel (middle-mouse drag)
    // WASD free-pan — deliberately not gated by the drag/highlight lock below,
    // so players can still reposition the view while placing a device instead
    // of being stuck with whatever framing they had when the drag started.
    [SerializeField] private float keyboardPanSpeed = 8f; // world-units per second

    [Header("Rotate")]
    // Pitch/distance never change here — only yaw steps, so players can walk the
    // camera around a device's sides (for cable ports) without losing the isometric look.
    [SerializeField] private float rotateStepDegrees = 45f;

    [Header("Entrance")]
    // On scene load the camera starts zoomed in tight and eases out to
    // startDistance — the play area's own "reveal" shot, echoing the menu's
    // slide-in entrances (see SlidePanel). Runs once, via its own coroutine
    // rather than the live zoomSmoothing above, so it plays at a deliberate,
    // fixed pace regardless of how snappy scroll-zoom is tuned.
    [SerializeField] private float introDistance = 14f;
    [SerializeField] private float introDuration  = 1.1f;

    private bool introPlaying;

    private Vector2 lastMousePos;
    private bool isPanning;
    private float orbitDistance;
    private float targetDistance;
    private Vector3 currentLookTarget;
    private Vector3 targetLookTarget;
    private float currentPitch;
    private float currentYaw;
    private float targetPitch;
    private float targetYaw;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        currentLookTarget = lookTarget;
        targetLookTarget  = lookTarget;
        // Resting pose once the intro finishes — near-zoomed-out at the
        // top-down survey angle; scrolling in gradually levels the camera
        // out toward groundPitch from there (see HandleZoomInput).
        targetDistance    = startDistance;
        targetPitch       = PitchForDistance(startDistance);
        currentYaw        = yawAngle;
        targetYaw         = yawAngle;

        // Start zoomed in tight and ease out to the resting pose above —
        // see IntroZoomOut.
        orbitDistance = introDistance;
        currentPitch  = PitchForDistance(introDistance);
        ApplyPose();

        introPlaying = true;
        StartCoroutine(IntroZoomOut());
    }

    IEnumerator IntroZoomOut()
    {
        float fromDistance = orbitDistance;
        float fromPitch    = currentPitch;
        float t = 0f;
        while (t < introDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / introDuration), 3f); // ease-out cubic
            orbitDistance = Mathf.Lerp(fromDistance, targetDistance, p);
            currentPitch  = Mathf.LerpAngle(fromPitch, targetPitch, p);
            ApplyPose();
            yield return null;
        }
        orbitDistance = targetDistance;
        currentPitch  = targetPitch;
        ApplyPose();
        introPlaying = false;
    }

    float PitchForDistance(float distance)
    {
        float t = Mathf.InverseLerp(minDistance, maxDistance, distance);
        return Mathf.Lerp(groundPitch, topPitch, t);
    }

    void ApplyPose()
    {
        transform.rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
        transform.position = currentLookTarget - transform.forward * orbitDistance;
    }

    void Update()
    {
        bool cliOpen = (CLIPanelManager.Instance != null && CLIPanelManager.Instance.IsOpen) ||
                       (SettingsPanelManager.Instance != null && SettingsPanelManager.Instance.IsOpen);
        // A highlighted (selected) placed device can be scroll-rotated in place —
        // lock mouse-pan/rotate entirely so it doesn't drift out from under the edit.
        bool deviceHighlighted = DragDropManager.Instance != null && DragDropManager.Instance.HasHighlightedDevice;
        // Placing/dragging a device or table locks mouse-pan/rotate too, so the
        // framing doesn't drift on its own mid-drag; WASD/zoom stay free (see
        // below) so the player can still reposition the view manually while placing.
        bool dragging = (DragDropManager.Instance != null && DragDropManager.Instance.IsDragging) ||
                         (TableManager.Instance != null && TableManager.Instance.IsDragging);
        bool mouseInputLocked = cliOpen || deviceHighlighted || dragging;

        // Zoom and keyboard pan stay available even mid-drag/highlight — only
        // a full-screen modal (the CLI, which wants W/A/S/D and scroll for
        // itself, or the Settings dialog) shuts them off.
        if (!cliOpen)
        {
            HandleZoomInput();
            HandleKeyboardPanInput();
        }

        if (!mouseInputLocked)
        {
            HandlePanInput();
            HandleRotateInput();
        }

        // While the intro plays it owns orbitDistance/currentPitch exclusively
        // (see IntroZoomOut) — skipping the regular lerp here stops it from
        // fighting the intro's own, slower-paced easing every frame.
        if (!introPlaying)
            ApplyLerp();
    }

    void HandleZoomInput()
    {
        // Holding R repurposes scroll for free device rotation instead
        // (DragDropManager.HandleFreeRotation) — zoom must not fight it.
        if (Keyboard.current != null && Keyboard.current.rKey.isPressed) return;

        // Scrolling over UI (e.g. the sidebar button list) must not also zoom the scene.
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) >= 0.01f)
        {
            // New Input System reports ~120 units per scroll notch; normalize to 1 notch = 1 step
            scroll /= 120f;
            targetDistance = Mathf.Clamp(targetDistance - scroll * zoomSpeed, minDistance, maxDistance);
            targetPitch    = PitchForDistance(targetDistance);
        }
    }

    void HandleKeyboardPanInput()
    {
        if (Keyboard.current == null) return;

        float x = 0f, y = 0f;
        if (Keyboard.current.dKey.isPressed) x += 1f;
        if (Keyboard.current.aKey.isPressed) x -= 1f;
        if (Keyboard.current.wKey.isPressed) y += 1f;
        if (Keyboard.current.sKey.isPressed) y -= 1f;
        if (x == 0f && y == 0f) return;

        // Same camera-relative right/forward basis as HandlePanInput, but applied
        // directly (not inverted) — pressing W should move the view forward, not
        // drag the world toward you the way a middle-mouse grab-pan does.
        Vector3 right   = transform.right;
        right.y = 0; right.Normalize();
        Vector3 forward = Vector3.Cross(right, Vector3.up);

        Vector3 moveDelta = (right * x + forward * y).normalized * keyboardPanSpeed * Time.deltaTime;
        targetLookTarget += moveDelta;
    }

    void HandlePanInput()
    {
        // Middle mouse button to pan
        if (Mouse.current.middleButton.wasPressedThisFrame)
        {
            isPanning = true;
            lastMousePos = Mouse.current.position.ReadValue();
        }

        if (Mouse.current.middleButton.wasReleasedThisFrame)
            isPanning = false;

        if (!isPanning) return;

        Vector2 currentPos = Mouse.current.position.ReadValue();
        Vector2 delta = currentPos - lastMousePos;
        lastMousePos = currentPos;

        // Pan in camera-local right/up projected onto the XZ plane
        Vector3 right   = transform.right;
        right.y = 0; right.Normalize();
        Vector3 forward = Vector3.Cross(right, Vector3.up);

        Vector3 panDelta = (right * delta.x + forward * delta.y) * panSpeed;
        targetLookTarget -= panDelta;
    }

    void HandleRotateInput()
    {
        // Q/E step the view 90° around the same fixed pitch/distance — enough to see
        // every side of a device (and its ports) without breaking the isometric angle.
        if (Keyboard.current == null) return;

        if (Keyboard.current.qKey.wasPressedThisFrame)
            targetYaw -= rotateStepDegrees;
        else if (Keyboard.current.eKey.wasPressedThisFrame)
            targetYaw += rotateStepDegrees;
    }

    void ApplyLerp()
    {
        float t = Time.deltaTime * zoomSmoothing;
        orbitDistance     = Mathf.Lerp(orbitDistance, targetDistance, t);
        currentLookTarget = Vector3.Lerp(currentLookTarget, targetLookTarget, t);
        currentPitch      = Mathf.LerpAngle(currentPitch, targetPitch, t);
        currentYaw        = Mathf.LerpAngle(currentYaw, targetYaw, t);

        transform.rotation  = Quaternion.Euler(currentPitch, currentYaw, 0f);
        transform.position  = currentLookTarget - transform.forward * orbitDistance;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public Vector3 TargetLookTarget => targetLookTarget;
    public float   TargetDistance   => targetDistance;
    public float   TargetPitch      => targetPitch;
    public float   TargetYaw        => targetYaw;
    public float   GroundPitch      => groundPitch; // pitch at max zoom-in (minDistance)
    public float   MinDistance      => minDistance;  // closest the player can zoom in
    public float   MaxDistance      => maxDistance;  // farthest the player can zoom out

    /// <summary>Smoothly moves the camera's orbit target, distance, and look angle (e.g. to focus on a device).</summary>
    public void FocusOn(Vector3 newLookTarget, float distance, float pitch, float yaw)
    {
        targetLookTarget = newLookTarget;
        targetDistance   = distance;
        targetPitch      = pitch;
        targetYaw        = yaw;
    }
}
