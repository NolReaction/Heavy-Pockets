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
            // Depth is point-sampled. Reconstruct the very same texel's ray, otherwise
            // a sloping plane acquires a false height at every fractional sample.
            float2 pixel = _AOSampling.xy;
            uv = clamp((floor(uv / pixel) + 0.5) * pixel, 0.5 * pixel, 1.0 - 0.5 * pixel);
            float z = EyeDepth(uv);
            float scale = lerp(z, 1.0, _AOProjection.z);
            return float3((2.0 * uv - 1.0) * _AOProjection.xy * scale, z);
        }

        half4 Estimate(v2f_img input) : SV_Target
        {
            // Choose one full-resolution texel centre inside each 2x2 block; the
            // mathematical centre of an even-sized block lies on four depth texels.
            float2 halfPixel = floor(input.uv * _AOHalfResolution.xy);
            float2 uv = min((2.0 * halfPixel + 1.5) * _AOSampling.xy, 1.0 - 0.5 * _AOSampling.xy);
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
            // Use the same symmetric rings at every pixel. Rotating a sparse kernel
            // on a repeating pixel grid imprints that grid across walls and ceilings.
            float2 direction = float2(0.92387953, 0.38268343);
            float occlusion = 0;
            [unroll]
            for (int axis = 0; axis < 8; ++axis)
            {
                float2 outer = float2(0.92387953 * direction.x - 0.38268343 * direction.y,
                                      0.38268343 * direction.x + 0.92387953 * direction.y);
                [unroll]
                for (int ring = 0; ring < 2; ++ring)
                {
                    float2 offset = ring == 0 ? direction * 0.28 : outer * 0.68;
                    float3 delta = ViewPosition(uv + offset * uvRadius) - p;
                    float d2 = max(dot(delta, delta), 1e-8);
                    float angular = saturate((dot(n, delta) - bias) * rsqrt(d2) - 0.06);
                    occlusion += angular * saturate(1.0 - d2 / (radius * radius));
                }
                direction = 0.70710678 * float2(direction.x - direction.y, direction.x + direction.y);
            }
            float fade = 1.0 - smoothstep(30.0, 48.0, p.z);
            return half4(occlusion * (2.1 / 16.0) * fade, p.z, 0, 1);
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
            // A depth gradient within one surface is expected on oblique ceilings.
            // Limit the relaxation at depth discontinuities to preserve silhouettes.
            float slope = min(fwidth(depth) * 1.5, _AOSampling.z * 0.28);
            if (depth > 48.0 || depth >= _AOProjection.w * 0.999) return color;

            // Quadratic reconstruction folds denoising into the upsample. Its weights
            // stay continuous as the camera moves between low-resolution texels.
            float2 at = 0.5 * (uv / _AOSampling.xy - 0.5);
            float2 cell = floor(at);
            float2 d = at - (cell + 0.5);
            float3 wx = float3(0.5 * (0.5 - d.x) * (0.5 - d.x), 0.75 - d.x*d.x,
                               0.5 * (0.5 + d.x) * (0.5 + d.x));
            float3 wy = float3(0.5 * (0.5 - d.y) * (0.5 - d.y), 0.75 - d.y*d.y,
                               0.5 * (0.5 + d.y) * (0.5 + d.y));
            float2 stepUV = _AOHalfResolution.zw;
            float2 center = (cell + 0.5) * stepUV;
            float tolerance = max(depth * 0.002 + slope, _AOSampling.z * 0.06);
            float2 sum = 0;
            [unroll] for (int y = 0; y < 3; ++y)
            {
                [unroll] for (int x = 0; x < 3; ++x)
                    sum += BilateralTap(center + float2(x-1,y-1) * stepUV, wx[x] * wy[y], depth, tolerance);
            }
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
