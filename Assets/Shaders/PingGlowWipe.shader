Shader "Custom/PingGlowWipe"
{
    // Directional-reading glow "wipe" for a single ping's visit to a device —
    // driven by PingWipeEffect, which is the C# half of this shader (see that
    // file for the exact story of what _WipeThreshold/_FillStrength do over
    // time). Unlike BlueprintConstruction, this never clip()s a fragment away
    // — the device already exists and stays fully visible/lit throughout;
    // this only adds a green tint + a bright traveling band on top of it, so
    // it reads as "this already-built device is being visited by a packet",
    // not "being constructed".
    //
    // The wipe itself is a single scalar mask: how far a fragment's world
    // position is from _OriginPoint (the exact port the packet is entering or
    // leaving through), normalized by _MaxDist (the true farthest-corner
    // distance from that point — NOT just the bounds diagonal, since origin
    // is a port on the surface, not necessarily a bounding-box corner; see
    // PingWipeEffect.MaxCornerDistance). _WipeThreshold sweeps that mask
    // window outward (0 -> ~1, PlayForward) or inward (~1 -> 0, PlayReverse)
    // over time:
    //   - Ahead of the front: untouched, normal lit material.
    //   - At the front: a bright _GlowColor band (ScanGlow), like a scanline.
    //   - Behind the front: the lit material tinted toward _GlowColor, PLUS a
    //     procedural blueprint-style grid of thin lines (GridMask) so the
    //     covered area reads as technical-drawing schematic paper, not a flat
    //     wash — the same "blueprint" language BlueprintConstruction/
    //     HologramMaterial use elsewhere, just built from world/local
    //     position instead of a baked grid texture (this shader runs over
    //     arbitrary device meshes with no guaranteed tiling UVs).
    // _FillStrength is a separate 0..1 knob the C# side fades independently
    // once a forward wipe finishes sweeping — lets the "settle back to normal"
    // fade happen without re-animating (and re-clipping) the threshold itself.
    Properties
    {
        _BaseColor("Base Color", Color) = (0.8, 0.8, 0.8, 1)
        _GlowColor("Glow Color", Color) = (0.3, 1.0, 0.5, 1)
        _WipeThreshold("Wipe Threshold", Range(0, 2)) = 0
        _BandWidth("Band Width (mask units)", Range(0.01, 1)) = 0.16
        _BandIntensity("Band Glow Intensity", Range(0, 6)) = 2.6
        _FillStrength("Fill Strength", Range(0, 1)) = 1
        _BoundsMin("Bounds Min (world)", Vector) = (0, 0, 0, 0)
        _BoundsMax("Bounds Max (world)", Vector) = (1, 1, 1, 0)
        _OriginPoint("Origin Point (world)", Vector) = (0, 0, 0, 0)
        _MaxDist("Max Corner Distance (world)", Float) = 1
        _GridCellSize("Grid Cell Size (local units)", Range(0.02, 1)) = 0.1
        _GridLineThickness("Grid Line Thickness (local units)", Range(0.002, 0.05)) = 0.012
        _GridIntensity("Grid Line Intensity", Range(0, 4)) = 1.6
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
                float4 _GlowColor;
                float  _WipeThreshold;
                float  _BandWidth;
                float  _BandIntensity;
                float  _FillStrength;
                float4 _BoundsMin;
                float4 _BoundsMax;
                float4 _OriginPoint;
                float  _MaxDist;
                float  _GridCellSize;
                float  _GridLineThickness;
                float  _GridIntensity;
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

            // Bright right at the front (edge == 0), fading out over `width`
            // to either side — an animated scanline riding the sweep instead
            // of a flat two-tone edge. Same idea as BlueprintConstruction's
            // own ScanGlow.
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

            // 1 within _GridLineThickness of the nearest multiple of
            // _GridCellSize along one local axis, fading to 0 by exactly that
            // thickness away — a single repeating "line" mask, evaluated
            // independently per axis in frag() below.
            float GridAxisLine(float posOS)
            {
                float coord = posOS / max(_GridCellSize, 0.0001);
                float distToLine = abs(frac(coord + 0.5) - 0.5); // distance to nearest integer, in cell units
                float thicknessInCells = _GridLineThickness / max(_GridCellSize, 0.0001);
                return 1.0 - saturate(distToLine / max(thicknessInCells, 0.0001));
            }

            // Blueprint graph-paper grid over the surface, built from local
            // position rather than UVs (these device meshes have no
            // guaranteed tiling UVs). A true 3D grid would draw all three
            // axes' lines everywhere, which — on an axis-aligned box face —
            // makes the whole face flash solid whenever that face's constant
            // third coordinate happens to sit near a grid line. Triplanar-
            // style: per fragment, exclude whichever axis the surface normal
            // is most aligned with (the "flat" axis for that face) and only
            // combine the other two, so each face only ever shows the two
            // grid directions that actually run across it.
            float GridMask(float3 posOS, float3 normalWS)
            {
                float lineX = GridAxisLine(posOS.x);
                float lineY = GridAxisLine(posOS.y);
                float lineZ = GridAxisLine(posOS.z);

                float3 absN = abs(normalWS);
                float maxN = max(absN.x, max(absN.y, absN.z));
                float exX = absN.x < maxN - 0.001 ? 1.0 : 0.0;
                float exY = absN.y < maxN - 0.001 ? 1.0 : 0.0;
                float exZ = absN.z < maxN - 0.001 ? 1.0 : 0.0;

                return max(lineX * exX, max(lineY * exY, lineZ * exZ));
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 normal = normalize(IN.normalWS);
                half3 lit = LitColor(normal, _BaseColor.rgb);

                float mask = distance(IN.positionWS, _OriginPoint.xyz) / max(_MaxDist, 0.0001); // 0 at origin, ~1 at the farthest corner
                float edgeToFront = _WipeThreshold - mask;

                float band    = ScanGlow(edgeToFront, _BandWidth) * _BandIntensity;
                float covered = saturate(edgeToFront / _BandWidth) * _FillStrength;

                float grid = GridMask(IN.positionOS, normal) * covered * _GridIntensity;
                half3 gridColor = lerp(_GlowColor.rgb, half3(1, 1, 1), 0.5); // near-white, same blueprint-line-over-colored-fill language as BlueprintConstruction/HologramMaterial

                half3 result = lit + _GlowColor.rgb * covered + _GlowColor.rgb * band + gridColor * grid;
                return half4(result, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
