Shader "Custom/OutlineShell"
{
    // "Inverted hull" selection outline: draws ONLY this mesh's back faces
    // (Cull Front), each vertex pushed outward along its own object-space
    // normal by _Thickness before the normal object->clip transform. The
    // real, front-facing mesh (drawn separately, normally) sits entirely
    // inside that inflated shell and occludes its near side via the depth
    // test, so only a thin rim peeks out around the silhouette — exactly
    // tracing whatever shape this particular mesh actually has (a flat cube
    // face, a cylinder's round cap, a port's little stub) rather than one
    // bounding box guessed around the whole object.
    //
    // SelectionIndicator.cs is what actually makes this read as "the
    // device's outline" rather than "one mesh's outline": it stamps a copy
    // of THIS shader's material (as a child, at local identity, wearing the
    // same mesh) onto every renderer under the selected device/table, so the
    // combined rim traces the whole composite shape part-by-part. A cable
    // has no mesh to shell this way — SelectionIndicator instead widens a
    // duplicate LineRenderer behind the real one for that case.
    //
    // ZWrite is off so several overlapping shells (one per device part)
    // never depth-fight each other — each just draws its own rim wherever
    // it isn't already covered by the real (opaque, ZWrite-on) geometry.
    Properties
    {
        _Color("Outline Color", Color) = (0.2, 0.9, 1, 1)
        _Thickness("Outline Thickness (object units)", Range(0.001, 0.1)) = 0.012
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+1" }
        LOD 100

        Pass
        {
            Name "OutlineShell"
            Tags { "LightMode" = "UniversalForward" }

            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Thickness;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                // Hard per-face normals (every device part is a flat-shaded
                // primitive — cubes, cylinders) mean each face inflates
                // independently along its own true normal, so straight
                // edges/corners push straight outward with no pinching or
                // gapping the way a smooth-shaded mesh's averaged normals could.
                float3 posOS = IN.positionOS + normalize(IN.normalOS) * _Thickness;
                OUT.positionHCS = TransformObjectToHClip(posOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
