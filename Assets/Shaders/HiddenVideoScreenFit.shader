// Draws a video into the part of a texture that a screen mesh actually samples.
// _MapX/_MapY turn the mesh UV into screen space (0..1, x = right, y = up as seen from the front),
// so sideways or flipped UV layouts show the video upright. The video keeps its aspect ratio;
// the bars around it are filled with a dimmed, blurred copy of the same frame.
// _SourceRect picks one tile when several videos share one decoded frame (an atlas).
Shader "Hidden/DeFrag/VideoScreenFit"
{
    Properties
    {
        _MainTex ("Video", 2D) = "black" {}
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _MapX;       // screen.x = dot(_MapX.xyz, float3(uv, 1))
            float4 _MapY;       // screen.y = dot(_MapY.xyz, float3(uv, 1))
            float4 _VideoRect;  // xy = scale, zw = offset: videoUV = screen * xy + zw
            float4 _FillRect;   // same for the blurred background (covers the whole screen)
            float4 _Fill;       // x = blur mip, y = brightness, z = edge softness (screen units)
            float4 _SourceRect; // part of the decoded frame this screen shows (xy = min, zw = size)

            // Tile-local UV -> decoded frame UV; the inset keeps filtering from bleeding into neighbours.
            float2 ToSource(float2 uv, float inset)
            {
                return _SourceRect.xy + clamp(uv, inset, 1.0 - inset) * _SourceRect.zw;
            }

            float3 SampleBlur(float2 tileUV)
            {
                float2 uv = ToSource(tileUV, 0.04);
                float2 o = _MainTex_TexelSize.xy * exp2(_Fill.x) * 0.75;
                float3 c = tex2Dlod(_MainTex, float4(uv, 0, _Fill.x)).rgb * 0.4;
                c += tex2Dlod(_MainTex, float4(uv + float2( o.x,  o.y), 0, _Fill.x)).rgb * 0.15;
                c += tex2Dlod(_MainTex, float4(uv + float2(-o.x,  o.y), 0, _Fill.x)).rgb * 0.15;
                c += tex2Dlod(_MainTex, float4(uv + float2( o.x, -o.y), 0, _Fill.x)).rgb * 0.15;
                c += tex2Dlod(_MainTex, float4(uv + float2(-o.x, -o.y), 0, _Fill.x)).rgb * 0.15;
                return c;
            }

            fixed4 frag(v2f_img input) : SV_Target
            {
                float3 uv1 = float3(input.uv, 1.0);
                float2 screen = float2(dot(_MapX.xyz, uv1), dot(_MapY.xyz, uv1));

                float2 videoUV = screen * _VideoRect.xy + _VideoRect.zw;
                float3 video = tex2Dlod(_MainTex, float4(ToSource(videoUV, 0.0), 0, 0)).rgb;

                float2 fillUV = saturate(screen * _FillRect.xy + _FillRect.zw);
                float3 fill = SampleBlur(fillUV) * _Fill.y;

                // Soft edge where the video meets the blurred bars.
                float2 inside = min(videoUV, 1.0 - videoUV) / max(_VideoRect.xy, 1e-4);
                float edge = saturate(min(inside.x, inside.y) / max(_Fill.z, 1e-4));
                return fixed4(lerp(fill, video, edge), 1.0);
            }
            ENDCG
        }
    }
}
