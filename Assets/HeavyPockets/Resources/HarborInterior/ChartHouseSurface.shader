Shader "HeavyPockets/Chart House Surface"
{
    Properties
    {
        _Color ("Pigment", Color) = (1,1,1,1)
        _MainTex ("Base color", 2D) = "white" {}
        _Detail ("Normal XY / occlusion / smoothness", 2D) = "gray" {}
        _Scale ("Repeats per metre", Float) = 1
        _Print ("Use print UVs", Range(0,1)) = 0
        _Relief ("Normal strength", Range(0,2)) = 1
        _Metallic ("Metallic", Range(0,1)) = 0
        _Gloss ("Smoothness multiplier and offset", Vector) = (1,0,0,0)
        _Light ("Luminance", Range(0,3)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf HarborPBR vertex:vert fullforwardshadows addshadow
        #include "UnityPBSLighting.cginc"
        #include "../HarborSurfaceLighting.cginc"

        sampler2D _MainTex, _Detail;
        half4 _Color;
        half _Print, _Relief, _Metallic, _Light;
        float _Scale;
        half2 _Gloss;

        inline half4 LightingHarborPBR(SurfaceOutputStandard surface, float3 view, UnityGI gi)
        {
            return LightingStandard(surface, view, gi);
        }

        inline void LightingHarborPBR_GI(SurfaceOutputStandard surface, UnityGIInput data, inout UnityGI gi)
        {
            LightingStandard_GI(surface, data, gi);
            // Timber and ochre walls warm the indirect light in sheltered corners.
            gi.indirect.diffuse *= lerp(half3(1,.92h,.80h), half3(1,1,1), surface.Occlusion);
            #if defined(UNITY_PASS_FORWARDBASE)
            gi.light.color *= HarborKeyTransmission(data.worldPos.xz);
            #endif
        }

        inline half4 LightingHarborPBR_Deferred(SurfaceOutputStandard surface, float3 view, UnityGI gi,
            out half4 g0, out half4 g1, out half4 g2)
        {
            return LightingStandard_Deferred(surface, view, gi, g0, g1, g2);
        }

        struct Input
        {
            float2 scanUV;
            float2 printUV;
            half4 pigment;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input,o);
            float3 n = normalize(UnityObjectToWorldNormal(v.normal));
            float3 a = abs(n);
            float3 tangent, bitangent;
            if (a.y >= a.x && a.y >= a.z)
            {
                tangent = float3(1,0,0);
                bitangent = float3(0,0,-sign(n.y));
            }
            else if (a.x > a.z)
            {
                tangent = float3(0,0,-sign(n.x));
                bitangent = float3(0,1,0);
            }
            else
            {
                tangent = float3(sign(n.z),0,0);
                bitangent = float3(0,1,0);
            }
            // Metric projection keeps bricks, timber and grout the same physical size
            // across separately batched walls, furniture and the distant facade.
            float3 p = mul(unity_ObjectToWorld,v.vertex).xyz;
            o.scanUV = float2(dot(p,tangent),dot(p,bitangent)) * _Scale;
            o.printUV = v.texcoord.xy;
            o.pigment = v.color;
            float3 projectedTangent = normalize(tangent - n * dot(tangent,n));
            v.tangent = float4(normalize(mul((float3x3)unity_WorldToObject,projectedTangent)),
                unity_WorldTransformParams.w);
        }

        void surf(Input i, inout SurfaceOutputStandard o)
        {
            fixed3 color = tex2D(_MainTex,lerp(i.scanUV,i.printUV,_Print)).rgb * _Color.rgb * i.pigment.rgb;
            half4 detail = tex2D(_Detail,i.scanUV);
            half2 xy = (detail.rg * 2 - 1) * _Relief;
            o.Albedo = color;
            o.Normal = normalize(half3(xy,sqrt(saturate(1-dot(xy,xy)))));
            // Vertex alpha is the room's authored contact/portal occlusion.
            o.Occlusion = detail.b * i.pigment.a;
            o.Smoothness = saturate(detail.a * _Gloss.x + _Gloss.y);
            o.Metallic = _Metallic;
            o.Emission = _Light * color;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
