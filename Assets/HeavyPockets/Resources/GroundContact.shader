Shader "HeavyPockets/GroundContact"
{
    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" }
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(appdata_base v)
            {
                Varyings o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy * 2 - 1;
                return o;
            }
            fixed4 frag(Varyings i) : SV_Target
            {
                half coverage = 1 - smoothstep(0.12, 1.0, dot(i.uv, i.uv));
                return fixed4(0.032, 0.042, 0.048, coverage * 0.42);
            }
            ENDCG
        }
    }
}
