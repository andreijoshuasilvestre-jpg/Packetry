using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// White dust/debris puff for when a scaled-up piece of the level (ground,
/// a wall) finishes materializing — or starts dematerializing on the way
/// out, see GameBootstrap.PlayExitSequence. Same hand-rolled, built-in-code
/// ParticleSystem approach as ConstructionSparks, but a deliberately
/// different look: small, quick, widely-scattered flecks that fly outward
/// and fade fast, not ConstructionSparks' jittery "electric arc" streaks —
/// a construction piece popping into place reads as a physical thump
/// kicking up debris, not the same electrical effect used for cable/device
/// reveals. (An earlier pass used a handful of big, slow, muted-tan puffs
/// clustered near the center — read as thick and sluggish rather than a
/// sharp kick of dust, and randomDirectionAmount was never set so every
/// particle shot straight up together instead of scattering outward.)
/// </summary>
public static class ConstructionDust
{
    static Material s_material;
    static Texture2D s_texture;

    // Every burst still around — pruned as they're destroyed (see AnyAlive).
    static readonly List<ParticleSystem> s_bursts = new List<ParticleSystem>();

    /// <summary>
    /// True while any burst still has a particle on screen. Checked rather
    /// than timed off startLifetime: particles step on scaled time, capped
    /// per frame (Time.maximumParticleDeltaTime), so at a low frame rate a
    /// puff outlives its nominal lifetime in real seconds.
    /// </summary>
    public static bool AnyAlive
    {
        get
        {
            s_bursts.RemoveAll(ps => ps == null);
            foreach (var ps in s_bursts)
                if (ps.particleCount > 0) return true;
            return false;
        }
    }

    // A soft round falloff (not RadialGlowSprite's sharper glow falloff —
    // this needs to look like a hazy puff, not a light source) baked once
    // and reused for every burst.
    static Texture2D DustTexture()
    {
        if (s_texture != null) return s_texture;

        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.5f;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center) / radius;
                float alpha = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(dist)); // gradual, hazy falloff
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        s_texture = tex;
        return s_texture;
    }

    static Material DustMaterial()
    {
        if (s_material != null) return s_material;
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
        s_material = new Material(shader);
        s_material.mainTexture = DustTexture();
        return s_material;
    }

    static Gradient FadeOutGradient(Color color)
    {
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(color.a, 0.15f), new GradientAlphaKey(0f, 1f) });
        return grad;
    }

    /// <summary>
    /// One-shot puff of dust scattering outward across the ground from a
    /// flat disc of <paramref name="radius"/> centred on
    /// <paramref name="position"/> — call when a scaled-up piece (ground, a
    /// wall) finishes growing in, or right as it starts shrinking away on
    /// exit.
    /// </summary>
    public static void EmitBurst(Vector3 position, float radius, int count = 40)
    {
        var go = new GameObject("ConstructionDustBurst");
        go.transform.position = position;
        var ps = go.AddComponent<ParticleSystem>();

        Color dustColor = new Color(1f, 1f, 1f, 0.55f); // white

        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.55f); // quick — matches the faster startSpeed instead of lingering
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4.5f); // fast kick, not a slow drift
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f); // small flecks, not big puffs
        main.startColor = dustColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.1f; // drifts gently upward, like kicked-up dust settling
        main.loop = false;
        main.playOnAwake = false;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle; // flat disc across the ground, not a sphere
        // A Circle lies in the emitter's local XY plane (and particles leave it
        // only within that plane) — upright on an unrotated GameObject, which
        // read as a flat sheet spreading along a single axis. Tipped to XZ so it
        // actually lies on the ground and scatters both ways across it.
        shape.rotation = new Vector3(-90f, 0f, 0f);
        shape.radius = Mathf.Max(radius, 0.2f);
        shape.radiusThickness = 1f; // fill the whole disc, not just its rim — a puff spreads across the footprint, not off a ring
        shape.randomDirectionAmount = 0.75f; // scatters outward in varied directions instead of every particle flying straight up together, which is what read as "clustered"

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.4f, 1f), new Keyframe(1f, 1.4f)));

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = FadeOutGradient(dustColor);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = DustMaterial();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;

        ps.Emit(count);
        s_bursts.Add(ps);
        Object.Destroy(go, main.startLifetime.constantMax + 0.5f);
    }
}
