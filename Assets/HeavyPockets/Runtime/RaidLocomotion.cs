using UnityEngine;

namespace HeavyPockets
{
    [System.Flags]
    internal enum MoveButtons : byte { None = 0, Sprint = 1, Crouch = 2, Jump = 4 }

    public struct RaidLocomotion
    {
        internal const float WalkSpeed = 2.35f, SprintSpeed = 4.75f, SneakSpeed = 1.35f;
        internal const float CrouchEyeDrop = .57f, JumpImpulse = 4.65f, Gravity = 14.4f;
        public float Height, VerticalSpeed, Crouch;
        public bool Sprinting;
        public bool Airborne => Height > 0f || VerticalSpeed > 0f;
        internal float EyeOffset => Height - CrouchEyeDrop * Crouch;

        internal void Step(ref Vector2 position, ref Vector2 velocity, Vector2 direction, float yaw,
            MoveButtons buttons, float dt)
        {
            bool crouching = (buttons & MoveButtons.Crouch) != 0 && !Airborne;
            Crouch = Mathf.MoveTowards(Crouch, crouching ? 1f : 0f, dt * 5.2f);
            float angle = yaw * Mathf.Deg2Rad;
            float forward = direction.x * Mathf.Sin(angle) + direction.y * Mathf.Cos(angle);
            Sprinting = (buttons & MoveButtons.Sprint) != 0 && forward > .35f && !crouching && Crouch < .1f;
            float speed = Mathf.Lerp(Sprinting ? SprintSpeed : WalkSpeed, SneakSpeed, Crouch);
            Vector2 desired = direction * speed;
            float acceleration = Airborne ? 5.5f : desired.sqrMagnitude < velocity.sqrMagnitude ? 24f : 17f;
            velocity = Vector2.MoveTowards(velocity, desired, acceleration * dt);
            Vector2 moved = RaidMap.Move(position, velocity * dt);
            velocity = (moved - position) / dt;
            position = moved;
            if (Airborne)
            {
                Height += (VerticalSpeed - .5f * Gravity * dt) * dt;
                VerticalSpeed -= Gravity * dt;
                if (Height <= 0f) Height = VerticalSpeed = 0f;
            }
        }
    }
}
