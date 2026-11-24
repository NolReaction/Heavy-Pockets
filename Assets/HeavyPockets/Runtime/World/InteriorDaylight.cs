using System;
using UnityEngine;

namespace HeavyPockets
{
    [DisallowMultipleComponent]
    internal sealed class InteriorDaylight : MonoBehaviour
    {
        [SerializeField] private Light[] windows = Array.Empty<Light>();
        [SerializeField] private Light[] lamps = Array.Empty<Light>();
        [SerializeField, Min(0f)] private float windowStrength = 1.15f;
        [SerializeField, Min(0f)] private float lampsByDay = .72f, lampsAtNight = 2.45f;

        private static float daylight = 1f;
        private static event Action<float> changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            daylight = 1f;
            changed = null;
        }

        internal static void SetDaylight(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(value, daylight)) return;
            daylight = value;
            changed?.Invoke(value);
        }

        private void OnEnable()
        {
            changed += Apply;
            Apply(daylight);
        }

        private void OnDisable() => changed -= Apply;

        private void Apply(float value)
        {
            foreach (var window in windows)
            {
                if (!window) continue;
                window.intensity = windowStrength * value;
                window.enabled = window.intensity > .01f;
            }
            foreach (var lamp in lamps)
                if (lamp) lamp.intensity = Mathf.Lerp(lampsAtNight, lampsByDay, value);
        }
    }
}
