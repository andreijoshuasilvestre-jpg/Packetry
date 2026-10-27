using UnityEngine;

/// <summary>
/// World-space "look here" beacon — a Warframe star-chart-node-style pulse,
/// plus a small pixel-art mouse hovering just above the target, pressing the
/// button the player should use there (see MouseGesture: a left/right click,
/// press-and-hold where a drag starts, let-go where it ends). Each press is
/// timed to send out one of the pulse's rings, so the spot reads as "click
/// here" rather than a bare "look here" — the pulse is still what actually
/// draws the eye.
///
/// Two pulse styles, chosen per Show() call:
///  - Generic (no device given): two flat circular rings on the ground/table
///    surface, expanding outward and fading — used for spots that aren't a
///    real object yet (a table corner, a not-yet-placed device's drop hint).
///  - Device echo (a NetworkDevice given): a dim translucent full copy of
///    that device's own 3D model (built fresh via DeviceModelBuilder,
///    positioned/rotated to match the real device, scaled outward from its
///    true size while fading), PLUS a crisp wireframe box tracing its live
///    bounds that expands and fades the same way — the box read is what
///    actually sells "here's the object", the dim fill just keeps its shape
///    legible underneath.
///
/// Billboards the mouse to face the main camera every frame — the isometric
/// camera's yaw/pitch/zoom are all player-adjustable (see IsometricCamera),
/// so a fixed rotation would drift out of alignment the moment the player
/// rotated or zoomed. Runs in LateUpdate (not Update) so the camera read here
/// is always the transform that's about to be rendered this frame, not
/// whatever it was last frame — IsometricCamera moves the camera in its own
/// Update(), and Unity doesn't guarantee execution order between two
/// different scripts' Update() calls.
///
/// Every part's collider is removed (or never added) so nothing here ever
/// blocks the ground/table raycasts DragDropManager/TableManager read
/// clicks from.
/// </summary>
public class WorldPulseMarker : MonoBehaviour, IScreenOccupant
{

    // Vivid yellow by default (general "drop/click here" marker) — saturated
    // rather than a warm-gold tint specifically so it reads clearly against
    // the light-colored ground/table plane instead of blending into it.
    // Callers that want a visually distinct marker for a different kind of
    // target (see TutorialManager: table corners get a different color than
    // device-drop spots) call SetColor(), and ResetColor() puts this default
    // back.
    [SerializeField] private Color defaultColor = new Color(1f, 0.92f, 0.05f, 1f);
    private Color color;

    // Pushes the rendered color's brightness above 1.0 so URP's Bloom (see
    // DefaultVolumeProfile.asset, wired up by GameBootstrap.SetupCamera)
    // picks it up and gives the marker a genuine glow instead of a flat
    // color swatch — same technique and constant as HologramMaterial's own
    // GlowMultiplier, reused directly here so every glowing effect in the
    // game is calibrated the same way rather than drifting apart.
    const float GlowMultiplier = HologramMaterial.GlowMultiplier;

    // A gentler boost than the mouse button's own GlowMultiplier — the pulse (a
    // ring or a whole scaled-up device echo) covers far more screen area
    // than the small mouse glyph, so the same 2.2x bloom boost at that scale
    // read as overwhelming/eye-straining rather than just eye-catching.
    // NOTE: pairing a low multiplier with a low max-alpha doesn't just look
    // calmer — Bloom reacts to the rendered pixel AFTER alpha blending, so
    // that combination dilutes the pulse below the bloom threshold entirely
    // and it stops rendering as anything visible. Keep both this and the
    // various maxAlpha fields in the same ballpark when tuning.
    const float PulseGlowMultiplier = 1.6f;

    [Header("Mouse")]
    [SerializeField] private float pulseSpeed = 2.2f;
    [SerializeField] private float minAlpha = 0.8f, maxAlpha = 1f;
    [SerializeField] private float mouseSize = 0.9f;    // world-space glyph height
    [SerializeField] private float hoverHeight = 0.3f;  // clearance above the target point, measured to the mouse's BOTTOM edge (see LateUpdate)
    [SerializeField] private float bobHeight = 0.03f;   // idle vertical bob amplitude, world units — small, the press is the real motion
    [SerializeField] private float pressDip = 0.11f;    // how far the mouse pushes down toward the target while its button is pressed
    // The body wears the marker color with a modest bloom boost (a pale body
    // washed out against the light floor). The gesture's button is a darker
    // shade of it between presses — so it already says WHICH button — and
    // flares white-hot, with the full GlowMultiplier, on each press.
    [SerializeField] private float bodyGlow = 1.5f;
    [SerializeField, Range(0f, 1f)] private float buttonIdleShade = 0.5f;

    [Header("Generic pulse rings (no device)")]
    [SerializeField] private float ringMaxDiameter = 1.9f;
    [SerializeField] private float ringMaxAlpha = 0.85f;
    [SerializeField] private float ringLoopDuration = 1.8f; // seconds for one pulse to expand fully and fade out — slow, calm cadence rather than a fast flicker
    [SerializeField] private float ringYOffset = 0.02f;     // lifted just clear of the surface to avoid z-fighting

    [Header("Device echo — fill")]
    // Toned down from an earlier pass (was 1.9/0.85-ish) — the dim fill is
    // just there to keep the device's shape legible; the border lines below
    // are what should actually catch the eye.
    [SerializeField] private float ghostMaxScale = 1.3f;
    [SerializeField] private float ghostMaxAlpha = 0.28f;

    [Header("Device echo — border lines")]
    // A crisp wireframe box traced around the device's own live bounds,
    // expanding and fading in lockstep with the fill — the actual "here's
    // the object" read, since a dim dupe of the geometry alone reads as
    // muddy at low alpha.
    [SerializeField] private float borderMaxAlpha = 0.85f;
    [SerializeField] private float borderWidth = 0.025f;
    [SerializeField] private float borderExpand = 1.18f; // how much larger than the device's own bounds the box grows to by the end of its fade

    private const int PulseCount = 2; // staggered in phase so one is always mid-pulse

    // ── Spawn-in ──────────────────────────────────────────────────────────────
    // The mouse drops in from above and eases to rest instead of just
    // appearing in place, and its tint sweeps from a construction-blueprint
    // blue into its real target color as it lands — the same "blueprint
    // hardening into something solid" read ConstructionRevealEffect gives a
    // freshly-placed device, applied here as a direct tint/position blend on
    // this quad's own material instead of that system's shader-swap. This
    // marker is shown/hidden repeatedly across many tutorial steps (unlike a
    // device, which only ever gets built once), so a lightweight blend that
    // can safely restart on every Show() call is a better fit than reusing a
    // one-shot effect built around an object that's never reused.
    [Header("Spawn")]
    [SerializeField] private float dropHeight = 2.2f;    // world units above its resting height the mouse starts at
    [SerializeField] private float dropDuration = 0.45f; // seconds for the drop + blueprint-to-solid tint sweep
    private static readonly Color BlueprintTint = new Color(0.15f, 0.45f, 1f, 1f); // same blue family as ConstructionRevealEffect's default fill

    private Transform quad;
    private Renderer rend;
    private MaterialPropertyBlock block;
    // The lit-button overlay — a child of the body quad (so it shares its
    // position/rotation/scale exactly), its texture swapped per gesture.
    private Renderer buttonRend;
    private Material buttonMat;
    private MaterialPropertyBlock buttonBlock;
    private MouseGesture gesture;

    // Generic circular rings — always built, only shown when not in device mode.
    private Transform[] rings;
    private Renderer[] ringRends;
    private MaterialPropertyBlock ringBlock;

    // Device-echo ghosts — built fresh per Show() call for whichever device
    // is targeted (see BuildDeviceGhosts); torn down whenever the marker
    // switches back to a generic target or picks a new device.
    private GameObject[] deviceGhosts = new GameObject[PulseCount];
    private Material[] deviceGhostMats = new Material[PulseCount];

    // Border-line wireframes — built once in Awake (unlike the ghosts, a box
    // outline doesn't need per-device-type geometry, just live bounds), only
    // shown in device mode.
    private LineRenderer[] borderLines;
    // A wider black line under each border line — the same dark rim the
    // mouse/ring glyphs carry (see MarkerGlyphs), so the wireframe still
    // reads against a bright device or floor.
    private LineRenderer[] borderOutlines;
    [SerializeField] private float borderOutlineWidthMul = 2.6f;

    private bool deviceMode;

    private Camera cam;
    private Vector3 basePos; // mouse/ring anchor — a device's VisualTopPoint(), or a raw ground/table point
    private float spawnTime;
    // Set by Show() when pointing at a device — re-read every LateUpdate (see
    // TrackDevice) so the whole marker (mouse anchor + device echo position/
    // rotation) follows it live instead of freezing at wherever it was the
    // instant Show() was called, e.g. while the player is mid-drag on it.
    private NetworkDevice trackedDevice;

    void Awake()
    {
        color = defaultColor;
        cam = Camera.main;

        var quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quadGo.name = "Mouse";
        quadGo.transform.SetParent(transform, false);
        Destroy(quadGo.GetComponent<Collider>()); // must never intercept placement raycasts

        rend = quadGo.GetComponent<Renderer>();
        rend.sharedMaterial = MakeMaterial(Mouse.Body.texture);
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        quad = quadGo.transform;
        quad.localScale = Vector3.one * (mouseSize / Mouse.Span);
        block = new MaterialPropertyBlock();

        var buttonGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        buttonGo.name = "MouseButton";
        buttonGo.transform.SetParent(quad, false);
        Destroy(buttonGo.GetComponent<Collider>());
        buttonRend = buttonGo.GetComponent<Renderer>();
        buttonMat = MakeMaterial(Mouse.LeftButton.texture);
        // Coplanar with the body, and Sprites/Default doesn't write depth — a
        // later queue is what keeps it drawn on top instead of flickering.
        buttonMat.renderQueue = rend.sharedMaterial.renderQueue + 1;
        buttonRend.sharedMaterial = buttonMat;
        buttonRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        buttonBlock = new MaterialPropertyBlock();

        rings = new Transform[PulseCount];
        ringRends = new Renderer[PulseCount];
        for (int i = 0; i < PulseCount; i++)
        {
            var ringGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ringGo.name = $"PulseRing{i}";
            ringGo.transform.SetParent(transform, false);
            ringGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // lie flat on the surface
            ringGo.transform.localPosition = Vector3.up * ringYOffset;
            Destroy(ringGo.GetComponent<Collider>());

            var r = ringGo.GetComponent<Renderer>();
            r.sharedMaterial = MakeMaterial(GetRingTexture());
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rings[i] = ringGo.transform;
            ringRends[i] = r;
        }
        ringBlock = new MaterialPropertyBlock();

        borderLines = new LineRenderer[PulseCount];
        borderOutlines = new LineRenderer[PulseCount];
        for (int i = 0; i < PulseCount; i++)
        {
            // Outline first, one sorting order below its colored line, so the
            // two coincident transparent lines always draw black-under-yellow
            // instead of flickering between orders.
            borderOutlines[i] = MakeBorderLine($"DeviceBorderOutline{i}", borderWidth * borderOutlineWidthMul, 0);
            borderLines[i] = MakeBorderLine($"DeviceBorder{i}", borderWidth, 1);
        }

        gameObject.SetActive(false);
    }

    LineRenderer MakeBorderLine(string name, float width, int sortingOrder)
    {
        var lineGo = new GameObject(name);
        lineGo.transform.SetParent(transform, false);
        var lr = lineGo.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = false;
        lr.positionCount = BoxWireframePath.Length;
        lr.widthMultiplier = width;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.material = MakeMaterial(null);
        lr.sortingOrder = sortingOrder;
        lr.gameObject.SetActive(false);
        return lr;
    }

    static Material MakeMaterial(Texture2D tex)
    {
        // Sprites/Default: simple, always-transparent-capable, double-sided,
        // unlit shader — same fallback CableManager/Cable already lean on
        // for line materials, and works equally well tinting a real 3D
        // device mesh (see MakeGhostMaterial) or a LineRenderer as it does a
        // flat quad.
        Shader shader = Shader.Find("Sprites/Default");
        return new Material(shader) { mainTexture = tex };
    }

    // Untextured version of the same shader — solid white (tinted via
    // .color) rather than sampling one of the flat-quad textures below, used
    // to skin an entire instantiated device model as one uniform glowing
    // silhouette.
    static Material MakeGhostMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        return new Material(shader);
    }

    // Pixel-art, black-outlined glyphs (see MarkerGlyphs) — the mouse drawn at
    // the ping badges' own world-space cell size. Its texture carries a
    // 1-cell outline ring around the glyph, so the quad is drawn
    // 1 / Mouse.Span larger to keep the glyph itself at mouseSize tall; it's
    // centered and fills the glyph's height, so its bottom edge sits
    // mouseSize/2 below the quad's center.
    static MouseGlyph Mouse => MarkerGlyphs.WorldMouse;

    // Ring band — the expanding pulse itself, scaled/faded per instance
    // rather than baked as concentric rings in the texture. A bold 3-cell
    // stroke (closer to Warframe's own star-chart node rings than a
    // hairline), black-rimmed on both edges so it still reads on a bright
    // table or floor, where the yellow band alone washed out.
    static Texture2D GetRingTexture() => MarkerGlyphs.RingTexture;

    /// <summary>Shows the marker at a world position — pulse centered on it, mouse
    /// hovering just above it acting out `gesture`. worldPos is the mouse's own anchor
    /// (a device's VisualTopPoint(), or a raw ground/table spot) — pass
    /// pointingAtDevice too when it's an already-placed device, so the pulse itself
    /// becomes a dim echo of that device's real model plus a fading wireframe border
    /// around its live bounds, instead of a generic circle.</summary>
    public void Show(Vector3 worldPos, NetworkDevice pointingAtDevice = null, MouseGesture gesture = MouseGesture.Click)
    {
        basePos = worldPos;
        transform.position = basePos; // initial anchor; TrackDevice() below keeps it live if pointingAtDevice moves
        spawnTime = Time.time; // drives both the mouse's drop-in and the pulse's fresh cycle
        trackedDevice = pointingAtDevice;
        this.gesture = gesture;
        buttonMat.mainTexture = Mouse.Button(gesture).texture;

        deviceMode = pointingAtDevice != null;
        if (deviceMode)
        {
            BuildDeviceGhosts(pointingAtDevice);
            for (int i = 0; i < PulseCount; i++)
            {
                rings[i].gameObject.SetActive(false);
                borderLines[i].gameObject.SetActive(true);
                borderOutlines[i].gameObject.SetActive(true);
            }
        }
        else
        {
            DestroyDeviceGhosts();
            for (int i = 0; i < PulseCount; i++)
            {
                rings[i].gameObject.SetActive(true);
                borderLines[i].gameObject.SetActive(false);
                borderOutlines[i].gameObject.SetActive(false);
            }
        }

        gameObject.SetActive(true);
    }

    public void Hide() => gameObject.SetActive(false);

    public void SetColor(Color c) => color = c;
    public void ResetColor() => color = defaultColor;

    // ── Screen occupancy ──────────────────────────────────────────────────────
    // Registered while shown, so the tutorial callout (see TutorialUIManager's
    // placement solver) moves off a marker instead of covering the exact spot
    // it's telling the player to click/drop on.

    public OccupantLayer Layer => OccupantLayer.WorldGuide;

    void OnEnable() => ScreenOccupancy.Register(this);
    void OnDisable() => ScreenOccupancy.Unregister(this);

    public void CollectScreenRects(System.Collections.Generic.List<Rect> into)
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        // The ring's footprint on the surface (most of its visible expansion —
        // it's nearly faded out by full diameter) up to the mouse's top, plus
        // the tracked device's own bounds in device mode.
        float r = ringMaxDiameter * 0.35f;
        var box = new Bounds(basePos, Vector3.zero);
        box.Encapsulate(basePos + new Vector3(-r, 0f, -r));
        box.Encapsulate(basePos + new Vector3(r, hoverHeight + bobHeight + mouseSize, r));
        if (deviceMode && trackedDevice != null) box.Encapsulate(ComputeDeviceBounds(trackedDevice));

        bool any = false;
        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        foreach (var corner in BoxCorners(box))
        {
            Vector3 sp = cam.WorldToScreenPoint(corner);
            if (sp.z <= 0f) continue;
            any = true;
            min = Vector2.Min(min, sp);
            max = Vector2.Max(max, sp);
        }
        if (any) into.Add(Rect.MinMaxRect(min.x, min.y, max.x, max.y));
    }

    // ── Device echo ───────────────────────────────────────────────────────────

    // Builds PulseCount full copies of the target device's own model (fresh
    // via DeviceModelBuilder each time — cheap, a handful of primitive
    // GameObjects, same cost as placing a real device), stripped of every
    // gameplay component and skinned as one uniform dim silhouette per copy
    // so each can fade independently. Position/rotation set here is just the
    // initial placement — UpdateDevicePulse re-syncs both from the live
    // device every frame afterward (see TrackDevice).
    void BuildDeviceGhosts(NetworkDevice device)
    {
        DestroyDeviceGhosts();

        for (int i = 0; i < PulseCount; i++)
        {
            GameObject ghost = BuildDeviceVisual(device.deviceType);
            if (ghost == null) continue;

            ghost.name = $"SonarGhost{i}";
            ghost.transform.SetParent(transform, false);
            ghost.transform.SetPositionAndRotation(device.transform.position, device.transform.rotation);

            var mat = MakeGhostMaterial();
            foreach (var r in ghost.GetComponentsInChildren<Renderer>())
                r.sharedMaterial = mat;

            deviceGhosts[i] = ghost;
            deviceGhostMats[i] = mat;
        }
    }

    void DestroyDeviceGhosts()
    {
        for (int i = 0; i < PulseCount; i++)
        {
            if (deviceGhosts[i] != null) Destroy(deviceGhosts[i]);
            deviceGhosts[i] = null;
            if (deviceGhostMats[i] != null) Destroy(deviceGhostMats[i]);
            deviceGhostMats[i] = null;
        }
    }

    // A fresh, fully-built device model (same construction path a real
    // placed device goes through — prefab-backed where available, procedural
    // fallback otherwise) with every non-visual component stripped, so it's
    // purely decorative: no NetworkDevice/port/cable state, no physics, no
    // collider (must never intercept placement raycasts, same as every other
    // part of this marker).
    static GameObject BuildDeviceVisual(DeviceType type)
    {
        GameObject go = type switch
        {
            DeviceType.PC     => DeviceModelBuilder.BuildPC(),
            DeviceType.Switch => DeviceModelBuilder.BuildSwitch(),
            DeviceType.Router => DeviceModelBuilder.BuildRouter(),
            _ => null,
        };
        if (go == null) return null;

        // DestroyImmediate, not Destroy: Destroy() only marks a component for
        // removal at the END of the current frame, so a plain Destroy() here
        // would leave a fully real, save-eligible NetworkDevice (complete
        // with Collider/Rigidbody) sitting in the scene for the rest of this
        // frame. BuildDeviceGhosts() rebuilds these ghosts on every
        // Level2Guide/Level3Guide stage transition, which fires on the same
        // TaskManager.OnTasksChanged event AutoSaveManager listens to for its
        // own SaveNow() — so a save landing later in that same event dispatch
        // (or any other same-frame save trigger) would call
        // SceneStateCapture.Capture(), which finds every live NetworkDevice
        // with FindObjectsByType and has no way to tell a decorative echo
        // apart from a real placed device. That's how a "sonar ping" ghost
        // got permanently saved (and, on every later reload, faithfully
        // restored) as an indistinguishable extra PC stacked exactly on top
        // of the real one it was echoing. These components are being
        // stripped off an object Instantiate()'d a few lines up in this same
        // call — nothing else can hold a reference to them yet — so removing
        // them synchronously here is safe and closes the race entirely.
        foreach (var c in go.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) DestroyImmediate(rb);
        foreach (var nd in go.GetComponentsInChildren<NetworkDevice>()) DestroyImmediate(nd);

        return go;
    }

    // Corner-index path that traces all 12 edges of a box in a single
    // continuous polyline (LineRenderer only ever draws segments between
    // CONSECUTIVE points, so any pair here must be an actual box edge — a
    // handful of edges get revisited to keep the path continuous, which
    // just redraws over itself, harmlessly). Corners are indexed by bit
    // pattern (bit0=x, bit1=y, bit2=z), see BoxCorners.
    static readonly int[] BoxWireframePath = { 0, 1, 3, 2, 0, 4, 5, 1, 5, 7, 3, 7, 6, 2, 6, 4 };

    static Vector3[] BoxCorners(Bounds b)
    {
        Vector3 min = b.min, max = b.max;
        var c = new Vector3[8];
        for (int i = 0; i < 8; i++)
            c[i] = new Vector3((i & 1) != 0 ? max.x : min.x, (i & 2) != 0 ? max.y : min.y, (i & 4) != 0 ? max.z : min.z);
        return c;
    }

    // Re-anchors to the live device every frame instead of the position/
    // rotation it happened to be at when Show() was called — otherwise
    // dragging the device the marker is pointing at (e.g. the tutorial's own
    // "drag your PC" step) would leave the pulse frozen behind at its old spot.
    void TrackDevice()
    {
        if (trackedDevice == null) return;

        basePos = trackedDevice.VisualTopPoint();
        transform.position = basePos;
        // Ghost/border position is synced per-instance inside
        // UpdateDevicePulse, which already iterates them every frame.
    }

    // LateUpdate, not Update — see class doc comment: IsometricCamera moves
    // the camera in its own Update(), so reading it here (after every
    // script's Update() has already run this frame) keeps the billboard
    // rotation from lagging a frame behind and reading as jitter while the
    // player pans/zooms.
    void LateUpdate()
    {
        TrackDevice();

        float since = Time.time - spawnTime;
        float dropT = Mathf.Clamp01(since / dropDuration);
        // Same ease-out curve ConstructionRevealEffect uses for its own
        // sweeps — ties this hand-built spawn to the same "fast start,
        // decelerating arrival" feel as every device's own build-in.
        float landed = 1f - (1f - dropT) * (1f - dropT);

        float t = Mathf.Sin(Time.time * pulseSpeed) * 0.5f + 0.5f;

        // One press per ring (see MarkerGlyphs.GesturePress) — the cycle is
        // the gap between two rings, measured from spawnTime the same way
        // LoopT is, so u = 0 is exactly when a fresh ring leaves. Held off
        // until the drop-in has landed, so the first press reads as a click
        // rather than part of the fall.
        float cycle = since / (ringLoopDuration / PulseCount);
        float press = dropT < 1f ? 0f : MarkerGlyphs.GesturePress(gesture, cycle - Mathf.Floor(cycle));

        // hoverHeight/bobHeight describe where the mouse's BOTTOM edge should
        // sit, not the quad's own center — quad.position is a flat quad's
        // geometric center, so half of its rendered size would otherwise hang
        // below that point and clip through the ground/table plane underneath.
        // Not scaled with a heartbeat like the old arrow was: the press dip
        // toward the target is the motion now.
        float restingY = hoverHeight + Mathf.Lerp(-bobHeight, bobHeight, t) - pressDip * press;
        float startY = restingY + dropHeight;
        float bottomY = Mathf.Lerp(startY, restingY, landed);
        quad.position = basePos + Vector3.up * (bottomY + mouseSize * 0.5f);
        quad.localScale = Vector3.one * (mouseSize / Mouse.Span); // glyph stays mouseSize tall; the extra is its outline ring

        // Blueprint-blue ghost hardening into the marker's real color as it
        // lands, and a touch more transparent while still "under
        // construction" — the same two-stage blueprint→solid read
        // ConstructionRevealEffect gives a freshly-placed device (see the
        // Spawn header comment for why this is a tint blend rather than that
        // effect's shader-swap).
        Color spawnTint = Color.Lerp(BlueprintTint, color, landed);
        float mouseAlpha = Mathf.Lerp(minAlpha, maxAlpha, t) * Mathf.Lerp(0.4f, 1f, landed);
        block.SetColor("_Color", new Color(spawnTint.r * bodyGlow, spawnTint.g * bodyGlow, spawnTint.b * bodyGlow, mouseAlpha));
        rend.SetPropertyBlock(block);

        Color button = Color.Lerp(spawnTint * buttonIdleShade, Color.white, press) * Mathf.Lerp(1f, GlowMultiplier, press);
        buttonBlock.SetColor("_Color", new Color(button.r, button.g, button.b, mouseAlpha));
        buttonRend.SetPropertyBlock(buttonBlock);

        // Billboard toward the camera so the mouse reads correctly regardless
        // of the player's current yaw/pitch/zoom. Looks AWAY from the camera:
        // a default Quad's visible face is its local -Z, and facing the camera
        // would show its back face — mirrored, which a symmetric arrow hid but
        // would swap which of the mouse's buttons is lit.
        if (cam == null) cam = Camera.main;
        if (cam != null)
            quad.rotation = Quaternion.LookRotation(quad.position - cam.transform.position, Vector3.up);

        // Two pulses staggered half a loop apart (phase offset 0.5) so
        // there's always at least one mid-expansion — the classic Warframe
        // star-chart node "sonar ping" read, rather than a single pulse
        // popping and resetting with a visible gap. Phase is measured from
        // spawnTime (not raw Time.time) and always grows from nothing, so
        // every Show() starts its own fresh cycle instead of appearing
        // already mid-pulse wherever the global clock happened to be.
        if (deviceMode) UpdateDevicePulse(since);
        else UpdateRingPulse(since);
    }

    void UpdateRingPulse(float since)
    {
        for (int i = 0; i < PulseCount; i++)
        {
            float loopT = LoopT(since, i);
            float diameter = Mathf.Lerp(0f, ringMaxDiameter, loopT);
            rings[i].localScale = new Vector3(diameter, diameter, diameter);

            float ringAlpha = Mathf.Lerp(ringMaxAlpha, 0f, loopT);
            ringBlock.SetColor("_Color", new Color(color.r * PulseGlowMultiplier, color.g * PulseGlowMultiplier, color.b * PulseGlowMultiplier, ringAlpha));
            ringRends[i].SetPropertyBlock(ringBlock);
        }
    }

    void UpdateDevicePulse(float since)
    {
        Bounds bounds = trackedDevice != null ? ComputeDeviceBounds(trackedDevice) : default;

        for (int i = 0; i < PulseCount; i++)
        {
            float loopT = LoopT(since, i);
            Color tint = new Color(color.r * PulseGlowMultiplier, color.g * PulseGlowMultiplier, color.b * PulseGlowMultiplier);

            // Fill — re-synced every frame (not just once when built) so the
            // echo tracks the real device live (see TrackDevice), grows
            // uniformly outward from nothing to ghostMaxScale, and stays dim
            // throughout (see the header comment: the border below is the
            // actual "here's the object" read, this just keeps its shape
            // legible underneath).
            if (deviceGhosts[i] != null)
            {
                if (trackedDevice != null)
                    deviceGhosts[i].transform.SetPositionAndRotation(trackedDevice.transform.position, trackedDevice.transform.rotation);

                float scale = Mathf.Lerp(0f, ghostMaxScale, loopT);
                deviceGhosts[i].transform.localScale = Vector3.one * scale;

                float ghostAlpha = Mathf.Lerp(ghostMaxAlpha, 0f, loopT);
                deviceGhostMats[i].color = new Color(tint.r, tint.g, tint.b, ghostAlpha);
            }

            // Border — a wireframe box traced around the device's own live
            // bounds, expanding a little past them and fading in lockstep
            // with the fill.
            if (trackedDevice != null)
            {
                float expand = Mathf.Lerp(1f, borderExpand, loopT);
                Bounds padded = new Bounds(bounds.center, bounds.size * expand);
                Vector3[] corners = BoxCorners(padded);

                var lr = borderLines[i];
                var outline = borderOutlines[i];
                for (int p = 0; p < BoxWireframePath.Length; p++)
                {
                    lr.SetPosition(p, corners[BoxWireframePath[p]]);
                    outline.SetPosition(p, corners[BoxWireframePath[p]]);
                }

                float borderAlpha = Mathf.Lerp(borderMaxAlpha, 0f, loopT);
                Color outlineColor = new Color(0f, 0f, 0f, borderAlpha);
                outline.startColor = outlineColor;
                outline.endColor = outlineColor;
                Color borderColor = new Color(tint.r, tint.g, tint.b, borderAlpha);
                lr.startColor = borderColor;
                lr.endColor = borderColor;
            }
        }
    }

    static Bounds ComputeDeviceBounds(NetworkDevice device)
    {
        var renderers = device.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(device.transform.position, Vector3.one * 0.5f);

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);
        return b;
    }

    float LoopT(float since, int index)
    {
        float phase = (since / ringLoopDuration) + (float)index / PulseCount;
        return phase - Mathf.Floor(phase); // 0..1, wraps every ringLoopDuration seconds
    }
}
