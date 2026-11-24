Shader "HeavyPockets/DepotSign"
{
    Properties
    {
        _MainTex ("Font atlas", 2D) = "white" {}
        [PerRendererData] _Glow ("Luminous coating", Range(0,2)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "DisableBatching" = "True" }
        ZWrite Off
        ZTest LEqual
        Cull Off
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf HarborDiffuse alpha:fade fullforwardshadows vertex:vert
        #include "HarborSurfaceLighting.cginc"

        sampler2D _MainTex;
        half _Glow, _HarborDusk;
        struct Input
        {
            float2 uv_MainTex;
            fixed4 color : COLOR;
        };
        void vert(inout appdata_full v)
        {
            // TextMesh glyphs face local -Z. Keep this frame for oppositely
            // mounted signs when Unity would otherwise batch them in world space.
            v.normal = float3(0,0,-1);
        }
        void surf(Input i, inout SurfaceOutput o)
        {
            o.Albedo = i.color.rgb;
            o.Emission = i.color.rgb * _Glow * (1 + 1.4h * _HarborDusk);
            o.Alpha = i.color.a * tex2D(_MainTex, i.uv_MainTex).a;
        }
        ENDCG
    }
    Fallback Off
}
