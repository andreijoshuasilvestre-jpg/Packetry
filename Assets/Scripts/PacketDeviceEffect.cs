using System.Collections;
using UnityEngine;

/// <summary>
/// One-shot "packet touched this device" feedback: a brief glow flash over
/// the model's current colors plus a grounded squash-stretch bounce (scaled
/// about a chosen visual-root transform's own origin, which sits at the
/// model's base, so the bounce reads as a jelly wobble rather than the
/// object lifting off the ground). Fired by PacketManager every time a
/// packet spawns out of, passes through, or arrives at a device — cheap and
/// frequent, so unlike ConstructionRevealEffect it never swaps materials,
/// only nudges MaterialPropertyBlocks and localScale. Also reused as the
/// placement "landing" pop for devices and tables alike (see the
/// NetworkDevice overload below and TableManager's use of the GameObject one).
/// </summary>
public class PacketDeviceEffect : MonoBehaviour
{
    const float Duration    = 0.4f;
    const float ScaleAmp    = 0.09f; // peak squash/stretch magnitude
    const float GlowAmp     = 0.5f;  // peak blend toward glow color, 0..1
    const float Frequency   = 16f;   // wobble speed, rad/s
    const float Decay       = 9f;    // oscillation damping

    // Multiplier passed to Play() for moments that need the bounce to read as
    // a bigger, more emphatic "pop" than the routine packet-touch flash —
    // e.g. a device settling right after its placement animation, or right
    // after being dropped from a drag. Same wobble shape, just louder.
    public const float PlacementIntensity = 2.4f;

    static MaterialPropertyBlock s_block;

    Transform visual;
    Renderer[] renderers;
    Color[] baseColors;
    string[] colorProps;
    Color glowColor;
    float intensity;
    AnimationKind kind;
    Coroutine routine;

    /// <summary>Plays (or restarts, if already mid-flash) the effect on device.
    /// intensity scales the squash/stretch and glow amplitude — 1 is the
    /// default packet-touch feedback, see PlacementIntensity for a stronger
    /// preset. kind is which animation speed setting it follows: a packet
    /// touch by default, AnimationKind.Build for a placement landing.</summary>
    public static void Play(NetworkDevice device, Color tint, float intensity = 1f, AnimationKind kind = AnimationKind.Packets)
    {
        if (device == null) return;
        Play(device.gameObject, device.visualRoot != null ? device.visualRoot : device.transform, tint, intensity, kind);
    }

    /// <summary>Same effect, for objects that aren't a NetworkDevice (e.g. a
    /// just-placed Table) — visualRoot is whatever transform should be scaled
    /// for the squash/stretch; pass the object's own transform if it has no
    /// separate visual child.</summary>
    public static void Play(GameObject target, Transform visualRoot, Color tint, float intensity = 1f,
        AnimationKind kind = AnimationKind.Packets)
    {
        if (target == null) return;
        var effect = target.GetComponent<PacketDeviceEffect>();
        if (effect == null) effect = target.AddComponent<PacketDeviceEffect>();
        effect.Restart(visualRoot, tint, intensity, kind);
    }

    void Restart(Transform visualRoot, Color tint, float intensity, AnimationKind kind)
    {
        if (routine != null) { StopCoroutine(routine); RestoreColors(); }

        this.intensity = intensity;
        this.kind = kind;
        visual = visualRoot != null ? visualRoot : transform;

        // Not every renderer under a device is the "real" material anymore —
        // DeviceRimLight stamps an additive Custom/RimGlow shell (no
        // _BaseColor/_Color, just _RimColor) alongside each one. Skip
        // anything that exposes neither property rather than assuming _Color
        // as a blind fallback, which errored on those shells every time this
        // effect played.
        var allRenderers = GetComponentsInChildren<Renderer>();
        var tempRenderers = new System.Collections.Generic.List<Renderer>(allRenderers.Length);
        var tempProps = new System.Collections.Generic.List<string>(allRenderers.Length);
        var tempColors = new System.Collections.Generic.List<Color>(allRenderers.Length);

        s_block ??= new MaterialPropertyBlock();
        foreach (Renderer r in allRenderers)
        {
            string prop = ColorPropertyOf(r.sharedMaterial);
            if (prop == null) continue;

            // Read whatever's actually showing right now (subnet tint, drag
            // highlight, ...) rather than the plain material color, so the
            // flash brightens the current look instead of resetting it.
            r.GetPropertyBlock(s_block);
            tempRenderers.Add(r);
            tempProps.Add(prop);
            tempColors.Add(s_block.isEmpty ? r.sharedMaterial.GetColor(prop) : s_block.GetColor(prop));
        }
        renderers  = tempRenderers.ToArray();
        colorProps = tempProps.ToArray();
        baseColors = tempColors.ToArray();

        glowColor = Color.Lerp(tint, Color.white, 0.6f);
        routine = StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        float t = 0f;
        while (t < Duration)
        {
            t += AnimationSpeed.Delta(kind);
            float decay = Mathf.Exp(-Decay * t);
            float wob   = Mathf.Sin(t * Frequency) * decay;

            float scaleAmp = ScaleAmp * intensity;
            if (visual != null)
                visual.localScale = new Vector3(1f - wob * scaleAmp * 0.5f, 1f + wob * scaleAmp, 1f - wob * scaleAmp * 0.5f);

            float glow = decay * GlowAmp * intensity;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                s_block.Clear();
                s_block.SetColor(colorProps[i], Color.Lerp(baseColors[i], glowColor, glow));
                renderers[i].SetPropertyBlock(s_block);
            }

            yield return null;
        }

        RestoreColors();
        routine = null;
        Destroy(this);
    }

    // Null (not a blind "_Color" fallback) when a material exposes neither —
    // e.g. DeviceRimLight's additive Custom/RimGlow shells, which only have
    // _RimColor. Callers skip any renderer this returns null for.
    static string ColorPropertyOf(Material mat)
    {
        if (mat == null) return null;
        if (mat.HasProperty("_BaseColor")) return "_BaseColor";
        if (mat.HasProperty("_Color")) return "_Color";
        return null;
    }

    void RestoreColors()
    {
        if (visual != null) visual.localScale = Vector3.one;
        if (renderers == null) return;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            s_block.Clear();
            s_block.SetColor(colorProps[i], baseColors[i]);
            renderers[i].SetPropertyBlock(s_block);
        }
    }
}
