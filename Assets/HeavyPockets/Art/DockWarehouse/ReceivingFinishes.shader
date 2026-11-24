Shader "HeavyPockets/Receiving finishes"
{
    Properties
    {
        _MainTex ("Surface atlas", 2D) = "white" {}
        _Surface ("Normal XY and gloss", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
        sampler2D _MainTex, _Surface;
        struct Input { float2 metricUV; float finish; fixed4 tint; };
        void vert(inout appdata_full v, out Input data)
        {
            UNITY_INITIALIZE_OUTPUT(Input, data);
            data.metricUV = v.texcoord.xy;
            data.finish = v.texcoord1.x;
            data.tint = v.color;
        }
        void surf(Input i, inout SurfaceOutputStandard o)
        {
            float finish = floor(i.finish + .5);
            float2 tile = float2(fmod(finish,4), 1 - floor(finish/4));
            const float gutter = 4.0/384.0;
            float2 scale = (1-2*gutter)*float2(.25,.5);
            float2 pos = (tile+gutter)*float2(.25,.5)+frac(i.metricUV)*scale;
            float2 dx = ddx(i.metricUV)*scale, dy = ddy(i.metricUV)*scale;
            fixed3 baseColor = tex2Dgrad(_MainTex,pos,dx,dy).rgb;
            fixed3 surface = tex2Dgrad(_Surface,pos,dx,dy).rgb;
            o.Albedo = baseColor * i.tint.rgb;
            half2 xy = (surface.rg*2-1)*.52;
            o.Normal = half3(xy,sqrt(saturate(1-dot(xy,xy))));
            o.Metallic = finish==5 ? .68 : 0;
            o.Smoothness = finish==5 ? .54 : finish==4 ? .33 : .04 + .23*surface.b;
            o.Emission = i.tint.a < .5 ? o.Albedo*.67 : 0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
