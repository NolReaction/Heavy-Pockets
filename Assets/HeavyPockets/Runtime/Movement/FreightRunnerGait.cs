using UnityEngine;

namespace HeavyPockets
{
    [DefaultExecutionOrder(20)]
    internal sealed class FreightRunnerGait : MonoBehaviour
    {
        private static readonly int Speed = Animator.StringToHash("Speed");
        private static readonly int MotionSpeed = Animator.StringToHash("MotionSpeed");
        private static readonly int Grounded = Animator.StringToHash("Grounded");
        private static readonly int Jump = Animator.StringToHash("Jump");
        private static readonly int FreeFall = Animator.StringToHash("FreeFall");

        private RunnerFacing facing;
        private Transform hips, chest, head;
        private Leg leftLeg, rightLeg;
        private Vector3 lastPosition, planarVelocity;
        private float lookHeading, crouch, liftSpeed, slideWeight;
        private bool hasHeading, airborne, sliding;

        internal RunnerRig Rig { get; private set; }

        internal void Bind(RunnerRig rig)
        {
            Rig = rig;
            hips = rig.Bone(HumanBodyBones.Hips);
            chest = rig.Bone(HumanBodyBones.UpperChest);
            head = rig.Bone(HumanBodyBones.Head);
            leftLeg = new Leg(rig, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
            rightLeg = new Leg(rig, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
            if (isActiveAndEnabled) ResetMotion();
        }

        internal void SetLocomotion(float crouch, bool sprinting, bool airborne, float verticalSpeed, bool sliding = false)
        {
            this.crouch = Mathf.Clamp01(crouch);
            this.airborne = airborne;
            this.sliding = sliding && !airborne;
            liftSpeed = verticalSpeed;
        }

        internal void SetLookHeading(float heading, bool snap = false)
        {
            lookHeading = heading;
            hasHeading = true;
            if (snap || !isActiveAndEnabled) ResetMotion();
        }

        private void OnEnable()
        {
            ResetMotion();
        }

        private void ResetMotion()
        {
            if (hasHeading) transform.rotation = Quaternion.Euler(0, lookHeading, 0);
            facing.Reset(transform.eulerAngles.y);
            lastPosition = transform.position;
            planarVelocity = Vector3.zero;
        }

        private void LateUpdate()
        {
            if (Rig == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            var position = transform.position;
            var delta = position - lastPosition;
            delta.y = 0f;
            lastPosition = position;
            if (delta.sqrMagnitude > Mathf.Pow(.45f + 10f * dt, 2f))
            {
                ResetMotion();
                delta = Vector3.zero;
            }

            float smoothing = 1f - Mathf.Exp(-dt * 18f);
            planarVelocity = Vector3.Lerp(planarVelocity, Vector3.ClampMagnitude(delta / dt, 10f), smoothing);
            float speed = planarVelocity.magnitude;
            if (hasHeading)
            {
                // The Starter Assets walk and run are forward motions. A side input sends the
                // body in the movement direction while the player's view continues to orbit.
                float heading = speed > .22f
                    ? Mathf.Atan2(planarVelocity.x, planarVelocity.z) * Mathf.Rad2Deg : lookHeading;
                facing.Advance(heading, speed, airborne, dt);
                transform.rotation = Quaternion.Euler(0, facing.FeetYaw, 0);
            }

            // The source blend tree was authored at 0, 2 and 6 m/s. Map those thresholds to
            // the raid motor, with matching cadence so the official clips keep their stride.
            float walk = Mathf.Clamp01(speed / RaidLocomotion.WalkSpeed);
            float run = Mathf.InverseLerp(RaidLocomotion.WalkSpeed, RaidLocomotion.SprintSpeed, speed);
            slideWeight = Mathf.MoveTowards(slideWeight, sliding ? 1f : 0f, dt * (sliding ? 8f : 6f));
            float sampleSpeed = Mathf.Lerp(2f * walk + 4f * run, 0f, slideWeight);
            float cadence = sampleSpeed > .15f ? Mathf.Clamp(speed / sampleSpeed, .70f, 1.25f) : 1f;
            Animator animator = Rig.Animator;
            animator.SetFloat(Speed, sampleSpeed);
            animator.SetFloat(MotionSpeed, Mathf.Lerp(cadence, 1f, slideWeight));
            animator.SetBool(Grounded, !airborne);
            animator.SetBool(Jump, airborne && liftSpeed > 0f);
            animator.SetBool(FreeFall, airborne && liftSpeed <= 0f);

            float low = Mathf.Max(crouch, slideWeight);
            if (!airborne && low > .001f) LowerStance(low, slideWeight);
            if (hasHeading && speed < .55f)
            {
                float twist = facing.LookTwist * (1f - Mathf.InverseLerp(.08f, .55f, speed));
                chest.rotation = Quaternion.AngleAxis(twist * .55f, Vector3.up) * chest.rotation;
                head.rotation = Quaternion.AngleAxis(twist * .45f, Vector3.up) * head.rotation;
            }
        }

        // The original controller has no crouch/slide clips. Lower the hips only in
        // the raid, and rebuild the two leg chains at the clip's current foot positions.
        private void LowerStance(float amount, float slide)
        {
            Vector3 travel = transform.forward;
            Vector3 leftTarget = leftLeg.Foot.position + travel * (.14f * slide);
            Vector3 rightTarget = rightLeg.Foot.position - travel * (.12f * slide);
            hips.position += Vector3.down * (amount * .34f + slide * .07f);
            leftLeg.Solve(leftTarget, travel);
            rightLeg.Solve(rightTarget, travel);
            chest.rotation = Quaternion.AngleAxis(amount * 14f - slide * 24f, transform.right) * chest.rotation;
        }

        private readonly struct Leg
        {
            private readonly Transform upper, lower;
            internal readonly Transform Foot;

            internal Leg(RunnerRig rig, HumanBodyBones thigh, HumanBodyBones calf, HumanBodyBones foot)
            {
                upper = rig.Bone(thigh);
                lower = rig.Bone(calf);
                Foot = rig.Bone(foot);
            }

            internal void Solve(Vector3 target, Vector3 forward)
            {
                Vector3 hip = upper.position, knee = lower.position, ankle = Foot.position;
                float upperLength = (knee - hip).magnitude, lowerLength = (ankle - knee).magnitude;
                Vector3 line = target - hip;
                if (line.sqrMagnitude < .000001f) return;

                float length = Mathf.Clamp(line.magnitude, Mathf.Abs(upperLength - lowerLength) + .0001f,
                    upperLength + lowerLength - .0001f);
                Vector3 direction = line.normalized;
                Vector3 bend = Vector3.ProjectOnPlane(knee - hip, direction);
                if (bend.sqrMagnitude < .0001f || Vector3.Dot(bend, forward) < 0f)
                    bend = Vector3.ProjectOnPlane(forward, direction);
                float kneeDistance = (upperLength * upperLength - lowerLength * lowerLength + length * length) / (2f * length);
                float kneeHeight = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - kneeDistance * kneeDistance));
                Vector3 desiredKnee = hip + direction * kneeDistance + bend.normalized * kneeHeight;
                Quaternion keepFoot = Foot.rotation;
                upper.rotation = Quaternion.FromToRotation(knee - hip, desiredKnee - hip) * upper.rotation;
                lower.rotation = Quaternion.FromToRotation(Foot.position - lower.position,
                    hip + direction * length - lower.position) * lower.rotation;
                Foot.rotation = keepFoot;
            }
        }
    }
}
