using UnityEngine;

namespace HeavyPockets
{
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    internal sealed class RaidAmbientOcclusion : MonoBehaviour
    {
        internal const float DefaultIntensity = 0.2f;
        internal const float DefaultRadius = 0.34f;
        internal const float MinRadius = 0.1f, MaxRadius = 1.2f, MaxIntensity = 2f;

        private static readonly int Projection = Shader.PropertyToID("_AOProjection");
        private static readonly int Sampling = Shader.PropertyToID("_AOSampling");
        private static readonly int HalfResolution = Shader.PropertyToID("_AOHalfResolution");
        private static readonly int Occlusion = Shader.PropertyToID("_OcclusionTex");
        private Camera view;
        private Material effect;
        private bool addedDepth;
        private float intensity = DefaultIntensity, radius = DefaultRadius;
        private RenderTextureFormat occlusionFormat;

        internal static void Configure(Camera camera, bool enabled, float intensity, float radiusMeters)
        {
            if (!camera) return;
            bool apply = enabled && intensity > 0f;
            camera.TryGetComponent<RaidAmbientOcclusion>(out var ao);
            if (!ao && !apply) return;
            if (!ao) ao = camera.gameObject.AddComponent<RaidAmbientOcclusion>();
            ao.intensity = Mathf.Clamp(intensity, 0f, MaxIntensity);
            ao.radius = Mathf.Clamp(radiusMeters, MinRadius, MaxRadius);
            ao.enabled = apply;
        }

        private void OnEnable()
        {
            view = GetComponent<Camera>();
            var shader = Resources.Load<Shader>("RaidAmbientOcclusion");
            occlusionFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGHalf)
                ? RenderTextureFormat.RGHalf : RenderTextureFormat.ARGBHalf;
            if (!shader || !shader.isSupported || !SystemInfo.SupportsRenderTextureFormat(occlusionFormat))
            {
                enabled = false;
                return;
            }
            effect = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            addedDepth = (view.depthTextureMode & DepthTextureMode.Depth) == 0;
            view.depthTextureMode |= DepthTextureMode.Depth;
        }

        // Composite opaque geometry first. Glass, particles and the HUD are drawn afterwards.
        [ImageEffectOpaque]
        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            int width = source.width, height = source.height;
            int halfWidth = (width + 1) / 2, halfHeight = (height + 1) / 2;
            Matrix4x4 projection = view.projectionMatrix;
            effect.SetVector(Projection, new Vector4(1f / projection.m00, 1f / projection.m11,
                view.orthographic ? 1f : 0f, view.farClipPlane));
            effect.SetVector(Sampling, new Vector4(1f / width, 1f / height, radius, intensity));
            effect.SetVector(HalfResolution, new Vector4(halfWidth, halfHeight, 1f / halfWidth, 1f / halfHeight));

            var buffer = RenderTexture.GetTemporary(halfWidth, halfHeight, 0, occlusionFormat, RenderTextureReadWrite.Linear);
            buffer.filterMode = FilterMode.Point;
            buffer.wrapMode = TextureWrapMode.Clamp;
            try
            {
                Graphics.Blit(source, buffer, effect, 0);
                effect.SetTexture(Occlusion, buffer);
                Graphics.Blit(source, destination, effect, 1);
            }
            finally
            {
                effect.SetTexture(Occlusion, null);
                RenderTexture.ReleaseTemporary(buffer);
            }
        }

        private void OnDisable()
        {
            if (addedDepth && view) view.depthTextureMode &= ~DepthTextureMode.Depth;
            addedDepth = false;
            if (effect) Destroy(effect);
            effect = null;
        }
    }
}
