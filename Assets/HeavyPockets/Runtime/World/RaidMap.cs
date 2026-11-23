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

        // Interior torso and head samples follow the shared moving silhouette.
        // Feet and swinging arms are sampled only once the runner comes to rest.
        private static readonly Vector3[] StridingSamples =
        {
            new Vector3(0, 1.26f, 0), new Vector3(-.14f, 1.26f, 0), new Vector3(.14f, 1.26f, 0),
            new Vector3(-.13f, 1.34f, -.02f), new Vector3(.13f, 1.34f, -.02f), new Vector3(0, 1.24f, -.07f),
            new Vector3(0, 1.24f, .07f), new Vector3(0, 1.07f, 0), new Vector3(-.095f, 1.07f, 0),
            new Vector3(.095f, 1.07f, 0), new Vector3(0, 1.58f, .039f), new Vector3(-.046f, 1.58f, .039f),
            new Vector3(.046f, 1.58f, .039f)
        };

        // Inset support points at ankle, shin, hand, pelvis, elbow, shoulder and
        // head height, measured from the skinned 180 cm FBX in its relaxed pose.
        private static readonly Vector3[] StandingSamples =
        {
            new Vector3(-0.211f, 0.075f, 0.096f), new Vector3(-0.232f, 0.075f, 0.100f), new Vector3(-0.258f, 0.075f, 0.219f),
            new Vector3(-0.241f, 0.075f, 0.282f), new Vector3(-0.197f, 0.075f, 0.315f), new Vector3(0.172f, 0.075f, -0.301f),
            new Vector3(0.299f, 0.075f, -0.221f), new Vector3(0.313f, 0.075f, -0.175f), new Vector3(0.297f, 0.075f, -0.136f),
            new Vector3(0.298f, 0.075f, -0.137f), new Vector3(-0.192f, 0.430f, 0.082f), new Vector3(-0.248f, 0.430f, 0.111f),
            new Vector3(-0.254f, 0.430f, 0.112f), new Vector3(-0.227f, 0.430f, 0.167f), new Vector3(-0.208f, 0.430f, 0.182f),
            new Vector3(0.216f, 0.430f, -0.259f), new Vector3(0.234f, 0.430f, -0.246f), new Vector3(0.247f, 0.430f, -0.201f),
            new Vector3(0.235f, 0.430f, -0.164f), new Vector3(0.174f, 0.430f, -0.152f), new Vector3(-0.027f, 0.830f, -0.076f),
            new Vector3(-0.313f, 0.830f, -0.007f), new Vector3(-0.326f, 0.830f, 0.019f), new Vector3(-0.333f, 0.830f, 0.013f),
            new Vector3(-0.109f, 0.830f, 0.150f), new Vector3(0.257f, 0.830f, -0.174f), new Vector3(0.263f, 0.830f, -0.172f),
            new Vector3(0.277f, 0.830f, -0.140f), new Vector3(0.215f, 0.830f, -0.010f), new Vector3(0.015f, 0.830f, 0.079f),
            new Vector3(-0.003f, 1.040f, -0.131f), new Vector3(-0.299f, 1.040f, -0.092f), new Vector3(-0.310f, 1.040f, -0.062f),
            new Vector3(-0.312f, 1.040f, -0.050f), new Vector3(-0.011f, 1.040f, 0.080f), new Vector3(0.216f, 1.040f, -0.264f),
            new Vector3(0.237f, 1.040f, -0.242f), new Vector3(0.242f, 1.040f, -0.211f), new Vector3(0.140f, 1.040f, 0.010f),
            new Vector3(0.013f, 1.040f, 0.081f), new Vector3(-0.003f, 1.240f, -0.167f), new Vector3(-0.264f, 1.240f, -0.115f),
            new Vector3(-0.275f, 1.240f, -0.081f), new Vector3(-0.250f, 1.240f, -0.052f), new Vector3(-0.020f, 1.240f, 0.043f),
            new Vector3(0.159f, 1.240f, -0.290f), new Vector3(0.201f, 1.240f, -0.266f), new Vector3(0.207f, 1.240f, -0.203f),
            new Vector3(0.112f, 1.240f, -0.019f), new Vector3(0.001f, 1.240f, 0.047f), new Vector3(-0.009f, 1.420f, -0.168f),
            new Vector3(-0.214f, 1.420f, -0.092f), new Vector3(-0.225f, 1.420f, -0.049f), new Vector3(-0.209f, 1.420f, -0.007f),
            new Vector3(-0.064f, 1.420f, 0.030f), new Vector3(0.131f, 1.420f, -0.214f), new Vector3(0.189f, 1.420f, -0.184f),
            new Vector3(0.202f, 1.420f, -0.136f), new Vector3(0.184f, 1.420f, -0.099f), new Vector3(0.064f, 1.420f, 0.009f),
            new Vector3(-0.009f, 1.610f, -0.069f), new Vector3(-0.074f, 1.610f, -0.005f), new Vector3(-0.061f, 1.610f, -0.017f),
            new Vector3(-0.028f, 1.610f, 0.054f), new Vector3(-0.004f, 1.610f, 0.070f), new Vector3(0.021f, 1.610f, -0.074f),
            new Vector3(0.088f, 1.610f, -0.032f), new Vector3(0.086f, 1.610f, -0.038f), new Vector3(0.059f, 1.610f, 0.055f),
            new Vector3(0.029f, 1.610f, 0.099f), new Vector3(0.004f, 1.745f, -0.009f),
        };

        private static readonly Vector3[] CrouchingSamples =
        {
            new Vector3(-0.258f, 0.070f, 0.114f), new Vector3(-0.281f, 0.070f, 0.125f), new Vector3(-0.327f, 0.070f, 0.261f),
            new Vector3(-0.319f, 0.070f, 0.287f), new Vector3(-0.290f, 0.070f, 0.315f), new Vector3(-0.258f, 0.270f, 0.122f),
            new Vector3(-0.286f, 0.270f, 0.141f), new Vector3(-0.287f, 0.270f, 0.150f), new Vector3(-0.270f, 0.270f, 0.184f),
            new Vector3(-0.262f, 0.270f, 0.186f), new Vector3(0.233f, 0.270f, -0.444f), new Vector3(0.241f, 0.270f, -0.437f),
            new Vector3(0.268f, 0.270f, -0.364f), new Vector3(0.257f, 0.270f, -0.326f), new Vector3(0.231f, 0.270f, -0.314f),
            new Vector3(-0.435f, 0.460f, -0.258f), new Vector3(-0.460f, 0.460f, -0.241f), new Vector3(-0.441f, 0.460f, -0.269f),
            new Vector3(-0.278f, 0.460f, 0.167f), new Vector3(-0.240f, 0.460f, 0.188f), new Vector3(0.044f, 0.460f, -0.274f),
            new Vector3(0.324f, 0.460f, -0.084f), new Vector3(0.313f, 0.460f, -0.099f), new Vector3(0.301f, 0.460f, -0.085f),
            new Vector3(0.154f, 0.460f, 0.024f), new Vector3(0.001f, 0.610f, -0.313f), new Vector3(-0.444f, 0.610f, -0.231f),
            new Vector3(-0.437f, 0.610f, -0.240f), new Vector3(-0.236f, 0.610f, 0.161f), new Vector3(-0.219f, 0.610f, 0.177f),
            new Vector3(0.054f, 0.610f, -0.316f), new Vector3(0.317f, 0.610f, -0.119f), new Vector3(0.315f, 0.610f, -0.119f),
            new Vector3(0.311f, 0.610f, -0.107f), new Vector3(0.255f, 0.610f, -0.073f), new Vector3(-0.397f, 0.790f, -0.229f),
            new Vector3(-0.403f, 0.790f, -0.224f), new Vector3(-0.411f, 0.790f, -0.199f), new Vector3(-0.405f, 0.790f, -0.184f),
            new Vector3(-0.101f, 0.790f, -0.023f), new Vector3(0.250f, 0.790f, -0.207f), new Vector3(0.265f, 0.790f, -0.200f),
            new Vector3(0.277f, 0.790f, -0.175f), new Vector3(0.261f, 0.790f, -0.128f), new Vector3(0.007f, 0.790f, -0.065f),
            new Vector3(-0.016f, 0.910f, -0.187f), new Vector3(-0.342f, 0.910f, -0.147f), new Vector3(-0.367f, 0.910f, -0.100f),
            new Vector3(-0.323f, 0.910f, 0.008f), new Vector3(-0.090f, 0.910f, 0.301f), new Vector3(0.177f, 0.910f, -0.189f),
            new Vector3(0.219f, 0.910f, -0.178f), new Vector3(0.223f, 0.910f, -0.170f), new Vector3(0.121f, 0.910f, -0.005f),
            new Vector3(0.001f, 0.910f, 0.063f), new Vector3(-0.011f, 1.040f, -0.049f), new Vector3(-0.176f, 1.040f, -0.036f),
            new Vector3(-0.188f, 1.040f, -0.023f), new Vector3(-0.128f, 1.040f, 0.317f), new Vector3(-0.093f, 1.040f, 0.338f),
            new Vector3(0.005f, 1.040f, -0.047f), new Vector3(0.001f, 1.040f, -0.050f), new Vector3(0.011f, 1.040f, -0.013f),
            new Vector3(0.013f, 1.040f, -0.019f), new Vector3(0.002f, 1.040f, -0.016f), new Vector3(-0.081f, 1.083f, 0.273f),
        };

        internal static bool CanSeeRunner(RaidView view, Vector2 actor, float yaw, bool standing, RaidLocomotion motion = default)
        {
            float sin = Mathf.Sin(yaw * Mathf.Deg2Rad), cos = Mathf.Cos(yaw * Mathf.Deg2Rad);
            if (standing && !motion.Airborne && (motion.Crouch < .02f || motion.Crouch > .98f))
                return SeesSamples(view, actor, sin, cos, motion.Crouch > .5f ? CrouchingSamples : StandingSamples, motion.Height);
            return SeesSamples(view, actor, sin, cos, StridingSamples, motion.EyeOffset);
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
