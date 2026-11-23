Shader "HeavyPockets/GroundContact"
{
    Properties
    {
        [HideInInspector] _Body ("Body shadow", Vector) = (1,1,0.42,0)
        [HideInInspector] _LeftHeel ("Left heel", Vector) = (0,0,0,0)
        [HideInInspector] _LeftToe ("Left toe", Vector) = (0,0,0,0)
        [HideInInspector] _RightHeel ("Right heel", Vector) = (0,0,0,0)
        [HideInInspector] _RightToe ("Right toe", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" }
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Body, _LeftHeel, _LeftToe, _RightHeel, _RightToe;
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; float2 worldXZ : TEXCOORD1; };
            Varyings vert(appdata_base v)
            {
                Varyings o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy * 2 - 1;
                o.worldXZ = mul(unity_ObjectToWorld, v.vertex).xz;
                return o;
            }
            half soleShadow(float2 position, float4 heel, float4 toe)
            {
                float2 lengthwise = toe.xy - heel.xy;
                float t = saturate(dot(position - heel.xy, lengthwise) / max(dot(lengthwise, lengthwise), .00001));
                float4 contact = lerp(heel, toe, t);
                float2 delta = position - contact.xy;
                return contact.w * (1 - smoothstep(0, max(.00001, contact.z * contact.z), dot(delta, delta)));
            }
            fixed4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv * _Body.xy;
                half body = (1 - smoothstep(.12, 1, dot(uv, uv))) * _Body.z;
                half soles = max(soleShadow(i.worldXZ, _LeftHeel, _LeftToe), soleShadow(i.worldXZ, _RightHeel, _RightToe));
                return fixed4(.032, .042, .048, body + (1 - body) * soles);
            }
            ENDCG
        }
    }
}
