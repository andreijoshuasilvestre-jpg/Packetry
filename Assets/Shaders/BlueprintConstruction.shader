Shader "Custom/BlueprintConstruction"
{
    // Two-phase, whole-object "construction reveal" material driven by a single
    // _RevealThreshold that ConstructionRevealEffect.cs animates from 0 up to
    // roughly 2 + _SolidFadeWidth:
    //
    //   Phase 1 (threshold 0 -> 1): a "blueprint front" sweeps outward from a
    //   single origin corner of the object's world-space bounding box (see
    //   _OriginPoint, set by ConstructionRevealEffect.cs). Ahead of it,
    //   fragments are fully discarded (not built yet); behind it, a solid
    //   blueprint-blue fill with white line art tracing that part's actual
    //   geometric edges appears at full brightness — a real technical-drawing
    //   look, not a repeating grid. This phase ends with the ENTIRE object
    //   rendered as a blueprint — nothing solid yet.
    //
    //   Phase 2 (threshold 1 -> ~2): a second, independent "solid front" then
    //   sweeps outward from that same origin point over the now-complete
    //   blueprint, gradient-blending each fragment from the blueprint look
    //   into the finished lit _BaseColor as it passes. This phase ends with
    //   the whole object fully built.
    //
    // Because both fronts compare against the same per-fragment
    // distance-from-origin mask, each phase reads as a single wave expanding
    // from that one corner/side — first drawing the outline, then filling it
    // in — rather than one band that sweeps once leaving a trail of mixed
    // states behind it.
    Properties
    {
        _BaseColor("Base Color", Color) = (0.8, 0.8, 0.8, 1)
        _LineColor("Blueprint Line Color", Color) = (0.95, 0.97, 1, 1)
        _FillColor("Blueprint Fill Color", Color) = (0.04, 0.16, 0.75, 1)
        _LineThickness("Blueprint Line Thickness (local units)", Range(0.005, 0.2)) = 0.035
        _LineRevealWidth("Line Reveal Lag (mask units)", Range(0.01, 1)) = 0.12
        _SolidFadeWidth("Solid Leading-Edge Fade (mask units)", Range(0.01, 1)) = 0.14
        _RevealThreshold("Reveal Threshold", Range(0, 3)) = 0
        _BoundsMin("Bounds Min (world)", Vector) = (0, 0, 0, 0)
        _BoundsMax("Bounds Max (world)", Vector) = (1, 1, 1, 0)
        _OriginPoint("Origin Point (world)", Vector) = (0, 0, 0, 0)
        _ScanWidth("Scan Band Width (mask units)", Range(0.01, 1)) = 0.1
        _ScanIntensity("Scan Band Glow Intensity", Range(0, 5)) = 2.0
        _LocalHalfExtents("Local-Space Half Extents", Vector) = (0.5, 0.5, 0.5, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 positionOS  : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _LineColor;
                float4 _FillColor;
                float  _LineThickness;
                float  _LineRevealWidth;
                float  _SolidFadeWidth;
                float  _RevealThreshold;
                float4 _BoundsMin;
                float4 _BoundsMax;
                float4 _OriginPoint;
                float  _ScanWidth;
                float  _ScanIntensity;
                float4 _LocalHalfExtents;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS);
                OUT.positionHCS = vpi.positionCS;
                OUT.positionWS  = vpi.positionWS;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionOS  = IN.positionOS;
                return OUT;
            }

            // 1 right at a mesh edge (where two of the part's local faces
            // meet), fading to 0 by <thickness> away from it. halfExtents is
            // the part's own local-space bounding-box half-size (see
            // ConstructionRevealEffect, which sets _LocalHalfExtents from
            // each renderer's sharedMesh.bounds) rather than a hardcoded 0.5
            // — devices are now built from ProBuilder parts baked at their
            // real size with localScale left at 1 (see DevicePrefabBuilder),
            // not the old unit-cube-plus-transform-scale primitives, so a
            // fixed 0.5 would put the "near a face" band outside every mesh
            // that isn't exactly 1 local unit across and the line art would
            // never appear at all.
            float BoxEdgeFactor(float3 localPos, float thickness, float3 halfExtents)
            {
                float3 distToFace = halfExtents - abs(localPos); // 0 exactly at a face, grows inward
                float3 nearFace = 1.0 - saturate(distToFace / thickness);
                return saturate(max(max(nearFace.x * nearFace.y, nearFace.y * nearFace.z), nearFace.x * nearFace.z));
            }

            // Bright glow riding right at whichever front is currently
            // active (edge == 0 exactly at it, growing either direction away
            // from it) — reads as an animated scanline tracing the
            // construction wave instead of a flat two-tone edge, the same
            // idea as the sparks trailing the front in
            // ConstructionRevealEffect but baked into the material itself so
            // it holds up even without particles.
            float ScanGlow(float edge, float width)
            {
                return saturate(1.0 - abs(edge) / max(width, 0.0001));
            }

            half3 LitColor(float3 normalWS, float3 baseColor)
            {
                Light mainLight = GetMainLight();
                float ndotl = saturate(dot(normalWS, mainLight.direction));
                float3 ambient = SampleSH(normalWS);
                return baseColor * (ambient + mainLight.color * ndotl);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 bmin = _BoundsMin.xyz;
                float3 bmax = _BoundsMax.xyz;
                // Full diagonal, not half — the mask now measures distance from a
                // single origin corner, so the farthest any surface point can be is
                // the opposite corner, not the box's center.
                float maxDist = max(distance(bmax, bmin), 0.0001);

                float distFromOrigin = distance(IN.positionWS, _OriginPoint.xyz);
                float mask = saturate(distFromOrigin / maxDist); // 0 at the origin corner, 1 at the opposite corner

                // Phase 1: 0 during build-up, holds at 1 once the blueprint
                // front has swept past every corner-distance value that exists.
                // The clip() below is the front — a fragment either has been
                // reached or hasn't, so it needs no further per-pixel fade on
                // top (an opaque shader can't fade color without also fading
                // it toward black, which just reads as a phantom dark band
                // trailing the front).
                float blueprintFront = saturate(_RevealThreshold);
                float edgeToBlueprintFront = blueprintFront - mask;
                clip(edgeToBlueprintFront); // ahead of the blueprint front — not built yet

                float3 normal = normalize(IN.normalWS);

                // The white line art lags a beat behind the blue fill instead of
                // appearing with it — a plain-blue wash sweeps through first, then
                // the linework "draws itself in" shortly after, like ink following
                // a pen. Blends between two already-lit colors (never toward
                // black), same reasoning as the fix for the clip-front fade.
                float lineReveal = saturate(edgeToBlueprintFront / _LineRevealWidth);
                float lineMask = BoxEdgeFactor(IN.positionOS, _LineThickness, _LocalHalfExtents.xyz);
                float3 blueprintWithLines = lerp(_FillColor.rgb, _LineColor.rgb, lineMask);
                float3 blueprint = lerp(_FillColor.rgb, blueprintWithLines, lineReveal);

                // Phase 1's traveling scan band — brightest exactly where the
                // blueprint front currently is, fading out over _ScanWidth.
                blueprint += _LineColor.rgb * ScanGlow(edgeToBlueprintFront, _ScanWidth) * _ScanIntensity;

                // Phase 2: stays 0 for all of phase 1 (threshold <= 1), then
                // sweeps outward from the corners again over the now-complete
                // blueprint. Deliberately not saturated at the top so it can
                // overshoot mask's [0,1] range and fully clear the fade band
                // everywhere by the time the effect ends.
                float solidFront = max(_RevealThreshold - 1.0, 0.0);
                float edgeToSolidFront = solidFront - mask;
                float scanSolid = ScanGlow(edgeToSolidFront, _ScanWidth) * _ScanIntensity;

                if (edgeToSolidFront >= _SolidFadeWidth)
                    // Fully built here — scanSolid is already ~0 this far past
                    // the front, so this only ever glows right as it passes.
                    return half4(LitColor(normal, _BaseColor.rgb) + _LineColor.rgb * scanSolid, 1.0);

                if (edgeToSolidFront > 0.0)
                {
                    // Mid-transition: gradient-blend blueprint -> finished material.
                    float t = saturate(edgeToSolidFront / _SolidFadeWidth);
                    float3 lit = LitColor(normal, _BaseColor.rgb);
                    float3 result = lerp(blueprint, lit, smoothstep(0.0, 1.0, t));
                    return half4(result + _LineColor.rgb * scanSolid, 1.0);
                }

                // Still blueprint — shown at full brightness as soon as the front reaches it.
                return half4(blueprint, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
