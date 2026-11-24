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
        _SunDiameter ("Sun angular diameter (degrees)", Range(.2,12)) = 1.15
        _SunVisibility ("Sun brightness", Range(0,1)) = 1
        _Haze ("Maritime haze", Range(0,1)) = .35
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
            #include "HarborAtmosphere.cginc"
            struct Varyings { float4 position : SV_POSITION; float3 ray : TEXCOORD0; };
            half4 _Zenith, _Horizon, _CloudLit, _CloudShade, _Disk;
            float4 _SunDirection;
            half _HarborPoster, _HarborDusk, _SunVisibility, _Haze;
            float _SunDiameter;

            Varyings vert(float4 vertex : POSITION)
            {
                Varyings o;
                o.position = UnityObjectToClipPos(vertex);
                o.ray = vertex.xyz;
                return o;
            }

            float StarHash(float2 p)
            {
                float3 n = frac(float3(p.xyx) * .1031);
                n += dot(n, n.yzx + 33.33);
                return frac((n.x + n.y) * n.z);
            }
            half Stars(float3 dir)
            {
                // Octahedral projection keeps the small, fixed stars stable when the camera turns.
                float2 grid = dir.xz / dot(abs(dir), float3(1,1,1)) * 122;
                float2 cell = floor(grid);
                float seed = StarHash(cell);
                float2 toStar = frac(grid) - (.18 + .64 * float2(StarHash(cell + 51), StarHash(cell - 13)));
                float aa = max(fwidth(grid.x) + fwidth(grid.y), .019);
                float radius = .035 + .040 * seed;
                float disk = 1 - smoothstep(max(0, radius - aa * .4), radius + aa * .4, length(toStar));
                return disk * smoothstep(.975, .999, seed) * saturate(2 * radius / aa);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 dir = normalize(i.ray);
                float y = saturate(dir.y);
                float3 sun = dot(_HarborSunDirection.xyz, _HarborSunDirection.xyz) > .5
                    ? _HarborSunDirection.xyz : normalize(_SunDirection.xyz);
                float sunDot = saturate(dot(dir,sun));
                half dusk = 1 - smoothstep(.05h, .48h, sun.y);
                half night = 1 - smoothstep(-.24h, -.035h, sun.y);
                float extinction = exp2(-y * lerp(6.7h, 2.4h, _Haze));
                half3 col = lerp(_Zenith.rgb, _Horizon.rgb, extinction);
                float sunDistance = length(dir - sun);
                float radius = radians(_SunDiameter) * .5;
                half aureole = exp2(-sunDistance * lerp(12.0, 5.3, _Haze));
                half scatter = aureole * (.22h + .22h * _Haze) +
                               pow(sunDot, 5) * dusk * .19h * extinction;
                col = lerp(col, _Disk.rgb, saturate(scatter * _SunVisibility) * (1 - .73h * _HarborPoster));

                if (sun.y < -.035)
                {
                    half starVisibility = night * smoothstep(.025h, .26h, y) * (1 - .55h * _Haze);
                    col += half3(.76h,.86h,1) * Stars(dir) * starVisibility;
                    float moonDistance = length(dir + sun);
                    half moon = 1 - smoothstep(.007, .007 + max(fwidth(moonDistance), .00025), moonDistance);
                    col += half3(.73h,.82h,1) * night * (moon * .84h + exp2(-moonDistance * 33) * .12h);
                }

                float diskAA = max(fwidth(sunDistance), .00016);
                half disk = 1 - smoothstep(radius - diskAA * .5, radius + diskAA * .5, sunDistance);
                col = lerp(col, _Disk.rgb * lerp(1.32h, 1.0h, _HarborPoster), disk * _SunVisibility);

                float2 uv = HarborCloudUV(_WorldSpaceCameraPos.xz, dir);
                half4 cumulus = tex2D(_HarborCloudField, uv);
                // Cirrus is projected higher and shears relative to the low deck.
                float2 cirrusUV = uv * 3.8 + _HarborWindOffset.xy * (.30 / 11300.0);
                half4 fine = tex2D(_HarborCloudField, cirrusUV + float2(.49,.31));
                half horizonFade = smoothstep(.003h, .048h + .10h * _Haze, y);
                half veil = smoothstep(.52h,.76h, fine.a) * (.10h + .20h * _Haze);
                veil *= smoothstep(0,.35h,_HarborCloudCoverage) * horizonFade * (1 - .75h * _HarborPoster);
                col = lerp(col, _CloudLit.rgb, veil);

                half threshold = HarborCloudThreshold();
                half detail = (fine.r - .64h) * .10h * (1 - _HarborPoster);
                half density = cumulus.r + detail;
                half edgeWidth = max(fwidth(density), lerp(.053h,.014h,_HarborPoster));
                half opacity = smoothstep(threshold - edgeWidth * .6h, threshold + edgeWidth, density) * horizonFade;
                // Looking across a distant cloud reveals the higher, narrower sunlit crown
                // above its broad base. Offset the ray to that shell before drawing the underside.
                half4 crown = tex2D(_HarborCloudField, uv + HarborCloudRayTravel(dir) * .145);
                half crownCover = smoothstep(threshold + .044h, threshold + .13h, crown.r + detail);
                half crownLight = saturate(.86h - dot(crown.gb * 2 - 1, sun.xz) * .52h);
                half3 crownColor = lerp(_CloudShade.rgb, _CloudLit.rgb, crownLight);
                col = lerp(col, lerp(crownColor, _Horizon.rgb, extinction * .26h), crownCover * horizonFade);
                // GB encodes the change in density across the cloud. The shoulder
                // facing the sun catches light; deep undersides pick up the sky.
                half2 gradient = (cumulus.gb * 2 - 1) + (fine.gb * 2 - 1) * .22h * (1 - _HarborPoster);
                half sunward = -dot(gradient, sun.xz);
                half thickness = smoothstep(threshold - .035h, threshold + .235h, density);
                half light = saturate(.77h - .63h * thickness + .64h * sunward);
                light = lerp(light, .27h + .73h * smoothstep(.53h,.57h,light), _HarborPoster);
                half3 cloud = lerp(_CloudShade.rgb, _CloudLit.rgb, light);
                half rim = (1 - thickness) * (.10h + .72h * aureole) * _SunVisibility * (1 - _HarborPoster);
                cloud = lerp(cloud, _Disk.rgb * 1.15h, rim);
                cloud = lerp(cloud, _Horizon.rgb, extinction * (.13h + .27h * _Haze));
                col = lerp(col, cloud, opacity);
                // Static sub-LSB dither prevents bands in the dim, wide twilight gradient.
                col += (frac(52.9829189 * frac(dot(i.position.xy, float2(.06711056,.00583715)))) - .5) / 255.0;
                return half4(col,1);
            }
            ENDCG
        }
    }
    Fallback Off
}
