using System.Collections;
using UnityEngine;

/// <summary>
/// A checkmark (or, on a failed ping, a cross — see the icon parameter below)
/// that floats above a device and "materializes" the instant a ping result
/// lands on it — the actual "yes/no, that's what happened" payoff. On success
/// this sits on top of PingPacketEffect's own wipe/spark-burst arrival; on
/// failure there's no orb journey to land (a failed ping never walks a Path —
/// see ReachabilityService.Fail), so PingToolManager plays this directly on
/// both the source and destination instead of a top-of-screen banner. Built
/// the same way WorldPulseMarker's arrow is (a billboarded quad sampling a
/// runtime-baked Sprite) rather than a new icon asset — reuses
/// CheckmarkIconBuilder's tick shape (or CrossIconBuilder's, for failure),
/// requested in plain white so this can tint/boost/fade it entirely at
/// runtime via one MaterialPropertyBlock, the same separation WorldPulseMarker's
/// own arrow texture already relies on.
///
/// "Materializes" as a small spark burst at the hover point (the same
/// ConstructionSparks language every other spawn-in effect in this project
/// uses) landing in the same instant the icon itself pops in on an elastic
/// overshoot — the requested bounce. Holds with a gentle idle bob, then
/// fades out and self-destroys.
/// </summary>
public class PingCheckmarkEffect : MonoBehaviour
{
    const float HoverHeight   = 0.55f; // clearance above the device's own visual top point
    const float PopInDuration = 0.22f;
    const float HoldDuration  = 0.9f;
    const float FadeDuration  = 0.35f;
    const float IconSize      = 0.42f; // world-space quad diameter
    const float BobAmplitude  = 0.03f;
    const float BobSpeed      = 3f;
    const float GlowMultiplier = HologramMaterial.GlowMultiplier; // same bloom calibration every glowing effect in this project shares

    /// <param name="icon">Which glyph to pop in — defaults to CheckmarkIconBuilder's
    /// tick (the success case) when left null; pass CrossIconBuilder's X for a
    /// failed ping.</param>
    public static void Play(NetworkDevice device, Color color, Sprite icon = null)
    {
        if (device == null) return;
        var effect = new GameObject("PingResultIcon").AddComponent<PingCheckmarkEffect>();
        effect.StartCoroutine(effect.Run(device, color, icon));
    }

    IEnumerator Run(NetworkDevice device, Color color, Sprite icon)
    {
        Vector3 basePos = device.VisualTopPoint() + Vector3.up * HoverHeight;

        var quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quadGo.name = "ResultIcon";
        quadGo.transform.SetParent(transform, false);
        Destroy(quadGo.GetComponent<Collider>()); // must never intercept placement/ping raycasts

        var renderer = quadGo.GetComponent<Renderer>();
        Shader shader = Shader.Find("Sprites/Default");
        Sprite glyph = icon != null ? icon : CheckmarkIconBuilder.GetSprite(Color.white);
        var material = new Material(shader) { mainTexture = glyph.texture };
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var block = new MaterialPropertyBlock();
        Transform quad = quadGo.transform;
        quad.position = basePos;
        quad.localScale = Vector3.zero;

        Camera cam = Camera.main;

        // Materialize — a quick spark burst right where the icon is about to
        // pop in, timed to land in the same instant as the bounce below.
        ConstructionSparks.EmitBurst(basePos, color, count: 18, spawnRadius: 0.12f);

        float t = 0f;
        while (t < PopInDuration)
        {
            t += Time.deltaTime;
            float p = EaseOutBack(Mathf.Clamp01(t / PopInDuration));
            quad.localScale = Vector3.one * IconSize * Mathf.Max(p, 0f);
            Billboard(quad, ref cam);
            SetTint(renderer, block, color, 1f);
            yield return null;
        }
        quad.localScale = Vector3.one * IconSize;

        float held = 0f;
        while (held < HoldDuration)
        {
            held += Time.deltaTime;
            quad.position = basePos + Vector3.up * Mathf.Sin(held * BobSpeed) * BobAmplitude;
            Billboard(quad, ref cam);
            yield return null;
        }

        t = 0f;
        while (t < FadeDuration)
        {
            t += Time.deltaTime;
            float alpha = 1f - Mathf.Clamp01(t / FadeDuration);
            Billboard(quad, ref cam);
            SetTint(renderer, block, color, alpha);
            yield return null;
        }

        Destroy(material);
        Destroy(gameObject);
    }

    static void Billboard(Transform quad, ref Camera cam)
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        // A default Quad's visible face normal is its local -Z, so LookRotation's
        // target direction needs to be AWAY from the camera (quad -> camera would
        // point the normal away from the camera instead, showing the quad's back
        // face — invisible on WorldPulseMarker's symmetric arrow, but mirrored the
        // checkmark's asymmetric tick backwards).
        quad.rotation = Quaternion.LookRotation(quad.position - cam.transform.position, Vector3.up);
    }

    static void SetTint(Renderer renderer, MaterialPropertyBlock block, Color color, float alpha)
    {
        block.Clear();
        block.SetColor("_Color", new Color(color.r * GlowMultiplier, color.g * GlowMultiplier, color.b * GlowMultiplier, alpha));
        renderer.SetPropertyBlock(block);
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float f = x - 1f;
        return 1f + c3 * f * f * f + c1 * f * f;
    }
}
