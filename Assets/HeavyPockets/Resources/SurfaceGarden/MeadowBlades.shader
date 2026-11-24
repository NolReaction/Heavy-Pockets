Shader "HeavyPockets/Coastal meadow blades"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf HarborDiffuse fullforwardshadows vertex:vert addshadow
        #include "../HarborSurfaceLighting.cginc"
        float4 _CoastalFeet;
        struct Input { fixed4 color : COLOR; };
        void vert(inout appdata_full v)
        {
            float3 w = mul(unity_ObjectToWorld,v.vertex).xyz;
            float2 delta = w.xz - _CoastalFeet.xz;
            float contact = saturate(1.0-length(delta)/.66)*_CoastalFeet.w;
            float phase = dot(w.xz,float2(.48,.21)) + _Time.y*1.9;
            float wind = .079*_HarborWindOffset.z*(sin(phase)+.28*sin(phase*1.73));
            v.vertex.xz += v.texcoord.y*v.texcoord.y * (wind*float2(.85,.51)+delta*contact*.88);
            v.vertex.y -= contact*v.texcoord.y*.035;
            v.vertex.y *= 1-smoothstep(29.0,46.0,distance(_WorldSpaceCameraPos,w));
        }
        void surf(Input i, inout SurfaceOutput o)
        {
            o.Albedo = i.color.rgb;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
