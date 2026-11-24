using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace HeavyPockets
{
    // One instance per placed passage. The server, offline range, player motor,
    // camera and streamed meshes sample the same pose, even with no renderer loaded.
    internal sealed class WorldMechanism
    {
        internal readonly byte Id;
        internal readonly bool IsShutter;
        internal readonly Vector3 Pivot, Size;
        internal readonly Quaternion Frame;
        internal float Width => Size.x;
        internal float Height => Size.y;
        internal int From { get; private set; }
        internal int Target { get; private set; }
        internal double Started { get; private set; }
        internal bool IsOpen => Target != 0;
        internal bool Moving { get { Sample(); return moving; } }
        internal float Amount { get { Sample(); return amount; } }
        internal Quaternion LeafRotation { get { Sample(); return leafRotation; } }
        internal float Lift { get { Sample(); return lift; } }
        internal Bounds ClosedLocalBox => new Bounds(new Vector3(IsShutter ? 0f : Size.x * .5f, Size.y * .5f, 0), Size);

        private readonly Quaternion frameInverse;
        private readonly float duration;
        private int sampledFrame = -1;
        private bool moving;
        private float amount, lift;
        private Quaternion leafRotation, inverse;
        private Bounds box, broadBox;

        internal WorldMechanism(byte id, bool shutter, Vector3 pivot, Vector3 size, float yaw = 0f)
        {
            Id = id;
            IsShutter = shutter;
            Pivot = pivot;
            Size = size;
            Frame = Quaternion.Euler(0, yaw, 0);
            frameInverse = Quaternion.Inverse(Frame);
            duration = shutter ? 1.15f : .32f;
        }

        internal void SetMotion(int from, int target, double started)
        {
            From = from;
            Target = target;
            Started = started;
            sampledFrame = -1;
        }

        private void Sample()
        {
            int frame = Time.frameCount;
            if (sampledFrame == frame || (sampledFrame >= 0 && !moving)) return;
            sampledFrame = frame;
            float t = From == Target ? 1f : Mathf.Clamp01((float)((WorldMechanisms.Now - Started) / duration));
            moving = t < 1f;
            amount = Mathf.SmoothStep(From, Target, t);
            lift = IsShutter ? amount * (Size.y - .14f) : 0f;
            leafRotation = IsShutter ? Quaternion.identity : Quaternion.Euler(0, -90f * amount, 0);
            Quaternion orientation = Frame * leafRotation;
            inverse = Quaternion.Inverse(orientation);
            box = ClosedLocalBox;
            if (IsShutter) box.SetMinMax(new Vector3(-Size.x * .5f, lift, -Size.z * .5f), new Vector3(Size.x * .5f, Size.y, Size.z * .5f));
            Vector3 x = orientation * Vector3.right, z = orientation * Vector3.forward, size = box.size;
            broadBox = new Bounds(Pivot + orientation * box.center, new Vector3(
                Mathf.Abs(x.x) * size.x + Mathf.Abs(z.x) * size.z, size.y,
                Mathf.Abs(x.z) * size.x + Mathf.Abs(z.z) * size.z));
        }

        internal int NextTarget(Vector2 feet)
        {
            if (IsOpen) return 0;
            Vector3 local = frameInverse * (new Vector3(feet.x, Pivot.y, feet.y) - Pivot);
            return IsShutter || local.z <= 0f ? 1 : -1;
        }

        internal float GripDistance(Vector2 feet, RaidLocomotion motion)
        {
            Vector3 eye = RaidCameraBoom.Eye(feet) + Vector3.up * motion.EyeOffset;
            Vector3 local = frameInverse * (eye - Pivot);
            if (Mathf.Abs(local.z) > Size.x + 2f || Mathf.Abs(local.x) > Size.x + 2f) return float.PositiveInfinity;
            Vector3 grip;
            if (IsShutter)
                grip = new Vector3(Size.x * .5f + .23f, 1.1f, local.z < 0f ? -.36f : .36f);
            else
            {
                Vector3 span = LeafRotation * Vector3.right;
                float along = IsOpen ? Mathf.Clamp(Vector3.Dot(local, span), .3f, Size.x - .12f) : Size.x - .12f;
                grip = span * along + Vector3.up;
            }
            Vector3 ray = Pivot + Frame * grip - eye;
            float d = ray.sqrMagnitude;
            if (d > 1.85f * 1.85f || YardOcclusion.BlocksStaticSight(eye, ray)) return float.PositiveInfinity;
            foreach (var other in WorldMechanisms.Active)
                if (other != this && other.Trace(eye, ray, 1f, 0f, out _)) return float.PositiveInfinity;
            return d;
        }

        internal bool Sweeps(Vector2 feet, float bottom, float top, int side)
        {
            if (top < Pivot.y || bottom > Pivot.y + Size.y) return false;
            Vector3 local = frameInverse * (new Vector3(feet.x, Pivot.y, feet.y) - Pivot);
            float clearance = RaidMap.PlayerRadius + Size.z * .5f;
            if (IsShutter)
            {
                if (side != 0) return false;
                float dx = Mathf.Max(0, Mathf.Abs(local.x) - Size.x * .5f);
                return dx * dx + local.z * local.z <= clearance * clearance;
            }
            // Distance to the quarter-disc swept from the closed leaf to its chosen stop.
            Vector2 offset = new Vector2(local.x, local.z * (side == 0 ? Target : side));
            Vector2 closest = Vector2.ClampMagnitude(Vector2.Max(Vector2.zero, offset), Size.x);
            return (offset - closest).sqrMagnitude <= clearance * clearance;
        }

        internal bool Sweeps(Vector2 feet, RaidLocomotion motion, int side) => Sweeps(feet, motion.Height,
            motion.Height + RaidMap.StandingHeight - motion.Crouch * RaidLocomotion.CrouchEyeDrop, side);

        // These checks run only when a player presses the control. Use PhysX's actual
        // collider shapes for movable props, including compound bodies on the range.
        internal bool ObstructedByBody(int target)
        {
            if (IsShutter && target != 0) return false;
            int samples = IsShutter ? 1 : 12;
            int side = target != 0 ? target : Target;
            for (int i = 0; i <= samples; i++)
            {
                Quaternion rotation = IsShutter ? Frame : Frame * Quaternion.Euler(0, -90f * side * i / samples, 0);
                Bounds probe = ClosedLocalBox;
                if (!IsShutter) probe.Expand(new Vector3(0, 0, Size.x * Mathf.PI / (2 * samples)));
                foreach (Collider hit in Physics.OverlapBox(Pivot + rotation * probe.center, probe.extents, rotation,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    if (hit.attachedRigidbody && !hit.attachedRigidbody.isKinematic) return true;
                if (IsShutter) break;
            }
            return false;
        }

        internal bool Touches(Vector2 feet, out float bottom, out float top)
        {
            Sample();
            bottom = Pivot.y + box.min.y;
            top = Pivot.y + box.max.y;
            if (Mathf.Abs(feet.x - broadBox.center.x) > broadBox.extents.x + RaidMap.PlayerRadius ||
                Mathf.Abs(feet.y - broadBox.center.z) > broadBox.extents.z + RaidMap.PlayerRadius) return false;
            Vector3 local = inverse * (new Vector3(feet.x, Pivot.y, feet.y) - Pivot) - box.center;
            float dx = Mathf.Max(Mathf.Abs(local.x) - box.extents.x, 0f);
            float dz = Mathf.Max(Mathf.Abs(local.z) - box.extents.z, 0f);
            return dx * dx + dz * dz < RaidMap.PlayerRadius * RaidMap.PlayerRadius;
        }

        internal bool BlocksTravel(Vector2 feet, float bottom, float top)
        {
            Sample();
            // Reserve the swing until the motor stops, so a second runner cannot step into it mid-motion.
            if (moving && (!IsShutter || Target == 0) &&
                Sweeps(feet, bottom, top, Target == 0 && !IsShutter ? From : Target)) return true;
            if (!Touches(feet, out float low, out float high)) return false;
            if (!IsShutter || lift < .001f) low = Pivot.y - .05f;
            return high > bottom + .0005f && low < top - .0005f;
        }

        internal bool Trace(Vector3 origin, Vector3 direction, float length, float clearance, out float first)
        {
            Sample();
            Bounds outside = broadBox;
            outside.Expand(2f * clearance);
            if (!RaidMap.Intersects(outside, origin, direction, length, out first)) return false;
            Bounds local = box;
            local.Expand(2f * clearance);
            return RaidMap.Intersects(local, inverse * (origin - Pivot), inverse * direction, length, out first);
        }

        internal void Bind(Transform leaf) => leaf.gameObject.AddComponent<PassageLeaf>().Initialize(this);
    }

    internal static class WorldMechanisms
    {
        internal static readonly WorldMechanism WarehouseDoor = new WorldMechanism(0, false,
            ReceivingWarehouseLayout.DoorHinge, new Vector3(ReceivingWarehouseLayout.DoorWidth, ReceivingWarehouseLayout.DoorHeight, ReceivingWarehouseLayout.DoorThickness));
        internal static readonly WorldMechanism WarehouseGate = new WorldMechanism(1, true,
            ReceivingWarehouseLayout.GateBottom, new Vector3(ReceivingWarehouseLayout.GateWidth, ReceivingWarehouseLayout.GateHeight, ReceivingWarehouseLayout.GateThickness));
        internal static readonly WorldMechanism TrainingDoor = new WorldMechanism(0, false,
            new Vector3(19f, .02f, -7f), new Vector3(1.08f, 2.10f, .045f));
        internal static readonly WorldMechanism TrainingGate = new WorldMechanism(1, true,
            new Vector3(27f, .02f, -7f), new Vector3(3.60f, 3f, .07f));
        private static readonly Vector3 rangeOffset = TrainingBuildings.Warehouse - ReceivingWarehouseLayout.Origin;
        private static readonly WorldMechanism rangeWarehouseDoor = new WorldMechanism(2, false, WarehouseDoor.Pivot + rangeOffset, WarehouseDoor.Size);
        private static readonly WorldMechanism rangeWarehouseGate = new WorldMechanism(3, true, WarehouseGate.Pivot + rangeOffset, WarehouseGate.Size);
        internal static WorldMechanism PlacedWarehouseDoor => TrainingGround.Active ? rangeWarehouseDoor : WarehouseDoor;
        internal static WorldMechanism PlacedWarehouseGate => TrainingGround.Active ? rangeWarehouseGate : WarehouseGate;
        private static readonly WorldMechanism[] port = { WarehouseDoor, WarehouseGate };
        private static readonly WorldMechanism[] training = { TrainingDoor, TrainingGate, rangeWarehouseDoor, rangeWarehouseGate };
        private static readonly Dictionary<byte, WorldMechanism> portIds = Index(port);
        private static readonly Dictionary<byte, WorldMechanism> trainingIds = Index(training);
        internal static WorldMechanism[] Active => TrainingGround.Active ? training : port;

        private static Dictionary<byte, WorldMechanism> Index(WorldMechanism[] placements)
        {
            var ids = new Dictionary<byte, WorldMechanism>(placements.Length);
            foreach (var m in placements)
                if (!ids.TryAdd(m.Id, m))
                    throw new InvalidOperationException($"Duplicate passage id {m.Id} at {m.Pivot}.");
            return ids;
        }

        internal static bool TryGet(byte id, out WorldMechanism passage) =>
            (TrainingGround.Active ? trainingIds : portIds).TryGetValue(id, out passage);
        internal static double Now
        {
            get
            {
                var net = NetworkManager.Singleton;
                return net != null && net.IsListening ? net.ServerTime.Time : Time.timeAsDouble;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Reset()
        {
            foreach (var mechanism in port) mechanism.SetMotion(0, 0, 0);
            foreach (var mechanism in training) mechanism.SetMotion(0, 0, 0);
        }

        internal static WorldMechanism Closest(Vector2 feet, RaidLocomotion motion)
        {
            float best = float.PositiveInfinity;
            WorldMechanism found = null;
            foreach (var m in Active)
            {
                float distance = m.GripDistance(feet, motion);
                if (distance < best) { found = m; best = distance; }
            }
            return found;
        }

        internal static bool TryUseLocal(Vector2 feet, RaidLocomotion motion)
        {
            var m = Closest(feet, motion);
            if (m == null || m.Moving) return false;
            int target = m.NextTarget(feet);
            if (m.Sweeps(feet, motion, target) || m.ObstructedByBody(target)) return false;
            m.SetMotion(m.Target, target, Now);
            return true;
        }

        internal static void CreateColliders(Transform parent)
        {
            foreach (var m in Active)
            {
                var go = new GameObject(m.IsShutter ? "Shutter hitbox" : "Swing door hitbox");
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(m.Pivot, m.Frame);
                var body = go.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.isKinematic = true;
                Bounds box = m.ClosedLocalBox;
                var collider = go.AddComponent<BoxCollider>();
                collider.center = box.center;
                collider.size = box.size;
                m.Bind(go.transform);
            }
        }
    }
}
