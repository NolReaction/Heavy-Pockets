Shader "Hidden/Heavy Pockets/Tone Mapping"
{
    Properties { _MainTex ("Scene color", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment ToneMap
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _ExposureMultiplier;

            float4 ToneMap(v2f_img input) : SV_Target
            {
                float4 source = tex2D(_MainTex, input.uv);
                float3 color = max(0.0, source.rgb);
                #if defined(UNITY_COLORSPACE_GAMMA)
                    color = GammaToLinearSpace(color);
                #endif
                color *= _ExposureMultiplier;

                // Unit slope through the mids, then a soft shoulder that tends to display white.
                // A shared RGB scale keeps saturated paint and sunset highlights in their original hue.
                const float knee = 0.55;
                float peak = max(color.r, max(color.g, color.b));
                float shoulder = max(peak - knee, 0.0);
                float compressed = min(peak, knee) + (1.0 - knee) * shoulder / (1.0 - knee + shoulder);
                color *= compressed / max(peak, 1e-6);
                #if defined(UNITY_COLORSPACE_GAMMA)
                    color = LinearToGammaSpace(color);
                #endif
                return float4(color, source.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
