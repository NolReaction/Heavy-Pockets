using UnityEngine;

namespace HeavyPockets
{
    public enum RaidCameraMode : byte { FirstPerson, RightShoulder, LeftShoulder }

    // Reconstructed only from the server's own camera origin and validated lens inputs.
    internal readonly struct RaidView
    {
        internal readonly Vector3 Origin;
        private readonly Vector3 right, up, forward;
        private readonly float horizontalSlope;
        private static readonly float VerticalSlope = Mathf.Tan(RaidCameraBoom.VerticalFov * .5f * Mathf.Deg2Rad);

        internal RaidView(Vector3 origin, float yaw, float pitch, float aspect)
        {
            Origin = origin;
            float sinYaw = Mathf.Sin(yaw * Mathf.Deg2Rad), cosYaw = Mathf.Cos(yaw * Mathf.Deg2Rad);
            float sinPitch = Mathf.Sin(pitch * Mathf.Deg2Rad), cosPitch = Mathf.Cos(pitch * Mathf.Deg2Rad);
            right = new Vector3(cosYaw, 0, -sinYaw);
            up = new Vector3(sinYaw * sinPitch, cosPitch, cosYaw * sinPitch);
            forward = new Vector3(sinYaw * cosPitch, -sinPitch, cosYaw * cosPitch);
            horizontalSlope = VerticalSlope * aspect;
        }

        internal bool InFrame(Vector3 point)
        {
            Vector3 offset = point - Origin;
            float depth = Vector3.Dot(offset, forward);
            return depth >= RaidCameraBoom.NearClip && depth <= RaidCameraBoom.FarClip
                && Mathf.Abs(Vector3.Dot(offset, right)) <= depth * horizontalSlope
                && Mathf.Abs(Vector3.Dot(offset, up)) <= depth * VerticalSlope;
        }
    }

    // The depot meshes are render-only. Sweep the camera arm against the same
    // footprints used for movement, padded to keep the near plane clear.
    internal static class RaidCameraBoom
    {
        internal const float Clearance = 0.16f;
        internal const float MaxPitch = 82f;
        internal const float VerticalFov = 76f, NearClip = .045f, FarClip = 650f;
        internal const float MinAspect = .25f, MaxAspect = 3.6f;
        internal const float ShoulderSeconds = .34f;
        internal const byte CenterSample = 127, LastSample = 254;
        internal static Vector3 Eye(Vector2 ground) => new Vector3(ground.x, RaidMap.StandingHeight - .12f, ground.y);

        // Phase runs from left (0) to right (1). Changing direction keeps the current
        // phase; the eased position has zero speed at either end of the travel.
        internal static float AdvanceShoulder(float phase, RaidCameraMode mode, float delta)
        {
            if (mode == RaidCameraMode.FirstPerson) return phase;
            return Mathf.MoveTowards(phase, mode == RaidCameraMode.LeftShoulder ? 0f : 1f,
                Mathf.Min(delta, .075f) / ShoulderSeconds);
        }

        internal static byte SampleShoulder(float phase, RaidCameraMode mode)
        {
            if (mode == RaidCameraMode.FirstPerson) return CenterSample;
            float value = Mathf.Clamp01(phase) * LastSample;
            return (byte)(mode == RaidCameraMode.LeftShoulder ? Mathf.CeilToInt(value) : Mathf.FloorToInt(value));
        }

        private static Vector3 Arm(float yaw, float pitch, float phase, float aspect)
        {
            // Pull the shoulder in on narrow viewports to keep the near character in frame.
            float side = .92f * Mathf.Min(1f, aspect / (16f / 9f)) * Mathf.SmoothStep(-1f, 1f, phase);
            float y = yaw * Mathf.Deg2Rad, p = pitch * Mathf.Deg2Rad;
            float sinYaw = Mathf.Sin(y), cosYaw = Mathf.Cos(y);
            float sinPitch = Mathf.Sin(p), cosPitch = Mathf.Cos(p);
            float z = .22f * sinPitch - 2.15f * cosPitch;
            return new Vector3(side * cosYaw + z * sinYaw, .22f * cosPitch + 2.15f * sinPitch,
                -side * sinYaw + z * cosYaw);
        }

        internal static Vector3 Position(Vector2 ground, float yaw, float pitch, RaidCameraMode mode,
            float aspect, float phase, ref float boomLength, float delta, bool snap, float eyeOffset = 0f)
        {
            Vector3 eye = Eye(ground) + Vector3.up * eyeOffset;
            if (mode == RaidCameraMode.FirstPerson) { boomLength = 0f; return eye; }
            Vector3 arm = Arm(yaw, pitch, phase, aspect);
            float distance = arm.magnitude;
            Vector3 direction = arm / distance;
            float available = AvailableLength(eye, direction, distance);
            boomLength = AdvanceLength(boomLength, available, delta, snap);
            return eye + direction * boomLength;
        }

        internal static float AdvanceLength(float previous, float available, float delta, bool snap)
        {
            return snap || available <= previous ? available : Mathf.MoveTowards(previous, available, delta * 5.5f);
        }

        private static readonly Bounds Partition = Obstacle(Vector2.zero, RaidMap.WallX + 0.02f, RaidMap.WallZ + 0.05f, 2.34f);
        private static readonly Bounds Cargo = PaddedCargoCase();

        private static Bounds PaddedCargoCase()
        {
            Bounds box = RaidMap.CargoCase;
            box.Expand(2f * Clearance);
            return box;
        }

        private static Bounds Obstacle(Vector2 center, float halfX, float halfZ, float height)
        {
            return new Bounds(new Vector3(center.x, height * 0.5f, center.y),
                new Vector3(2f * (halfX + Clearance), height + 2f * Clearance, 2f * (halfZ + Clearance)));
        }

        internal static float AvailableLength(Vector3 eye, Vector3 direction, float length)
        {
            float xLimit = RaidMap.RoomX - Clearance, zLimit = RaidMap.RoomZ - Clearance;
            length = LimitAxis(eye.x, direction.x, -xLimit, xLimit, length);
            length = LimitAxis(eye.z, direction.z, -zLimit, zLimit, length);
            if (direction.y < -0.00001f)
                length = Mathf.Min(length, (Clearance + .04f - eye.y) / direction.y);
            if (!TrainingGround.Active)
            {
                if (RaidMap.Intersects(Partition, eye, direction, length, out float wall)) length = wall;
                if (RaidMap.Intersects(Cargo, eye, direction, length, out float crate)) length = crate;
            }
            length = YardOcclusion.ClipCamera(eye, direction, length);
            return Mathf.Max(0f, length);
        }

        private static float LimitAxis(float origin, float direction, float min, float max, float length)
        {
            if (direction > 0.00001f) return Mathf.Min(length, (max - origin) / direction);
            if (direction < -0.00001f) return Mathf.Min(length, (min - origin) / direction);
            return length;
        }
    }
}
