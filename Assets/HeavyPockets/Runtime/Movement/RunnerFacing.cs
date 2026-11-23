using UnityEngine;

namespace HeavyPockets
{
    // Visual facing only. Input, aiming and the camera use the original view heading.
    internal struct RunnerFacing
    {
        internal float FeetYaw { get; private set; }
        internal float LookTwist { get; private set; }
        private float previousAim, remainingTurn, stillTime, turnVelocity;
        private bool turning;

        internal void Reset(float yaw)
        {
            FeetYaw = previousAim = Mathf.Repeat(yaw, 360f);
            LookTwist = remainingTurn = stillTime = turnVelocity = 0f;
            turning = false;
        }

        internal void Advance(float aim, float speed, bool airborne, float dt)
        {
            if (dt <= 0f) return;
            float aimTravel = Mathf.DeltaAngle(previousAim, aim);
            // Unwrap consecutive view samples, so a quick pan through the rear
            // hemisphere never tells an ongoing pivot to reverse direction.
            remainingTurn += aimTravel;
            previousAim = aim;
            float aimSpeed = Mathf.Abs(aimTravel) / dt;
            stillTime = aimSpeed <= 5f ? Mathf.Min(.4f, stillTime + dt) : 0f;
            float moving = Mathf.InverseLerp(.08f, 1.8f, speed);
            // A glance leaves the feet planted. Holding a wider look, moving, or
            // reaching the comfortable limit of the spine starts a pivot.
            if (Mathf.Abs(remainingTurn) > 25f || (stillTime >= .32f && Mathf.Abs(remainingTurn) > 8f) || moving > 0f || airborne)
                turning = true;
            float footTravel = 0f;
            if (turning)
            {
                float whip = Mathf.InverseLerp(50f, 120f, Mathf.Abs(remainingTurn)) * Mathf.InverseLerp(300f, 600f, aimSpeed);
                float alignTime = Mathf.Lerp(Mathf.Lerp(.21f, .11f, moving), .07f, whip);
                float turnLimit = Mathf.Lerp(490f, 740f, moving) + whip * aimSpeed * 1.5f;
                footTravel = Mathf.SmoothDamp(0f, remainingTurn, ref turnVelocity, alignTime, turnLimit, dt);
                FeetYaw = Mathf.Repeat(FeetYaw + footTravel, 360f);
                remainingTurn -= footTravel;
                if (Mathf.Abs(remainingTurn) < .25f && Mathf.Abs(turnVelocity) < 3f)
                {
                    footTravel += remainingTurn;
                    FeetYaw = Mathf.Repeat(aim, 360f);
                    remainingTurn = turnVelocity = 0f;
                    turning = false;
                }
            }
            // Keep the upper body continuous in world space while the feet catch up.
            float limit = Mathf.Lerp(53f, 36f, moving);
            float target = Mathf.Clamp(remainingTurn, -limit, limit);
            LookTwist = Mathf.Clamp(Mathf.Lerp(LookTwist - footTravel, target, 1f - Mathf.Exp(-21f * dt)), -limit, limit);
        }
    }
}
