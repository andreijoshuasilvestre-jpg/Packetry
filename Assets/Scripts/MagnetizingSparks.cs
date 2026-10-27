using System.Collections;
using UnityEngine;

/// <summary>
/// A second, denser flavor of ConstructionSparks' "electric arc" spark look —
/// smaller particles, many more of them, and (the part plain ParticleSystem
/// modules can't do on their own) actively homing rather than just drifting
/// on their initial velocity. Always green (PingPacketEffect passes
/// GameTheme.Success throughout). One continuous swarm carries the whole
/// story from source to destination:
///
///  CreateSwarmTrail — starts the swarm's continuous low-rate trickle, always
///  chasing a live moving point (the traveling packet) every frame via
///  UpdateSwarmTrail, which re-steers every already-alive particle's velocity
///  toward the current target using GetParticles/SetParticles — the
///  "magnetize with the packet" read, mirroring how ConstructionSparks'
///  CreateTrail/UpdateAsPoint is driven by the caller's own per-frame loop.
///
///  BurstAt — layered on top of that same swarm right when the packet departs
///  the source: an immediate burst of extra particles kicked outward from the
///  exit port (the "source device explodes with particles" beat). They join
///  the swarm already alive, so UpdateSwarmTrail immediately starts homing
///  them onto the packet too — the burst doesn't fly off on its own, it gets
///  pulled in.
///
///  ConvergeAndCling — the hand-off at arrival: stops the swarm's trickle,
///  gives every currently-alive particle (both the steady trickle and the
///  source's own burst — the very same particles that have been chasing the
///  packet the whole trip) a fresh outward kick from the entry point (the
///  "explode again" beat), then steers each one toward its own fixed point on
///  the destination device's bounding surface (derived deterministically from
///  that particle's randomSeed, so it chases the same spot every frame
///  instead of jittering) until it arrives and settles there — "magnetize
///  with the whole device". Self-driving from that point on (its own
///  coroutine on the swarm's existing GameObject), so the caller doesn't need
///  to pump it further.
/// </summary>
public class MagnetizingSparks : MonoBehaviour
{
    const float GlowMultiplier = 2.5f; // matches ConstructionSparks' own bloom boost

    // ── Swarm trail (chases a moving point) ─────────────────────────────────
    const float TrailSize          = 0.011f; // smaller than ConstructionSparks' 0.025
    const float TrailRateOverTime  = 170f;   // more in quantity than ConstructionSparks' 55
    const float TrailLifetime      = 0.6f;
    const float TrailSeekStrength  = 16f;    // how fast velocity turns toward the target, per second
    const float TrailSeekMaxSpeed  = 4f;     // a bit faster than PingPacketEffect's own orb travel speed, so the swarm can actually keep up

    // ── Source burst (joins the swarm, gets pulled onto the packet) ─────────
    const int   BurstAtCount       = 50;
    const float BurstKickSpeed     = 1.6f;
    const float BurstKickRadius    = 0.05f; // spawn offset from the exit port, so the burst doesn't all emit from one exact point

    // ── Converge-and-cling hand-off at the destination ───────────────────────
    const float ClingKickSpeed     = 1.8f;
    const float ExplodeDuration    = 0.14f;  // the fresh outward kick is left alone this long before homing starts
    const float MagnetizeDuration  = 0.5f;   // homing toward each particle's own device-surface point
    const float StickHold          = 0.3f;   // settle at zero velocity, clinging to the surface
    const float ArrivalRadius      = 0.12f;  // inside this distance, seek speed tapers off instead of overshooting/orbiting
    const float ClingLifetimeTail  = 1f;     // extra time given to every alive particle once clinging starts, so it can survive the whole hand-off + a visible fade

    static Material s_material;

    static Material SparkMaterial()
    {
        if (s_material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            s_material = new Material(shader);
        }
        return s_material;
    }

    static Gradient FadeOutGradient(Color color)
    {
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 0.8f) });
        return grad;
    }

    // ── Swarm trail ──────────────────────────────────────────────────────────

    /// <summary>Starts the swarm's continuous low-rate trickle. Reposition/steer it every
    /// frame with UpdateSwarmTrail while the target is moving; hand it off to
    /// ConvergeAndCling once it arrives, rather than destroying it.</summary>
    public static ParticleSystem CreateSwarmTrail(Color color)
    {
        var go = new GameObject("PingSparkSwarm");
        var ps = go.AddComponent<ParticleSystem>();
        Color glowColor = color * GlowMultiplier;

        var main = ps.main;
        main.startLifetime = TrailLifetime;
        main.startSpeed = 0.6f;
        main.startSize = TrailSize;
        main.startColor = glowColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.loop = true;
        main.playOnAwake = false;
        // The swarm chases a ping, so it runs at the Packets animation speed
        // too — otherwise a sped-up orb leaves its sparks trailing far behind.
        main.simulationSpeed = AnimationSpeed.Get(AnimationKind.Packets);

        var emission = ps.emission;
        emission.rateOverTime = TrailRateOverTime;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.02f;

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 4f;
        noise.scrollSpeed = 2.5f;
        noise.damping = true;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = FadeOutGradient(glowColor);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = SparkMaterial();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;

        ps.Play();
        return ps;
    }

    /// <summary>Moves the emitter to the live target position and re-steers every
    /// already-alive particle's velocity toward it — the "magnetize with the packet"
    /// behavior. Call every frame while the target is moving.</summary>
    public static void UpdateSwarmTrail(ParticleSystem ps, Vector3 targetPos)
    {
        if (ps == null) return;
        ps.transform.position = targetPos;

        int count = ps.particleCount;
        if (count == 0) return;

        var particles = new ParticleSystem.Particle[count];
        int written = ps.GetParticles(particles);
        for (int i = 0; i < written; i++)
        {
            Vector3 toTarget = targetPos - particles[i].position;
            Vector3 desired = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized * TrailSeekMaxSpeed : Vector3.zero;
            particles[i].velocity = Vector3.Lerp(particles[i].velocity, desired, TrailSeekStrength * AnimationSpeed.Delta(AnimationKind.Packets));
        }
        ps.SetParticles(particles, written);
    }

    /// <summary>Emits an immediate burst of extra particles kicked radially outward from
    /// origin, on top of ps's own ongoing swarm — "the source device explodes with
    /// particles". They join whatever's already alive, so the very next UpdateSwarmTrail
    /// call starts pulling them onto the traveling packet along with everything else.</summary>
    public static void BurstAt(ParticleSystem ps, Vector3 origin, Color color, int count = BurstAtCount)
    {
        if (ps == null) return;
        Color glowColor = color * GlowMultiplier;

        var emitParams = new ParticleSystem.EmitParams { applyShapeToPosition = false };
        for (int i = 0; i < count; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            emitParams.position = origin + dir * BurstKickRadius;
            emitParams.velocity = dir * BurstKickSpeed;
            emitParams.startColor = glowColor;
            ps.Emit(emitParams, 1);
        }
    }

    // ── Converge-and-cling hand-off ──────────────────────────────────────────

    /// <summary>Hands an already-running swarm (from CreateSwarmTrail, optionally topped
    /// up by BurstAt) off into its arrival behavior: stop the trickle, kick everything
    /// currently alive outward from origin, then converge and cling across the whole
    /// surface of device before fading out. Takes over ps's own GameObject — the caller
    /// doesn't need to destroy it separately.</summary>
    public static void ConvergeAndCling(ParticleSystem ps, Vector3 origin, NetworkDevice device, Color color)
    {
        if (ps == null) return;
        if (device == null) { Destroy(ps.gameObject); return; }

        var driver = ps.gameObject.AddComponent<MagnetizingSparks>();
        driver.StartCoroutine(driver.RunConvergeAndCling(ps, origin, device, color));
    }

    IEnumerator RunConvergeAndCling(ParticleSystem ps, Vector3 origin, NetworkDevice device, Color color)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); // no more trickle — whatever's alive right now is what converges
        ExtendLifetimes(ps, ExplodeDuration + MagnetizeDuration + StickHold + ClingLifetimeTail);
        ApplyKick(ps, origin, ClingKickSpeed);

        // Phase 1 — explode again: the fresh kick above is left alone this
        // long so arrival still reads as a burst, before anything starts
        // pulling the sparks back in toward the surface.
        yield return new WaitForSeconds(AnimationSpeed.Seconds(AnimationKind.Packets, ExplodeDuration));

        // Phase 2 — magnetize: every alive particle gets pulled toward its
        // own fixed point on the device's bounding surface (derived from its
        // own randomSeed, so it's the SAME point every frame — a stable
        // convergence rather than a jittering one), tapering its seek speed
        // down as it arrives so it settles instead of overshooting/orbiting.
        Bounds bounds = ComputeBounds(device);
        float t = 0f;
        while (t < MagnetizeDuration)
        {
            t += AnimationSpeed.Delta(AnimationKind.Packets);
            if (device == null) break; // device could've been removed mid-effect
            SteerToward(ps, bounds);
            yield return null;
        }

        // Phase 3 — stick: velocity settles to zero so the sparks visibly
        // cling to the surface for whatever's left of their lifetime, rather
        // than drifting off it once the seek force stops.
        float holdT = 0f;
        while (holdT < StickHold)
        {
            holdT += AnimationSpeed.Delta(AnimationKind.Packets);
            DampVelocityToZero(ps);
            yield return null;
        }

        Destroy(gameObject, AnimationSpeed.Seconds(AnimationKind.Packets, ClingLifetimeTail + 0.5f));
    }

    // Kicks every currently-alive particle radially outward from origin —
    // used at the destination hand-off, where the particles already exist
    // (they've been trailing the packet the whole trip) rather than being
    // freshly spawned, so the "explode" has to be applied as a velocity
    // override instead of an initial-velocity module.
    static void ApplyKick(ParticleSystem ps, Vector3 origin, float speed)
    {
        int count = ps.particleCount;
        if (count == 0) return;

        var particles = new ParticleSystem.Particle[count];
        int written = ps.GetParticles(particles);
        for (int i = 0; i < written; i++)
        {
            Vector3 dir = particles[i].position - origin;
            if (dir.sqrMagnitude < 0.0001f) dir = Random.onUnitSphere;
            particles[i].velocity = dir.normalized * speed;
        }
        ps.SetParticles(particles, written);
    }

    // The swarm trail's own particles are short-lived by design (TrailLifetime,
    // tuned for a fast-drifting continuous trickle) — without topping this up,
    // most of them would simply expire mid-convergence at the destination
    // instead of surviving the whole explode/magnetize/stick/fade hand-off.
    static void ExtendLifetimes(ParticleSystem ps, float minRemaining)
    {
        int count = ps.particleCount;
        if (count == 0) return;

        var particles = new ParticleSystem.Particle[count];
        int written = ps.GetParticles(particles);
        for (int i = 0; i < written; i++)
            if (particles[i].remainingLifetime < minRemaining)
                particles[i].remainingLifetime = minRemaining;
        ps.SetParticles(particles, written);
    }

    static void SteerToward(ParticleSystem ps, Bounds bounds)
    {
        int count = ps.particleCount;
        if (count == 0) return;

        var particles = new ParticleSystem.Particle[count];
        int written = ps.GetParticles(particles);
        for (int i = 0; i < written; i++)
        {
            Vector3 targetPoint = SurfaceTargetFor(particles[i].randomSeed, bounds);
            Vector3 toTarget = targetPoint - particles[i].position;
            float dist = toTarget.magnitude;

            float speedScale = Mathf.Clamp01(dist / ArrivalRadius); // tapers off right as it arrives, instead of overshooting/orbiting
            Vector3 desired = dist > 0.0001f ? toTarget / dist * (ClingKickSpeed * speedScale) : Vector3.zero;
            particles[i].velocity = Vector3.Lerp(particles[i].velocity, desired, 8f * AnimationSpeed.Delta(AnimationKind.Packets));
        }
        ps.SetParticles(particles, written);
    }

    static void DampVelocityToZero(ParticleSystem ps)
    {
        int count = ps.particleCount;
        if (count == 0) return;

        var particles = new ParticleSystem.Particle[count];
        int written = ps.GetParticles(particles);
        for (int i = 0; i < written; i++)
            particles[i].velocity = Vector3.Lerp(particles[i].velocity, Vector3.zero, 10f * AnimationSpeed.Delta(AnimationKind.Packets));
        ps.SetParticles(particles, written);
    }

    // A stable pseudo-random point on the device's own bounding-box surface,
    // derived deterministically from the particle's randomSeed (constant for
    // that particle's whole life) — projects a random point in the box onto
    // whichever face its largest axis is closest to.
    static Vector3 SurfaceTargetFor(uint seed, Bounds bounds)
    {
        float r0 = HashToSigned(seed);
        float r1 = HashToSigned(seed ^ 0x9E3779B9u);
        float r2 = HashToSigned(seed ^ 0x85EBCA6Bu);
        Vector3 p = new Vector3(r0, r1, r2);

        float ax = Mathf.Abs(p.x), ay = Mathf.Abs(p.y), az = Mathf.Abs(p.z);
        if (ax >= ay && ax >= az) p.x = Mathf.Sign(p.x);
        else if (ay >= ax && ay >= az) p.y = Mathf.Sign(p.y);
        else p.z = Mathf.Sign(p.z);

        return bounds.center + Vector3.Scale(p, bounds.extents);
    }

    // Cheap deterministic integer hash (xorshift) -> a float in [-1, 1].
    static float HashToSigned(uint seed)
    {
        seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
        float unit = (seed & 0x00FFFFFF) / (float)0x01000000; // [0,1)
        return unit * 2f - 1f;
    }

    static Bounds ComputeBounds(NetworkDevice device)
    {
        var renderers = device.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(device.transform.position, Vector3.one * 0.5f);

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);
        return b;
    }
}
