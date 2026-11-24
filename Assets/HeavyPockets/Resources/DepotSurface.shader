Shader "HeavyPockets/DepotSurface"
{
    Properties
    {
        _Color ("Base colour", Color) = (0.65, 0.65, 0.65, 1)
        _Grain ("Pigment, surface slopes, abrasion", 2D) = "gray" {}
        _Finish ("Pigment variation, gloss, reflectance, relief", Vector) = (0.25, 0.36, 0.07, 0.16)
        _GrainScale ("Horizontal and vertical grain frequency", Vector) = (0.85, 0.85, 0, 0)
        _Wear ("Exposed substrate", Range(0,1)) = 0.25
        _Glow ("Luminous coating", Range(0,2)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf HarborSpecular fullforwardshadows vertex:vert
        #pragma multi_compile_instancing
        #include "UnityPBSLighting.cginc"
        #include "HarborSurfaceLighting.cginc"

        inline half4 LightingHarborSpecular(SurfaceOutputStandardSpecular s, float3 viewDir, UnityGI gi)
        {
            return LightingStandardSpecular(s, viewDir, gi);
        }

        inline void LightingHarborSpecular_GI(SurfaceOutputStandardSpecular s, UnityGIInput data, inout UnityGI gi)
        {
            LightingStandardSpecular_GI(s, data, gi);
            #if defined(UNITY_PASS_FORWARDBASE)
            gi.light.color *= HarborKeyTransmission(data.worldPos.xz);
            #endif
        }

        inline half4 LightingHarborSpecular_Deferred(SurfaceOutputStandardSpecular s, float3 viewDir,
            UnityGI gi, out half4 g0, out half4 g1, out half4 g2)
        {
            return LightingStandardSpecular_Deferred(s, viewDir, gi, g0, g1, g2);
        }

        sampler2D _Grain;
        half4 _Color, _Finish;
        float4 _GrainScale;
        half _Glow, _Wear, _HarborPoster, _HarborDusk;

        struct Input
        {
            float4 materialPosition;
            half3 surfaceNormal;
            half4 surfaceTangent;
        };

        void vert(inout appdata_full v, out Input data)
        {
            UNITY_INITIALIZE_OUTPUT(Input, data);
            // Metric grain size follows the part as it moves. Combined room meshes
            // share the room origin, so adjoining metal panels have a common scale.
            data.materialPosition.xyz = mul((float3x3)unity_ObjectToWorld, v.vertex.xyz);
            data.materialPosition.w = mul(unity_ObjectToWorld, v.vertex).y;
            data.surfaceNormal = UnityObjectToWorldNormal(v.normal);
            data.surfaceTangent.xyz = UnityObjectToWorldDir(v.tangent.xyz);
            data.surfaceTangent.w = v.tangent.w * unity_WorldTransformParams.w;
        }

        void surf(Input i, inout SurfaceOutputStandardSpecular o)
        {
            float3 p = i.materialPosition.xyz;
            half3 n = normalize(i.surfaceNormal);
            half3 weights = abs(n);
            weights *= weights;
            weights *= weights;
            weights /= max(0.001h, weights.x + weights.y + weights.z);

            half4 side = tex2D(_Grain, p.zy * _GrainScale.xy);
            half4 top = tex2D(_Grain, p.xz * _GrainScale.xy);
            half4 front = tex2D(_Grain, p.xy * _GrainScale.xy);
            half4 grain = side * weights.x + top * weights.y + front * weights.z;
            half stain = tex2D(_Grain, p.xz * 0.17 + p.yy * float2(0.08, 0.12)).r;
            half pigment = 0.68h * (grain.r * 2 - 1) + 0.32h * (stain * 2 - 1);
            half lowDust = saturate(1 - i.materialPosition.w / 0.65) * (1 - abs(n.y));
            half3 coat = _Color.rgb * (1 + pigment * _Finish.x) * (1 - lowDust * 0.08h);
            half abrasion = saturate(grain.a * _Wear);
            o.Albedo = lerp(coat, coat * 0.53h + half3(0.25h, 0.24h, 0.21h), abrasion);
            o.Smoothness = saturate(_Finish.y + pigment * _Finish.x * 0.09h + abrasion * 0.08h);
            o.Specular = _Finish.z * lerp(half3(1,1,1), _Color.rgb, 0.18h) + abrasion * 0.035h;
            o.Occlusion = 1 - lowDust * 0.11h;
            if (_HarborPoster > .5h && _Glow < .2h)
            {
                // In the printed treatment, colored ink sits in broad shapes.
                // Screen-frequency filtering keeps the world-space hatch stable at range.
                half tone = dot(_Color.rgb, half3(.28h,.53h,.19h));
                half3 ink = half3(.10h,.245h,.26h);
                half3 mid = lerp(half3(.24h,.52h,.51h), half3(.77h,.40h,.20h),
                                 smoothstep(-.025h,.09h,_Color.r - _Color.b));
                half3 paper = half3(.94h,.84h,.60h);
                half3 printColor = lerp(ink, mid, smoothstep(.15h,.47h,tone));
                printColor = lerp(printColor, paper, smoothstep(.52h,.85h,tone));
                float phase = (p.x + p.z) * 4.5 + p.y * 7.5;
                float footprint = max(fwidth(phase), .001);
                float stripe = (1 - smoothstep(.10,.10 + footprint, abs(frac(phase) - .5)))
                             * (1 - smoothstep(.16,.42,footprint));
                half shade = 1 - smoothstep(-.4,.6,dot(n,_HarborSunDirection.xyz));
                o.Albedo = lerp(printColor, ink, .34h * stripe * shade) * (1 + pigment * .055h);
                o.Smoothness *= .26h;
                o.Specular *= .18h;
            }
            o.Emission = _Glow * _Color.rgb * (1 + _HarborDusk * 1.4h);
            o.Alpha = 1;

            half2 sx = side.gb * 2 - 1, sy = top.gb * 2 - 1, sz = front.gb * 2 - 1;
            half3 slopes = half3(0, sx.y, sx.x) * weights.x
                         + half3(sy.x, 0, sy.y) * weights.y
                         + half3(sz.x, sz.y, 0) * weights.z;
            half3 t = i.surfaceTangent.xyz;
            half3 b = cross(n, t) * i.surfaceTangent.w;
            o.Normal = normalize(half3(-dot(t, slopes) * _Finish.w, -dot(b, slopes) * _Finish.w, 1));
        }
        ENDCG
    }
    FallBack "Diffuse"
}
