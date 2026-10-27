using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A PC's working monitor — a tiny runtime-drawn pixel screen laid over the
/// model's "PC_Screen" part, showing what that PC is doing instead of a plain
/// blue panel.
///
///   Base state (polled from the PC's own address + cabling, see ComputeState):
///     Off        no IP and no cable: black glass.
///     NoAddress  cabled, no IP yet: a yellow screen with four filled boxes
///                (the IP address's four numbers) that pop in and pulse.
///     NoCable    addressed but unplugged: red screen, an unplugged cable and
///                its socket in lighter red, a blinking "!" between them.
///     Idle       addressed, cabled, and on the same subnet as the network it's
///                plugged into: a plain blue screen that lines of mock text
///                pop onto one at a time, scrolling once it's full.
///     Mismatch   addressed and cabled, but out of step with that network —
///                outvoted by the subnet most of it is on, or on a network
///                tied with no subnet in the majority: pulsing red with a
///                warning triangle over an "own color ≠ network color" pair
///                of swatches. The vote is SubnetVisualizer.IsOutOfStep — the
///                same one it colors that network's Switch/cables by, so this
///                warning always agrees with the colors on screen.
///
///   Events, played over the base state and then released back to it:
///     PlaySend / PlayReceive  a full-screen flash in the event color, then a
///                tinted screen showing its glyph — a sonar "ping" whose ring
///                expands out on send / converges in on receive, or a packet
///                type's globe/folder (the same pixel art as its floating
///                icon) with a progress bar and direction chevrons. Replaces
///                PingWipeEffect's screen wipe on PCs.
///     PlayError  the same in red with a cross, for failed pings/packets.
///   Callers fall back to PingWipeEffect when these return false (a device
///   without a monitor), so nothing else needs to know which devices have one.
///
///   Standby, a looping "armed and waiting" screen between the base state and
///   events, for as long as a tool has this PC picked but hasn't fired yet
///   (SetStandby / ClearStandby): the ping tool's chosen source shows a radar
///   sweep; a Packet Assign sender/receiver shows its packet type's glyph
///   breathing with chevrons drifting up (will send) or down (will receive).
///   Both get a dashed border marching around the edge.
///
/// Everything is drawn CPU-side into a small HDR texture (values above 1 feed
/// URP Bloom, the same idea as HologramMaterial.GlowMultiplier) and the
/// texture carries every color itself: the quad's material color stays white,
/// so the per-renderer MaterialPropertyBlock tints other systems stamp over a
/// whole device (PacketDeviceEffect's bounce glow, selection highlights) just
/// multiply over the screen instead of masking it.
///
/// Attached by DeviceModelBuilder.BuildPC, plus a scene-load sweep for PCs
/// baked into level template scenes (see HookSceneLoads).
/// </summary>
public class PCMonitor : MonoBehaviour
{
    public enum ScreenGlyph { None, Ping, Globe, Folder, Cross, Warning }

    enum BaseState  { Off, NoAddress, NoCable, Idle, Mismatch }
    enum Transition { None, Crossfade, PowerOn, PowerOff }

    // ── Screen texture / placement ───────────────────────────────────────────
    const int    W = 48, H = 36;                   // ~ the PC_Screen face's own 0.60 x 0.44 aspect
    const string ScreenPartName = "PC_Screen";     // DeviceModelBuilder / DevicePrefabBuilder both name it this
    const string DisplayName    = "PC_MonitorDisplay";
    const float  FaceStandoff   = 0.004f;          // in front of the screen face, clear of z-fighting at max zoom-out

    // ── Timing ───────────────────────────────────────────────────────────────
    const float StatePollInterval = 0.2f;
    const float CrossfadeDuration = 0.25f;
    const float PowerOnDuration   = 0.4f;
    const float PowerOffDuration  = 0.3f;
    const float FlashInDuration   = 0.14f; // ~ PingPacketEffect.SourceGlowBuild: onPeak lands on the same beat the old glow pulse did
    const float SettleDuration    = 0.3f;
    const float ReleaseDuration   = 0.35f;
    const float SendHold          = 0.9f;
    const float ReceiveHold       = 1.3f;
    const float ErrorHold         = 1.3f;
    const float BlipDuration      = 0.2f;  // a repeat arrival during a sustained receive
    const float BarFillDuration   = 0.7f;
    const float EventRedrawRate   = 30f;
    const float IdleRedrawRate    = 20f;
    const float StandbyRedrawRate = 10f;

    // ── Idle text ────────────────────────────────────────────────────────────
    const int   TextTop         = 4;
    const int   TextLeft        = 4;
    const int   TextWidth       = W - TextLeft * 2;
    const int   LinePitch       = 4;
    const int   LineHeight      = 2;
    const int   VisibleLines    = 8;
    const float LinePopDuration = 0.15f; // a new line appears whole, bright, and settles to the text color
    const float ScrollDuration  = 0.18f;

    // ── Look ─────────────────────────────────────────────────────────────────
    const float FlashBoost        = HologramMaterial.GlowMultiplier; // same bloom calibration every glowing effect shares
    const float GlyphBoost        = 1.6f;
    const float MismatchPulseRate = 0.9f; // pulses / second
    const float FieldPopDuration  = 0.35f;
    const float StandbyFadeDuration = 0.25f;

    static readonly Color OffGlass     = new Color(0.010f, 0.011f, 0.014f);
    static readonly Color OffGlare     = new Color(0.035f, 0.040f, 0.050f);
    static readonly Color IdleBg       = new Color(0.10f, 0.28f, 0.78f); // the PC_Screen panel's own original blue
    static readonly Color IdleText     = new Color(0.50f, 0.76f, 1.00f); // lighter blue on the blue background
    static readonly Color MismatchDark = new Color(0.16f, 0.02f, 0.02f);
    static readonly Color MismatchLit  = new Color(0.85f, 0.10f, 0.08f);
    static readonly Color WarnYellow   = new Color(1.00f, 0.85f, 0.25f);

    // Warning triangle with its "!" cut out, 13 x 11 — drawn at 2x.
    static readonly string[] WarningBitmap =
    {
        "......#......",
        ".....###.....",
        ".....###.....",
        "....##.##....",
        "....##.##....",
        "...###.###...",
        "...###.###...",
        "..#########..",
        "..####.####..",
        ".###########.",
        "#############",
    };

    class ScreenEvent
    {
        public Color       color;
        public ScreenGlyph glyph;
        public bool        outgoing, error, sustained;
        public float       start, end;       // end = when the release back to the base state begins
        public float       blipAt = -1f;
        public Action      onPeak;
    }

    struct Span { public int x, len; } // one "word"

    class IdleLine
    {
        public readonly List<Span> spans = new();
        public float pause; // how long it stays the newest line before the next one pops up
    }

    NetworkDevice device;
    MeshRenderer  display;
    Material      material;
    Texture2D     texture;
    bool          ready;

    Color[] frame, scratch;
    static float[] s_post; // scanline x corner-vignette multiplier, identical for every monitor

    BaseState  baseState = BaseState.Off, prevState = BaseState.Off;
    Transition transition;
    float      transitionStart;
    Color      ownColor, netColor;

    ScreenEvent evt;

    class StandbyScreen
    {
        public Color       color;
        public ScreenGlyph glyph;
        public bool        outgoing;
    }

    StandbyScreen standby;   // kept after ClearStandby so it can fade out
    bool          standbyActive;
    float         standbyWeight, standbySince;

    float pollTimer, redrawTimer;
    bool  dirty = true;

    System.Random rng;
    readonly List<IdleLine> idleLines = new();
    float idlePause, idleScroll, idleLineBornAt;
    float fieldPopAt;

    static Mesh s_quadMesh;
    static Material s_screenCasing;
    static readonly Dictionary<ScreenGlyph, (bool[] mask, int w, int h)> s_glyphMasks = new();

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Adds the monitor to a freshly built PC (idempotent).</summary>
    public static PCMonitor Attach(GameObject pc)
    {
        if (pc == null) return null;
        var monitor = pc.GetComponent<PCMonitor>();
        return monitor != null ? monitor : pc.AddComponent<PCMonitor>();
    }

    /// <summary>This device is sending something: flash, then its glyph for a
    /// moment. onPeak fires as the flash peaks — the moment the packet/orb
    /// should leave. False (onPeak not called) if the device has no monitor.</summary>
    public static bool PlaySend(NetworkDevice device, Color color, ScreenGlyph glyph, Action onPeak = null) =>
        TryGet(device, out var m) && m.Begin(color, glyph, outgoing: true, error: false, SendHold, sustained: false, onPeak);

    /// <summary>Something arrived: flash, then its glyph. Repeat arrivals while
    /// the screen is still showing a receive just re-tint it, extend the hold
    /// and blip it (onPeak fires immediately) — a burst reads as one steady
    /// "receiving" screen, as PingWipeEffect.NotifyArrival did for the wipe.</summary>
    public static bool PlayReceive(NetworkDevice device, Color color, ScreenGlyph glyph, Action onPeak = null) =>
        TryGet(device, out var m) && m.Begin(color, glyph, outgoing: false, error: false, ReceiveHold, sustained: true, onPeak);

    /// <summary>A failed ping/packet on this device: red flash and a cross.</summary>
    public static bool PlayError(NetworkDevice device, Action onPeak = null) =>
        TryGet(device, out var m) && m.Begin(GameTheme.Danger, ScreenGlyph.Cross, outgoing: false, error: true, ErrorHold, sustained: false, onPeak);

    /// <summary>Shows the looping "armed" screen while a tool has this PC
    /// picked but hasn't fired yet (the ping tool's chosen source, a Packet
    /// Assign sender/receiver) — stays until ClearStandby; events still play
    /// over it. glyph Ping draws a radar sweep; any other glyph breathes with
    /// chevrons drifting up (outgoing) or down.</summary>
    public static void SetStandby(NetworkDevice device, Color color, ScreenGlyph glyph, bool outgoing)
    {
        if (!TryGet(device, out var m)) return;
        if (!m.standbyActive) m.standbySince = Time.time;
        m.standby = new StandbyScreen { color = color, glyph = glyph, outgoing = outgoing };
        m.standbyActive = true;
        m.dirty = true;
    }

    public static void ClearStandby(NetworkDevice device)
    {
        if (!TryGet(device, out var m) || !m.standbyActive) return;
        m.standbyActive = false;
        m.dirty = true;
    }

    static bool TryGet(NetworkDevice device, out PCMonitor monitor)
    {
        monitor = device != null ? device.GetComponent<PCMonitor>() : null;
        return monitor != null && monitor.ready && monitor.isActiveAndEnabled;
    }

    bool Begin(Color color, ScreenGlyph glyph, bool outgoing, bool error, float hold, bool sustained, Action onPeak)
    {
        // Send/receive/error screens follow the Packets animation speed (see
        // AnimationSpeed.Clock) — every evt time lives on that clock. The
        // idle/standby screens and power transitions stay on Time.time.
        float now = AnimationSpeed.Clock(AnimationKind.Packets);

        if (sustained && evt != null && evt.sustained && now <= evt.end)
        {
            evt.color  = color;
            evt.glyph  = glyph;
            evt.end    = Mathf.Max(evt.end, now + hold);
            evt.blipAt = now;
            dirty = true;
            onPeak?.Invoke();
            return true;
        }

        if (evt != null) FirePeak(evt); // replaced before its flash peaked — its caller is still waiting on it

        evt = new ScreenEvent
        {
            color = color, glyph = glyph, outgoing = outgoing, error = error, sustained = sustained,
            start = now, end = now + FlashInDuration + SettleDuration + hold, onPeak = onPeak,
        };
        dirty = true;
        return true;
    }

    static void FirePeak(ScreenEvent e)
    {
        Action cb = e.onPeak;
        e.onPeak = null;
        cb?.Invoke();
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    void Awake()
    {
        // BuildPC also runs in Edit Mode (LevelTemplateAuthoringWindow) — only
        // ever build the live display in Play Mode.
        if (!Application.isPlaying) return;

        device    = GetComponent<NetworkDevice>();
        rng       = new System.Random(UnityEngine.Random.Range(0, int.MaxValue)); // each PC types its own text
        pollTimer = UnityEngine.Random.Range(0f, StatePollInterval); // staggered so a whole level's PCs don't all boot on the same frame
        ready     = TryBuildDisplay();
    }

    // A pending onPeak is deliberately dropped, not fired, here — it would
    // spawn effects on a device that's mid-destroy (or mid scene-unload), and
    // PingWipeEffect's own pending callbacks died with the device the same way.
    void OnDestroy()
    {
        if (material != null) Destroy(material);
        if (texture  != null) Destroy(texture);
    }

    void Update()
    {
        if (!ready) return;
        if (display == null) { ready = false; return; } // quad removed out from under us — callers fall back to PingWipeEffect
        float now = Time.time;
        float dt  = Time.deltaTime;
        float eventNow = AnimationSpeed.Clock(AnimationKind.Packets); // see Begin

        pollTimer -= dt;
        if (pollTimer <= 0f)
        {
            pollTimer = StatePollInterval;
            PollState(now);
        }

        if (transition != Transition.None && now - transitionStart >= TransitionDuration(transition))
        {
            transition = Transition.None;
            dirty = true;
        }

        if (evt != null)
        {
            if (evt.onPeak != null && eventNow - evt.start >= FlashInDuration) FirePeak(evt);
            if (eventNow >= evt.end + ReleaseDuration) { evt = null; dirty = true; }
        }

        if (baseState == BaseState.Idle) AdvanceIdleText(dt);

        float standbyTarget = standbyActive ? 1f : 0f;
        if (standbyWeight != standbyTarget)
            standbyWeight = Mathf.MoveTowards(standbyWeight, standbyTarget, dt / StandbyFadeDuration);

        redrawTimer -= dt;
        if ((dirty || redrawTimer <= 0f) && display.isVisible)
        {
            Redraw(now, eventNow);
            dirty = false;
            redrawTimer = 1f / RedrawRate();
        }
    }

    float RedrawRate()
    {
        if (evt != null || transition != Transition.None || standbyWeight > 0f) return EventRedrawRate;
        if (baseState == BaseState.NoAddress && Time.time - fieldPopAt < FieldPopDuration) return EventRedrawRate; // smooth pop-in
        return baseState switch
        {
            BaseState.Idle or BaseState.Mismatch        => IdleRedrawRate,
            BaseState.NoAddress or BaseState.NoCable    => StandbyRedrawRate,
            _                                           => 0.2f, // Off never changes — redrawn on `dirty` only
        };
    }

    static float TransitionDuration(Transition t) => t switch
    {
        Transition.PowerOn  => PowerOnDuration,
        Transition.PowerOff => PowerOffDuration,
        _                   => CrossfadeDuration,
    };

    // ── State ────────────────────────────────────────────────────────────────

    void PollState(float now)
    {
        BaseState next = ComputeState(out Color own, out Color net);
        if (own != ownColor || net != netColor) dirty = true;
        ownColor = own;
        netColor = net;
        if (next == baseState) return;

        transition = baseState == BaseState.Off ? Transition.PowerOn
                   : next == BaseState.Off      ? Transition.PowerOff
                   : Transition.Crossfade;
        prevState       = baseState;
        baseState       = next;
        transitionStart = now;
        dirty           = true;

        if (next == BaseState.Idle) ResetIdleText(); // freshly joined the network — type out from an empty screen
        if (next == BaseState.NoAddress)
            fieldPopAt = now + (transition == Transition.PowerOn ? PowerOnDuration : 0f); // pop once the screen has opened
    }

    BaseState ComputeState(out Color own, out Color net)
    {
        own = net = default;
        if (device == null || device.isPaletteItem) return BaseState.Off;

        var addr = GetComponent<DeviceAddress>(); // added lazily by DeviceAddressPopup, so not cached
        bool hasIp = addr != null && addr.HasAddress;

        bool cabled = false;
        for (int p = 0; p < device.PortCount && !cabled; p++)
            cabled = device.GetPortCable(p) != null;

        if (!hasIp) return cabled ? BaseState.NoAddress : BaseState.Off;

        Subnet ownSubnet = SubnetMath.SubnetOf(addr.Ip, addr.Mask);
        own = net = GameTheme.SubnetTint(ownSubnet);
        if (!cabled) return BaseState.NoCable;

        for (int p = 0; p < device.PortCount; p++)
        {
            Cable cable = device.GetPortCable(p);
            if (cable == null) continue;
            if (SubnetVisualizer.IsOutOfStep(device, cable, ownSubnet, out Subnet network))
            {
                net = GameTheme.SubnetTint(network);
                return BaseState.Mismatch;
            }
        }
        return BaseState.Idle;
    }

    // ── Display construction ─────────────────────────────────────────────────

    bool TryBuildDisplay()
    {
        Transform screen = FindScreenPart();
        if (screen == null) return false;

        // A drag clone (DragDropManager instantiates a BuildTemplate() PC)
        // already carries its template's display quad — reuse it, but give it
        // this PC's own material/texture instead of sharing the template's.
        Transform existing = screen.parent.Find(DisplayName);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            // Built from a MeshFilter rather than CreatePrimitive(Quad) — no
            // collider to strip (a pending Destroy would still get copied by a
            // same-frame Instantiate of this template).
            go = new GameObject(DisplayName);
            go.transform.SetParent(screen.parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
            go.AddComponent<MeshRenderer>();
            PlaceOverScreen(go.transform, screen);
        }

        display = go.GetComponent<MeshRenderer>();
        display.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        display.receiveShadows    = false;

        // The old blue panel now sits behind the display — darken it, or its
        // thin side edges show as a blue sliver around the screen at an angle.
        var screenRenderer = screen.GetComponent<Renderer>();
        if (screenRenderer != null) screenRenderer.sharedMaterial = ScreenCasingMaterial(screenRenderer.sharedMaterial);

        bool hdr = SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf);
        texture = new Texture2D(W, H, hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, true, true)
        {
            name       = "PCMonitorScreen",
            filterMode = FilterMode.Point, // crisp pixel art up close; the mip chain averages it cleanly when zoomed out
            wrapMode   = TextureWrapMode.Clamp,
        };

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
        material = new Material(shader) { name = "PCMonitorScreen", mainTexture = texture };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        display.sharedMaterial = material;

        frame   = new Color[W * H];
        scratch = new Color[W * H];
        s_post ??= BuildPostMask();
        return true;
    }

    // One shared dark copy of the screen part's own material (same shader, so
    // everything that tints/swaps a device's materials treats it as before).
    static Material ScreenCasingMaterial(Material original)
    {
        if (original == null || original == s_screenCasing) return original;
        if (s_screenCasing == null)
        {
            s_screenCasing = new Material(original) { name = "PCMonitorCasing" };
            Color casing = new Color(0.03f, 0.035f, 0.04f);
            if (s_screenCasing.HasProperty("_BaseColor")) s_screenCasing.SetColor("_BaseColor", casing);
            if (s_screenCasing.HasProperty("_Color"))     s_screenCasing.SetColor("_Color", casing);
        }
        return s_screenCasing;
    }

    Transform FindScreenPart()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == ScreenPartName) return t;
        return null;
    }

    // Covers the screen part's front (-Z) face exactly, in its parent's
    // (the visual root's) space — works for both the prefab path (baked mesh,
    // unit scale) and the primitive fallback (unit cube, scaled transform).
    static void PlaceOverScreen(Transform display, Transform screen)
    {
        var mf = screen.GetComponent<MeshFilter>();
        Bounds local = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
        Matrix4x4 toParent = Matrix4x4.TRS(screen.localPosition, screen.localRotation, screen.localScale);

        var b = new Bounds(toParent.MultiplyPoint3x4(local.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3(
                (i & 1) != 0 ? local.max.x : local.min.x,
                (i & 2) != 0 ? local.max.y : local.min.y,
                (i & 4) != 0 ? local.max.z : local.min.z);
            b.Encapsulate(toParent.MultiplyPoint3x4(corner));
        }

        display.localPosition = new Vector3(b.center.x, b.center.y, b.min.z - FaceStandoff);
        display.localRotation = Quaternion.identity;
        display.localScale    = new Vector3(b.size.x, b.size.y, 1f);
    }

    // Unit quad facing -Z, UVs flipped vertically so texture row 0 is the TOP
    // row — lets every draw routine below use top-down y like a screen does.
    static Mesh QuadMesh()
    {
        if (s_quadMesh != null) return s_quadMesh;
        s_quadMesh = new Mesh
        {
            name = "PCMonitorQuad",
            vertices  = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) },
            uv        = new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) },
            normals   = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back },
            triangles = new[] { 0, 2, 1, 2, 3, 1 },
        };
        s_quadMesh.RecalculateBounds();
        return s_quadMesh;
    }

    static float[] BuildPostMask()
    {
        var post = new float[W * H];
        for (int y = 0; y < H; y++)
        {
            float scan = (y & 1) == 1 ? 0.82f : 1f;
            float dy = (y + 0.5f) / H * 2f - 1f;
            for (int x = 0; x < W; x++)
            {
                float dx = (x + 0.5f) / W * 2f - 1f;
                float vignette = 1f - 0.45f * Mathf.Abs(dx * dy) * Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                post[y * W + x] = scan * vignette;
            }
        }
        // Knock out the outermost corner texels — a slightly rounded CRT tube.
        foreach (int i in new[] { 0, W - 1, (H - 1) * W, H * W - 1 }) post[i] *= 0.25f;
        return post;
    }

    // ── Compositing ──────────────────────────────────────────────────────────

    void Redraw(float now, float eventNow)
    {
        float tq = transition == Transition.None ? 1f : Mathf.Clamp01((now - transitionStart) / TransitionDuration(transition));
        switch (transition)
        {
            case Transition.PowerOn:
                RenderBase(baseState, frame, now);
                ApplyPowerOn(frame, tq);
                break;
            case Transition.PowerOff:
                RenderBase(prevState, frame, now);
                ApplyPowerOff(frame, tq);
                break;
            case Transition.Crossfade:
                RenderBase(baseState, frame, now);
                RenderBase(prevState, scratch, now);
                Mix(frame, scratch, 1f - tq);
                break;
            default:
                RenderBase(baseState, frame, now);
                break;
        }

        if (standbyWeight > 0f && standby != null)
        {
            RenderStandby(standby, scratch, now);
            Mix(frame, scratch, standbyWeight);
        }

        if (evt != null)
        {
            float tau = eventNow - evt.start;
            float flash = tau < FlashInDuration
                ? tau / FlashInDuration * (tau / FlashInDuration)
                : 1f - Mathf.Clamp01((tau - FlashInDuration) / SettleDuration);
            float mix = tau < FlashInDuration ? 0f
                      : eventNow <= evt.end ? 1f
                      : 1f - Mathf.Clamp01((eventNow - evt.end) / ReleaseDuration);

            if (mix > 0f)
            {
                RenderEvent(evt, scratch, eventNow);
                Mix(frame, scratch, mix);
            }

            float blip = evt.blipAt >= 0f ? (1f - Mathf.Clamp01((eventNow - evt.blipAt) / BlipDuration)) * 0.45f : 0f;
            float overlay = Mathf.Max(flash, blip);
            if (overlay > 0f)
            {
                Color flashColor = Lin(evt.color, FlashBoost);
                for (int i = 0; i < frame.Length; i++) frame[i] = Color.Lerp(frame[i], flashColor, overlay);
            }
        }

        for (int i = 0; i < frame.Length; i++)
        {
            Color c = frame[i] * s_post[i];
            c.a = 1f;
            frame[i] = c;
        }

        texture.SetPixels(frame);
        texture.Apply(true);
    }

    void RenderBase(BaseState state, Color[] buf, float now)
    {
        switch (state)
        {
            case BaseState.Idle:      DrawIdle(buf, now);      break;
            case BaseState.Mismatch:  DrawMismatch(buf, now);  break;
            case BaseState.NoAddress: DrawNoAddress(buf, now); break;
            case BaseState.NoCable:   DrawNoCable(buf, now);   break;
            default:                  DrawOff(buf);            break;
        }
    }

    // A bright line stretches across the middle, then opens vertically onto the
    // new screen — an old CRT warming up.
    static void ApplyPowerOn(Color[] buf, float q)
    {
        const float LinePhase = 0.3f;
        Color hot = Lin(Color.white, 2f);
        if (q < LinePhase)
        {
            int half = Mathf.Max(1, Mathf.RoundToInt(q / LinePhase * W * 0.5f));
            Fill(buf, Color.black);
            Rect(buf, W / 2 - half, H / 2 - 1, half * 2, 2, hot);
            return;
        }

        float open  = (q - LinePhase) / (1f - LinePhase);
        float halfH = Mathf.Max(1f, open * H * 0.5f);
        float boost = 1f + (1f - open) * 1.5f;
        for (int y = 0; y < H; y++)
        {
            bool inside = Mathf.Abs(y + 0.5f - H * 0.5f) <= halfH;
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                buf[i] = inside ? buf[i] * boost : Color.black;
            }
        }
    }

    // The reverse: the picture collapses to a line, the line shrinks and fades.
    static void ApplyPowerOff(Color[] buf, float q)
    {
        const float CollapsePhase = 0.55f;
        Color hot = Lin(Color.white, 2f);
        if (q < CollapsePhase)
        {
            float open  = 1f - q / CollapsePhase;
            float halfH = Mathf.Max(1f, open * H * 0.5f);
            for (int y = 0; y < H; y++)
            {
                bool inside = Mathf.Abs(y + 0.5f - H * 0.5f) <= halfH;
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    buf[i] = inside ? Color.Lerp(buf[i], hot, 1f - open) : Color.black;
                }
            }
            return;
        }

        float w = 1f - (q - CollapsePhase) / (1f - CollapsePhase);
        int half = Mathf.Max(1, Mathf.RoundToInt(w * W * 0.5f));
        Fill(buf, Color.black);
        Rect(buf, W / 2 - half, H / 2 - 1, half * 2, 2, hot * w);
    }

    // ── Base screens ─────────────────────────────────────────────────────────

    static void DrawOff(Color[] buf)
    {
        Fill(buf, Lin(OffGlass));
        // A faint diagonal glare so it still reads as glass, not a hole.
        Color glare = Lin(OffGlare);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float d = x + y * 1.3f;
                if ((d > 20f && d < 25f) || (d > 27f && d < 28.5f)) buf[y * W + x] = glare;
            }
    }

    // A plain blue screen that whole lines of text pop onto, one at a time.
    void DrawIdle(Color[] buf, float now)
    {
        Fill(buf, Lin(IdleBg));

        Color text = Lin(IdleText);
        Color pop  = Lin(Color.Lerp(IdleText, Color.white, 0.5f), 1.6f);
        int offset = Mathf.RoundToInt(idleScroll * LinePitch);
        for (int i = 0; i < idleLines.Count; i++)
        {
            bool  newest = i == idleLines.Count - 1;
            float settle = newest ? Mathf.Clamp01((now - idleLineBornAt) / LinePopDuration) : 1f;
            Color c      = Color.Lerp(pop, text, settle);
            int   y      = TextTop + i * LinePitch + offset;
            foreach (var span in idleLines[i].spans)
                Rect(buf, TextLeft + span.x, y, span.len, LineHeight, c, TextTop);
        }
    }

    void DrawMismatch(Color[] buf, float now)
    {
        float p = Mathf.Sin(now * Mathf.PI * 2f * MismatchPulseRate) * 0.5f + 0.5f;
        p *= p;
        Fill(buf, Color.Lerp(Lin(MismatchDark), Lin(MismatchLit, 1.3f), p)); // peak slightly above 1 so the pulse blooms

        Stamp(buf, ScreenGlyph.Warning, W / 2, 14, 2, Lin(WarnYellow, 1f + 0.8f * p));

        // "this PC's subnet ≠ the network's subnet", in the same colors the
        // PC's tower and the Switch/cable it's plugged into already show.
        // All in 2x pixels, like the triangle above it.
        const int SwatchY = 26;
        Color outline = Lin(Color.black);
        Rect(buf, 6, SwatchY, 12, 8, outline);
        Rect(buf, 8, SwatchY + 2, 8, 4, Lin(ownColor, 1.1f));
        Rect(buf, 30, SwatchY, 12, 8, outline);
        Rect(buf, 32, SwatchY + 2, 8, 4, Lin(netColor, 1.1f));

        Color sign = Lin(Color.white, 1.2f);
        Rect(buf, 20, SwatchY + 1, 8, 2, sign);
        Rect(buf, 20, SwatchY + 5, 8, 2, sign);
        for (int k = 0; k < 4; k++) Rect(buf, 26 - k * 2, SwatchY - 1 + k * 2, 2, 2, sign); // the "/" through the "="
    }

    // Cabled but no IP yet: a yellow screen with four filled boxes — the four
    // numbers of the IP address it's missing — pulsing together. They pop in
    // with an overshoot (the floating result icons' own EaseOutBack pop).
    void DrawNoAddress(Color[] buf, float now)
    {
        Fill(buf, Lin(WarnYellow, 0.6f));

        const int BoxW = 7, BoxH = 9, Gap = 4;
        const int FieldX = (W - (BoxW * 4 + Gap * 3)) / 2, FieldY = (H - BoxH) / 2;

        float t     = (now - fieldPopAt) / FieldPopDuration;
        float pop   = t <= 0f ? 0f : EaseOutBack(Mathf.Clamp01(t));
        float pulse = 1f + 0.3f * Mathf.Sin(now * 3f);
        Color box   = Lin(Color.Lerp(WarnYellow, Color.white, 0.6f), GlyphBoost * pulse);

        int w = Mathf.RoundToInt(BoxW * pop), h = Mathf.RoundToInt(BoxH * pop);
        for (int k = 0; k < 4; k++)
        {
            int bx = FieldX + k * (BoxW + Gap);
            Rect(buf, bx + (BoxW - w) / 2, FieldY + (BoxH - h) / 2, w, h, box);
        }
    }

    // Addressed but unplugged: a red screen with an unplugged cable and its
    // empty socket in lighter red, and a blinking "!" in the gap between them.
    void DrawNoCable(Color[] buf, float now)
    {
        Fill(buf, Lin(GameTheme.Danger, 0.6f));
        Color icon = Lin(Color.Lerp(GameTheme.Danger, Color.white, 0.4f), 1.3f);

        // All in 2x pixels, like the glyphs.
        Rect(buf, 4, 16, 12, 2, icon);                     // cable
        Rect(buf, 16, 12, 6, 10, icon);                    // plug body
        Rect(buf, 22, 14, 2, 2, icon);                     // contacts
        Rect(buf, 22, 18, 2, 2, icon);
        Rect(buf, 34, 10, 12, 2, icon);                    // socket: top, bottom, sides
        Rect(buf, 34, 22, 12, 2, icon);
        Rect(buf, 34, 10, 2, 14, icon);
        Rect(buf, 44, 10, 2, 14, icon);
        Rect(buf, 38, 18, 4, 4, icon);                     // its latch slot

        if (Mathf.Repeat(now, 0.8f) < 0.45f)
        {
            Color alert = Lin(Color.white, 1.2f);
            Rect(buf, 28, 12, 2, 6, alert);                // "!" in the gap
            Rect(buf, 28, 20, 2, 2, alert);
        }
    }

    // ── Event screen ─────────────────────────────────────────────────────────

    void RenderEvent(ScreenEvent e, Color[] buf, float now)
    {
        float tau = now - e.start;
        Color tint = e.color;

        float bg = 0.3f;
        if (e.error)
        {
            float t = tau - FlashInDuration; // two hard blinks right after the flash
            if (t > 0f && t < 0.6f && Mathf.Repeat(t, 0.3f) < 0.15f) bg = 0.7f;
        }
        Fill(buf, Lin(tint, bg));

        Color bright = Lin(Color.Lerp(tint, Color.white, 0.3f), GlyphBoost);
        Color mid    = Lin(tint, 0.9f);

        if (e.glyph == ScreenGlyph.Ping)
        {
            DrawPing(buf, tau, e.outgoing, bright, mid);
            Rect(buf, e.outgoing ? 17 : 13, 32, e.outgoing ? 14 : 22, 2, mid); // "Pinging…" / "Reply from…" caption
            return;
        }

        int glyphCenterY = e.error ? 14 : 15;
        Stamp(buf, e.glyph, W / 2, glyphCenterY, 2, bright);

        if (e.error)
        {
            if (Mathf.Repeat(tau, 0.5f) < 0.3f) Rect(buf, 14, 30, 20, 2, mid);
            return;
        }

        // Transfer progress plus direction chevrons (up = sending, down = receiving).
        float progress = Mathf.Clamp01((tau - FlashInDuration) / BarFillDuration);
        Rect(buf, 8, 30, 32, Px, Lin(tint, 0.5f));                                // track
        Rect(buf, 8, 30, Px * Mathf.RoundToInt(16 * progress), Px, bright);       // fill, a 2x pixel at a time

        float drift = Mathf.Repeat(tau * 2.5f, 1f);
        for (int k = 0; k < 2; k++)
        {
            float phase = Mathf.Repeat(drift + k * 0.5f, 1f);
            float y = e.outgoing ? 22 - phase * 16 : 6 + phase * 16;
            Chevron(buf, 43, Px * Mathf.RoundToInt(y / Px), e.outgoing, mid);
        }
    }

    // A sonar ping: a center blip, two faint static rings, and one bright ring
    // that sweeps outward (sending) or converges inward (receiving).
    static void DrawPing(Color[] buf, float tau, bool outgoing, Color bright, Color mid)
    {
        const float Cx = W * 0.5f, Cy = 16f; // on a 2x2 block corner, so the rings come out symmetric
        Ring(buf, Cx, Cy, 7f, 2.2f, mid, 0.55f);
        Ring(buf, Cx, Cy, 12f, 2.2f, mid, 0.35f);

        for (int k = 0; k < 2; k++)
        {
            float phase = Mathf.Repeat(tau * 1.1f + k * 0.5f, 1f);
            float r = outgoing ? Mathf.Lerp(3f, 15f, phase) : Mathf.Lerp(15f, 3f, phase);
            float a = outgoing ? 1f - phase : phase;
            Ring(buf, Cx, Cy, r, 2.6f, bright, a);
        }

        Disc(buf, Cx, Cy, 2.4f, bright);
    }

    // ── Standby screen ───────────────────────────────────────────────────────

    // Calm and looping, unlike the one-shot events: a dim tint of the tool's
    // color, a dashed border marching around the edge (this PC is picked), and
    // the glyph idling — a radar sweep for a ping source ("looking for a
    // target"), or the packet type's glyph breathing beside chevrons that bob
    // gently in place (up = will send, down = will receive).
    void RenderStandby(StandbyScreen s, Color[] buf, float now)
    {
        Fill(buf, Lin(s.color, 0.22f));
        Color bright = Lin(Color.Lerp(s.color, Color.white, 0.3f), GlyphBoost);
        Color mid    = Lin(s.color, 0.8f);

        if (s.glyph == ScreenGlyph.Ping)
        {
            DrawRadar(buf, now - standbySince, bright, mid);
        }
        else
        {
            float breathe = 0.75f + 0.25f * Mathf.Sin(now * 3f);
            Stamp(buf, s.glyph, 21, H / 2, 2, bright * breathe);

            int bob = Mathf.RoundToInt(Mathf.Sin(now * 4f)) * Px; // one 2x pixel up, level, one down
            Chevron(buf, 41, 12 + bob, s.outgoing, mid);
            Chevron(buf, 41, 18 + bob, s.outgoing, mid);
        }

        DrawMarchingBorder(buf, now, mid);
    }

    // A radar sweep turning over the ping rings, with a fading trail behind it.
    static void DrawRadar(Color[] buf, float t, Color bright, Color mid)
    {
        const float Cx = W * 0.5f, Cy = H * 0.5f, R = 14f, TrailRadians = 1.2f;
        Ring(buf, Cx, Cy, 7f, 2.2f, mid, 0.5f);
        Ring(buf, Cx, Cy, R, 2.2f, mid, 0.7f);

        float sweep = Mathf.Repeat(t * 0.7f, 1f) * Mathf.PI * 2f; // clockwise on screen (y runs down)
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float dx = CellCenter(x) - Cx, dy = CellCenter(y) - Cy;
                if (dx * dx + dy * dy > (R - 1f) * (R - 1f)) continue;
                float behind = Mathf.Repeat(sweep - Mathf.Atan2(dy, dx), Mathf.PI * 2f);
                float trail  = 1f - behind / TrailRadians;
                if (trail > 0f) buf[y * W + x] = Color.Lerp(buf[y * W + x], bright, trail * 0.85f);
            }

        Disc(buf, Cx, Cy, 2.4f, Mathf.Repeat(t, 0.8f) < 0.5f ? bright : mid);
    }

    // Dashes of 2x pixels marching clockwise around the screen's outer edge.
    static void DrawMarchingBorder(Color[] buf, float now, Color c)
    {
        const int CW = W / Px, CH = H / Px; // the edge, in 2x2 blocks
        int offset = Mathf.FloorToInt(now * 6f);
        int p = 0;
        for (int x = 0; x < CW - 1; x++, p++) Dash(buf, x, 0, p, offset, c);
        for (int y = 0; y < CH - 1; y++, p++) Dash(buf, CW - 1, y, p, offset, c);
        for (int x = CW - 1; x > 0; x--, p++) Dash(buf, x, CH - 1, p, offset, c);
        for (int y = CH - 1; y > 0; y--, p++) Dash(buf, 0, y, p, offset, c);
    }

    static void Dash(Color[] buf, int cellX, int cellY, int p, int offset, Color c)
    {
        if (((p - offset) % 4 + 4) % 4 < 2) Rect(buf, cellX * Px, cellY * Px, Px, Px, c);
    }

    // "^" (up) or "v" (down) in 2x pixels: a 4-wide tip row at cy, then the
    // two arms one block below (up) or above (down) it.
    static void Chevron(Color[] buf, int cx, int cy, bool up, Color c)
    {
        int arms = up ? cy + Px : cy - Px;
        Rect(buf, cx - 2, cy, 4, Px, c);
        Rect(buf, cx - 4, arms, Px, Px, c);
        Rect(buf, cx + 2, arms, Px, Px, c);
    }

    // ── Idle text simulation ─────────────────────────────────────────────────

    void ResetIdleText()
    {
        idleLines.Clear();
        idlePause  = 0.4f; // a beat of plain blue before the first line pops up
        idleScroll = 0f;
    }

    void AdvanceIdleText(float dt)
    {
        idleScroll = Mathf.Max(0f, idleScroll - dt / ScrollDuration);

        idlePause -= dt;
        if (idlePause > 0f) return;

        if (idleLines.Count >= VisibleLines)
        {
            idleLines.RemoveAt(0);
            idleScroll = 1f; // remaining lines start one line low and slide up into place
        }

        IdleLine line = NewIdleLine();
        idleLines.Add(line);
        idleLineBornAt = Time.time;
        idlePause      = line.pause;
    }

    // One line of mock text: words of random length, sometimes indented, the
    // occasional blank line — so it reads as text rather than a bar chart.
    IdleLine NewIdleLine()
    {
        var line = new IdleLine { pause = 0.35f + (float)rng.NextDouble() * 0.8f };
        if (rng.Next(8) == 0) { line.pause = 0.2f; return line; } // blank line

        int indent = rng.Next(5) == 0 ? 4 : rng.Next(4) == 0 ? 2 : 0;
        int target = indent + rng.Next(10, TextWidth - indent + 1);
        int x = indent;
        while (true)
        {
            int len = Px * rng.Next(1, 4); // words and gaps in whole 2x pixels, like the glyphs
            if (x + len > target)
            {
                if (x != indent) break;
                len = (target - x) / Px * Px;
            }
            if (len <= 0) break;

            line.spans.Add(new Span { x = x, len = len });
            x += len + (rng.Next(5) == 0 ? 2 * Px : Px);
        }
        return line;
    }

    // ── Pixel primitives (top-down y, clipped) ───────────────────────────────

    static Color Lin(Color c, float boost = 1f)
    {
        Color l = c.linear;
        return new Color(l.r * boost, l.g * boost, l.b * boost, 1f);
    }

    static void Fill(Color[] buf, Color c)
    {
        for (int i = 0; i < buf.Length; i++) buf[i] = c;
    }

    static void Mix(Color[] into, Color[] other, float t)
    {
        if (t <= 0f) return;
        for (int i = 0; i < into.Length; i++) into[i] = Color.Lerp(into[i], other[i], t);
    }

    static void Rect(Color[] buf, int x, int y, int w, int h, Color c, int clipTop = 0)
    {
        int x0 = Mathf.Max(x, 0), x1 = Mathf.Min(x + w, W);
        int y0 = Mathf.Max(y, clipTop), y1 = Mathf.Min(y + h, H);
        for (int yy = y0; yy < y1; yy++)
            for (int xx = x0; xx < x1; xx++)
                buf[yy * W + xx] = c;
    }

    // Every procedural shape (rings, discs, the radar sweep) is evaluated once
    // per Px x Px block, at the block's center, so it comes out in the same
    // chunky 2x pixels as the glyphs Stamp draws at scale 2 — the cross, globe
    // and folder — instead of finer 1-texel detail beside them.
    const int Px = 2;
    static float CellCenter(int v) => v - v % Px + Px * 0.5f;

    // thickness is in texels; keep it >= ~2.2 or a ring breaks into dashes on the 2x grid.
    static void Ring(Color[] buf, float cx, float cy, float r, float thickness, Color c, float alpha)
    {
        if (alpha <= 0f) return;
        float half = thickness * 0.5f;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r - half) - Px), x1 = Mathf.Min(W, Mathf.CeilToInt(cx + r + half) + Px);
        int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r - half) - Px), y1 = Mathf.Min(H, Mathf.CeilToInt(cy + r + half) + Px);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                float dx = CellCenter(x) - cx, dy = CellCenter(y) - cy;
                if (Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - r) <= half)
                    buf[y * W + x] = Color.Lerp(buf[y * W + x], c, alpha);
            }
    }

    static void Disc(Color[] buf, float cx, float cy, float r, Color c)
    {
        int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r) - Px), x1 = Mathf.Min(W, Mathf.CeilToInt(cx + r) + Px);
        int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r) - Px), y1 = Mathf.Min(H, Mathf.CeilToInt(cy + r) + Px);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                float dx = CellCenter(x) - cx, dy = CellCenter(y) - cy;
                if (dx * dx + dy * dy <= r * r) buf[y * W + x] = c;
            }
    }

    // Draws a glyph's mask centered on (cx, cy), each mask pixel as a
    // scale x scale block.
    static void Stamp(Color[] buf, ScreenGlyph glyph, int cx, int cy, int scale, Color c)
    {
        if (!TryGetGlyphMask(glyph, out bool[] mask, out int gw, out int gh)) return;
        int left = cx - gw * scale / 2, top = cy - gh * scale / 2;
        for (int gy = 0; gy < gh; gy++)
            for (int gx = 0; gx < gw; gx++)
                if (mask[gy * gw + gx])
                    Rect(buf, left + gx * scale, top + gy * scale, scale, scale, c);
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float f = x - 1f;
        return 1f + c3 * f * f * f + c1 * f * f;
    }

    // The glyphs are the exact same pixel art as the floating packet/result
    // icons — built by their own builders at pixelScale 1 (one texel per art
    // pixel) and read back as a top-down mask — so the monitor and the icon
    // above it always match.
    static bool TryGetGlyphMask(ScreenGlyph glyph, out bool[] mask, out int w, out int h)
    {
        if (s_glyphMasks.TryGetValue(glyph, out var cached))
        {
            (mask, w, h) = cached;
            return mask != null;
        }

        mask = null; w = h = 0;
        string[] bitmap = glyph switch
        {
            ScreenGlyph.Warning => WarningBitmap,
            _                   => null,
        };
        if (bitmap != null)
        {
            h = bitmap.Length;
            w = bitmap[0].Length;
            mask = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    mask[y * w + x] = bitmap[y][x] == '#';
        }
        else
        {
            Sprite sprite = glyph switch
            {
                ScreenGlyph.Globe  => GridGlobeIconBuilder.GetSprite(Color.white, 14, 1, 1),
                ScreenGlyph.Folder => FolderIconBuilder.GetSprite(Color.white, 14, 1),
                ScreenGlyph.Cross  => CrossIconBuilder.GetSprite(Color.white, 12, 2, 1),
                _                  => null,
            };
            if (sprite != null && sprite.texture != null && sprite.texture.isReadable)
            {
                Texture2D tex = sprite.texture;
                w = tex.width;
                h = tex.height;
                Color32[] px = tex.GetPixels32();
                mask = new bool[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        mask[(h - 1 - y) * w + x] = px[y * w + x].a > 127; // texture rows are bottom-up
            }
        }

        s_glyphMasks[glyph] = (mask, w, h);
        return mask != null;
    }

    // ── Template-scene PCs ───────────────────────────────────────────────────

    // PCs baked into a level template scene (LevelTemplateScene, loaded by
    // TemplateTopologyLoader) never go through DeviceModelBuilder.BuildPC at
    // runtime — give them a monitor as their scene loads.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void HookSceneLoads()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (NetworkDevice nd in root.GetComponentsInChildren<NetworkDevice>(true))
                if (nd.deviceType == DeviceType.PC) Attach(nd.gameObject);
    }
}
