// Analog/digital algorithms adapted from KinoGlitchURP by Keijiro Takahashi.
// https://github.com/keijiro/KinoGlitchURP (Unlicense)
Shader "Hidden/DeFrag/KinoTvMonsterGlitch"
{
    HLSLINCLUDE
    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 texcoord : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
        output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);
        return output;
    }

    TEXTURE2D_X(_InputTexture);
    TEXTURE2D_X(_HistoryTexture);
    float _Intensity;
    half _ScanLineJitter;
    half2 _VerticalJump;
    half _HorizontalShake;
    half _ColorDrift;
    half _HorizontalRipple;
    half _DigitalIntensity;

    float Hash(float2 value)
    {
        return frac(sin(dot(value, float2(12.9898, 78.233))) * 43758.5453);
    }

    half MirrorRepeat(half x)
    {
        return 1 - abs(frac(x * 0.5h) * 2 - 1);
    }

    float GradientNoise(float value, float seed)
    {
        float cell = floor(value);
        float fraction = frac(value);
        float a = Hash(float2(cell, seed)) * 2 - 1;
        float b = Hash(float2(cell + 1, seed)) * 2 - 1;
        float smoothFraction = fraction * fraction * (3 - 2 * fraction);
        return lerp(a * fraction, b * (fraction - 1), smoothFraction);
    }

    half3 DamageColor(half3 color)
    {
        return half3(color.b, color.r, 1 - color.g);
    }

    float4 FullScreenPass(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        half u = input.texcoord.x;
        half v = input.texcoord.y;
        uint pixelY = (uint)floor(v * _ScreenSize.y);
        float safeTime = fmod(_Time.y, 600);
        uint frame = (uint)floor(_Time.y * 60);

        half jitter1 = Hash(float2(pixelY, frame)) * 2 - 1;
        half jitter2 = Hash(float2(pixelY, frame + 1000)) * 2 - 1;
        jitter2 = jitter2 * jitter2 * jitter2 * jitter2 * jitter2;
        half jitter = (jitter1 + jitter2 * 2.5) * _ScanLineJitter;

        half displacedV = frac(v + _VerticalJump.y);
        displacedV = max(1 - smoothstep(0, 0.05, displacedV), displacedV);
        half jump = lerp(v, displacedV, _VerticalJump.x);

        half noise1 = GradientNoise(jump * 1.5 - safeTime * 10.11, 1);
        half noise2 = GradientNoise(jump * 1.5 - safeTime * 13.04, 2);
        half burst = abs(noise1);
        burst /= burst + (1 - burst) * lerp(6, 1, _HorizontalRipple);
        half wiggle = abs(GradientNoise(jump * 20 + safeTime * 16, 12));
        half ripple = 0.3 * _HorizontalRipple * burst * (wiggle + abs(jitter2));

        half x = u + jitter + _HorizontalShake - ripple;
        half drift = _ColorDrift * 0.1;
        half2 uvR = half2(MirrorRepeat(x + noise1 * drift), jump);
        half2 uvG = half2(MirrorRepeat(x + noise2 * drift), jump);
        half2 uvB = half2(MirrorRepeat(x - noise2 * drift), jump);
        half3 analog;
        analog.r = SAMPLE_TEXTURE2D_X(_InputTexture, s_linear_clamp_sampler, uvR).r;
        analog.g = SAMPLE_TEXTURE2D_X(_InputTexture, s_linear_clamp_sampler, uvG).g;
        analog.b = SAMPLE_TEXTURE2D_X(_InputTexture, s_linear_clamp_sampler, uvB).b;

        float aspect = _ScreenSize.x / _ScreenSize.y;
        int rows = max(1, (int)round(sqrt(2048.0 / aspect)));
        int cols = max(1, (int)round(rows * aspect));
        int2 block = (int2)(input.texcoord * float2(cols, rows));
        float blockId = block.x + block.y * cols;
        float4 digitalNoise = float4(
            Hash(float2(blockId, frame / 3)),
            Hash(float2(blockId + 17, frame / 5)),
            Hash(float2(blockId + 43, frame / 7)),
            Hash(float2(blockId + 91, frame / 11)));
        float threshold = 1 - _DigitalIntensity;
        float2 digitalUv = input.texcoord;
        if (threshold < digitalNoise.z * digitalNoise.z)
            digitalUv = frac(digitalUv + (digitalNoise.xy - 0.5) * 0.35);

        half3 current = SAMPLE_TEXTURE2D_X(_InputTexture, s_linear_clamp_sampler, digitalUv).rgb;
        half3 previous = SAMPLE_TEXTURE2D_X(_HistoryTexture, s_linear_clamp_sampler, digitalUv).rgb;
        half3 digital = threshold < digitalNoise.w * digitalNoise.w ? previous : current;
        if (threshold * 0.2 + 0.8 < frac(digitalNoise.x * 83.32))
            digital = DamageColor(digital);

        half3 color = lerp(analog, digital, saturate(_DigitalIntensity * 1.35));
        half3 original = SAMPLE_TEXTURE2D_X(
            _InputTexture, s_linear_clamp_sampler, input.texcoord).rgb;
        return half4(lerp(original, color, _Intensity), 1);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "Kino TV Monster Glitch"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FullScreenPass
            ENDHLSL
        }
    }
    Fallback Off
}
