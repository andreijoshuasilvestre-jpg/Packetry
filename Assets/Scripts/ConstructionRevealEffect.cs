using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// One-shot "blueprint" construction reveal, played when a device or table is
/// first placed. Temporarily swaps every renderer under the object onto
/// Custom/BlueprintConstruction (see BlueprintConstruction.shader) and drives
/// it through two sequential, whole-object phases, both originating from a
/// single corner of the object's bounds (see NearestBoundsCorner):
///   1. A blueprint (solid blue fill, with white line art tracing each part's
///      actual edges drawing itself in shortly after) sweeps outward from that
///      one corner until the ENTIRE object is a blueprint — nothing solid yet.
///      A trickle of sparks follows the sweeping front while this happens.
///   2. Only then does a second sweep, again from the same corner, convert the
///      completed blueprint into the finished lit material — this final phase
///      is when the caller's onFinalPhaseStart callback fires (e.g. the
///      squeeze-bounce placement pop), so the "landing" reads as simultaneous
///      with the material actually solidifying rather than happening after.
/// A burst of sparks marks the moment the object is fully built. Original
/// materials are restored once phase 2 completes — this is a spawn-in effect,
/// not a permanent shader. Every phase follows the Build animation speed
/// setting (see AnimationSpeed).
/// </summary>
public class ConstructionRevealEffect : MonoBehaviour
{
    private const float DefaultBlueprintPhaseDuration = 0.32f;
    private const float DefaultHoldDuration = 0.2f;
    private const float DefaultSolidPhaseDuration = 0.25f;
    private const float DefaultLineRevealWidth = 0.12f;
    private const float DefaultSolidFadeWidth = 0.14f;
    private const float DefaultLineThickness = 0.035f;
    // How wide (mask units, same space as LineRevealWidth/SolidFadeWidth) and
    // how bright the glowing band riding the active sweep front is — see
    // BlueprintConstruction.shader's ScanGlow.
    private const float DefaultScanWidth = 0.1f;
    private const float DefaultScanIntensity = 2.0f;
    private static readonly Color DefaultLineColor = new Color(0.95f, 0.97f, 1f, 1f);
    private static readonly Color DefaultFillColor = new Color(0.04f, 0.16f, 0.75f, 1f);
    private static readonly Color DefaultSparkColor = new Color(0.25f, 0.55f, 1f, 1f); // vivid electric blue, not washed-out white

    // Pushes the blueprint's fill/line colors above 1.0 so URP's Bloom (see
    // DefaultVolumeProfile.asset) actually picks them up — Bloom only reacts
    // to HDR-bright pixels above its threshold, and this project's normal lit
    // materials all stay well under 1.0, so boosting just these two keeps the
    // glow scoped to the reveal effect instead of blooming the whole scene.
    // The line art blooms harder than the fill so the linework reads as the
    // brighter focal highlight rather than an even blue haze.
    private const float FillGlowMultiplier = 1.5f;
    private const float LineGlowMultiplier = 2.2f;

    private static Shader s_shader;

    // originHint: an approximate world position near the corner the sweep
    // should start from (e.g. the point the player clicked) — snapped to
    // whichever of the object's actual bounding-box corners is nearest. Omit
    // it to just use the bounds' own min corner (fine for devices, which have
    // no player-chosen reference point the way a table's placement click does).
    //
    // boundsOverride: use exact known final dimensions instead of measuring
    // renderers live. Needed for objects that animate their own scale on
    // spawn (e.g. Table's axis-grow) — measuring mid-animation would capture
    // a squashed size and throw off the whole distance-based reveal mask.
    public static void Play(GameObject root,
        float blueprintPhaseDuration = DefaultBlueprintPhaseDuration, float holdDuration = DefaultHoldDuration,
        float solidPhaseDuration = DefaultSolidPhaseDuration, float lineRevealWidth = DefaultLineRevealWidth,
        float solidFadeWidth = DefaultSolidFadeWidth, float lineThickness = DefaultLineThickness,
        float scanWidth = DefaultScanWidth, float scanIntensity = DefaultScanIntensity,
        Color? lineColor = null, Color? fillColor = null, Color? sparkColor = null,
        Vector3? originHint = null, Bounds? boundsOverride = null, Action onFinalPhaseStart = null)
    {
        if (root == null) return;
        var effect = root.AddComponent<ConstructionRevealEffect>();
        effect.StartCoroutine(effect.Run(blueprintPhaseDuration, holdDuration, solidPhaseDuration, lineRevealWidth,
            solidFadeWidth, lineThickness, scanWidth, scanIntensity, lineColor ?? DefaultLineColor, fillColor ?? DefaultFillColor,
            sparkColor ?? DefaultSparkColor, originHint, boundsOverride, onFinalPhaseStart));
    }

    IEnumerator Run(float blueprintPhaseDuration, float holdDuration, float solidPhaseDuration, float lineRevealWidth,
        float solidFadeWidth, float lineThickness, float scanWidth, float scanIntensity, Color lineColor, Color fillColor, Color sparkColor,
        Vector3? originHint, Bounds? boundsOverride, Action onFinalPhaseStart)
    {
        if (s_shader == null)
            s_shader = Shader.Find("Custom/BlueprintConstruction");

        if (s_shader == null)
        {
            Debug.LogWarning("ConstructionRevealEffect: Custom/BlueprintConstruction shader not found; skipping reveal.");
            Destroy(this);
            yield break;
        }

        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { Destroy(this); yield break; }

        Bounds bounds;
        if (boundsOverride.HasValue)
        {
            bounds = boundsOverride.Value;
        }
        else
        {
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
        }

        Vector3 origin = NearestBoundsCorner(bounds, originHint ?? bounds.min);
        float maxDist = Mathf.Max(Vector3.Distance(bounds.max, bounds.min), 0.0001f); // mirrors the shader's own normalization

        var originals = new Material[renderers.Length][];
        var reveal = new Material[renderers.Length][];

        for (int r = 0; r < renderers.Length; r++)
        {
            Material[] src = renderers[r].sharedMaterials;
            originals[r] = src;

            // The shader's line art traces this part's own local-space faces
            // (see BoxEdgeFactor) — it needs each part's actual half-size,
            // not a hardcoded assumption, since DevicePrefabBuilder's
            // ProBuilder parts are baked at their real size with localScale
            // left at 1 rather than the old unit-cube-plus-transform-scale
            // primitives. Falls back to a unit cube's 0.5 if a part somehow
            // has no mesh to measure.
            var meshFilter = renderers[r].GetComponent<MeshFilter>();
            Vector3 localHalfExtents = meshFilter != null && meshFilter.sharedMesh != null
                ? meshFilter.sharedMesh.bounds.extents
                : Vector3.one * 0.5f;

            var built = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var m = new Material(s_shader);
                if (src[i] != null && src[i].HasProperty("_BaseColor"))
                    m.SetColor("_BaseColor", src[i].GetColor("_BaseColor"));
                m.SetColor("_LineColor", lineColor * LineGlowMultiplier);
                m.SetColor("_FillColor", fillColor * FillGlowMultiplier);
                m.SetFloat("_LineThickness", lineThickness);
                m.SetFloat("_LineRevealWidth", lineRevealWidth);
                m.SetFloat("_SolidFadeWidth", solidFadeWidth);
                m.SetFloat("_ScanWidth", scanWidth);
                m.SetFloat("_ScanIntensity", scanIntensity);
                m.SetVector("_BoundsMin", bounds.min);
                m.SetVector("_BoundsMax", bounds.max);
                m.SetVector("_OriginPoint", origin);
                m.SetVector("_LocalHalfExtents", localHalfExtents);
                m.SetFloat("_RevealThreshold", 0f);
                built[i] = m;
            }
            reveal[r] = built;
            renderers[r].materials = built;
        }

        // Sparks trail the blueprint front while it's actively drawing — an
        // expanding sphere of sparks centred on the origin corner, radius
        // matched to how far the front has swept.
        ParticleSystem sparks = ConstructionSparks.CreateTrail(sparkColor);

        // Phase 1 — blueprint sweeps out from the origin corner (threshold 0 -> 1)
        // until the whole object is a blueprint.
        yield return AnimateThreshold(reveal, 0f, 1f, blueprintPhaseDuration,
            threshold => ConstructionSparks.UpdateAsSphere(sparks, origin, threshold * maxDist));

        ConstructionSparks.StopAndDestroy(sparks);

        // Hold the fully-built blueprint for a beat so it actually reads as a
        // distinct stage before it starts solidifying.
        if (holdDuration > 0f)
            yield return new WaitForSeconds(AnimationSpeed.Seconds(AnimationKind.Build, holdDuration));

        // Phase 3 (final) — solid front sweeps out from the same corner again
        // (threshold 1 -> maxThreshold), converting the blueprint into the
        // finished material. The overshoot past 2 guarantees the fade band
        // fully clears even at the mask's extreme (~1) values. The caller's
        // callback (e.g. the squeeze-bounce pop) fires the instant this phase
        // starts rather than after it finishes, so the device visibly "lands"
        // right as it solidifies instead of sitting still afterward.
        onFinalPhaseStart?.Invoke();

        float maxThreshold = 2f + solidFadeWidth;
        yield return AnimateThreshold(reveal, 1f, maxThreshold, solidPhaseDuration, null);

        for (int r = 0; r < renderers.Length; r++)
        {
            if (renderers[r] != null)
                renderers[r].materials = originals[r];
            foreach (var m in reveal[r])
                Destroy(m);
        }

        // Spawned from a shell matching the device's own half-diagonal, centred
        // on it — every spawn point lands at or outside the actual surface
        // (the shell exactly circumscribes the bounds), so sparks read as
        // bursting from around/outside the device rather than erupting from
        // its buried center or clipping through from a single corner.
        float spreadRadius = bounds.extents.magnitude;
        ConstructionSparks.EmitBurst(bounds.center, sparkColor, count: 48, spawnRadius: spreadRadius);

        Destroy(this);
    }

    // Cables render as a LineRenderer strip, not a mesh with faces/corners, so
    // they don't go through the shader-swap path above at all. Instead this
    // drives Cable's own RevealProgress/SolidifyProgress (see Cable.cs),
    // sweeping from _deviceA toward _deviceB — deviceA is always whichever
    // device the player clicked first when drawing the cable, i.e. already
    // the natural "origin", so no origin hint is needed here.
    public static void PlayForCable(Cable cable,
        float blueprintPhaseDuration = DefaultBlueprintPhaseDuration, float holdDuration = DefaultHoldDuration,
        float solidPhaseDuration = DefaultSolidPhaseDuration, Color? sparkColor = null, Action onComplete = null)
    {
        if (cable == null) return;
        var effect = cable.gameObject.AddComponent<ConstructionRevealEffect>();
        effect.StartCoroutine(effect.RunForCable(cable, blueprintPhaseDuration, holdDuration, solidPhaseDuration,
            sparkColor ?? DefaultSparkColor, onComplete));
    }

    IEnumerator RunForCable(Cable cable, float blueprintPhaseDuration, float holdDuration, float solidPhaseDuration, Color sparkColor, Action onComplete)
    {
        cable.RevealProgress = 0f;
        cable.SolidifyProgress = 0f;

        // Sparks trail the drawing tip — the same "running with the lines" idea
        // as the sphere-shell trail above, just following a moving point
        // instead of an expanding radius.
        ParticleSystem sparks = ConstructionSparks.CreateTrail(sparkColor);

        float t = 0f;
        while (t < blueprintPhaseDuration)
        {
            t += AnimationSpeed.Delta(AnimationKind.Build);
            cable.RevealProgress = EaseOut(Mathf.Clamp01(t / blueprintPhaseDuration));
            ConstructionSparks.UpdateAsPoint(sparks, cable.GetRevealTipPoint());
            yield return null;
        }
        cable.RevealProgress = 1f;

        ConstructionSparks.StopAndDestroy(sparks);

        if (holdDuration > 0f)
            yield return new WaitForSeconds(AnimationSpeed.Seconds(AnimationKind.Build, holdDuration));

        t = 0f;
        while (t < solidPhaseDuration)
        {
            t += AnimationSpeed.Delta(AnimationKind.Build);
            cable.SolidifyProgress = EaseOut(Mathf.Clamp01(t / solidPhaseDuration));
            yield return null;
        }
        cable.SolidifyProgress = 1f;

        ConstructionSparks.EmitBurst(cable.GetRevealTipPoint(), sparkColor); // tip is now resting at deviceB, the target

        // Fires only once the cable itself has fully finished drawing — lets a
        // caller (CableManager) play the target device's own connect pop
        // (glow + bounce + sparks) exactly when the cable "arrives" there,
        // rather than the instant the cable object is created.
        onComplete?.Invoke();

        Destroy(this);
    }

    // Whichever of the bounds' 8 corners is closest to hint — lets a caller
    // pass an approximate reference point (like a click position) without
    // having to work out the exact corner coordinates itself.
    static Vector3 NearestBoundsCorner(Bounds bounds, Vector3 hint)
    {
        Vector3 min = bounds.min, max = bounds.max;
        float best = float.MaxValue;
        Vector3 bestCorner = min;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3(
                (i & 1) != 0 ? max.x : min.x,
                (i & 2) != 0 ? max.y : min.y,
                (i & 4) != 0 ? max.z : min.z);
            float d = Vector3.SqrMagnitude(corner - hint);
            if (d < best) { best = d; bestCorner = corner; }
        }
        return bestCorner;
    }

    // Ease-out only, not the old SmoothStep (which eased both in and out).
    // SmoothStep's zero velocity at the START of a phase read as a hesitation
    // right where the two phases meet, stacking with the intentional
    // holdDuration pause between them. Starting at full speed removes that
    // stutter, while easing out at the end still gives the sweep a smooth,
    // decelerating arrival instead of stopping dead — and the explicit hold
    // (see Run/RunForCable) keeps the two phases legibly separated regardless.
    static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

    static IEnumerator AnimateThreshold(Material[][] reveal, float from, float to, float duration, Action<float> onProgress)
    {
        float t = 0f;
        while (t < duration)
        {
            t += AnimationSpeed.Delta(AnimationKind.Build);
            float p = EaseOut(Mathf.Clamp01(t / duration));
            float threshold = Mathf.Lerp(from, to, p);
            for (int r = 0; r < reveal.Length; r++)
                for (int i = 0; i < reveal[r].Length; i++)
                    reveal[r][i].SetFloat("_RevealThreshold", threshold);
            onProgress?.Invoke(threshold);
            yield return null;
        }
        for (int r = 0; r < reveal.Length; r++)
            for (int i = 0; i < reveal[r].Length; i++)
                reveal[r][i].SetFloat("_RevealThreshold", to);
        onProgress?.Invoke(to);
    }
}
