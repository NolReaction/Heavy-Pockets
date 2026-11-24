using UnityEngine;

namespace HeavyPockets
{
    internal static class RaidContainers
    {
        internal const byte Count = 3;
        internal const float LidSeconds = .58f;
        // Interior coordinates in metres. Slot zero is the user's wooden chest.
        internal static readonly Bounds[] WarehouseBoxes = {
            new Bounds(new Vector3(-4.65f,.34f,1.65f), new Vector3(1.18f,.68f,.78f)),
            new Bounds(new Vector3(2.65f,.34f,3.40f), new Vector3(1.18f,.68f,.78f))
        };

        internal static Bounds BoundsFor(byte id)
        {
            if (id == 0) return RaidMap.CargoCase;
            var bounds = WarehouseBoxes[id-1];
            bounds.center += TrainingGround.Active ? TrainingBuildings.Warehouse : ReceivingWarehouseLayout.Origin;
            return bounds;
        }

        internal static bool Closest(Vector2 feet, RaidLocomotion motion, out byte id)
        {
            float nearest = float.PositiveInfinity;
            id = 0;
            for (byte i = 0; i < Count; ++i)
            {
                if (!RaidMap.CanOpen(feet,motion,i)) continue;
                var center = BoundsFor(i).center;
                float distance = (feet - new Vector2(center.x,center.z)).sqrMagnitude;
                if (distance < nearest) { nearest = distance; id = i; }
            }
            return nearest < float.PositiveInfinity;
        }

        internal static bool UnderPointer(Ray ray, Vector2 feet, RaidLocomotion motion, out byte id)
        {
            float best = float.PositiveInfinity;
            id = 0;
            for (byte i=0; i<Count; i++)
            {
                var bounds = BoundsFor(i);
                if (!bounds.IntersectRay(ray,out float distance) || distance >= best || !RaidMap.CanOpen(feet,motion,i)) continue;
                // The camera may be on the other side of a wall from the runner.
                if (distance > .025f && !RaidMap.ClearSegment(ray.origin, ray.GetPoint(distance-.025f))) continue;
                best = distance;
                id = i;
            }
            return best < float.PositiveInfinity;
        }
    }
}
