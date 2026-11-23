Shader "HeavyPockets/DepotSign"
{
    Properties { _MainTex ("Font atlas", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        ZWrite Off
        ZTest LEqual
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Vertex { float4 position : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct Fragment { float4 position : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            sampler2D _MainTex;
            Fragment vert(Vertex input)
            {
                Fragment o;
                o.position = UnityObjectToClipPos(input.position);
                o.uv = input.uv;
                o.color = input.color;
                return o;
            }
            fixed4 frag(Fragment i) : SV_Target
            {
                return fixed4(i.color.rgb, i.color.a * tex2D(_MainTex, i.uv).a);
            }
            ENDCG
        }
    }
}
