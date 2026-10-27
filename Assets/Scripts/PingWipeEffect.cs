using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Directional glow "wipe" played over one device's own materials for the
/// duration a ping packet is touching it — the same shader-swap technique
/// ConstructionRevealEffect uses (temporarily replace every renderer's
/// materials with an instance of a dedicated effect shader, animate one
/// scalar, restore the originals), but for an already-built, currently-live
/// device rather than a spawn-in reveal: the model stays fully lit and
/// recognizable throughout, just tinted/banded by Custom/PingGlowWipe (see
/// that shader for the exact math). Driven entirely by PingPacketEffect,
/// which supplies the origin point — always the exact port the packet is
/// physically entering or leaving through, never just the device center — so
/// the wipe reads as originating from the right side of the model.
///
/// PCs no longer get this at all — their callers play PCMonitor's screen
/// flash + glyph instead and only fall back to this if a PC somehow has no
/// monitor. In that fallback it's scoped to just the screen part (named
/// "PC_Screen" by both DeviceModelBuilder and DevicePrefabBuilder — see
/// FindWipeTargets) rather than the whole chassis. Routers (a ping's mid-path
/// forwarding wipe) get the whole-model treatment; the caller separately plays
/// PacketDeviceEffect alongside this for the general glow + bounce.
///
///  PlayForward — sweeps outward from origin, 0% covered -> fully covered.
///  "the packet has arrived here and its glow is spreading across the
///  model". Used long-form for the ping's actual destination, and
///  short-form (quickFade) for a router mid-path forwarding the packet — the
///  one hop type that meaningfully *processes* it, unlike a switch's plain
///  transparent flood-through.
///
///  PlayGlowPulse — no sweeping front at all: the whole model glows/breathes
///  uniformly (build -> hold -> fade), used on the source device instead of a
///  directional wipe — the source isn't being "arrived at" from one side, the
///  whole thing is charging up before it releases the packet, which reads
///  better as a plain pulse. onPeak fires right as the glow hits full
///  brightness — PingPacketEffect uses that moment to spawn the traveling
///  orb and burst its own spark swarm outward from the exit port.
/// </summary>
public class PingWipeEffect : MonoBehaviour
{
    const float FullCoverPoint   = 1.15f; // safety overshoot past 1 so the band fully clears every fragment
    const float FadeOutDuration  = 0.2f;
    const float BandWidth        = 0.18f;
    const float BandIntensity    = 2.6f;
    const float GlowBoost        = 2.0f;  // pushes _GlowColor above 1.0 so URP Bloom picks it up, matching HologramMaterial/ConstructionRevealEffect's own multipliers
    const float GridCellSize     = 0.1f;  // local units — see PingGlowWipe.shader's GridMask
    const float GridLineThickness = 0.012f;
    const float GridIntensity    = 1.6f;

    static Shader s_shader;

    // A device can now legitimately have two of these effects overlapping on
    // the same renderer — e.g. an HttpRequest's arrival wipe on the
    // destination is still fading out the instant PacketManager.Finish auto-
    // spawns the HttpResponse, whose own PlayGlowPulse charges up on that same
    // device (now the response's source). TrySetup used to re-sample
    // renderers[r].sharedMaterials as "the original" on every call — the
    // second effect would capture the FIRST effect's own temporary
    // Custom/PingGlowWipe materials as its "original", and when the first
    // effect's Restore() ran, it destroyed those same Material instances out
    // from under the second effect's still-held reference — so that effect's
    // own eventual Restore() reassigned a destroyed material, which Unity
    // renders as flat magenta. These two dictionaries make every renderer's
    // REAL original materials a single fact, captured once by whichever
    // effect touches that renderer first and shared by every later
    // overlapping effect, with a ref count so only the last one to finish
    // actually restores it.
    static readonly Dictionary<Renderer, Material[]> s_trueOriginals = new();
    static readonly Dictionary<Renderer, int> s_activeCount = new();

    public static void PlayForward(NetworkDevice device, Vector3 origin, Color glowColor, float duration,
        Action onFullyCovered = null, bool quickFade = false)
    {
        if (device == null) { onFullyCovered?.Invoke(); return; }
        var effect = device.gameObject.AddComponent<PingWipeEffect>();
        effect.StartCoroutine(effect.RunForward(device, origin, glowColor, duration, onFullyCovered, quickFade));
    }

    public static void PlayGlowPulse(NetworkDevice device, Vector3 origin, Color glowColor,
        float buildDuration, float holdDuration, float fadeDuration, Action onPeak = null)
    {
        if (device == null) { onPeak?.Invoke(); return; }
        var effect = device.gameObject.AddComponent<PingWipeEffect>();
        effect.StartCoroutine(effect.RunGlowPulse(device, origin, glowColor, buildDuration, holdDuration, fadeDuration, onPeak));
    }

    // ── Sustained arrival glow ───────────────────────────────────────────────
    // Coalesces a burst of packets landing on the same device in quick
    // succession into ONE continuous glow — sweep in on the first arrival,
    // hold fully lit (re-tinting live if a later arrival is a different
    // packet type) while arrivals keep refreshing it, and only fade out once
    // SustainGrace passes with nothing new landing — instead of each packet
    // independently retriggering its own full sweep-in/fade-out (what
    // PlayForward did before, called once per delivered packet: a burst read
    // as the screen flashing over and over rather than one steady "actively
    // receiving" glow). See PacketManager.PlayDeliveryEffects.
    const float SustainBuildDuration = 0.18f;
    const float SustainGrace         = 0.35f; // no new arrival within this long => the burst is over
    const float SustainFadeDuration  = 0.3f;

    static readonly Dictionary<NetworkDevice, PingWipeEffect> s_sustained = new();

    float sustainLastArrival;
    Color sustainColor;

    /// <summary>Call once per delivered packet. The first call for a device
    /// starts the glow and fires onPeak once the sweep-in finishes (same
    /// "materializes right as it's fully covered" beat PlayForward gave every
    /// packet before); every call after that, while the glow is still active,
    /// just refreshes its fade-out countdown and re-tints it to this packet's
    /// color, firing onPeak immediately since the screen is already lit.</summary>
    public static void NotifyArrival(NetworkDevice device, Vector3 origin, Color glowColor, Action onPeak = null)
    {
        if (device == null) { onPeak?.Invoke(); return; }

        if (s_sustained.TryGetValue(device, out var existing) && existing != null)
        {
            existing.sustainColor = glowColor;
            existing.sustainLastArrival = Time.time;
            onPeak?.Invoke();
            return;
        }

        var effect = device.gameObject.AddComponent<PingWipeEffect>();
        effect.sustainColor = glowColor;
        effect.sustainLastArrival = Time.time;
        s_sustained[device] = effect;
        effect.StartCoroutine(effect.RunSustained(device, origin, onPeak));
    }

    IEnumerator RunSustained(NetworkDevice device, Vector3 origin, Action onPeak)
    {
        if (!TrySetup(device, origin, sustainColor, out Renderer[] renderers, out Material[][] originals, out Material[][] live))
        {
            s_sustained.Remove(device);
            onPeak?.Invoke();
            Destroy(this);
            yield break;
        }

        // Same directional sweep PlayForward uses — still reads as "entering
        // from the port" for the burst's first packet.
        yield return AnimateThreshold(live, 0f, FullCoverPoint, SustainBuildDuration, EaseOut);
        onPeak?.Invoke();

        while (Time.time - sustainLastArrival < AnimationSpeed.Seconds(AnimationKind.Packets, SustainGrace))
        {
            SetGlowColor(live, sustainColor * GlowBoost);
            yield return null;
        }

        yield return FadeFillTo(live, 1f, 0f, SustainFadeDuration);

        s_sustained.Remove(device);
        Restore(renderers, originals, live);
        Destroy(this);
    }

    IEnumerator RunForward(NetworkDevice device, Vector3 origin, Color glowColor, float duration, Action onFullyCovered, bool quickFade)
    {
        if (!TrySetup(device, origin, glowColor, out Renderer[] renderers, out Material[][] originals, out Material[][] live))
        {
            onFullyCovered?.Invoke();
            Destroy(this);
            yield break;
        }

        yield return AnimateThreshold(live, 0f, FullCoverPoint, duration, EaseOut);

        onFullyCovered?.Invoke();

        yield return FadeFillTo(live, 1f, 0f, quickFade ? FadeOutDuration * 0.6f : FadeOutDuration);

        Restore(renderers, originals, live);
        Destroy(this);
    }

    IEnumerator RunGlowPulse(NetworkDevice device, Vector3 origin, Color glowColor,
        float buildDuration, float holdDuration, float fadeDuration, Action onPeak)
    {
        if (!TrySetup(device, origin, glowColor, out Renderer[] renderers, out Material[][] originals, out Material[][] live))
        {
            onPeak?.Invoke();
            Destroy(this);
            yield break;
        }

        // Threshold pinned permanently at "fully covering" — unlike
        // PlayForward there's no sweeping front here, just FillStrength
        // breathing the whole model's glow in and out uniformly.
        SetThreshold(live, FullCoverPoint);

        yield return FadeFillTo(live, 0f, 1f, buildDuration);

        onPeak?.Invoke();

        if (holdDuration > 0f) yield return new WaitForSeconds(AnimationSpeed.Seconds(AnimationKind.Packets, holdDuration));

        yield return FadeFillTo(live, 1f, 0f, fadeDuration);

        Restore(renderers, originals, live);
        Destroy(this);
    }

    bool TrySetup(NetworkDevice device, Vector3 origin, Color glowColor, out Renderer[] renderers, out Material[][] originals, out Material[][] live)
    {
        renderers = FindWipeTargets(device);
        originals = new Material[renderers.Length][];
        live      = new Material[renderers.Length][];
        if (renderers.Length == 0) return false;

        if (s_shader == null) s_shader = Shader.Find("Custom/PingGlowWipe");
        if (s_shader == null)
        {
            Debug.LogWarning("PingWipeEffect: Custom/PingGlowWipe shader not found; skipping wipe.");
            return false;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        float maxDist = MaxCornerDistance(bounds, origin);
        Color boostedGlow = glowColor * GlowBoost;

        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer rend = renderers[r];
            if (!s_trueOriginals.TryGetValue(rend, out Material[] trueOriginal))
            {
                trueOriginal = rend.sharedMaterials;
                s_trueOriginals[rend] = trueOriginal;
            }
            s_activeCount[rend] = s_activeCount.TryGetValue(rend, out int count) ? count + 1 : 1;

            Material[] src = trueOriginal;
            originals[r] = trueOriginal;

            var built = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var m = new Material(s_shader);
                if (src[i] != null && src[i].HasProperty("_BaseColor"))
                    m.SetColor("_BaseColor", src[i].GetColor("_BaseColor"));
                m.SetColor("_GlowColor", boostedGlow);
                m.SetFloat("_BandWidth", BandWidth);
                m.SetFloat("_BandIntensity", BandIntensity);
                m.SetFloat("_FillStrength", 1f);
                m.SetVector("_BoundsMin", bounds.min);
                m.SetVector("_BoundsMax", bounds.max);
                m.SetVector("_OriginPoint", origin);
                m.SetFloat("_MaxDist", maxDist);
                m.SetFloat("_GridCellSize", GridCellSize);
                m.SetFloat("_GridLineThickness", GridLineThickness);
                m.SetFloat("_GridIntensity", GridIntensity);
                m.SetFloat("_WipeThreshold", 0f);
                built[i] = m;
            }
            live[r] = built;
            rend.materials = built;
        }
        return true;
    }

    // A PC's own "PC_Screen" part (see DeviceModelBuilder/DevicePrefabBuilder)
    // if it has one, otherwise every renderer on the device — Switch/Router
    // have no screen to scope to, and an older save's PC somehow missing the
    // part falls back to the old whole-model behavior rather than glowing
    // nothing at all.
    static Renderer[] FindWipeTargets(NetworkDevice device)
    {
        Renderer[] all = device.GetComponentsInChildren<Renderer>();
        if (device.deviceType != DeviceType.PC) return all;

        List<Renderer> screen = null;
        foreach (var r in all)
        {
            if (r.gameObject.name != "PC_Screen") continue;
            screen ??= new List<Renderer>();
            screen.Add(r);
        }
        return screen != null ? screen.ToArray() : all;
    }

    // The farthest any of the device's own bounding-box corners sits from
    // origin — origin is a port point on the surface, not necessarily a
    // corner, so (unlike ConstructionRevealEffect's NearestBoundsCorner) this
    // can't just use the bounds diagonal; it has to measure from the actual
    // origin to guarantee the shader's mask never exceeds ~1 for any
    // fragment on the model.
    static float MaxCornerDistance(Bounds bounds, Vector3 origin)
    {
        Vector3 min = bounds.min, max = bounds.max;
        float best = 0f;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3(
                (i & 1) != 0 ? max.x : min.x,
                (i & 2) != 0 ? max.y : min.y,
                (i & 4) != 0 ? max.z : min.z);
            best = Mathf.Max(best, Vector3.Distance(corner, origin));
        }
        return Mathf.Max(best, 0.0001f);
    }

    static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

    static IEnumerator AnimateThreshold(Material[][] live, float from, float to, float duration, Func<float, float> ease)
    {
        float t = 0f;
        while (t < duration)
        {
            t += AnimationSpeed.Delta(AnimationKind.Packets);
            float p = ease(Mathf.Clamp01(t / Mathf.Max(duration, 0.0001f)));
            SetThreshold(live, Mathf.Lerp(from, to, p));
            yield return null;
        }
        SetThreshold(live, to);
    }

    static IEnumerator FadeFillTo(Material[][] live, float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += AnimationSpeed.Delta(AnimationKind.Packets);
            float p = Mathf.Clamp01(t / Mathf.Max(duration, 0.0001f));
            SetFill(live, Mathf.Lerp(from, to, p));
            yield return null;
        }
        SetFill(live, to);
    }

    static void SetThreshold(Material[][] live, float v)
    {
        foreach (var arr in live)
            foreach (var m in arr)
                if (m != null) m.SetFloat("_WipeThreshold", v);
    }

    static void SetFill(Material[][] live, float v)
    {
        foreach (var arr in live)
            foreach (var m in arr)
                if (m != null) m.SetFloat("_FillStrength", v);
    }

    static void SetGlowColor(Material[][] live, Color boostedColor)
    {
        foreach (var arr in live)
            foreach (var m in arr)
                if (m != null) m.SetColor("_GlowColor", boostedColor);
    }

    static void Restore(Renderer[] renderers, Material[][] originals, Material[][] live)
    {
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer rend = renderers[r];

            // Only the LAST of any overlapping effects on this renderer
            // actually restores it and destroys its own materials — an
            // earlier one finishing while another is still mid-wipe must
            // leave the renderer showing whichever effect is still live (see
            // the class-level comment on s_trueOriginals/s_activeCount).
            int remaining = 0;
            if (rend != null && s_activeCount.TryGetValue(rend, out int count))
            {
                remaining = count - 1;
                if (remaining <= 0) s_activeCount.Remove(rend);
                else s_activeCount[rend] = remaining;
            }

            if (rend != null && remaining <= 0)
            {
                rend.materials = originals[r];
                s_trueOriginals.Remove(rend);
            }

            if (live[r] != null)
                foreach (var m in live[r])
                    Destroy(m);
        }
    }
}
