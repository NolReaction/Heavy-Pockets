using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    internal sealed class WarehouseFloorScale : MonoBehaviour
    {
        internal static Vector3 DepotPosition => ReceivingWarehouseLayout.Origin + new Vector3(1.7f, 0f, -.4f);
        internal static Vector3 Position => TrainingGround.Active ? DepotPosition + (TrainingBuildings.Warehouse - ReceivingWarehouseLayout.Origin) : DepotPosition;
        // These are authored in the port layout and translated together with the warehouse.
        internal static Bounds ColumnBounds => new Bounds(DepotPosition + new Vector3(0, .45f, .49f), new Vector3(.085f, .90f, .08f));
        internal static Bounds HeadBounds => new Bounds(DepotPosition + new Vector3(0, .99f, .49f), new Vector3(.38f, .38f, .11f));
        internal const float Width = 1.10f, Depth = .85f;
        internal const byte RunnerMass = 75, Capacity = 200;
        [SerializeField] private Transform needle;
        private readonly HashSet<Collider> contacts = new HashSet<Collider>();
        private readonly HashSet<Rigidbody> weighed = new HashSet<Rigidbody>();
        private float velocity;
        internal float Reading { get; private set; }

        internal static bool Supports(Vector2 feet, RaidLocomotion motion)
        {
            Vector3 center = Position;
            return !motion.Airborne && Mathf.Abs(motion.Height - center.y) < .035f &&
                Mathf.Abs(feet.x - center.x) <= Width * .5f && Mathf.Abs(feet.y - center.z) <= Depth * .5f;
        }

        internal static bool CanRead(Vector2 feet, RaidLocomotion motion)
        {
            Vector3 eye = RaidCameraBoom.Eye(feet) + Vector3.up * motion.EyeOffset;
            // Use the exposed crown of the gauge so its solid housing does not block the endpoint.
            Vector3 gauge = Position + new Vector3(0f, 1.20f, .49f);
            return (gauge - eye).sqrMagnitude <= 12f * 12f &&
                (RaidMap.ClearSegment(eye, gauge) || RaidMap.ClearSegment(eye, Position + Vector3.up * .05f));
        }

        internal void Weigh(int runners, float delta) => WeighLoad(runners * RunnerMass, delta);

        internal void WeighLoad(float load, float delta)
        {
            weighed.Clear();
            foreach (var contact in contacts)
            {
                if (!contact || !contact.enabled || !contact.gameObject.activeInHierarchy) continue;
                var body = contact.attachedRigidbody;
                if (body && !body.isKinematic && weighed.Add(body)) load += body.mass;
            }
            Reading = Mathf.SmoothDamp(Reading, Mathf.Min(load, Capacity), ref velocity, .16f, Mathf.Infinity, delta);
            needle.localRotation = Quaternion.Euler(0, 0, 125f - Reading * 1.25f);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.isTrigger && other.attachedRigidbody) contacts.Add(other);
        }

        private void OnTriggerExit(Collider other) => contacts.Remove(other);
        private void OnDisable() => contacts.Clear();
    }

    internal sealed partial class RaidScenery
    {
        internal WarehouseFloorScale FloorScale { get; private set; }
    }
}
