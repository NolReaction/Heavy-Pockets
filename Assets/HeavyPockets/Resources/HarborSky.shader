Shader "HeavyPockets/HarborSky"
{
    Properties
    {
        _Zenith ("Upper sky", Color) = (.2,.48,.68,1)
        _Horizon ("Horizon", Color) = (.8,.85,.82,1)
        _CloudLit ("Cloud light", Color) = (.97,.96,.89,1)
        _CloudShade ("Cloud shade", Color) = (.55,.66,.72,1)
        _Disk ("Sun", Color) = (1,.94,.75,1)
        _SunDirection ("Sun direction", Vector) = (.36,.62,.48,0)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct Varyings { float4 position : SV_POSITION; float3 ray : TEXCOORD0; };
            half4 _Zenith, _Horizon, _CloudLit, _CloudShade, _Disk;
            float4 _SunDirection;
            half _HarborPoster, _HarborDusk;

            Varyings vert(float4 vertex : POSITION)
            {
                Varyings o;
                o.position = UnityObjectToClipPos(vertex);
                o.ray = vertex.xyz;
                return o;
            }

            float hash(float2 p)
            {
                float3 n = frac(float3(p.xyx) * .1031);
                n += dot(n, n.yzx + 33.33);
                return frac((n.x + n.y) * n.z);
            }
            float noise2(float2 p)
            {
                float2 id = floor(p), f = frac(p);
                f = f * f * (3 - 2*f);
                return lerp(lerp(hash(id), hash(id + float2(1,0)), f.x),
                            lerp(hash(id + float2(0,1)), hash(id + 1), f.x), f.y);
            }
            float cloudField(float2 p)
            {
                return .56 * noise2(p) + .28 * noise2(p*2.08 + 4.7) +
                       .12 * noise2(p*4.31 - 13.5) + .04 * noise2(p*8.6);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 dir = normalize(i.ray);
                float y = max(dir.y, 0);
                float3 sun = normalize(_SunDirection.xyz);
                float sunDot = saturate(dot(dir,sun));
                float skyGradient = pow(saturate(y), .52);
                half3 col = lerp(_Horizon.rgb, _Zenith.rgb, skyGradient);
                col = lerp(col, _Disk.rgb, pow(sunDot, 16) * (.10 + .14 * _HarborDusk) * (1 - _HarborPoster));

                // Project the cloud deck onto an elevated plane so it naturally
                // compresses at the horizon, independent of the runner's position.
                float2 plane = dir.xz / (y + .16);
                float2 uv = plane * float2(.84,1.52) + float2(4.3,1.1);
                uv *= lerp(1.42, 1.0, _HarborPoster);
                float cloud = cloudField(uv);
                float detail = noise2(uv * 14.7) - .5;
                cloud += .023 * detail * (1 - _HarborPoster);
                float edge = smoothstep(.504, .602, cloud);
                float posterEdge = smoothstep(.546, .552 + fwidth(cloud), cloud);
                edge = lerp(edge, posterEdge, _HarborPoster);
                float sunEdge = saturate(.62 + 4.8 * (cloudField(uv + sun.xz * .19) - cloud));
                sunEdge = lerp(sunEdge, smoothstep(.52,.56,sunEdge), _HarborPoster);
                half3 cloudColor = lerp(_CloudShade.rgb, _CloudLit.rgb, sunEdge);
                col = lerp(col, cloudColor, edge * smoothstep(.008,.1,y) * (.84 + .14 * _HarborPoster));
                float radius = lerp(.009, .066, _HarborPoster) + .006 * _HarborDusk;
                float dist = length(dir - sun);
                float disk = 1 - smoothstep(radius, radius + max(fwidth(dist),.0008), dist);
                col = lerp(col, _Disk.rgb, disk * (1 - .68 * edge));
                return half4(col,1);
            }
            ENDCG
        }
    }
    Fallback Off
}
