Shader "HeavyPockets/CoastalWater"
{
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            #include "HarborSurfaceLighting.cginc"

            struct Varyings
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                SHADOW_COORDS(2)
            };
            half4 _HarborSeaColor, _HarborSkyZenith, _HarborSkyHorizon;
            half _HarborPoster;

            Varyings vert(appdata_base v)
            {
                Varyings o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_FOG(o,o.pos);
                TRANSFER_SHADOW(o)
                return o;
            }

            float2 WaveSlopes(float2 p, float t)
            {
                float2 phase = float2(dot(p,float2(.56,.21)) + sin(p.y*.24+t*.25) + t,
                                      dot(p,float2(-.32,1.45)) + sin(p.x*.21-t*.45) - t*1.35);
                // Drop wave frequencies smaller than a pixel; distant highlights
                // then resolve into a continuous band instead of flashing specks.
                float2 amplitude = 1 - smoothstep(.7, 3.14, fwidth(phase));
                float2 ripple = cos(phase) * amplitude;
                return ripple.x * float2(.075,.033) + ripple.y * float2(-.035,.093);
            }

            half4 frag(Varyings i):SV_Target
            {
                float2 p = i.world.xz;
                float2 slopes = WaveSlopes(p, _Time.y*.76) + .28*WaveSlopes(p*3.7, _Time.y*1.08);
                float3 n = normalize(float3(slopes.x,1,slopes.y));
                float3 toEye = _WorldSpaceCameraPos - i.world;
                float distanceToEye = length(toEye);
                float3 eye = toEye / max(distanceToEye,.001);
                float3 reflection = reflect(-eye,n);
                half skyHeight = sqrt(saturate(reflection.y));
                half3 sky = lerp(_HarborSkyHorizon.rgb, _HarborSkyZenith.rgb, skyHeight);
                half clouds = HarborCloudCover(HarborCloudUV(p, reflection)) * smoothstep(.015h,.17h, reflection.y);
                half3 cloudBank = lerp(_HarborSkyHorizon.rgb, _HarborSkyZenith.rgb, .18h);
                sky = lerp(sky, cloudBank, clouds * .72h);

                // Unity's directional light follows the visible sun or moon.
                float3 key = _WorldSpaceLightPos0.xyz;
                half visibility = SHADOW_ATTENUATION(i) * HarborKeyTransmission(p) * smoothstep(-.02h,.08h,key.y);
                half3 keyLight = _LightColor0.rgb * visibility;
                half3 skylight = .30h * _HarborSkyZenith.rgb + .27h * _HarborSkyHorizon.rgb;
                half3 body = _HarborSeaColor.rgb * (.06h + skylight + keyLight * (.71h * saturate(dot(n,key))));
                float grazing = 1 - saturate(dot(n,eye));
                half fresnel = .025h + .975h * grazing * grazing * grazing * grazing * grazing;
                half3 color = lerp(body, sky, fresnel);

                float3 halfVector = eye + key;
                float alignment = saturate(dot(n, halfVector * rsqrt(max(dot(halfVector,halfVector),.0001))));
                float roughness = smoothstep(25,190,distanceToEye);
                float glint = pow(alignment, lerp(260,105,roughness));
                glint = lerp(glint, smoothstep(.24,.51,glint), _HarborPoster);
                color += keyLight * glint * lerp(1.70h,.88h,roughness);
                half stroke = smoothstep(.11,.14,slopes.y) * saturate(1-distanceToEye/110) * _HarborPoster;
                color += stroke * .21h * (skylight + keyLight);
                half4 result = half4(color,1);
                UNITY_APPLY_FOG(i.fogCoord,result);
                return result;
            }
            ENDCG
        }
    }
}
