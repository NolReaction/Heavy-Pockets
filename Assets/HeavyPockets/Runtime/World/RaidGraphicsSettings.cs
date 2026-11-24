using UnityEngine;

namespace HeavyPockets
{
    internal static class RaidGraphicsSettings
    {
        private const string Prefix = "HeavyPockets.Graphics.";
        private const string SamplesKey = Prefix + "MSAA";
        private const string OcclusionKey = Prefix + "AmbientOcclusion";
        private const string StrengthKey = Prefix + "OcclusionStrength";
        private const string RadiusKey = Prefix + "OcclusionRadius";
        private const string ReflectionsKey = Prefix + "LocalReflections";
        private const string IntensityKey = Prefix + "ReflectionIntensity";
        private static bool dirty;

        internal static int Msaa
        {
            get
            {
                int value = PlayerPrefs.GetInt(SamplesKey, -1);
                return ValidSamples(value) ? value : RaidCameraSampling.ProfileSamples;
            }
            set
            {
                if (!ValidSamples(value) || Msaa == value) return;
                PlayerPrefs.SetInt(SamplesKey, value);
                dirty = true;
                RaidCameraSampling.ApplySampling();
            }
        }

        private static bool ValidSamples(int value) => value == 0 || value == 2 || value == 4 || value == 8;

        internal static bool AmbientOcclusion
        {
            get => PlayerPrefs.GetInt(OcclusionKey, 1) != 0;
            set { if (SetToggle(OcclusionKey, AmbientOcclusion, value)) RaidCameraSampling.ApplyOcclusion(); }
        }

        internal static float OcclusionStrength
        {
            get => Read(StrengthKey, RaidAmbientOcclusion.DefaultIntensity, 0f, 2f);
            set { if (SetNumber(StrengthKey, OcclusionStrength, value, 0f, 2f)) RaidCameraSampling.ApplyOcclusion(); }
        }

        internal static float OcclusionRadius
        {
            get => Read(RadiusKey, RaidAmbientOcclusion.DefaultRadius, .1f, 1.2f);
            set { if (SetNumber(RadiusKey, OcclusionRadius, value, .1f, 1.2f)) RaidCameraSampling.ApplyOcclusion(); }
        }

        internal static bool LocalReflections
        {
            get => PlayerPrefs.GetInt(ReflectionsKey, 1) != 0;
            set { if (SetToggle(ReflectionsKey, LocalReflections, value)) ApplyReflections(); }
        }

        internal static float ReflectionIntensity
        {
            get => Read(IntensityKey, 1f, 0f, 2f);
            set { if (SetNumber(IntensityKey, ReflectionIntensity, value, 0f, 2f)) ApplyReflections(); }
        }

        internal static void ApplyReflections() => RoomReflection.SetOptions(LocalReflections, ReflectionIntensity);

        private static float Read(string key, float fallback, float min, float max)
        {
            float value = PlayerPrefs.GetFloat(key, fallback);
            return float.IsFinite(value) ? Mathf.Clamp(value, min, max) : fallback;
        }

        private static bool SetNumber(string key, float current, float value, float min, float max)
        {
            if (!float.IsFinite(value)) return false;
            value = Mathf.Clamp(value, min, max);
            if (Mathf.Approximately(value, current)) return false;
            PlayerPrefs.SetFloat(key, value);
            dirty = true;
            return true;
        }

        private static bool SetToggle(string key, bool current, bool value)
        {
            if (value == current) return false;
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            dirty = true;
            return true;
        }

        internal static void RestoreDefaults()
        {
            PlayerPrefs.DeleteKey(SamplesKey);
            PlayerPrefs.DeleteKey(OcclusionKey);
            PlayerPrefs.DeleteKey(StrengthKey);
            PlayerPrefs.DeleteKey(RadiusKey);
            PlayerPrefs.DeleteKey(ReflectionsKey);
            PlayerPrefs.DeleteKey(IntensityKey);
            RaidCameraSampling.ApplySampling();
            RaidCameraSampling.ApplyOcclusion();
            ApplyReflections();
            dirty = true;
            Save();
        }

        internal static void Save()
        {
            if (!dirty) return;
            PlayerPrefs.Save();
            dirty = false;
        }
    }
}
