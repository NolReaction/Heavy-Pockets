using UnityEngine;

namespace HeavyPockets
{
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    internal sealed class RaidToneMapping : MonoBehaviour
    {
        internal const float MinExposure = -2f, MaxExposure = 2f, DefaultExposure = 0f;
        private static readonly int Exposure = Shader.PropertyToID("_ExposureMultiplier");
        private Camera view;
        private Material effect;
        private bool previousHdr;
        private float exposureEV;

        internal static void Configure(Camera camera, bool enabled, float exposureEV)
        {
            if (!camera) return;
            camera.TryGetComponent<RaidToneMapping>(out var tone);
            if (!tone && !enabled) return;
            if (!tone) tone = camera.gameObject.AddComponent<RaidToneMapping>();
            tone.exposureEV = float.IsFinite(exposureEV) ? Mathf.Clamp(exposureEV, MinExposure, MaxExposure) : DefaultExposure;
            tone.enabled = enabled;
            if (tone.effect) tone.effect.SetFloat(Exposure, Mathf.Pow(2f, tone.exposureEV));
        }

        private void OnEnable()
        {
            view = GetComponent<Camera>();
            previousHdr = view.allowHDR;
            var shader = Resources.Load<Shader>("RaidToneMapping");
            if (!shader || !shader.isSupported)
            {
                enabled = false;
                return;
            }
            effect = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            effect.SetFloat(Exposure, Mathf.Pow(2f, exposureEV));
            view.allowHDR = true;
        }

        // Unity calls the final image effect after transparents and before the IMGUI HUD.
        [ImageEffectTransformsToLDR]
        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            bool previousWrite = GL.sRGBWrite;
            GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear && (!destination || destination.sRGB);
            Graphics.Blit(source, destination, effect);
            GL.sRGBWrite = previousWrite;
        }

        private void OnDisable()
        {
            if (view) view.allowHDR = previousHdr;
            if (effect) Destroy(effect);
            effect = null;
        }
    }
}
