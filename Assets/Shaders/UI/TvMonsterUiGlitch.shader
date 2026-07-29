Shader "DeFrag/UI/TvMonsterGlitchOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Intensity ("Intensity", Range(0, 1)) = 0
        _NoiseAmount ("Noise Amount", Range(0, 1)) = 0.4
    }
    SubShader
    {
        Tags
        {
            "Queue"="Overlay"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "CanUseSpriteAtlas"="True"
        }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            float _Intensity;
            float _NoiseAmount;

            float Hash(float2 value)
            {
                return frac(sin(dot(value, float2(12.9898, 78.233))) * 43758.5453);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityObjectToClipPos(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float timeStep = floor(_Time.y * 16.0);
                float band = floor(input.uv.y * 110.0);
                float bandRandom = Hash(float2(band, timeStep));
                float bandMask = step(lerp(0.99, 0.79, _Intensity), bandRandom);
                float pixelNoise = Hash(floor(input.uv * _ScreenParams.xy * 0.25) + timeStep);
                float fineStatic = step(lerp(0.997, 0.93, _Intensity), pixelNoise);
                float alpha = saturate((bandMask * 0.32 + fineStatic * _NoiseAmount) * _Intensity);
                float tintChoice = Hash(float2(band + 19.0, timeStep));
                float3 tint = lerp(float3(0.1, 0.85, 1.0), float3(1.0, 0.08, 0.32), tintChoice);
                return float4(tint, alpha);
            }
            ENDHLSL
        }
    }
}
