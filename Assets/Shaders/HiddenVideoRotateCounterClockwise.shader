Shader "Hidden/DeFrag/VideoRotateCounterClockwise"
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
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            fixed4 frag(v2f_img input) : SV_Target
            {
                // Match FFmpeg transpose=2. The FBX screen UV rotates this
                // portrait texture back into landscape orientation.
                float2 rotatedUv = float2(input.uv.y, 1.0 - input.uv.x);
                return tex2D(_MainTex, rotatedUv);
            }
            ENDCG
        }
    }
}
