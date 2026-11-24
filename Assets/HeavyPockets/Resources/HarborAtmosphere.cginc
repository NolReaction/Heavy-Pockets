#ifndef HEAVY_POCKETS_HARBOR_ATMOSPHERE
#define HEAVY_POCKETS_HARBOR_ATMOSPHERE

// Periodic texture: R = cumulus density, GB = density gradient along U and V,
// A = fine cirrus. All four channels are linear data. The wind moves in metres.
sampler2D _HarborCloudField;
float4 _HarborWindOffset, _HarborSunDirection;
float _HarborCloudCoverage;

float2 HarborCloudRayTravel(float3 direction)
{
    // Intersect a curved cloud deck. This stays finite even for a horizontal
    // ray, with the same parallax for the sky, the sea and shadows on the quay.
    float y = max(direction.y, 0.0);
    float distanceToDeck = 2840.0 / (y + sqrt(y * y + .026));
    return direction.xz * (distanceToDeck / 11300.0);
}

float2 HarborCloudUV(float2 worldXZ, float3 direction)
{
    return HarborCloudRayTravel(direction) + (worldXZ - _HarborWindOffset.xy) / 11300.0 + float2(.23, .674);
}

half HarborCloudThreshold()
{
    return lerp(1.10h, .08h, saturate(_HarborCloudCoverage));
}

half HarborCloudCover(float2 uv)
{
    half density = tex2D(_HarborCloudField, uv).r;
    half threshold = HarborCloudThreshold();
    return smoothstep(threshold - .048h, threshold + .091h, density);
}

half HarborCloudShadow(float2 worldXZ, float3 directionToLight)
{
    float2 uv = HarborCloudUV(worldXZ, directionToLight);
    half density = tex2D(_HarborCloudField, uv).r;
    half threshold = HarborCloudThreshold();
    // The soft penumbra fades out in the longer optical path near the horizon.
    return smoothstep(threshold - .085h, threshold + .16h, density) * smoothstep(.015h, .15h, directionToLight.y);
}

#endif
