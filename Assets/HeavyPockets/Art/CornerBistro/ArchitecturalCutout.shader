Shader "Heavy Pockets/Architecture/Coverage Cutout"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Colour and opacity", 2D) = "white" {}
        _Cutoff ("Coverage threshold", Range(0,1)) = 0.42
        [Normal] _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal strength", Range(0,2)) = 1
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.27
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        AlphaToMask On
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard fullforwardshadows addshadow keepalpha
        #include "UnityStandardUtils.cginc"

        sampler2D _MainTex, _BumpMap;
        fixed4 _Color;
        half _Cutoff, _Glossiness, _Metallic, _BumpScale;
        float _HarborSampledEdges;
        struct Input { float2 uv_MainTex; };

        void surf(Input i, inout SurfaceOutputStandard o)
        {
            fixed4 texel = tex2D(_MainTex, i.uv_MainTex) * _Color;
            o.Albedo = texel.rgb;
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, i.uv_MainTex), _BumpScale);
            o.Smoothness = _Glossiness;
            o.Metallic = _Metallic;
            o.Alpha = 1;
            #if defined(UNITY_PASS_FORWARDBASE) || defined(UNITY_PASS_FORWARDADD)
                if (_HarborSampledEdges > 0.5)
                {
                    float width = max(fwidth(texel.a), 1.0 / 255.0);
                    o.Alpha = saturate((texel.a - _Cutoff) / width + 0.5);
                    clip(o.Alpha - 0.001);
                    return;
                }
            #endif
            clip(texel.a - _Cutoff);
        }
        ENDCG
    }
    FallBack "Legacy Shaders/Transparent/Cutout/Diffuse"
}
