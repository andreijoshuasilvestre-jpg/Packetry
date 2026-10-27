using System.Collections;
using UnityEngine;

/// <summary>
/// Visual cable between two placed network devices.
/// Rendered as a true catenary curve — the exact shape a chain of fixed length
/// forms when hanging under gravity between two points — rather than an
/// approximated parabola. Each cable also has a small independent sway for a
/// physical feel.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class Cable : MonoBehaviour
{
    // Shared constants so CableManager can match the preview curve exactly.
    public const int   Segments       = 24;
    public const float SagFactor      = 0.18f; // target dip below the chord, as a fraction of cable length
    public const float MinSag         = 0.28f; // minimum dip for short cables (world units)
    public const float GroundClearance = 0.04f; // minimum Y above the ground plane
    public const float TableClearance  = 0.03f; // minimum Y above a tabletop surface it passes over

    // ── Device-body avoidance ────────────────────────────────────────────────
    // Table/ground clearance above only keeps a cable from sinking through the
    // floor/tabletop it passes over — it says nothing about a THIRD device's
    // own body sitting between the cable's two endpoints. AvoidDevices (below)
    // pushes a sample point fully out of any such device's footprint, picking
    // whichever escape is cheaper: stepping sideways past the nearest edge, or
    // lifting straight up above the device's top. Always resolved completely
    // (not blended by a soft weight) — see AvoidDevices' own doc comment for
    // why a partial blend can leave a point still inside the box.
    public const float DeviceClearance         = 0.05f; // gap above a device's top when routing over it
    public const float DeviceHorizontalMargin  = 0.08f; // gap outside a device's footprint when routing around it

    private NetworkDevice _deviceA;
    private NetworkDevice _deviceB;
    private int           _portA;
    private int           _portB;
    private LineRenderer  _lr;
    private float         _swayPhase;

    private const float SwayAmplitude = 0.038f;
    private const float SwayFrequency = 0.55f;

    [SerializeField] private Color highlightColor = Color.cyan;

    public Color BaseColor { get; private set; }

    // The plain, untinted color this cable started with (CableManager's
    // cableColor, or a baked cable's own serialized gray) — SetSubnetTint
    // reverts to this rather than a hardcoded gray so a highlight-colored or
    // custom-authored cable still has something sane to fall back to.
    private Color _plainColor;

    // ── Construction reveal (see ConstructionRevealEffect) ──────────────────
    // Both default to "fully built" so a normal cable's per-frame Refresh()
    // does no extra work — these only matter while a reveal is actively
    // playing. RevealProgress grows 0->1 from _deviceA's end toward _deviceB's
    // (deviceA is always the end the player clicked first when drawing the
    // cable, i.e. the natural "origin"), clamping how much of the curve is
    // actually drawn. SolidifyProgress then blends the whole line from
    // BlueprintColor to BaseColor once the full length is drawn.
    public float RevealProgress { get; set; } = 1f;
    public float SolidifyProgress { get; set; } = 1f;
    public Color BlueprintColor { get; set; } = new Color(0.04f, 0.16f, 0.75f);

    // ── Packet flow ──────────────────────────────────────────────────────────
    // Small subtle dots that trace the direction a real packet is currently
    // traveling — driven entirely by PacketManager.SetFlowing (one call per
    // frame per cable that actually has an in-flight Packet on it right now),
    // not by ambient port-active state. No real traffic on this cable this
    // frame == no dots; this is a "which way is it going" guide, not ambient
    // flavor for a link that merely exists.
    [Header("Packet Flow")]
    [SerializeField] private int   packetCount     = 2;
    [SerializeField] private float packetSpeed     = 1.0f;  // world units per second
    [SerializeField] private float packetThickness = 0.05f; // diameter — close to the wire's own visual width
    [SerializeField] private float packetLength    = 0.16f; // elongated along the direction of travel

    private bool  _flowActive;
    private bool  _flowReversed;
    private Color _flowTint = new Color(0.35f, 1f, 0.6f);

    /// <summary>Called by PacketManager once per frame per cable currently carrying
    /// an in-flight Packet — active/reversed/tint describe that packet's direction
    /// and subnet color. Cables with no in-flight packet this frame simply never
    /// get called and their dots go inactive on the next Update.</summary>
    public void SetFlowing(bool active, bool reversed = false, Color? tint = null)
    {
        _flowActive   = active;
        _flowReversed = reversed;
        if (tint.HasValue) _flowTint = tint.Value;
    }

    private static Material s_packetMaterial;
    private Transform[] _packets;
    private Renderer[]  _packetRenderers;
    private float[]     _packetT;

    // Accumulated once per frame in Refresh() (which already visits every
    // segment) rather than re-summed by every caller that needs it — both the
    // decorative dot loop below and PacketManager's real cross-cable packets
    // convert world speed to a t-step via this.
    private float _cachedPathLength;
    public  float PathLength => _cachedPathLength;

    public bool IsCarryingTraffic =>
        (_deviceA != null && _deviceA.IsPortActive(_portA)) ||
        (_deviceB != null && _deviceB.IsPortActive(_portB));

    // ── Subnet tint ───────────────────────────────────────────────────────────
    // Driven by SubnetVisualizer, not polled here — recomputing "what subnet
    // does this cable's segment belong to" needs a graph walk across the whole
    // topology (through switches, stopping at routers), which only makes sense
    // to do once centrally after something relevant actually changes (an
    // address, a router interface, a cable added/removed) rather than every
    // cable re-deriving it every frame.

    /// <summary>Overrides this cable's plain color with a subnet's tint (see
    /// SubnetVisualizer for which subnet a cable shows), or reverts to the
    /// original color when passed null (nothing at either end has an address).
    /// Applied immediately — unlike Refresh()'s reveal-time color blend, this
    /// cable is long past construction by the time a player addresses a device.</summary>
    public void SetSubnetTint(Color? tint)
    {
        BaseColor = tint ?? _plainColor;
        if (_lr == null) return;
        _lr.startColor = BaseColor;
        _lr.endColor   = BaseColor;
    }

    // ── Ping pulse ──────────────────────────────────────────────────────────
    // Temporary override independent of IsCarryingTraffic — used by
    // PingToolManager to flash a result along the exact cables a ping walked,
    // even across a switch-to-switch span that has no ambient traffic of its own.
    [Header("Ping Pulse")]
    [SerializeField] private float pulseDuration         = 2.2f;
    [SerializeField] private float pulseSpeedMultiplier  = 2.2f;
    [SerializeField] private float pulseJudderAmplitude  = 0.05f;

    private Coroutine _pulseRoutine;
    private bool      _pulseActive;
    private bool      _pulseSuccess;
    private static MaterialPropertyBlock s_packetPulseBlock;

    /// <summary>Flashes this cable's packets green-and-fast (success) or red-and-juddering
    /// (failure) for a few seconds, regardless of the cable's own ambient traffic state.</summary>
    public void SetPulse(bool success)
    {
        if (_pulseRoutine != null) StopCoroutine(_pulseRoutine);
        _pulseRoutine = StartCoroutine(PulseRoutine(success));
    }

    IEnumerator PulseRoutine(bool success)
    {
        _pulseActive  = true;
        _pulseSuccess = success;
        yield return new WaitForSeconds(AnimationSpeed.Seconds(AnimationKind.Packets, pulseDuration));
        _pulseActive = false;
        // No explicit property-block clear needed — UpdatePackets() sets it
        // fresh (back to the subnet tint) every visible frame regardless.
        _pulseRoutine = null;
    }

    // _deviceA/_deviceB (and _packets/_packetRenderers/_packetT) are plain
    // private fields (not [SerializeField]), so a cable baked into a
    // level-template scene (see LevelTemplateAuthoringWindow) comes back from
    // any scene load with them all null/empty — whether that scene got
    // merged additively into a real GameBootstrap play session or is being
    // played completely standalone (the template scene supports both; see
    // TemplatePreviewOnly). Left alone, Update()'s null-endpoint self-destroy
    // deletes it the very first frame either way. Self-repairing here in
    // Awake() — rather than depending on an external loader like
    // TemplateTopologyLoader to reach in and fix it — means a baked cable
    // works no matter how/where the scene it lives in ends up being played.
    // Harmless no-op for a normally-created cable too: CableManager.CreateCable
    // calls AddComponent<Cable>() (which runs this Awake() synchronously,
    // before ConnectPort or Initialize() have run) and only then calls
    // Initialize() itself, so at this point nothing has ports pointing at
    // this cable yet and TryRestoreLinks below simply finds nothing.
    void Awake()
    {
        _lr ??= GetComponent<LineRenderer>();
        // Captured before Initialize() (fresh cable) or TryRestoreLinks (baked
        // cable) can override it — either way this read happens first, so it's
        // always whatever color this cable actually started life with.
        _plainColor = _lr.startColor;
        if (_deviceA == null || _deviceB == null) TryRestoreLinks();
    }

    void TryRestoreLinks()
    {
        NetworkDevice devA = null, devB = null;
        int portA = -1, portB = -1;
        foreach (var d in FindObjectsByType<NetworkDevice>(FindObjectsInactive.Exclude))
            for (int i = 0; i < d.PortCount; i++)
                if (d.GetPortCable(i) == this)
                {
                    if (devA == null) { devA = d; portA = i; }
                    else { devB = d; portB = i; }
                }

        if (devA == null || devB == null) return;

        RestoreLinks(devA, portA, devB, portB);
        CableManager.Instance?.RegisterExistingCable(this);
    }

    /// <summary>Re-links this cable to its endpoint devices/ports — derives them
    /// from NetworkDevice.Port's own (correctly serialized) connectedDevice/cable
    /// fields, and re-points the packet arrays at the "Packet" child objects
    /// BuildPackets() already created at bake time instead of calling it again
    /// (which would create a second, duplicate set).</summary>
    public void RestoreLinks(NetworkDevice a, int portA, NetworkDevice b, int portB)
    {
        _deviceA = a;
        _portA   = portA;
        _deviceB = b;
        _portB   = portB;
        _lr    ??= GetComponent<LineRenderer>();

        int n = transform.childCount;
        var packets = new System.Collections.Generic.List<Transform>(n);
        for (int i = 0; i < n; i++)
        {
            var child = transform.GetChild(i);
            if (child.name == "Packet") packets.Add(child);
        }

        _packets         = packets.ToArray();
        _packetRenderers = new Renderer[_packets.Length];
        _packetT         = new float[_packets.Length];
        for (int i = 0; i < _packets.Length; i++)
        {
            _packetRenderers[i] = _packets[i].GetComponent<Renderer>();
            _packetT[i]         = _packets.Length > 0 ? (float)i / _packets.Length : 0f;
        }
    }

    public void Initialize(NetworkDevice a, int portA, NetworkDevice b, int portB, Material mat, float width, Color color)
    {
        _deviceA   = a;
        _portA     = portA;
        _deviceB   = b;
        _portB     = portB;
        _swayPhase  = Random.Range(0f, Mathf.PI * 2f);
        BaseColor   = color;
        _plainColor = color;

        _lr = GetComponent<LineRenderer>();
        _lr.positionCount    = Segments + 1;
        _lr.startWidth       = width;
        _lr.endWidth         = width;
        _lr.material         = mat;
        _lr.startColor       = color;
        _lr.endColor         = color;
        _lr.useWorldSpace    = true;
        _lr.textureMode      = LineTextureMode.Tile;
        _lr.numCornerVertices = 5;
        _lr.numCapVertices    = 5;
        _lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        BuildPackets();
        Refresh();
    }

    void Update()
    {
        if (_deviceA == null || _deviceB == null) { Destroy(gameObject); return; }

        // Nothing drawn yet (LevelBuildInEffect hasn't reached this cable) or
        // already cleared away (exit sequence) — hide the line and its dots
        // outright rather than collapsing it to a zero-length stub, whose
        // round end caps still render as a dot at the origin port.
        bool hidden = RevealProgress <= 0f;
        if (_lr.enabled == hidden) _lr.enabled = !hidden;

        Refresh();
        UpdatePackets(hidden);
        if (_hologramActive)
        {
            Color tint = hologramTint;
            tint.a = HologramMaterial.Pulse();
            HologramMaterial.ApplyTint(_hologramInstance, tint);
        }
    }

    void Refresh()
    {
        Vector3 a = _deviceA.GetPortWorldPoint(_portA);
        Vector3 b = _deviceB.GetPortWorldPoint(_portB);

        Vector3 flat      = new Vector3(b.x - a.x, 0f, b.z - a.z);
        float   horizDist = flat.magnitude;
        Vector3 horizDir  = horizDist > 0.0001f ? flat / horizDist : Vector3.forward;

        CatenaryParams cat = SolveCatenary(horizDist, b.y - a.y);

        // Lateral sway direction (perpendicular to cable in XZ plane)
        Vector3 swayDir = Vector3.Cross(horizDir, Vector3.up);
        if (swayDir.sqrMagnitude < 0.001f) swayDir = Vector3.right;
        else swayDir.Normalize();

        float swayOffset = Mathf.Sin(Time.time * SwayFrequency + _swayPhase) * SwayAmplitude;

        // Reference dip (chord height minus curve height) at the curve's own
        // lowest point — used to scale sway so it's strongest where the cable
        // actually hangs lowest and fades to zero at the anchored ends.
        float dipRef = cat.degenerate ? 0f
            : Mathf.Max(0.001f, Mathf.Lerp(a.y, b.y, 0.5f) - (a.y + CatenaryY(cat, horizDist * 0.5f)));

        _cachedPathLength = 0f;
        Vector3 prevP = default;
        for (int i = 0; i <= Segments; i++)
        {
            float t = (float)i / Segments;
            // Beyond the reveal front, collapse to the front's own position
            // instead of the segment's real one — the tail just disappears
            // rather than the line extending past where it's actually "drawn".
            if (t > RevealProgress) t = RevealProgress;

            Vector3 p;
            if (cat.degenerate)
            {
                p = Vector3.Lerp(a, b, t); // near-vertical span: a taut chain just hangs straight
            }
            else
            {
                float x = t * horizDist;
                p   = a + horizDir * x;
                p.y = a.y + CatenaryY(cat, x);                    // gravity pulls down along the true catenary
                float dip = Mathf.Lerp(a.y, b.y, t) - p.y;
                p += swayDir * (swayOffset * dip / dipRef);       // sway is proportional to local dip
            }

            p = ApplyClearance(p, _deviceA, _deviceB);
            _lr.SetPosition(i, p);

            if (i > 0) _cachedPathLength += Vector3.Distance(prevP, p);
            prevP = p;
        }

        if (RevealProgress < 1f || SolidifyProgress < 1f)
        {
            Color liveColor = Color.Lerp(BlueprintColor, BaseColor, SolidifyProgress);
            _lr.startColor = liveColor;
            _lr.endColor   = liveColor;
        }
    }

    /// <summary>World position of the cable's current reveal front — the point sparks should follow while it's being drawn in.</summary>
    public Vector3 GetRevealTipPoint() => _lr.GetPosition(Mathf.RoundToInt(Mathf.Clamp01(RevealProgress) * Segments));

    void BuildPackets()
    {
        // Not `??=` — that lowers to a raw CLR null check that misses a
        // "fake null" destroyed UnityEngine.Object (see DeviceModelBuilder.
        // EnsureMaterials for the full story: this static can be left
        // pointing at a Material that was created during Play Mode and then
        // destroyed the instant Play Mode stopped, e.g. when the Level
        // Template Editor tool bakes a scene right after exiting Play).
        if (s_packetMaterial == null) s_packetMaterial = MakePacketMaterial();

        _packets         = new Transform[packetCount];
        _packetRenderers = new Renderer[packetCount];
        _packetT         = new float[packetCount];
        for (int i = 0; i < packetCount; i++)
        {
            // Capsule instead of a sphere — a short rounded segment reads as a
            // bead of the wire itself sliding along it, not a ball floating
            // beside the cable. Default capsule mesh is height 2 / radius 0.5
            // along local Y at scale 1, so scale.y = packetLength/2 and
            // scale.x/z = packetThickness give the desired physical size.
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Packet";
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(packetThickness, packetLength * 0.5f, packetThickness);
            // DestroyImmediate outside Play Mode — the level-template Editor tool
            // calls Initialize() too, and plain Destroy() is a no-op (with a
            // console error) in edit mode.
            if (Application.isPlaying) Destroy(go.GetComponent<Collider>());
            else DestroyImmediate(go.GetComponent<Collider>());
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = s_packetMaterial;
            go.SetActive(false);

            _packets[i]         = go.transform;
            _packetRenderers[i] = renderer;
            _packetT[i]         = (float)i / packetCount; // evenly spaced along the cable
        }
    }

    void UpdatePackets(bool cableHidden)
    {
        bool visible = !cableHidden && (_flowActive || _pulseActive);

        if (!visible)
        {
            foreach (var packet in _packets)
                if (packet.gameObject.activeSelf) packet.gameObject.SetActive(false);
            return;
        }

        float pathLength = _cachedPathLength;
        if (pathLength < 0.0001f) return;

        float speedMul = _pulseActive ? pulseSpeedMultiplier : 1f;
        float step = packetSpeed * speedMul * AnimationSpeed.Delta(AnimationKind.Packets) / pathLength;

        s_packetPulseBlock ??= new MaterialPropertyBlock();

        for (int i = 0; i < _packets.Length; i++)
        {
            if (!_packets[i].gameObject.activeSelf) _packets[i].gameObject.SetActive(true);
            _packetT[i] = (_packetT[i] + step) % 1f;
            float sampleT = _flowReversed ? 1f - _packetT[i] : _packetT[i];
            Vector3 pos = SampleCurve(sampleT);

            // Orient along the curve's own tangent, in the actual direction of
            // travel, so the capsule reads as a segment of wire sliding along
            // the cable rather than a bead floating at a fixed angle.
            float aheadT = Mathf.Clamp01(sampleT + (_flowReversed ? -0.01f : 0.01f));
            Vector3 tangent = SampleCurve(aheadT) - pos;
            if (tangent.sqrMagnitude > 1e-8f)
                _packets[i].rotation = Quaternion.FromToRotation(Vector3.up, tangent.normalized);

            // A ping pulse briefly overrides the dots' own subnet-tint guide
            // color with a success/failure flash, independent of whatever real
            // packet (if any) is currently flowing on this cable.
            Color tint = _flowTint;
            if (_pulseActive)
            {
                if (!_pulseSuccess)
                {
                    Vector2 jitter = Random.insideUnitCircle * pulseJudderAmplitude;
                    pos += new Vector3(jitter.x, 0f, jitter.y);
                }
                tint = _pulseSuccess ? GameTheme.Success : GameTheme.Danger;
            }

            string prop = _packetRenderers[i].sharedMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
            s_packetPulseBlock.Clear();
            s_packetPulseBlock.SetColor(prop, tint);
            _packetRenderers[i].SetPropertyBlock(s_packetPulseBlock);

            _packets[i].position = pos;
        }
    }

    /// <summary>Samples a point along the already-computed curve segments (see Refresh).
    /// Public so PacketManager can position real cross-cable packets, not just
    /// this cable's own decorative dots.</summary>
    public Vector3 SampleCurve(float t)
    {
        float segF = t * Segments;
        int   i0   = Mathf.Clamp(Mathf.FloorToInt(segF), 0, Segments - 1);
        return Vector3.Lerp(_lr.GetPosition(i0), _lr.GetPosition(i0 + 1), segF - i0);
    }

    static Material MakePacketMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        var mat = new Material(shader);
        Color color = new Color(0.35f, 1f, 0.6f);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        else if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        return mat;
    }

    // ── Cable surface texture ───────────────────────────────────────────────
    // A small tileable grayscale texture multiplied over each cable's flat
    // color (via LineRenderer's start/end vertex color) to fake a round,
    // insulated-cable material out of the flat ribbon LineRenderer actually
    // draws: brighter in the middle / darker at the edges reads as a rounded
    // highlight, and per-pixel grain reads as a rubber/plastic surface. Built
    // once and shared by every cable and the connection preview.
    static Texture2D s_cableTexture;

    public static Texture2D GetCableTexture()
    {
        if (s_cableTexture != null) return s_cableTexture;

        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name       = "CableTexture",
            wrapMode   = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
        };

        var rng    = new System.Random(12345); // fixed seed: identical texture every run
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            float v         = (y + 0.5f) / size;                  // 0..1 across the cable's width
            float edge      = Mathf.Abs(v - 0.5f) * 2f;            // 0 at centre .. 1 at edge
            float roundShade = Mathf.Lerp(1.1f, 0.55f, edge * edge); // fake cylindrical highlight
            for (int x = 0; x < size; x++)
            {
                float grain = 0.94f + (float)rng.NextDouble() * 0.12f; // subtle rubber/plastic grain
                byte  g     = (byte)Mathf.RoundToInt(Mathf.Clamp01(roundShade * grain) * 255f);
                pixels[y * size + x] = new Color32(g, g, g, 255);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();

        s_cableTexture = tex;
        return tex;
    }

    /// <summary>
    /// Fills a LineRenderer with the static catenary curve between two points.
    /// Used by CableManager to draw the placement preview with the same shape.
    /// excludeA/excludeB skip the endpoint device(s) already known at preview
    /// time (e.g. the source device the player clicked first) so the preview
    /// doesn't try to route around the very device it's plugged into.
    /// </summary>
    public static void FillCurvePoints(LineRenderer line, Vector3 a, Vector3 b, NetworkDevice excludeA = null, NetworkDevice excludeB = null)
    {
        line.positionCount = Segments + 1;

        Vector3 flat      = new Vector3(b.x - a.x, 0f, b.z - a.z);
        float   horizDist = flat.magnitude;
        Vector3 horizDir  = horizDist > 0.0001f ? flat / horizDist : Vector3.forward;
        CatenaryParams cat = SolveCatenary(horizDist, b.y - a.y);

        for (int i = 0; i <= Segments; i++)
        {
            float   t = (float)i / Segments;
            Vector3 p;
            if (cat.degenerate)
            {
                p = Vector3.Lerp(a, b, t);
            }
            else
            {
                float x = t * horizDist;
                p   = a + horizDir * x;
                p.y = a.y + CatenaryY(cat, x);
            }
            p = ApplyClearance(p, excludeA, excludeB);
            line.SetPosition(i, p);
        }
    }

    /// <summary>Combines device-body avoidance with the existing ground/tabletop
    /// floor — shared by Refresh() and FillCurvePoints() so the live cable and
    /// its placement preview always resolve obstacles identically.</summary>
    static Vector3 ApplyClearance(Vector3 p, NetworkDevice excludeA, NetworkDevice excludeB)
    {
        p = AvoidDevices(p, excludeA, excludeB);
        float minY = Mathf.Max(GroundClearance, Table.ClearanceAt(p) + TableClearance);
        p.y = Mathf.Max(p.y, minY);                                // rest on floor / ride over any table, don't clip through
        return p;
    }

    /// <summary>Pushes a sample point out of any OTHER device's footprint (the
    /// cable's own two endpoints are excluded so it can still leave from their
    /// ports). Picks whichever full escape — step sideways past the nearest
    /// edge, or lift above the device's top — costs less displacement, and
    /// applies it completely rather than blending a fraction of each: blending
    /// between the (still-penetrating) original point and a fully-safe one by
    /// a soft weight can land back inside the box on every axis at once for
    /// moderate penetration depths (verified — a point halfway between "push
    /// past the X edge" and "lift above the top" can still have all three
    /// axes within range). A full, cost-picked resolution is guaranteed safe;
    /// it costs the small continuity Table.ClearanceAt's own hard step already
    /// accepts elsewhere in this file, in exchange for actually working.</summary>
    static Vector3 AvoidDevices(Vector3 p, NetworkDevice excludeA, NetworkDevice excludeB)
    {
        foreach (var d in NetworkDevice.AllDevices)
        {
            if (d == null || d.isPaletteItem || d == excludeA || d == excludeB) continue;

            Bounds? fb = d.FootprintBounds();
            if (fb == null) continue;
            Bounds b = fb.Value;

            float dx = p.x - b.center.x;
            float dz = p.z - b.center.z;
            float halfX = b.extents.x, halfZ = b.extents.z;
            float penX = halfX - Mathf.Abs(dx);
            float penZ = halfZ - Mathf.Abs(dz);
            if (penX <= 0f || penZ <= 0f) continue; // outside this device's footprint

            float sidewaysCost = Mathf.Min(penX, penZ) + DeviceHorizontalMargin;
            float verticalCost = Mathf.Max(0f, (b.max.y + DeviceClearance) - p.y);

            if (sidewaysCost <= verticalCost)
            {
                // Step out past whichever edge is nearer — cheaper than lifting.
                if (penX <= penZ) p.x = b.center.x + Mathf.Sign(dx == 0f ? 1f : dx) * (halfX + DeviceHorizontalMargin);
                else               p.z = b.center.z + Mathf.Sign(dz == 0f ? 1f : dz) * (halfZ + DeviceHorizontalMargin);
            }
            else
            {
                p.y = b.max.y + DeviceClearance;
            }
        }
        return p;
    }

    // ── Catenary math ───────────────────────────────────────────────────────
    // The exact curve a chain hanging under gravity forms between two points
    // is y = a*cosh((x - x0)/a) + y0, where 'a' is the catenary constant
    // (inverse curvature) — this is the real physical shape, unlike a
    // parabola which is only a shallow-sag approximation of it. We pick 'a'
    // so the curve's own lowest point dips a target amount (SagFactor/MinSag,
    // same tuning as the old parabola) below the straight chord between equal-
    // height endpoints, solved by bisection since the dip is monotonically
    // decreasing in 'a'. x0/y0 are then the unique shift that makes the curve
    // pass through both endpoints exactly, including when they're not the
    // same height.

    struct CatenaryParams
    {
        public float a, x0, y0;
        public bool  degenerate; // near-vertical span: no meaningful catenary, hang straight
    }

    static CatenaryParams SolveCatenary(float horizDist, float heightDiff)
    {
        if (horizDist < 0.05f) return new CatenaryParams { degenerate = true };

        float sag = Mathf.Max(MinSag, horizDist * SagFactor);

        float lo = 0.01f, hi = Mathf.Max(horizDist, 1f);
        while (MidSag(hi, horizDist) > sag && hi < 1e6f) hi *= 2f;
        for (int i = 0; i < 30; i++)
        {
            float mid = 0.5f * (lo + hi);
            if (MidSag(mid, horizDist) > sag) lo = mid; else hi = mid;
        }
        float a = 0.5f * (lo + hi);

        float S  = 2f * a * (float)System.Math.Sinh(horizDist / (2f * a));
        float x0 = horizDist * 0.5f - a * (float)Asinh(heightDiff / S);
        float y0 = -a * (float)System.Math.Cosh(x0 / a);
        return new CatenaryParams { a = a, x0 = x0, y0 = y0, degenerate = false };
    }

    // Dip of the catenary's lowest point below its equal-height endpoints, for a given 'a'.
    static float MidSag(float a, float horizDist) => a * ((float)System.Math.Cosh(horizDist / (2f * a)) - 1f);

    static float CatenaryY(CatenaryParams p, float x) =>
        (float)(p.a * System.Math.Cosh((x - p.x0) / p.a)) + p.y0;

    static double Asinh(double x) => System.Math.Log(x + System.Math.Sqrt(x * x + 1.0));

    public NetworkDevice DeviceA => _deviceA;
    public NetworkDevice DeviceB => _deviceB;
    // Exposed for UndoManager — it needs the exact port pair, captured from
    // OnCableRemoved, to reconnect the same two interfaces on Undo() rather
    // than just "some free port on each device."
    public int PortA => _portA;
    public int PortB => _portB;

    /// <summary>Frees both endpoint ports. Does not destroy this cable's GameObject.</summary>
    public void Disconnect()
    {
        if (_deviceA != null) _deviceA.DisconnectPort(_portA);
        if (_deviceB != null) _deviceB.DisconnectPort(_portB);
    }

    // ── Port-hover hologram preview ──────────────────────────────────────────
    // Driven by PortHologramHoverManager: while the mouse hovers a connected
    // port, that port's cable wears the same grid-lined glowing material as
    // the drag ghost (see HologramMaterial) instead of inventing a separate
    // "hovered cable" look — one shared hologram material for the whole game.
    [Header("Hover Hologram")]
    [SerializeField] private Color hologramTint = new Color(0.45f, 0.85f, 1f, 0.9f);
    [SerializeField] private float hologramGridCellSize = 0.35f;

    private Material _preHologramMaterial;
    private Material _hologramInstance;
    private bool      _hologramActive;

    /// <summary>Swaps this cable onto (or back off of) the shared hologram material.
    /// Safe to call every frame with the same value — only acts on a state change.</summary>
    public void SetHologramPreview(bool on)
    {
        if (on == _hologramActive) return;
        _hologramActive = on;

        if (on)
        {
            _preHologramMaterial = _lr.material; // already a per-cable instance (see Initialize) — safe to hold onto directly
            _hologramInstance = HologramMaterial.CreateInstance(
                new Vector3(Mathf.Max(_cachedPathLength, 0.5f), 1f, 1f), hologramGridCellSize, true, hologramTint);
            _lr.material = _hologramInstance;
            // Vertex colors otherwise multiply into the hologram tint (Particles/Unlit
            // "Multiply" color mode) — flatten them to white so the hologram reads as
            // its own clean glow instead of blended with the cable's plain/subnet color.
            _lr.startColor = Color.white;
            _lr.endColor   = Color.white;
        }
        else
        {
            _lr.material   = _preHologramMaterial;
            _lr.startColor = BaseColor;
            _lr.endColor   = BaseColor;
            if (_hologramInstance != null) Destroy(_hologramInstance);
            _hologramInstance = null;
            _preHologramMaterial = null;
        }
    }

    static MaterialPropertyBlock s_highlightBlock;

    /// <summary>Tints this cable to show it's the one being configured in the CLI.</summary>
    public void SetHighlight(bool on)
    {
        s_highlightBlock ??= new MaterialPropertyBlock();
        if (on)
        {
            string prop = _lr.sharedMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
            s_highlightBlock.Clear();
            s_highlightBlock.SetColor(prop, Color.Lerp(BaseColor, highlightColor, 0.6f));
            _lr.SetPropertyBlock(s_highlightBlock);
        }
        else
        {
            _lr.SetPropertyBlock(null);
        }
    }
}
