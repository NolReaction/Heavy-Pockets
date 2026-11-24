Shader "HeavyPockets/Coastal grove"
{
    Properties
    {
        _MainTex ("Sprays and bark", 2D) = "white" {}
        _Cutoff ("Leaf silhouette", Range(0,1)) = .32
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "DisableBatching"="True" }
        Cull Off
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf HarborDiffuse alphatest:_Cutoff addshadow fullforwardshadows vertex:vert
        #pragma multi_compile_instancing
        #include "../HarborSurfaceLighting.cginc"
        sampler2D _MainTex;
        float4 _CoastalFeet;
        struct Input { float2 uv_MainTex; fixed4 color : COLOR; };

        void vert(inout appdata_full v)
        {
            float3 p=mul(unity_ObjectToWorld,v.vertex).xyz;
            float w=v.texcoord1.x;
            float phase=dot(p.xz,float2(.43,.29))+_HarborWindOffset.w;
            float sway=_HarborWindOffset.z*(sin(phase)+sin(phase*.43+_HarborWindOffset.w)*.36);
            float flutter=_HarborWindOffset.z*sin(dot(p,float3(2.4,.7,1.8))+_Time.y*3.1);
            float2 delta=p.xz-_CoastalFeet.xz;
            float contact=saturate(1-length(delta)/.91)*v.texcoord1.y*_CoastalFeet.w;
            float3 offset=float3(.10,.012,.042)*sway*w + float3(.020,.016,.009)*flutter*w*w;
            offset.xz+=delta*contact*w*.52;
            offset.y-=contact*w*.055;
            v.vertex.xyz+=mul((float3x3)unity_WorldToObject,offset);
        }
        void surf(Input i,inout SurfaceOutput o)
        {
            fixed4 leaf=tex2D(_MainTex,i.uv_MainTex);
            o.Albedo=leaf.rgb*i.color.rgb;
            o.Emission=o.Albedo*.17;
            o.Alpha=leaf.a;
        }
        ENDCG
    }
    Fallback "Transparent/Cutout/VertexLit"
}
