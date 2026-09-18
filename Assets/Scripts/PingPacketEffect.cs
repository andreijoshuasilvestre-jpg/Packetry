using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays a ping's full visual "journey" — a small glowing orb that visibly
/// departs the source device, travels cable-to-cable along the exact Path
/// ReachabilityService.Ping walked (reusing Cable.SampleCurve/PathLength, the
/// same idiom PacketManager's own real packets use), and arrives at the
/// destination. Accounts for how each hop type actually behaves:
///   - PC/Switch mid-path: a switch is a transparent L2 flooder (see
///     ReachabilityService.ExpandFlood) — the orb barely pauses, just a
///     PacketDeviceEffect touch-bounce as it passes through.
///   - Router mid-path: the one hop that actually *does* something (validates
///     the entry interface, forwards out another) — gets the touch-bounce
///     plus a quick fire-and-forget PingWipeEffect.PlayForward pulse so it
///     visibly reads as "processed here", still without stopping the orb.
/// Bookended by PingWipeEffect and MagnetizingSparks: the source glows
/// uniformly (no directional wipe — it's charging up, not being "arrived at"
/// from one side; on a PC that glow is scoped to just its screen, see
/// PingWipeEffect) and, right as that glow peaks, bursts its own spark swarm
/// outward from the exit port and plays the same whole-device
/// PacketDeviceEffect bounce every other touch on this journey gets; that
/// swarm then chases the orb the whole trip and, on arrival, explodes again
/// and converges/clings across the entire destination device while it plays
/// its own forward wipe (likewise screen-scoped on a PC). Entirely
/// fire-and-forget from PingToolManager's point of view — driven by its own
/// coroutine on a scratch GameObject.
/// </summary>
public class PingPacketEffect : MonoBehaviour
{
    const float TravelSpeed        = 3.2f;  // world units / second along the cable path
    const float SourceGlowBuild    = 0.16f;
    const float SourceGlowHold     = 0.05f;
    const float SourceGlowFade     = 0.22f;
    const float DestWipeDuration   = 0.4f;
    const float RouterWipeDuration = 0.16f;
    const float OrbRadius          = 0.075f;
    const float OrbGlowBoost       = 2.6f;
    const float PopInDuration      = 0.12f;
    const float SquashOutDuration  = 0.08f;

    static Material s_orbMaterial;

    /// <summary>Fire-and-forget: spawns a scratch driver object that plays out the whole
    /// source-to-destination journey, then cleans itself up. Does nothing if path is empty
    /// (a failed ping has no walked path to animate — see PingToolManager).</summary>
    public static void Play(NetworkDevice source, NetworkDevice dest, List<Cable> path, Color color)
    {
        if (source == null || dest == null || path == null || path.Count == 0) return;
        var effect = new GameObject("PingPacketJourney").AddComponent<PingPacketEffect>();
        effect.StartCoroutine(effect.Run(source, dest, path, color));
    }

    IEnumerator Run(NetworkDevice source, NetworkDevice dest, List<Cable> path, Color color)
    {
        List<NetworkDevice> waypoints = PacketManager.BuildWaypoints(source, path);

        // ── 1. Source: uniform glow, peaking right as the packet forms ──────
        int exitPortIndex = source.GetPortIndexForCable(path[0]);
        Vector3 exitPoint = exitPortIndex >= 0 ? source.GetPortWorldPoint(exitPortIndex) : source.VisualTopPoint();

        bool peaked = false;
        PingWipeEffect.PlayGlowPulse(source, exitPoint, color, SourceGlowBuild, SourceGlowHold, SourceGlowFade, () => peaked = true);
        yield return new WaitUntil(() => peaked);
        if (source == null) yield break; // device could've been removed mid-effect

        // ── 2. Spawn the orb, burst the source's own spark swarm outward from
        //      the exit port, and travel hop by hop ──────────────────────────
        Transform orb = CreateOrb(color);
        orb.position = exitPoint;
        yield return PopIn(orb);

        // PingWipeEffect's own glow is scoped to just the source's screen (see
        // that class) — this is the whole-device counterpart every other
        // touch on this journey already gets: the general glow + bounce.
        PacketDeviceEffect.Play(source, color);

        // Smaller, denser, and homed onto the orb every frame rather than
        // just drifting on their initial velocity — see MagnetizingSparks.
        // BurstAt is the "source device explodes with particles" beat; those
        // burst particles join the same swarm, so the very next
        // UpdateSwarmTrail call below starts pulling them onto the packet
        // right along with everything else.
        ParticleSystem sparks = MagnetizingSparks.CreateSwarmTrail(color);
        MagnetizingSparks.BurstAt(sparks, exitPoint, color);

        for (int hop = 0; hop < path.Count; hop++)
        {
            Cable cable = path[hop];
            NetworkDevice from = waypoints[hop];
            NetworkDevice to   = waypoints[hop + 1];
            if (cable == null || from == null || to == null) break; // path broke mid-flight (device/cable removed) — just stop quietly

            bool reversed = cable.DeviceA != from;
            float t = 0f;
            Vector3 prevPos = orb.position;

            while (t < 1f)
            {
                t += TravelSpeed * Time.deltaTime / Mathf.Max(0.0001f, cable.PathLength);
                float clamped  = Mathf.Min(t, 1f);
                float sampleT  = reversed ? 1f - clamped : clamped;
                Vector3 pos = cable.SampleCurve(Mathf.Clamp01(sampleT));

                ApplyStretch(orb, pos, prevPos);
                orb.position = pos;
                prevPos = pos;
                MagnetizingSparks.UpdateSwarmTrail(sparks, pos);
                yield return null;
            }

            bool isFinalHop = hop == path.Count - 1;
            if (!isFinalHop)
            {
                // A mid-path device the orb visibly passes through — every
                // type gets the shared touch-bounce, but only a router gets
                // the extra forwarding wipe (see class comment).
                PacketDeviceEffect.Play(to, color);
                if (to.deviceType == DeviceType.Router)
                {
                    int inPort = to.GetPortIndexForCable(cable);
                    Vector3 routerPoint = inPort >= 0 ? to.GetPortWorldPoint(inPort) : to.VisualTopPoint();
                    PingWipeEffect.PlayForward(to, routerPoint, color, RouterWipeDuration, quickFade: true);
                }
            }
        }

        yield return SquashOut(orb);
        if (orb != null) Destroy(orb.gameObject);

        // ── 3. Destination: those very same particles explode again and
        //      converge/cling across the whole device, alongside its own
        //      forward wipe ────────────────────────────────────────────────
        if (dest != null)
        {
            int entryPortIndex = dest.GetPortIndexForCable(path[path.Count - 1]);
            Vector3 entryPoint = entryPortIndex >= 0 ? dest.GetPortWorldPoint(entryPortIndex) : dest.VisualTopPoint();

            // Hands the swarm off — stop-and-destroy is no longer this
            // method's job, ConvergeAndCling now owns sparks' GameObject.
            MagnetizingSparks.ConvergeAndCling(sparks, entryPoint, dest, color);

            PingWipeEffect.PlayForward(dest, entryPoint, color, DestWipeDuration, () =>
            {
                PacketDeviceEffect.Play(dest, color, PacketDeviceEffect.PlacementIntensity);
                PingCheckmarkEffect.Play(dest, color);
            });
        }
        else
        {
            ConstructionSparks.StopAndDestroy(sparks); // no destination to hand the swarm off to — just end it
        }

        Destroy(gameObject);
    }

    // Elongates the orb along its current direction of travel (a comet-style
    // motion streak — the "stretch" half of the requested stretch-bounce) and
    // orients it to face that direction so the streak actually points along
    // the cable rather than sitting at a fixed angle.
    static void ApplyStretch(Transform orb, Vector3 pos, Vector3 prevPos)
    {
        Vector3 delta = pos - prevPos;
        if (delta.sqrMagnitude < 1e-10f) return;

        orb.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);

        float speed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        float stretch = Mathf.Clamp(speed / TravelSpeed, 0.7f, 1.6f);
        float cross = OrbRadius / Mathf.Sqrt(stretch); // thins the cross-section as it elongates, roughly conserving volume
        orb.localScale = new Vector3(cross, cross, OrbRadius * stretch * 2f);
    }

    // Elastic pop from nothing up to full size right as the orb emerges from
    // the source's condensed wipe — the "bounce" half of stretch-bounce.
    static IEnumerator PopIn(Transform orb)
    {
        float t = 0f;
        while (t < PopInDuration)
        {
            t += Time.deltaTime;
            float p = EaseOutBack(Mathf.Clamp01(t / PopInDuration));
            orb.localScale = Vector3.one * OrbRadius * Mathf.Max(p, 0f);
            yield return null;
        }
        orb.localScale = Vector3.one * OrbRadius;
    }

    // A quick flattening squash right as the orb reaches the destination,
    // just before it's destroyed and the arrival wipe/burst take over —
    // reads as the packet "hitting" the model rather than just vanishing.
    static IEnumerator SquashOut(Transform orb)
    {
        if (orb == null) yield break;
        Vector3 start = orb.localScale;
        Vector3 squashed = new Vector3(start.x * 2.2f, start.y * 2.2f, start.z * 0.25f);
        float t = 0f;
        while (t < SquashOutDuration)
        {
            t += Time.deltaTime;
            if (orb == null) yield break;
            orb.localScale = Vector3.Lerp(start, squashed, t / SquashOutDuration);
            yield return null;
        }
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float f = x - 1f;
        return 1f + c3 * f * f * f + c1 * f * f;
    }

    static Transform CreateOrb(Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "PingPacketOrb";
        Destroy(go.GetComponent<Collider>());

        var renderer = go.GetComponent<Renderer>();
        s_orbMaterial ??= MakeOrbMaterial();
        renderer.sharedMaterial = s_orbMaterial;

        var block = new MaterialPropertyBlock();
        string prop = s_orbMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
        block.SetColor(prop, color * OrbGlowBoost);
        renderer.SetPropertyBlock(block);

        go.transform.localScale = Vector3.zero; // PopIn grows it from here
        return go.transform;
    }

    static Material MakeOrbMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        return new Material(shader);
    }
}
