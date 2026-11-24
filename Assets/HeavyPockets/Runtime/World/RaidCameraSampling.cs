using UnityEngine;

namespace HeavyPockets
{
    [RequireComponent(typeof(Camera))]
    internal sealed class RaidCameraSampling : MonoBehaviour
    {
        private static readonly int SampledEdges = Shader.PropertyToID("_HarborSampledEdges");
        private static RaidCameraSampling active;
        private Camera view;
        private int profileSamples;

        internal static int ProfileSamples => active ? active.profileSamples : QualitySettings.antiAliasing;

        private void OnEnable()
        {
            view = GetComponent<Camera>();
            active = this;
            profileSamples = QualitySettings.antiAliasing;
            ApplySampling();
            ApplyOcclusion();
            RaidGraphicsSettings.ApplyReflections();
        }

        internal static void ApplyOcclusion()
        {
            if (!active) return;
            RaidAmbientOcclusion.Configure(active.view, RaidGraphicsSettings.AmbientOcclusion,
                RaidGraphicsSettings.OcclusionStrength, RaidGraphicsSettings.OcclusionRadius);
        }

        internal static void ApplySampling()
        {
            if (!active) return;
            int samples = RaidGraphicsSettings.Msaa;
            if (QualitySettings.antiAliasing != samples) QualitySettings.antiAliasing = samples;
        }

        // Cutout coverage is only meaningful on a multisampled colour target.
        // Leave single-sample rendering, editor previews and shadow casters with ordinary alpha testing.
        private void OnPreRender()
        {
            int samples = view.targetTexture ? view.targetTexture.antiAliasing : QualitySettings.antiAliasing;
            bool multisampled = view.allowMSAA && samples > 1 && view.actualRenderingPath == RenderingPath.Forward;
            Shader.SetGlobalFloat(SampledEdges, multisampled ? 1f : 0f);
        }

        private void OnPostRender() => Shader.SetGlobalFloat(SampledEdges, 0f);

        private void OnDisable()
        {
            if (active != this) return;
            RaidGraphicsSettings.Save();
            Shader.SetGlobalFloat(SampledEdges, 0f);
            if (QualitySettings.antiAliasing != profileSamples) QualitySettings.antiAliasing = profileSamples;
            active = null;
        }
    }
}
