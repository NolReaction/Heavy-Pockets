Shader "Hidden/Heavy Pockets/Ambient Occlusion"
{
    Properties { _MainTex ("Camera color", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex, _OcclusionTex;
        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        float4 _MainTex_TexelSize;
        float4 _AOProjection;     // Inverse focal lengths, orthographic flag, far plane.
        float4 _AOSampling;       // Full-resolution texel size, world-space radius, intensity.
        float4 _AOHalfResolution; // Buffer size and its reciprocal.

        float EyeDepth(float2 uv)
        {
            float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
            if (_AOProjection.z < 0.5) return LinearEyeDepth(raw);
            #if defined(UNITY_REVERSED_Z)
                raw = 1.0 - raw;
            #endif
            return lerp(_ProjectionParams.y, _AOProjection.w, raw);
        }

        float3 ViewPosition(float2 uv)
        {
            float z = EyeDepth(uv);
            float scale = lerp(z, 1.0, _AOProjection.z);
            return float3((2.0 * uv - 1.0) * _AOProjection.xy * scale, z);
        }

        half4 Estimate(v2f_img input) : SV_Target
        {
            float2 uv = input.uv;
            float3 p = ViewPosition(uv);
            if (p.z > 48.0 || p.z >= _AOProjection.w * 0.999)
                return half4(0, p.z, 0, 1);

            // Pick the neighbour on the same surface at silhouettes.
            float2 stepUV = _AOSampling.xy;
            float3 left = p - ViewPosition(uv - float2(stepUV.x, 0));
            float3 right = ViewPosition(uv + float2(stepUV.x, 0)) - p;
            float3 down = p - ViewPosition(uv - float2(0, stepUV.y));
            float3 up = ViewPosition(uv + float2(0, stepUV.y)) - p;
            float3 dx = abs(left.z) < abs(right.z) ? left : right;
            float3 dy = abs(down.z) < abs(up.z) ? down : up;
            float3 n = cross(dy, dx);
            n *= rsqrt(max(dot(n, n), 1e-14));

            float radius = _AOSampling.z;
            float2 uvRadius = 0.5 * radius / (_AOProjection.xy * lerp(p.z, 1.0, _AOProjection.z));
            float bias = max(radius * 0.01, p.z * 0.00005);
            // Four fixed rotations distribute the taps without a moving noise pattern.
            float2 cell = fmod(floor(uv * _AOHalfResolution.xy), 2.0);
            float angle = (cell.x + 2.0 * cell.y) * 1.57079633;
            float2 direction;
            sincos(angle, direction.y, direction.x);
            float occlusion = 0;
            [unroll]
            for (int i = 0; i < 12; ++i)
            {
                float2 sampleUV = saturate(uv + direction * uvRadius * ((i + 0.65) / 12.0));
                float3 delta = ViewPosition(sampleUV) - p;
                float d2 = max(dot(delta, delta), 1e-8);
                float angular = saturate((dot(n, delta) - bias) * rsqrt(d2) - 0.06);
                occlusion += angular * saturate(1.0 - d2 / (radius * radius));
                // Golden angle between consecutive samples.
                direction = float2(-0.737369 * direction.x - 0.675490 * direction.y,
                                    0.675490 * direction.x - 0.737369 * direction.y);
            }
            float fade = 1.0 - smoothstep(30.0, 48.0, p.z);
            return half4(occlusion * (2.1 / 12.0) * fade, p.z, 0, 1);
        }

        float2 BilateralTap(float2 uv, float weight, float depth, float tolerance)
        {
            float2 sampleAO = tex2D(_OcclusionTex, uv).rg;
            weight *= exp2(-abs(depth - sampleAO.g) / tolerance);
            return float2(sampleAO.r * weight, weight);
        }

        half4 Composite(v2f_img input) : SV_Target
        {
            half4 color = tex2D(_MainTex, input.uv);
            float2 uv = input.uv;
            // In forward MSAA the resolved color can have the opposite Y orientation to depth.
            #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0) uv.y = 1.0 - uv.y;
            #endif
            float depth = EyeDepth(uv);
            if (depth > 48.0 || depth >= _AOProjection.w * 0.999) return color;

            float2 at = uv * _AOHalfResolution.xy - 0.5;
            float2 f = frac(at);
            float2 stepUV = _AOHalfResolution.zw;
            float2 corner = (floor(at) + 0.5) * stepUV;
            float tolerance = max(depth * 0.002, _AOSampling.z * 0.06);
            float2 sum = BilateralTap(corner, (1.0 - f.x) * (1.0 - f.y), depth, tolerance);
            sum += BilateralTap(corner + float2(stepUV.x, 0), f.x * (1.0 - f.y), depth, tolerance);
            sum += BilateralTap(corner + float2(0, stepUV.y), (1.0 - f.x) * f.y, depth, tolerance);
            sum += BilateralTap(corner + stepUV, f.x * f.y, depth, tolerance);
            color.rgb *= 1.0 - min(0.7, _AOSampling.w * (sum.x / max(sum.y, 0.01)));
            return color;
        }
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Estimate
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Composite
            ENDCG
        }
    }
    Fallback Off
}
