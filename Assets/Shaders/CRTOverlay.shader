// Subtle full-screen CRT-style darkening overlay: faint horizontal scanlines
// plus a soft corner vignette, alpha-blended over whatever this Image sits
// on top of (see CRTOverlay.cs). Same UI/Default-compatible layout
// (stencil/clip-rect passthrough) as UIAdditiveGlow.shader, but standard
// alpha blending since this only ever darkens, never adds light — and
// unlike AdditiveGlow it doesn't sample _MainTex at all, since the effect is
// computed purely from the full-screen quad's own UV (0..1 across the
// screen), not from any sprite content.
Shader "UI/CRTOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _ScanlineIntensity ("Scanline Intensity", Range(0,1)) = 0.05
        _ScanlineCount ("Scanline Count", Float) = 340
        _ScanOffset ("Scanline Scroll Offset", Float) = 0

        _VignetteIntensity ("Vignette Intensity", Range(0,1)) = 0.32
        _VignetteInner ("Vignette Inner Radius", Float) = 0.35
        _VignetteOuter ("Vignette Outer Radius", Float) = 0.9

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float4 _ClipRect;

            float _ScanlineIntensity;
            float _ScanlineCount;
            float _ScanOffset;

            float _VignetteIntensity;
            float _VignetteInner;
            float _VignetteOuter;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float scan = sin((IN.texcoord.y + _ScanOffset) * _ScanlineCount * 6.28318) * 0.5 + 0.5;
                float scanDarken = scan * _ScanlineIntensity;

                float2 centered = IN.texcoord - 0.5;
                float dist = length(centered) * 1.4142136; // 0 at screen center, ~1 at the farthest corner
                float vig = smoothstep(_VignetteInner, _VignetteOuter, dist) * _VignetteIntensity;

                float darken = saturate(scanDarken + vig) * IN.color.a;
                fixed4 color = fixed4(0, 0, 0, darken);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                return color;
            }
        ENDCG
        }
    }
}
