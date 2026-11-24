using UnityEngine;

namespace HeavyPockets
{
    internal enum HarborLightParameter { Time, Sun, Clouds, Wind, Haze }

    internal sealed class HarborLightingSettings
    {
        private readonly float[] values;
        private static readonly float[] maxima = { 24f, 2f, 1f, 30f, 1f };

        internal HarborLightingSettings(float hour, float clouds, float wind, float haze)
        {
            values = new[] { hour, 1f, clouds, wind, haze };
        }

        private HarborLightingSettings(HarborLightingSettings preset)
        {
            values = (float[])preset.values.Clone();
        }

        internal float this[HarborLightParameter parameter] => values[(int)parameter];
        internal bool Dirty { get; private set; }

        internal bool Change(HarborLightParameter parameter, float value)
        {
            int i = (int)parameter;
            if (float.IsNaN(value) || float.IsInfinity(value)) return false;
            value = Mathf.Clamp(value, 0f, maxima[i]);
            if (values[i] == value) return false;
            values[i] = value;
            Dirty = true;
            return true;
        }

        private static string Key(int style, int i) => $"HeavyPockets.Harbor.Light.{style}.{(HarborLightParameter)i}";

        internal static HarborLightingSettings Load(int style, HarborLightingSettings preset)
        {
            var settings = new HarborLightingSettings(preset);
            for (int i = 0; i < settings.values.Length; i++)
                settings.Change((HarborLightParameter)i, PlayerPrefs.GetFloat(Key(style, i), preset.values[i]));
            settings.Dirty = false;
            return settings;
        }

        internal void Save(int style)
        {
            for (int i = 0; i < values.Length; i++) PlayerPrefs.SetFloat(Key(style, i), values[i]);
            Dirty = false;
        }

        internal void Restore(HarborLightingSettings preset)
        {
            for (int i = 0; i < values.Length; i++) Change((HarborLightParameter)i, preset.values[i]);
        }
    }
}
