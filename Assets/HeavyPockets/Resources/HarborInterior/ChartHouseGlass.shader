Shader "HeavyPockets/Chart House Window"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard alpha:premul noforwardadd
        struct Input { float facing : VFACE; };

        void surf(Input i, inout SurfaceOutputStandard o)
        {
            o.Normal = half3(0,0,i.facing >= 0 ? 1 : -1);
            o.Albedo = half3(.39h,.53h,.49h);
            o.Metallic = 0;
            o.Smoothness = .92h;
            o.Occlusion = 1;
            o.Alpha = .045h;
        }
        ENDCG
    }
    FallBack Off
}
