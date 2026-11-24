using UnityEngine;

namespace HeavyPockets
{
    [RequireComponent(typeof(Camera))]
    internal sealed class RaidCameraSampling : MonoBehaviour
    {
        private const string Preference = "HeavyPockets.Graphics.MSAA";
        private static readonly int SampledEdges = Shader.PropertyToID("_HarborSampledEdges");
        private static RaidCameraSampling active;
        private Camera view;
        private int profileSamples, choice;

        private void OnEnable()
        {
            view = GetComponent<Camera>();
            active = this;
            profileSamples = QualitySettings.antiAliasing;
            choice = PlayerPrefs.GetInt(Preference, -1);
            if (choice != 0 && choice != 2 && choice != 4) choice = -1;
            Apply();
        }

        internal static string Label => active ? active.choice switch {
            0 => "Сглаживание   ·   Выкл", 2 => "Сглаживание   ·   2× MSAA",
            4 => "Сглаживание   ·   4× MSAA", _ => "Сглаживание   ·   По профилю"
        } : "Сглаживание";

        internal static void Cycle()
        {
            if (!active) return;
            active.choice = active.choice switch { -1 => 0, 0 => 2, 2 => 4, _ => -1 };
            if (active.choice < 0) PlayerPrefs.DeleteKey(Preference);
            else PlayerPrefs.SetInt(Preference, active.choice);
            PlayerPrefs.Save();
            active.Apply();
        }

        internal static void ResetToProfile()
        {
            if (!active) return;
            active.choice = -1;
            PlayerPrefs.DeleteKey(Preference);
            PlayerPrefs.Save();
            active.Apply();
        }

        private void Apply()
        {
            int samples = choice < 0 ? profileSamples : choice;
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
            Shader.SetGlobalFloat(SampledEdges, 0f);
            if (QualitySettings.antiAliasing != profileSamples) QualitySettings.antiAliasing = profileSamples;
            active = null;
        }
    }
}
