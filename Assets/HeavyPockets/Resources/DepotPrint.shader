Shader "HeavyPockets/DepotPrint"
{
    Properties { _MainTex ("Printed enamel", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        CGPROGRAM
        #pragma surface surf Lambert
        sampler2D _MainTex;
        struct Input { float2 uv_MainTex; };
        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed3 ink = tex2D(_MainTex, IN.uv_MainTex).rgb;
            o.Albedo = ink * 0.88;
            o.Emission = ink * 0.11;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
