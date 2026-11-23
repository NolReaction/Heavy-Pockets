using UnityEngine;

namespace HeavyPockets
{
    // All coordinates are horizontal world coordinates (X, Z), measured in metres.
    // Geometry and authoritative movement use the same dimensions.
    public static class RaidMap
    {
        public static readonly Vector2 Crate = new Vector2(0f, 0.8f);
        public static readonly Vector2 Exit = new Vector2(47f, 35f);
        public const float RoomX = 72f;
        public const float RoomZ = 56f;
        public const float WallX = 2f;
        public const float WallZ = 0.15f;
        public const float CrateX = 0.55f;
        public const float CrateZ = 0.36f;
        public const float ExitX = 1.15f;
        public const float ExitZ = 1f;
        private const float PlayerRadius = 0.30f;
        internal const float StandingHeight = 1.80f;
        public const float Reach = 2.45f;

        // Occluders follow the rendered partition, its cap, raised face details and the cargo case.
        // Camera clearance belongs to the boom only. It must not enlarge the sightline blocker.
        private static readonly Bounds Partition = Box(0, 0, 0, WallX, 2.31f, WallZ);
        private static readonly Bounds Capping = Box(0, 2.26f, 0, WallX + .0175f, 2.34f, .165f);
        private static readonly Bounds WearStrip = Box(0, .35f, 0, WallX, .45f, .16f);
        private static readonly Bounds FaceDetails = Box(0, .44f, 0, 1.98f, 2.245f, .17f);
        private static readonly Bounds Placard = Box(0, 1.39f, 0, .83f, 1.81f, .196f);
        private static readonly Bounds CargoCase = Box(Crate.x, 0, Crate.y, CrateX, .86f, CrateZ);

        private static Bounds Box(float x, float bottom, float z, float halfX, float top, float halfZ)
        {
            return new Bounds(new Vector3(x, (top + bottom) * .5f, z),
                new Vector3(halfX * 2f, top - bottom, halfZ * 2f));
        }

        public static Vector2 Spawn(int slot)
        {
            return slot % 2 == 0 ? new Vector2(-4f, -9f) : new Vector2(1.15f, 1.25f);
        }

        public static Vector2 Move(Vector2 from, Vector2 delta, float capsuleTop = 0f)
        {
            // Axis sliding with substeps keeps thin geometry solid even at a slow server tick.
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / 0.14f));
            Vector2 step = delta / steps;
            for (int i = 0; i < steps; i++)
            {
                var next = new Vector2(Mathf.Clamp(from.x + step.x, -RoomX + PlayerRadius, RoomX - PlayerRadius), from.y);
                if (!BlockedAtHeight(next, capsuleTop))
                    from = next;
                next = new Vector2(from.x, Mathf.Clamp(from.y + step.y, -RoomZ + PlayerRadius, RoomZ - PlayerRadius));
                if (!BlockedAtHeight(next, capsuleTop))
                    from = next;
            }
            return from;
        }

        private static bool Blocked(Vector2 p)
        {
            if (InBox(p, Vector2.zero, WallX + PlayerRadius, WallZ + PlayerRadius)
                || InBox(p, Crate, CrateX + PlayerRadius, CrateZ + PlayerRadius)) return true;

            foreach (OutdoorSolid solid in OutdoorLayout.Solids)
            {
                Vector3 center = solid.Bounds.center, half = solid.Bounds.extents;
                float dx = Mathf.Max(Mathf.Abs(p.x - center.x) - half.x, 0f);
                float dz = Mathf.Max(Mathf.Abs(p.y - center.z) - half.z, 0f);
                if (dx * dx + dz * dz < PlayerRadius * PlayerRadius) return true;
            }
            return false;
        }

        private static bool BlockedAtHeight(Vector2 p, float capsuleTop) => Blocked(p) ||
            (capsuleTop > 0f && StandingJumpClearance(p) < capsuleTop - StandingHeight);

        private static bool InBox(Vector2 p, Vector2 center, float x, float z)
        {
            return Mathf.Abs(p.x - center.x) <= x && Mathf.Abs(p.y - center.y) <= z;
        }

        // The case and partition exclude the entire footprint. Only fittings mounted above the
        // crouched capsule can overhang a reachable part of the floor.
        internal static float StandingJumpClearance(Vector2 p)
        {
            float overhead = Mathf.Min(UndersideAt(Capping, p), UndersideAt(Placard, p));
            return overhead - StandingHeight;
        }

        internal static bool CanStand(Vector2 p) => !Blocked(p) && StandingJumpClearance(p) >= 0f;

        private static float UndersideAt(Bounds box, Vector2 p)
        {
            Vector3 center = box.center, half = box.extents;
            float dx = Mathf.Max(0f, Mathf.Abs(p.x - center.x) - half.x);
            float dz = Mathf.Max(0f, Mathf.Abs(p.y - center.z) - half.z);
            return dx * dx + dz * dz < PlayerRadius * PlayerRadius ? center.y - half.y : float.PositiveInfinity;
        }

        public static bool InExit(Vector2 p)
        {
            return InBox(p, Exit, ExitX, ExitZ);
        }

        public static bool CanOpen(Vector2 p)
        {
            return (p - Crate).sqrMagnitude <= Reach * Reach && HasSightLine(p, Crate);
        }

        // All players have the same standing eye height. The partition crosses it,
        // while the low cargo case does not. Both endpoints are authoritative.
        public static bool HasSightLine(Vector2 from, Vector2 to)
        {
            float first = 0f, last = 1f;
            Vector2 direction = to - from;
            bool crossesWall = ClipAxis(from.x, direction.x, WallX, ref first, ref last)
                && ClipAxis(from.y, direction.y, WallZ, ref first, ref last);
            if (crossesWall) return false;
            return ClearYardSegment(RaidCameraBoom.Eye(from), new Vector3(direction.x, 0, direction.y));
        }

        // Keep the eye-to-crate interaction rule separate from camera-to-mesh replication.
        internal static bool ClearSegment(Vector3 from, Vector3 to)
        {
            Vector3 ray = to - from;
            return !Intersects(Partition, from, ray, 1f, out _)
                && !Intersects(Capping, from, ray, 1f, out _)
                && !Intersects(WearStrip, from, ray, 1f, out _)
                && !Intersects(FaceDetails, from, ray, 1f, out _)
                && !Intersects(Placard, from, ray, 1f, out _)
                && !Intersects(CargoCase, from, ray, 1f, out _)
                && ClearYardSegment(from, ray);
        }

        private static bool ClearYardSegment(Vector3 from, Vector3 ray)
        {
            foreach (Bounds box in OutdoorLayout.Occluders)
                if (Intersects(box, from, ray, 1f, out _)) return false;
            return true;
        }

        internal static bool Intersects(Bounds box, Vector3 origin, Vector3 direction, float length, out float first)
        {
            first = 0f;
            Vector3 local = origin - box.center, half = box.extents;
            return ClipAxis(local.x, direction.x, half.x, ref first, ref length)
                && ClipAxis(local.y, direction.y, half.y, ref first, ref length)
                && ClipAxis(local.z, direction.z, half.z, ref first, ref length);
        }

        // Points inset from the core silhouette of the 1.76 m Unity Armature.
        // Moving limbs are excluded from the visibility test while the animation is blending.
        private static readonly Vector3[] TorsoSamples =
        {
            new Vector3(0f, 1.63f, 0f),
            new Vector3(-.055f, 1.60f, .025f), new Vector3(.055f, 1.60f, .025f),
            new Vector3(0f, 1.34f, 0f),
            new Vector3(-.135f, 1.32f, 0f), new Vector3(.135f, 1.32f, 0f),
            new Vector3(0f, 1.30f, -.07f), new Vector3(0f, 1.30f, .07f),
            new Vector3(-.10f, 1.12f, 0f), new Vector3(.10f, 1.12f, 0f),
            new Vector3(0f, .98f, 0f)
        };

        private static readonly Vector3[] RestingLegSamples =
        {
            new Vector3(-.095f, .77f, 0f), new Vector3(.095f, .77f, 0f),
            new Vector3(-.10f, .46f, .01f), new Vector3(.10f, .46f, .01f),
            new Vector3(-.10f, .16f, .02f), new Vector3(.10f, .16f, .02f)
        };

        internal static bool CanSeeRunner(RaidView view, Vector2 actor, float yaw, bool standing, RaidLocomotion motion = default)
        {
            float sin = Mathf.Sin(yaw * Mathf.Deg2Rad), cos = Mathf.Cos(yaw * Mathf.Deg2Rad);
            return SeesSamples(view, actor, sin, cos, TorsoSamples, motion.EyeOffset) ||
                (standing && !motion.Airborne && motion.Crouch < .02f &&
                 SeesSamples(view, actor, sin, cos, RestingLegSamples, 0f));
        }

        private static bool SeesSamples(RaidView view, Vector2 actor, float sin, float cos, Vector3[] samples, float heightOffset)
        {
            foreach (Vector3 v in samples)
            {
                var world = new Vector3(actor.x + cos * v.x + sin * v.z, v.y + heightOffset, actor.y - sin * v.x + cos * v.z);
                if (view.InFrame(world) && ClearSegment(view.Origin, world)) return true;
            }
            return false;
        }

        private static bool ClipAxis(float origin, float direction, float halfSize, ref float first, ref float last)
        {
            if (direction == 0f)
                return Mathf.Abs(origin) <= halfSize;
            float a = (-halfSize - origin) / direction;
            float b = (halfSize - origin) / direction;
            first = Mathf.Max(first, Mathf.Min(a, b));
            last = Mathf.Min(last, Mathf.Max(a, b));
            return first <= last;
        }
    }
}
