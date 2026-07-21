Shader "Hidden/DeFrag/SyntheticVision"
{
    HLSLINCLUDE

    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
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
    float _Sharpening;
    float _ColorSteps;
    float _Quantization;
    float _PeripheralFalloff;
    float _PeripheralStart;

    float3 LoadInput(int2 pixelPosition)
    {
        int2 maximumPosition = max(int2(_ScreenSize.xy) - 1, 0);
        pixelPosition = clamp(pixelPosition, int2(0, 0), maximumPosition);
        return LOAD_TEXTURE2D_X(_InputTexture, pixelPosition).rgb;
    }

    float4 FullScreenPass(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        float2 uv = input.texcoord.xy;
        int2 positionSS = int2(uv * _ScreenSize.xy);
        float2 centeredUv = (uv - 0.5) * 2.0;
        centeredUv.x *= _ScreenSize.x / max(_ScreenSize.y, 1.0);

        float radius = saturate(length(centeredUv));
        float edgeMask = smoothstep(_PeripheralStart, 1.0, radius);
        float edgeStrength = edgeMask * _PeripheralFalloff;

        // 주변부에서는 화면 좌표를 작은 픽셀 블록에 맞춰 샘플링한다.
        float blockSize = lerp(1.0, 4.0, edgeStrength);
        float2 snappedPosition = floor(float2(positionSS) / blockSize) * blockSize + blockSize * 0.5;
        int2 processedPosition = int2(lerp(float2(positionSS), snappedPosition, edgeStrength));

        float3 original = LoadInput(positionSS);
        float3 center = LoadInput(processedPosition);
        float3 neighbours =
            LoadInput(processedPosition + int2(1, 0)) +
            LoadInput(processedPosition - int2(1, 0)) +
            LoadInput(processedPosition + int2(0, 1)) +
            LoadInput(processedPosition - int2(0, 1));

        float3 sharpened = center + (center - neighbours * 0.25) * _Sharpening;
        sharpened = max(sharpened, 0.0);

        float steps = max(_ColorSteps, 2.0);
        float3 quantized = floor(sharpened * steps + 0.5) / steps;
        float3 processed = lerp(sharpened, quantized, _Quantization);

        // 주변부의 채도도 아주 조금 줄여 렌즈 주변 처리 품질 저하를 표현한다.
        float luminance = Luminance(processed);
        processed = lerp(processed, luminance.xxx, edgeStrength * 0.2);

        return float4(lerp(original, processed, _Intensity), 1.0);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            Name "Synthetic Vision"
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
