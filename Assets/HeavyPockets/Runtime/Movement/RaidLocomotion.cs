using UnityEngine;

namespace HeavyPockets
{
    [System.Flags]
    internal enum MoveButtons : byte { None = 0, Sprint = 1, Crouch = 2, Jump = 4 }

    public struct RaidLocomotion
    {
        internal const float WalkSpeed = 2.35f, SprintSpeed = 4.75f, SneakSpeed = 1.35f;
        internal const float CrouchEyeDrop = .57f, JumpImpulse = 4.65f, Gravity = 14.4f;
        private const float MinSlideEntry = 3.65f, SlideExitSpeed = 2.0f, SlideDrag = 3.6f;
        public float Height, VerticalSpeed, Crouch;
        public bool Sprinting, Sliding;
        public bool Airborne => Height > 0f || VerticalSpeed > 0f;
        internal float EyeOffset => Height - CrouchEyeDrop * Crouch;

        // Button edges arrive on the ordered movement channel, sometimes twice between physics ticks.
        internal void Press(Vector2 position, Vector2 velocity, float yaw, MoveButtons previous, MoveButtons next)
        {
            if (Airborne) return;
            bool crouch = (next & MoveButtons.Crouch) != 0;
            bool wantsSlide = crouch && (next & MoveButtons.Sprint) != 0 && (previous & MoveButtons.Crouch) == 0;
            if (wantsSlide && Sprinting && velocity.sqrMagnitude >= MinSlideEntry * MinSlideEntry &&
                ForwardFraction(velocity.normalized, yaw) > .35f)
            {
                Sliding = true;
                Sprinting = false;
            }
            if ((next & MoveButtons.Jump) != 0 && (Sliding || (!crouch && Crouch < .1f)) && RaidMap.CanStand(position))
            {
                Sprinting = Sliding || Sprinting;
                Sliding = false;
                VerticalSpeed = JumpImpulse;
            }
        }

        private static float ForwardFraction(Vector2 direction, float yaw)
        {
            float angle = yaw * Mathf.Deg2Rad;
            return direction.x * Mathf.Sin(angle) + direction.y * Mathf.Cos(angle);
        }

        internal void Step(ref Vector2 position, ref Vector2 velocity, Vector2 direction, float yaw,
            MoveButtons buttons, float dt)
        {
            if (dt <= 0f) return;
            bool crouching = !Airborne && (Sliding || (buttons & MoveButtons.Crouch) != 0 || !RaidMap.CanStand(position));
            Crouch = Mathf.MoveTowards(Crouch, crouching ? 1f : 0f, dt * (Sliding ? 8f : 5.2f));
            float forward = ForwardFraction(direction, yaw);
            if (!Airborne)
                Sprinting = !crouching && Crouch < .1f && (buttons & MoveButtons.Sprint) != 0 && forward > .35f;
            if (Sliding)
            {
                float speed = Mathf.Max(0f, velocity.magnitude - SlideDrag * dt);
                Vector2 heading = velocity.normalized;
                // Steering bends the path, without turning a camera flick into a reversal or speed boost.
                Vector2 lateral = direction - Vector2.Dot(heading, direction) * heading;
                velocity = (heading + lateral * (.7f * dt)).normalized * speed;
            }
            else if (!Airborne || direction.sqrMagnitude > .0001f)
            {
                float strideSpeed = WalkSpeed * Mathf.Lerp(.78f, 1f, Mathf.Clamp01(forward + 1f));
                float speed = Mathf.Lerp(Sprinting ? SprintSpeed : strideSpeed, SneakSpeed, Crouch);
                Vector2 desired = direction * speed;
                float acceleration = Airborne ? 4.5f : desired.sqrMagnitude < velocity.sqrMagnitude ? 24f : 17f;
                velocity = Vector2.MoveTowards(velocity, desired, acceleration * dt);
            }
            float capsuleTop = RaidMap.StandingHeight + EyeOffset;
            Vector2 moved = RaidMap.Move(position, velocity * dt, capsuleTop);
            // A blocked axis can advance a fraction of a substep on contact. Do not retain that
            // component into the next slide tick and continually press the character into the wall.
            Vector2 actual = (moved - position) / dt;
            if (Sliding)
            {
                if (Mathf.Abs(actual.x - velocity.x) > .002f) actual.x = 0f;
                if (Mathf.Abs(actual.y - velocity.y) > .002f) actual.y = 0f;
                if (actual.sqrMagnitude <= SlideExitSpeed * SlideExitSpeed) Sliding = false;
            }
            velocity = actual;
            position = moved;
            if (Airborne)
            {
                Height += (VerticalSpeed - .5f * Gravity * dt) * dt;
                VerticalSpeed -= Gravity * dt;
                float clearance = RaidMap.StandingJumpClearance(position);
                if (Height > clearance && VerticalSpeed > 0f)
                {
                    Height = Mathf.Max(0f, clearance);
                    VerticalSpeed = 0f;
                }
                if (Height <= 0f) Height = VerticalSpeed = 0f;
            }
        }
    }
}
