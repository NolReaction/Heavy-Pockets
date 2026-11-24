Shader "HeavyPockets/CoastalGround"
{
    Properties
    {
        _Conifers ("Coastal conifer cutouts", 2D) = "white" {}
        _Plants ("Grass, scrub and pine sprays", 2D) = "white" {}
        _Bark ("Yard pine bark", 2D) = "white" {}
        _Walker ("Runner's feet", Vector) = (0,-100,0,0)
        _Cutoff ("Canopy coverage", Range(0,1)) = .32
        _Grain ("Surface detail", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf HarborDiffuse fullforwardshadows addshadow alphatest:_Cutoff vertex:vert
        #include "HarborSurfaceLighting.cginc"
        sampler2D _Grain, _Conifers, _Plants, _Bark;
        float4 _Walker;
        half _HarborPoster, _HarborDusk;
        struct Input
        {
            float3 worldPos;
            float2 cardUV;
            fixed4 color : COLOR;
        };
        void vert(inout appdata_full v, out Input data)
        {
            UNITY_INITIALIZE_OUTPUT(Input, data);
            data.cardUV = v.texcoord.xy;
            if (v.color.a < .375)
            {
                float3 world = mul(unity_ObjectToWorld,v.vertex).xyz;
                float tip = saturate(v.texcoord.y);
                float phase = _HarborWindOffset.w + dot(world.xz,float2(.19,.28));
                float wave = sin(phase) + .23*sin(phase*3 + world.x*.49);
                float flex = v.color.a < .125 ? .22 : .093;
                float2 offset = float2(.86,.51) * wave * flex * tip * tip * _HarborWindOffset.z;
                if (v.color.a > .125 && (v.texcoord.x < .5 || v.texcoord.x >= .75))
                {
                    float2 delta = world.xz - _Walker.xz;
                    float reach = saturate(1.0 - length(delta)/.70)
                        * saturate(1.0 - abs(world.y - _Walker.y - .24));
                    offset += delta * (reach * tip * 1.15);
                    v.vertex.y -= reach * tip * .095;
                }
                v.vertex.xz += offset;
                v.normal = normalize(float3(v.normal.x*.32,1,v.normal.z*.32));
            }
        }
        void surf(Input i, inout SurfaceOutput o)
        {
            float2 uv = i.worldPos.xz;
            half broad = tex2D(_Grain, uv * .012).r;
            half3 color = i.color.rgb;
            o.Alpha = 1;
            if (i.color.a < .125)
            {
                fixed4 tree = tex2D(_Conifers, i.cardUV);
                color *= tree.rgb;
                o.Alpha = tree.a;
            }
            else if (i.color.a < .375)
            {
                fixed4 foliage = tex2D(_Plants, i.cardUV);
                half3 leaves = foliage.rgb;
                // Lift the darkest leaves while keeping warm tips and stem silhouettes.
                leaves = leaves*.79h + half3(.092h,.11h,.061h);
                color *= leaves;
                o.Alpha = foliage.a;
            }
            else if (i.color.a < .625)
            {
                color *= tex2D(_Bark, i.cardUV).rgb;
            }
            // The foliage palette follows the same pigment shift as the meadow.
            color = lerp(color, color * half3(.75,1.02,1.12), _HarborDusk * .65);
            color = lerp(color, color * half3(.86,1.08,.88), _HarborPoster * .6);
            color *= .93h + broad*.14h;
            half band = floor(broad * 5 + .5) / 5;
            o.Albedo = color * lerp(1, .84 + band * .3, _HarborPoster);
        }
        ENDCG
    }
    FallBack "Diffuse"
}
