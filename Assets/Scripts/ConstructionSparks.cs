using UnityEngine;

/// <summary>
/// Small runtime-built spark particle effects used to dress up
/// ConstructionRevealEffect and cable reveals — a continuous trickle that
/// follows the construction front while it's actively drawing, plus a one-shot
/// burst when a reveal finishes. Both share the same "electric arc" look
/// (see ApplyElectricLook) so the finished-building sparks read as the same
/// effect as the ones that ran along the blueprint while it was drawing, not
/// a different particle system. Built entirely in code (no prefabs), matching
/// every other runtime-generated visual in this project.
/// </summary>
public static class ConstructionSparks
{
    // Pushes the spark color above 1.0 so it blooms under URP's Bloom (see
    // DefaultVolumeProfile.asset / ConstructionRevealEffect's own glow
    // multipliers for the blueprint material) instead of rendering as a flat,
    // non-glowing dot.
    private const float GlowMultiplier = 2.5f;

    private static Material s_material;

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

    // Erratic jitter + a short streak trailing each particle — the two things
    // that read as "electric arc" rather than "floating ember". Shared by the
    // continuous drawing-phase trail and the one-shot completion burst so both
    // are visibly the same kind of spark. Expects an already-glow-boosted color.
    static void ApplyElectricLook(ParticleSystem ps, Color glowColor, float trailLifetime)
    {
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.6f;
        noise.frequency = 3f;
        noise.scrollSpeed = 2f;
        noise.damping = true;

        var trails = ps.trails;
        trails.enabled = true;
        trails.ratio = 1f;
        trails.lifetime = trailLifetime;
        trails.minVertexDistance = 0.01f;
        trails.worldSpace = true;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = FadeOutGradient(glowColor);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = SparkMaterial();
        renderer.trailMaterial = SparkMaterial();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
    }

    /// <summary>
    /// Starts a continuous trickle of electric-arc-style sparks — short, fast,
    /// jittery streaks (Satisfactory's building-effect sparks were the
    /// reference) rather than soft floating embers. Reposition/reshape it
    /// every frame with <see cref="UpdateAsSphere"/> or <see cref="UpdateAsPoint"/>
    /// while the construction front is moving, then <see cref="StopAndDestroy"/>
    /// once it's done.
    /// </summary>
    public static ParticleSystem CreateTrail(Color color)
    {
        var go = new GameObject("ConstructionSparkTrail");
        var ps = go.AddComponent<ParticleSystem>();
        Color glowColor = color * GlowMultiplier;

        var main = ps.main;
        main.startLifetime = 0.15f;
        main.startSpeed = 1.4f;
        main.startSize = 0.025f;
        main.startColor = glowColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.loop = true;
        main.playOnAwake = false;

        var emission = ps.emission;
        emission.rateOverTime = 55f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.01f;
        shape.radiusThickness = 0f; // emit from the sphere's surface only, not its volume

        ApplyElectricLook(ps, glowColor, 0.4f);

        ps.Play();
        return ps;
    }

    /// <summary>Grows the trail's emission sphere in place — for a front expanding from a fixed origin.</summary>
    public static void UpdateAsSphere(ParticleSystem ps, Vector3 center, float radius)
    {
        if (ps == null) return;
        ps.transform.position = center;
        var shape = ps.shape;
        shape.radius = Mathf.Max(radius, 0.01f);
    }

    /// <summary>Moves the trail to a single travelling point — for a front moving along a path (e.g. a cable).</summary>
    public static void UpdateAsPoint(ParticleSystem ps, Vector3 position)
    {
        if (ps == null) return;
        ps.transform.position = position;
    }

    /// <summary>Stops new emission and destroys the emitter once its already-spawned particles have faded.</summary>
    public static void StopAndDestroy(ParticleSystem ps)
    {
        if (ps == null) return;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Object.Destroy(ps.gameObject, ps.main.startLifetime.constantMax + 0.5f);
    }

    /// <summary>
    /// One-shot burst of sparks, e.g. when a reveal finishes — the same
    /// per-particle look as <see cref="CreateTrail"/> (same speed, size,
    /// lifetime), just fired all at once instead of trickling continuously.
    /// Spawns from the surface of a sphere of <paramref name="spawnRadius"/>
    /// centred on <paramref name="position"/> rather than a single point — for
    /// a device-sized radius (see ConstructionRevealEffect) that shell sits at
    /// or outside the device's actual surface, so sparks read as bursting from
    /// around/outside it instead of erupting from its buried center.
    /// </summary>
    public static void EmitBurst(Vector3 position, Color color, int count = 20, float spawnRadius = 0.01f)
    {
        var go = new GameObject("ConstructionSparkBurst");
        go.transform.position = position;
        var ps = go.AddComponent<ParticleSystem>();
        Color glowColor = color * GlowMultiplier;

        var main = ps.main;
        main.startLifetime = 0.15f;
        main.startSpeed = 1.4f;
        main.startSize = 0.025f;
        main.startColor = glowColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.loop = false;
        main.playOnAwake = false;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = Mathf.Max(spawnRadius, 0.01f);
        shape.radiusThickness = 0f; // surface only — never spawns points buried inside the shell

        ApplyElectricLook(ps, glowColor, 0.4f);

        ps.Emit(count);
        Object.Destroy(go, main.startLifetime.constantMax + 0.5f);
    }
}
