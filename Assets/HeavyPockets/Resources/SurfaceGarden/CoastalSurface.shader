Shader "HeavyPockets/Coastal surface layers"
{
    Properties
    {
        _Brushes ("Packed surface brushwork", 2D) = "gray" {}
        _Asphalt ("Asphalt", Color) = (.45,.465,.472,1)
        _Sand ("Dune sand", Color) = (.703,.637,.530,1)
        _Lawn ("Short turf", Color) = (.514,.551,.401,1)
        _Meadow ("Meadow", Color) = (.444,.507,.349,1)
        _Straw ("Dry grass", Color) = (.605,.578,.419,1)
        [Enum(VertexPaint,0,Asphalt,1,Sand,2,ShortTurf,3,Meadow,4,DryTurf,5)] _Layer ("Surface", Float) = 0
        [Toggle(_PORT_COAST)] _Port ("Port road layout", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf HarborDiffuse fullforwardshadows vertex:vert
        #pragma multi_compile_local __ _PORT_COAST
        #include "../HarborSurfaceLighting.cginc"

        sampler2D _Brushes;
        half4 _Asphalt, _Sand, _Lawn, _Meadow, _Straw;
        half _Layer, _HarborPoster, _HarborDusk;
        half4 _HarborGrassColor, _HarborLandColor;
        float4 _QuayRoad, _CrossRoad, _SpurRoad;

        struct Input
        {
            float3 worldPos;
            half4 paint;
            half slope;
        };
        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input,o);
            o.slope = saturate((1-v.normal.y)*2.8);
            o.paint = _Layer < .5h ? v.color : half4(abs(_Layer-1)<.5h,abs(_Layer-2)<.5h,
                abs(_Layer-5)<.5h,abs(_Layer-4)<.5h);
        }
        // The vector stores the centre and half extents of the paved area in metres.
        inline float roadDistance(float2 p, float4 road)
        {
            float2 q = abs(p-road.xy)-road.zw;
            return length(max(q,0.0)) + min(max(q.x,q.y),0.0);
        }
        void surf(Input i, inout SurfaceOutput o)
        {
            float2 p = i.worldPos.xz;
            float2 rotated = float2(.891*p.x-.454*p.y,.454*p.x+.891*p.y);
            half2 drift = half2(tex2D(_Brushes,p*.0083).a,tex2D(_Brushes,rotated*.0107+float2(.37,.81)).a)-.5h;
            half macro = tex2D(_Brushes, rotated*.024+drift*.29h).a-.5h;
            half mottling = tex2D(_Brushes, p*.094+drift*.24h).a-.5h;
            // A metre-scale lane shoulder stays readable from a third-person camera.
            half4 w = i.paint;
            #if defined(_PORT_COAST)
            float road = min(roadDistance(p,_QuayRoad),min(roadDistance(p,_CrossRoad),roadDistance(p,_SpurRoad)));
            w.r = 1-smoothstep(-.30,1.05,road+mottling*.65);
            w.g = saturate((.54-i.paint.g)*1.65 + .16 + macro*.19);
            w.b = saturate(.34 + macro*.95 + mottling*.33);
            w.a = saturate(.76*i.paint.g + .22 + macro*.61);
            #endif
            // Displace only existing transitions; the centres retain their pigment.
            w += w*(1-w)*(mottling*.60+macro*.32);
            half4 grain = tex2D(_Brushes, p*.515 + .035*macro);
            half4 brush = tex2D(_Brushes, rotated*.278 + float2(mottling*.21,macro*.34));
            // Sand mixed into a compacted aggregate loses its exposed wind ripples.
            half ripple = 1-smoothstep(.15h,.53h,w.r);
            half sandLight = (brush.g-.5h)*.205h*ripple + (grain.r-.5h)*.035h;
            half aggregate = (grain.r-.5h)*.19h + (brush.r-.5h)*.082h;
            half grassLight = (grain.b-.5h)*.27h + (brush.b-.5h)*.14h;
            half3 grass = lerp(_Lawn.rgb, _Meadow.rgb, w.a);
            grass = lerp(grass, _Straw.rgb, w.b);
            grass *= 1 + grassLight + .16h*macro + .12h*mottling;
            half3 sand = _Sand.rgb * (1 + sandLight + .085h*macro);
            half3 earth = lerp(grass, sand, w.g);
            #if defined(_PORT_COAST)
            earth = lerp(earth,lerp(_HarborGrassColor.rgb,_HarborLandColor.rgb,w.g),.09h);
            // Desaturated scree only appears on steep coastal slopes.
            half3 scree = lerp(_Asphalt.rgb, _Sand.rgb, .38h)*(1+macro*.21h+aggregate);
            earth = lerp(earth,scree,i.slope*.8h);
            #endif
            half3 color = lerp(earth, _Asphalt.rgb*(1+aggregate+.082h*macro+.075h*mottling), w.r);
            o.Albedo = color * lerp(half3(1,1,1),half3(.90h,.975h,1.055h),_HarborDusk*.65h)
                * lerp(half3(1,1,1),half3(1.055h,1.016h,.919h),_HarborPoster*.65h);
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
