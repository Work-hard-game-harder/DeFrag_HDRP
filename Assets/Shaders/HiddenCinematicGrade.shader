// Film look for full-screen cinematic video drawn by a UI RawImage: exposure, contrast, saturation,
// split toning, vignette, slight chromatic fringe and animated grain. The vertex colour alpha fades the shot.
Shader "Hidden/DeFrag/CinematicGrade"
{
    Properties
    {
        [PerRendererData] _MainTex ("Video", 2D) = "black" {}
        _Exposure ("Exposure", Float) = 1
        _Contrast ("Contrast", Float) = 1.12
        _Saturation ("Saturation", Float) = 0.88
        _Shadows ("Shadow Tint", Color) = (0.93, 1.0, 0.98, 1)
        _Highlights ("Highlight Tint", Color) = (1.04, 1.01, 0.96, 1)
        _Vignette ("Vignette", Range(0, 1)) = 0.35
        _Fringe ("Chromatic Fringe", Range(0, 0.01)) = 0.0015
        _Grain ("Grain", Range(0, 0.3)) = 0.06
        _GrainSeed ("Grain Seed", Float) = 0
        _Aspect ("Screen Aspect", Float) = 1.777
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "False" }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Exposure, _Contrast, _Saturation, _Vignette, _Fringe, _Grain, _GrainSeed, _Aspect;
            float4 _Shadows, _Highlights;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            // sin 기반 해시는 큰 좌표에서 정밀도가 깨져 대각선 줄무늬가 생기므로 산술 해시를 씁니다.
            float hash(float2 p) { float3 p3 = frac(p.xyx * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 centred = i.uv - 0.5;
                float2 shift = centred * _Fringe;
                float3 c;
                c.r = tex2D(_MainTex, i.uv + shift).r;
                c.g = tex2D(_MainTex, i.uv).g;
                c.b = tex2D(_MainTex, i.uv - shift).b;

                c *= _Exposure;
                float luma = dot(c, float3(0.2126, 0.7152, 0.0722));
                c = lerp(luma.xxx, c, _Saturation);
                c = (c - 0.5) * _Contrast + 0.5;
                // Split toning: 소스의 보라 기운을 줄이도록 그림자는 살짝 청록, 하이라이트는 살짝 따뜻하게.
                float3 tint = lerp(_Shadows.rgb, _Highlights.rgb, smoothstep(0.15, 0.85, luma));
                c *= tint;

                float2 v = centred * float2(_Aspect / 1.777, 1);
                float vig = 1 - _Vignette * smoothstep(0.25, 0.85, length(v) * 1.35);
                c *= vig;

                float g = hash(floor(i.uv * float2(960, 540)) + float2(_GrainSeed, _GrainSeed * 1.37)) - 0.5;
                c += g * _Grain * (1.2 - luma);
                return fixed4(saturate(c), i.color.a);
            }
            ENDCG
        }
    }
}
