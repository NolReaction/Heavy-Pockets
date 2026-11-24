using System;
using UnityEngine;

namespace HeavyPockets
{
    // One world per dedicated server or client process. Both the streamed leaf and the
    // permanent physics pivot subscribe, including when a tile returns to an open door.
    internal sealed class WarehouseDoor : MonoBehaviour
    {
        private const float HandleHeight = 1.00f, HandleInset = .12f, Reach = 1.85f;
        private static readonly Quaternion OpenRotation = Quaternion.Euler(0, ReceivingWarehouseLayout.OpenAngle, 0);
        private static readonly Bounds ClosedBounds = BoundsFor(false), OpenBounds = BoundsFor(true);
        private static event Action Changed;
        internal static bool IsOpen { get; private set; }
        internal static Bounds LeafBounds => IsOpen ? OpenBounds : ClosedBounds;
        private static Vector3 Hinge => ReceivingWarehouseLayout.DoorHinge;
        internal static Vector3 LeafSize => new Vector3(ReceivingWarehouseLayout.DoorWidth,
            ReceivingWarehouseLayout.DoorHeight, ReceivingWarehouseLayout.DoorThickness);
        internal static Vector3 LeafCenter => new Vector3(LeafSize.x * .5f, LeafSize.y * .5f, 0f);

        private static Bounds BoundsFor(bool open)
        {
            Vector3 size = LeafSize;
            return open ? new Bounds(Hinge + OpenRotation * LeafCenter, new Vector3(size.z, size.y, size.x)) :
                new Bounds(Hinge + LeafCenter, size);
        }

        internal static void SetOpen(bool open)
        {
            if (IsOpen == open) return;
            IsOpen = open;
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            IsOpen = false;
            Changed = null;
        }

        internal static bool CanOperate(Vector2 position, RaidLocomotion motion)
        {
            Vector3 eye = RaidCameraBoom.Eye(position) + Vector3.up * motion.EyeOffset;
            Vector3 span = IsOpen ? OpenRotation * Vector3.right : Vector3.right;
            float along = LeafSize.x - HandleInset;
            // From the outside a swung-in door can be reached along its near edge.
            if (IsOpen) along = Mathf.Clamp(Vector3.Dot(eye - Hinge, span), .30f, along);
            Vector3 grip = Hinge + span * along + Vector3.up * HandleHeight;
            Vector3 ray = grip - eye;
            return ray.sqrMagnitude <= Reach * Reach && !YardOcclusion.BlocksStaticSight(eye, ray);
        }

        internal static bool SweepsPlayer(Vector2 position, RaidLocomotion motion)
        {
            float head = motion.Height + RaidMap.StandingHeight - motion.Crouch * RaidLocomotion.CrouchEyeDrop;
            if (head < Hinge.y || motion.Height > Hinge.y + LeafSize.y) return false;
            // In plan the inward swing covers a quarter disc, from +X to +Z.
            Vector2 offset = position - new Vector2(Hinge.x, Hinge.z);
            Vector2 nearest = new Vector2(Mathf.Max(0, offset.x), Mathf.Max(0, offset.y));
            nearest = Vector2.ClampMagnitude(nearest, LeafSize.x);
            float clearance = RaidMap.PlayerRadius + LeafSize.z * .5f;
            return (offset - nearest).sqrMagnitude <= clearance * clearance;
        }

        private void OnEnable() { Changed += Orient; Orient(); }
        private void OnDisable() => Changed -= Orient;
        private void Orient() => transform.localRotation = IsOpen ? OpenRotation : Quaternion.identity;
    }
}
