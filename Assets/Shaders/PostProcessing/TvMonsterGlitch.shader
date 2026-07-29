Shader "Hidden/DeFrag/TvMonsterGlitch"
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
    float _Intensity;
    float _TearAmount;
    float _NoiseAmount;

    float Hash(float2 value)
    {
        return frac(sin(dot(value, float2(12.9898, 78.233))) * 43758.5453);
    }

    float4 FullScreenPass(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        float2 uv = input.texcoord;
        float timeStep = floor(_Time.y * 18.0);
        float band = floor(uv.y * 95.0);
        float bandNoise = Hash(float2(band, timeStep));
        float activeBand = step(lerp(0.97, 0.72, _Intensity), bandNoise);
        float direction = Hash(float2(band + 31.0, timeStep)) * 2.0 - 1.0;
        float offset = direction * _TearAmount * _Intensity * activeBand;

        float2 tornUv = float2(saturate(uv.x + offset), uv.y);
        float3 original = SAMPLE_TEXTURE2D_X(_InputTexture, s_linear_clamp_sampler, uv).rgb;
        float3 color = SAMPLE_TEXTURE2D_X(_InputTexture, s_linear_clamp_sampler, tornUv).rgb;

        float chroma = _TearAmount * 0.22 * _Intensity * activeBand;
        color.r = SAMPLE_TEXTURE2D_X(_InputTexture, s_linear_clamp_sampler,
            float2(saturate(tornUv.x + chroma), tornUv.y)).r;
        color.b = SAMPLE_TEXTURE2D_X(_InputTexture, s_linear_clamp_sampler,
            float2(saturate(tornUv.x - chroma), tornUv.y)).b;

        float staticNoise = Hash(floor(uv * _ScreenSize.xy * 0.35) + timeStep);
        float scanline = step(0.94, Hash(float2(floor(uv.y * 420.0), timeStep + 77.0)));
        float noise = (staticNoise - 0.5) * _NoiseAmount * _Intensity;
        color += noise * (0.18 + activeBand * 0.55) + scanline * _Intensity * 0.12;

        return float4(lerp(original, color, _Intensity), 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "Tv Monster Glitch"
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
