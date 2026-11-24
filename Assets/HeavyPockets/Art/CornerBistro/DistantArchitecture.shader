Shader "Heavy Pockets/Architecture/Distant Vertex Color"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 150
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        struct Input { float4 color : COLOR; };
        void surf(Input input, inout SurfaceOutputStandard output)
        {
            half3 color = input.color.rgb;
            #ifndef UNITY_COLORSPACE_GAMMA
                color = GammaToLinearSpace(color);
            #endif
            output.Albedo = color;
            output.Smoothness = 0.21h;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
