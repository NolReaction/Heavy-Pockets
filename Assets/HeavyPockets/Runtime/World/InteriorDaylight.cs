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
        [SerializeField] private Light southFill, frameProjector;
        [SerializeField, Min(0f)] private float frameStrength = 3f;

        private static float daylight = 1f, directSun;
        private static Vector3 sunDirection;
        private static event Action changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            daylight = 1f;
            directSun = 0f;
            sunDirection = Vector3.zero;
            changed = null;
        }

        internal static void SetDaylight(float value, float direct, Vector3 toSun)
        {
            value = Mathf.Clamp01(value);
            direct = Mathf.Clamp01(direct);
            if (Mathf.Approximately(value, daylight) && Mathf.Approximately(direct, directSun) &&
                toSun == sunDirection) return;
            daylight = value;
            directSun = direct;
            sunDirection = toSun;
            changed?.Invoke();
        }

        private void OnEnable()
        {
            changed += Apply;
            RaidGraphicsSettings.WindowCookiesChanged += Apply;
            Apply();
        }

        // A streamed building receives the map piece's final rotation after instantiation.
        private void Start() => Apply();

        private void OnDisable()
        {
            changed -= Apply;
            RaidGraphicsSettings.WindowCookiesChanged -= Apply;
        }

        private void Apply()
        {
            // The projector is registered to the panes on the local -Z facade.
            float incidence = Vector3.Dot(-transform.forward, sunDirection);
            float framed = frameProjector && RaidGraphicsSettings.WindowCookies
                ? directSun * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .5f, incidence)) : 0f;
            foreach (var window in windows)
            {
                if (!window) continue;
                float fill = window == southFill ? 1f - framed : 1f;
                SetWindow(window, windowStrength * daylight * fill);
            }
            if (frameProjector) SetWindow(frameProjector, frameStrength * daylight * framed);
            foreach (var lamp in lamps)
                if (lamp) lamp.intensity = Mathf.Lerp(lampsAtNight, lampsByDay, daylight);
        }

        private static void SetWindow(Light source, float strength)
        {
            source.intensity = strength;
            source.enabled = strength > .01f;
        }
    }
}
