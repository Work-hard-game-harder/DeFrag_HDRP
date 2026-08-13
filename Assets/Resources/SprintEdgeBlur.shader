Shader "Hidden/DeFrag/SprintEdgeBlur"
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
    float _EdgeStart;
    float _BlurRadius;
    float _FullScreenBlend;

    float3 SampleInput(float2 uv)
    {
        float2 maxUv = 1.0 - _ScreenSize.zw;
        uint2 positionSS = clamp(uv, 0.0, maxUv) * _ScreenSize.xy;
        return LOAD_TEXTURE2D_X(_InputTexture, positionSS).rgb;
    }

    float4 Frag(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        float2 uv = input.texcoord;
        float2 fromCenter = uv - 0.5;
        float aspect = _ScreenSize.x / _ScreenSize.y;
        float radialDistance = length(float2(fromCenter.x * aspect, fromCenter.y));
        float edgeMask = lerp(
            smoothstep(_EdgeStart, 0.72, radialDistance),
            1.0,
            saturate(_FullScreenBlend)) * _Intensity;
        float2 sampleStep = fromCenter * (_BlurRadius * edgeMask);

        float3 color = SampleInput(uv) * 0.28;
        color += SampleInput(uv - sampleStep) * 0.22;
        color += SampleInput(uv - sampleStep * 2.0) * 0.18;
        color += SampleInput(uv - sampleStep * 3.0) * 0.14;
        color += SampleInput(uv + sampleStep) * 0.10;
        color += SampleInput(uv + sampleStep * 2.0) * 0.08;

        return float4(color, 1.0);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "Sprint Edge Blur"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
    Fallback Off
}
