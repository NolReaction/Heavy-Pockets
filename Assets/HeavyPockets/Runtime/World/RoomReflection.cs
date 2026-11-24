using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    [DisallowMultipleComponent, RequireComponent(typeof(ReflectionProbe))]
    internal sealed class RoomReflection : MonoBehaviour
    {
        private static readonly List<RoomReflection> active = new List<RoomReflection>();
        private static bool reflectionsEnabled = true;
        private static float strength = 1f, dusk;

        [SerializeField] private float dayIntensity = .85f, nightIntensity = .30f;
        // Built-in samples world-aligned cubemaps. Map pieces can turn in 90-degree steps.
        [SerializeField] private Cubemap[] quarterTurns;
        private ReflectionProbe room;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            active.Clear();
            reflectionsEnabled = true;
            strength = 1f;
            dusk = 0f;
        }

        internal static void SetOptions(bool enabled, float intensity)
        {
            reflectionsEnabled = enabled;
            strength = Mathf.Clamp(intensity, 0f, 2f);
            foreach (var item in active) item.Apply();
        }

        internal static void SetDusk(float value)
        {
            dusk = Mathf.Clamp01(value);
            foreach (var item in active) item.Apply();
        }

        private void OnEnable()
        {
            room = GetComponent<ReflectionProbe>();
            active.Add(this);
            Apply();
        }

        private void Start()
        {
            // The streamed tile receives its map rotation after its children are instantiated.
            if (quarterTurns == null || quarterTurns.Length != 4) return;
            int turn = Mathf.RoundToInt(transform.eulerAngles.y / 90f) & 3;
            room.customBakedTexture = quarterTurns[turn];
            if ((turn & 1) != 0)
            {
                var size = room.size;
                room.size = new Vector3(size.z, size.y, size.x);
            }
        }

        private void OnDisable() => active.Remove(this);

        private void Apply()
        {
            room.intensity = Mathf.Lerp(dayIntensity, nightIntensity, dusk) * strength;
            room.enabled = reflectionsEnabled;
        }
    }
}
