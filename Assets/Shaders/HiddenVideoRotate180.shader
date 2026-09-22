Shader "Hidden/DeFrag/VideoRotate180"
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
                return tex2D(_MainTex, 1.0 - input.uv);
            }
            ENDCG
        }
    }
}
