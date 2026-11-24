#ifndef HEAVY_POCKETS_HARBOR_SURFACE_LIGHTING_INCLUDED
#define HEAVY_POCKETS_HARBOR_SURFACE_LIGHTING_INCLUDED

#include "HarborAtmosphere.cginc"
#include "Lighting.cginc"

// The sky and its broad penumbra drift across the same world-space cloud field.
// Only direct light is filtered: the ambient sky, lamps and emissive paint
// remain visible when a bank of cloud passes over the quay.
inline half HarborKeyTransmission(float2 worldXZ)
{
    return 1.0h - 0.55h * HarborCloudShadow(worldXZ, _WorldSpaceLightPos0.xyz);
}

inline half4 LightingHarborDiffuse(SurfaceOutput s, UnityGI gi)
{
    return LightingLambert(s, gi);
}

inline void LightingHarborDiffuse_GI(SurfaceOutput s, UnityGIInput data, inout UnityGI gi)
{
    LightingLambert_GI(s, data, gi);
    #if defined(UNITY_PASS_FORWARDBASE)
    gi.light.color *= HarborKeyTransmission(data.worldPos.xz);
    #endif
}

inline half4 LightingHarborDiffuse_Deferred(SurfaceOutput s, UnityGI gi,
    out half4 g0, out half4 g1, out half4 g2)
{
    return LightingLambert_Deferred(s, gi, g0, g1, g2);
}

#endif
