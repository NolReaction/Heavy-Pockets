Shader "HeavyPockets/CoastalWater"
{
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            struct Varyings { float4 pos:SV_POSITION; float3 world:TEXCOORD0; UNITY_FOG_COORDS(1) };
            half4 _HarborSeaColor;
            half _HarborPoster, _HarborDusk;
            Varyings vert(appdata_base v)
            {
                Varyings o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_FOG(o,o.pos);
                return o;
            }
            float2 waves(float2 p, float t)
            {
                float a = p.x * .56 + p.y * .21 + sin(p.y*.24+t*.25) + t;
                float b = p.y * 1.45 - p.x*.32 + sin(p.x*.21-t*.45) - t*1.35;
                return float2(cos(a)*.075 - cos(b)*.035, cos(a)*.033 + cos(b)*.093);
            }
            half4 frag(Varyings i):SV_Target
            {
                float2 p = i.world.xz;
                float2 slopes = waves(p, _Time.y*.76) + .28*waves(p*3.7, _Time.y*1.08);
                float3 n = normalize(float3(slopes.x,1,slopes.y));
                float3 eye = normalize(_WorldSpaceCameraPos-i.world);
                float fresnel = pow(1-saturate(dot(n,eye)),3);
                float sky = .5 + .5*sin(p.y*.044 + p.x*.069 + sin(p.y*.09));
                half3 color = _HarborSeaColor.rgb * (.66 + sky*.25 + slopes.y*.48);
                color = lerp(color, unity_FogColor.rgb, fresnel * (.43+sky*.1));
                float sun = saturate(dot(n,normalize(eye + _WorldSpaceLightPos0.xyz)));
                float shine = pow(sun, 110);
                shine = lerp(shine, smoothstep(.18,.48,shine), _HarborPoster);
                color += _LightColor0.rgb * shine * .62;
                float stroke = smoothstep(.109,.13,slopes.y) * saturate(1-distance(i.world,_WorldSpaceCameraPos)/180);
                color += half3(.17,.18,.16) * stroke * (.38 + _HarborPoster*.32);
                half4 result = half4(color,1);
                UNITY_APPLY_FOG(i.fogCoord,result);
                return result;
            }
            ENDCG
        }
    }
}
