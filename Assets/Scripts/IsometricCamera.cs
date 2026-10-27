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
/// The on-screen zoom buttons (ViewControlsHud) zoom through ZoomBy, for a
/// trackpad with no wheel to scroll.
/// </summary>
public class IsometricCamera : MonoBehaviour
{
    public static IsometricCamera Instance { get; private set; }

    [Header("Initial Pose")]
    // Square-on, not the old 45° diagonal — tables and the devices on them
    // read straight across the screen when a level opens. Also where
    // ResetView returns to. A level that opens with a topology already in
    // place turns this to face it (see SetOpeningYaw).
    private float yawAngle      = 90f;
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
    // Pitch/distance never change here — only yaw steps (and the view re-centers
    // on the play area, see RotateStep), so players can walk the camera around
    // a device's sides (for cable ports) without losing the isometric look.
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
    // The frame this camera woke up on — nothing has rendered from it yet,
    // so SetOpeningYaw can still snap instead of visibly swinging round.
    private int awakeFrame;

    // Debounced disk flush for the saved zoom distance — scrolling fires many
    // notches in quick succession, so we only write to disk once it settles
    // (same reasoning as MakeSliderRow's pointer-up flush, adapted for a
    // continuous input that has no "release" event of its own).
    const float ZoomSaveDelay = 0.5f;
    private float zoomSaveTimer = -1f;

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
        awakeFrame = Time.frameCount;

        currentLookTarget = lookTarget;
        targetLookTarget  = lookTarget;
        // Resting pose once the intro finishes — near-zoomed-out at the
        // top-down survey angle; scrolling in gradually levels the camera
        // out toward groundPitch from there (see HandleZoomInput). Restores
        // the player's last saved zoom level when there is one, so the intro
        // eases into wherever they left off instead of always startDistance.
        float savedDistance = SaveManager.Settings.cameraZoomDistance;
        float restingDistance = savedDistance > 0f
            ? Mathf.Clamp(savedDistance, minDistance, maxDistance)
            : startDistance;
        targetDistance    = restingDistance;
        targetPitch       = PitchForDistance(restingDistance);
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
            t += AnimationSpeed.UnscaledDelta(AnimationKind.Level); // part of the level's entrance
            float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / introDuration), 3f); // ease-out cubic
            orbitDistance = Mathf.Lerp(fromDistance, targetDistance, p);
            currentPitch  = Mathf.LerpAngle(fromPitch, targetPitch, p);
            // Normally already there — but a SetOpeningYaw that arrives
            // mid-intro (a template topology merging in late) swings round
            // during the zoom-out instead of waiting for it to finish.
            currentYaw    = Mathf.LerpAngle(currentYaw, targetYaw, Time.unscaledDeltaTime * zoomSmoothing);
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

    /// <summary>A modal owns the view — the CLI (which wants W/A/S/D and
    /// scroll for itself), the Settings dialog, or an annotation waiting on its
    /// button. Shuts off every camera control.</summary>
    bool ModalOpen =>
        (CLIPanelManager.Instance != null && CLIPanelManager.Instance.IsOpen) ||
        InteractionMode.IsSuspended;

    /// <summary>Mouse pan and view rotation are locked: a modal is open, a
    /// highlighted (selected) placed device is being scroll-rotated in place
    /// (so it can't drift out from under the edit), or a device/table is being
    /// placed or dragged (so the framing doesn't drift on its own mid-drag).
    /// WASD and zoom stay free in the last two cases so the player can still
    /// reposition the view manually while placing.</summary>
    bool MouseInputLocked =>
        ModalOpen ||
        (DragDropManager.Instance != null && DragDropManager.Instance.HasHighlightedDevice) ||
        (DragDropManager.Instance != null && DragDropManager.Instance.IsDragging) ||
        (TableManager.Instance != null && (TableManager.Instance.IsDragging || TableManager.Instance.IsResizing));

    /// <summary>Whether the view can be turned right now — by Q/E or the
    /// on-screen turn buttons (ViewControlsHud), which follow the same locks.</summary>
    public bool CanRotate => !MouseInputLocked;

    /// <summary>Whether the view can be reset right now (see ResetView).</summary>
    public bool CanResetView => !ModalOpen;

    /// <summary>Whether the view can be zoomed right now — by the wheel or the
    /// on-screen zoom buttons. Only a modal stops it, same as the wheel (see
    /// Update): zooming stays free mid-drag/placement.</summary>
    public bool CanZoom => !ModalOpen;

    /// <summary>Room left to zoom in (true) or out (false) — the zoom buttons
    /// grey out at either end of the range.</summary>
    public bool CanZoomFurther(bool zoomIn) =>
        CanZoom && (zoomIn ? targetDistance > minDistance + 0.01f : targetDistance < maxDistance - 0.01f);

    /// <summary>Zooms by `amount` world units of orbit distance, positive in —
    /// scaled by the Zoom Speed setting like the wheel is.</summary>
    public void ZoomBy(float amount)
    {
        if (!CanZoom || amount == 0f) return;
        ApplyZoom(amount * SaveManager.Settings.cameraZoomSpeedMultiplier);
    }

    // The one place zoom changes the pose: distance, the pitch tied to it, and
    // the debounced save of the new zoom level.
    void ApplyZoom(float amount)
    {
        targetDistance = Mathf.Clamp(targetDistance - amount, minDistance, maxDistance);
        targetPitch    = PitchForDistance(targetDistance);
        zoomSaveTimer  = ZoomSaveDelay;
    }

    /// <summary>Turns the view one step. direction &lt; 0 swings the near side
    /// of the scene left (Q), &gt; 0 swings it right (E). Also glides the view
    /// back to the play area's center, so a turn after panning off to one side
    /// orbits the whole scene instead of swinging it out of frame. Zoom is kept.</summary>
    public void RotateStep(int direction)
    {
        if (!CanRotate || direction == 0) return;
        targetYaw       += Mathf.Sign(direction) * rotateStepDegrees;
        targetLookTarget = lookTarget;
    }

    /// <summary>Turns the opening view to `yaw` — e.g. to face a level's starting
    /// topology head-on (see GameBootstrap.FaceStartingTopology) — and makes it the
    /// angle ResetView returns to. Snaps while nothing has rendered yet (a topology
    /// built in the same frame as this camera); otherwise eases round, during the
    /// intro zoom-out too (a template scene merges in a frame or more late).</summary>
    public void SetOpeningYaw(float yaw)
    {
        yawAngle  = yaw;
        targetYaw = targetYaw + Mathf.DeltaAngle(targetYaw, yaw); // the short way round
        if (Time.frameCount == awakeFrame)
        {
            currentYaw = targetYaw;
            ApplyPose();
        }
    }

    /// <summary>Back to the opening framing — angle, zoom and position — for a
    /// player who's panned or turned themselves somewhere they can't read.</summary>
    public void ResetView()
    {
        if (!CanResetView) return;
        // Nearest equivalent of the opening yaw, so the reset swings the short
        // way round instead of unwinding every turn taken so far.
        targetYaw        = targetYaw + Mathf.DeltaAngle(targetYaw, yawAngle);
        targetLookTarget = lookTarget;
        targetDistance   = startDistance;
        targetPitch      = PitchForDistance(startDistance);
        zoomSaveTimer    = ZoomSaveDelay;
    }

    void Update()
    {
        bool modalOpen = ModalOpen;
        bool mouseInputLocked = MouseInputLocked;

        // Zoom and keyboard pan stay available even mid-drag/highlight — only
        // a modal (the CLI, which wants W/A/S/D and scroll for itself, the
        // Settings dialog, or an annotation waiting on its button) shuts them off.
        if (!modalOpen)
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

        if (zoomSaveTimer >= 0f)
        {
            zoomSaveTimer -= Time.deltaTime;
            if (zoomSaveTimer <= 0f)
                FlushZoomSave();
        }
    }

    /// <summary>Writes the current zoom distance into settings and flushes to disk —
    /// called once scrolling settles (see zoomSaveTimer) and again on teardown in
    /// case the player quits/leaves the scene mid-debounce.</summary>
    void FlushZoomSave()
    {
        zoomSaveTimer = -1f;
        SaveManager.Settings.cameraZoomDistance = targetDistance;
        SaveManager.Save();
    }

    void OnDestroy()
    {
        if (zoomSaveTimer >= 0f) FlushZoomSave();
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
            float effectiveZoomSpeed = zoomSpeed * SaveManager.Settings.cameraZoomSpeedMultiplier;
            ApplyZoom(scroll * effectiveZoomSpeed);
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

        float effectivePanSpeed = keyboardPanSpeed * SaveManager.Settings.cameraPanSpeedMultiplier;
        Vector3 moveDelta = (right * x + forward * y).normalized * effectivePanSpeed * Time.deltaTime;
        targetLookTarget = ClampToPlayArea(targetLookTarget + moveDelta);
    }

    /// <summary>Keeps a panned look target over the play area — the view can
    /// center on its very edge (a corner device) but never drift off into the
    /// void. Only player pans go through this; FocusOn framings are trusted.</summary>
    static Vector3 ClampToPlayArea(Vector3 point)
    {
        float half = DragDropManager.Instance != null ? DragDropManager.Instance.PlayAreaHalfExtent : 9f;
        point.x = Mathf.Clamp(point.x, -half, half);
        point.z = Mathf.Clamp(point.z, -half, half);
        return point;
    }

    void HandlePanInput()
    {
        // Middle mouse button to pan
        if (Mouse.current.middleButton.wasPressedThisFrame)
        {
            isPanning = true;
            lastMousePos = Mouse.current.position.ReadValue();
        }

        // isPressed rather than wasReleasedThisFrame: a release that happened
        // while mouse input was locked (see Update) never gets seen here, and
        // would leave the view panning after the button's long been let go.
        if (!Mouse.current.middleButton.isPressed)
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
        targetLookTarget = ClampToPlayArea(targetLookTarget - panDelta);
    }

    void HandleRotateInput()
    {
        // Q/E step the view 90° around the same fixed pitch/distance — enough to see
        // every side of a device (and its ports) without breaking the isometric angle.
        if (Keyboard.current == null) return;

        if (Keyboard.current.qKey.wasPressedThisFrame)
            RotateStep(-1);
        else if (Keyboard.current.eKey.wasPressedThisFrame)
            RotateStep(1);
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
