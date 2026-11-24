Shader "HeavyPockets/CoastalGround"
{
    Properties
    {
        _Habitat ("Material type", Float) = 0
        _Grain ("Surface detail", 2D) = "gray" {}
        _CoastColor ("Rock, gravel and moss", 2D) = "gray" {}
        _CoastNormals ("Ground relief", 2D) = "bump" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf HarborDiffuse fullforwardshadows addshadow vertex:vert
        #include "HarborSurfaceLighting.cginc"
        sampler2D _Grain, _CoastColor, _CoastNormals;
        half _Habitat, _HarborPoster, _HarborDusk;
        half4 _HarborLandColor, _HarborGrassColor;
        struct Input
        {
            float3 worldPos;
            half slope;
            fixed4 color : COLOR;
        };
        void vert(inout appdata_full v, out Input data)
        {
            UNITY_INITIALIZE_OUTPUT(Input, data);
            float3 n = normalize(v.normal);
            float3 axis = abs(n.x) < .96 ? float3(1,0,0) : float3(0,0,1);
            v.tangent = float4(normalize(axis - n * dot(axis,n)), -1);
            data.slope = saturate((1 - n.y) * 2.8);
        }
        void surf(Input i, inout SurfaceOutput o)
        {
            float2 uv = i.worldPos.xz;
            half broad = tex2D(_Grain, uv * .012).r;
            half grassy = i.color.g;
            half tone = i.color.r;
            half pattern = broad;
            half3 color;
            if (_Habitat < .5)
            {
                half grain = tex2D(_Grain, uv * .39).r;
                half middle = tex2D(_Grain, uv * .065).r;
                pattern = broad * .5 + grain * .18 + middle * .32;
                half coverage = smoothstep(.26, .72, grassy * .58 + broad * .25 + middle * .28);
                color = lerp(_HarborLandColor.rgb, _HarborGrassColor.rgb, coverage * .94);
                half slope = i.slope;
                half3 rock = lerp(half3(.24,.25,.24), half3(.43,.43,.40), broad);
                color = lerp(color, rock, slope * .82);
                color *= .69 + tone * .31 + pattern * .38;
                float2 rockUV = uv / 13.0 + .09 * float2(broad,middle);
                half3 photo = tex2D(_CoastColor, rockUV).rgb;
                half luma = dot(photo,half3(.24,.62,.14));
                half3 moss = lerp(luma.xxx, photo, .68);
                color = lerp(moss * lerp(.92h,1.14h,coverage), color * (.53h+luma), .28h + .37h*_HarborPoster);
                half2 normalXY = (tex2D(_CoastNormals,rockUV).rg * 2-1) * (.56h-.26h*_HarborPoster);
                o.Normal = normalize(half3(normalXY, 1));
            }
            else
            {
                color = i.color.rgb;
                // The foliage palette follows the same pigment shift as the meadow.
                color = lerp(color, color * half3(.75,1.02,1.12), _HarborDusk * .65);
                color = lerp(color, color * half3(.86,1.08,.88), _HarborPoster * .6);
                color *= .88 + pattern * .22;
            }
            half band = floor(pattern * 5 + .5) / 5;
            o.Albedo = color * lerp(1, .84 + band * .3, _HarborPoster);
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
