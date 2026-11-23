using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace HeavyPockets
{
    // Clip time follows the distance travelled by the raid motor. Jump and slide
    // poses consume authoritative movement state and never displace the capsule.
    internal sealed class FreightRunnerGait : MonoBehaviour
    {
        private enum Move { Idle, Walk, Back, Left, Right, Jog, Sprint, CrouchIdle, CrouchWalk,
            CrouchBack, CrouchLeft, CrouchRight, TurnLeft, TurnRight,
            Jump, RunJumpLeft, RunJumpRight, Land, SlideIn, Slide, SlideOut, Count }
        private enum Travel { Still, Longitudinal, Left, Right }

        private static AnimationClip[] clips;
        private static float sprintStride;
        private readonly AnimationClipPlayable[] players = new AnimationClipPlayable[(int)Move.Count];
        private readonly float[] weights = new float[(int)Move.Count];
        private Animator animator;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private readonly float[] gaitWeights = new float[(int)Move.Jump];
        private Transform spine, ribs, chest, neck, head;
        private Transform leftAnkle, rightAnkle;
        private RunnerFacing facing;
        private float lookHeading;
        private bool hasLookHeading;
        private Vector3 lastPosition, velocity;
        private float lastYaw, turnSpeed, phase, pivotPhase, idleTime, crouch, liftSpeed, runBlend, airBlend, landingTime;
        private Travel travel;
        private float leftPhaseOffset, rightPhaseOffset;
        private float travellingJump, slideTime, slideExit, entryWeight, slideYaw;
        private bool sprinting, airborne, wasAirborne, leftLeads, sliding, wasSliding;

        internal RunnerRig Rig { get; private set; }

        internal void Bind(RunnerRig rig)
        {
            Rig = rig;
            animator = rig.Animator;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            spine = rig.Bone("spine_01");
            ribs = rig.Bone("spine_02");
            chest = rig.Bone("spine_03");
            neck = rig.Bone("neck_01");
            head = rig.Bone("Head");
            leftAnkle = rig.Bone("foot_l");
            rightAnkle = rig.Bone("foot_r");
            if (isActiveAndEnabled) ActivateRig();
        }

        internal void SetLocomotion(float crouch, bool sprinting, bool airborne, float verticalSpeed, bool sliding = false)
        {
            this.crouch = Mathf.Clamp01(crouch);
            this.sprinting = sprinting;
            this.airborne = airborne;
            liftSpeed = verticalSpeed;
            this.sliding = sliding && !airborne;
        }

        internal void SetLookHeading(float heading, bool snap = false)
        {
            lookHeading = heading;
            hasLookHeading = true;
            if (snap || !isActiveAndEnabled)
            {
                facing.Reset(heading);
                transform.rotation = Quaternion.Euler(0f, heading, 0f);
            }
        }

        private void OnEnable() { if (animator != null) ActivateRig(); }

        private void ActivateRig()
        {
            if (!graph.IsValid())
            {
                if (clips == null)
                {
                    var loaded = new AnimationClip[(int)Move.Count];
                    for (int i = 0; i < loaded.Length; i++)
                    {
                        string path = "QuaterniusMotion/" + (Move)i;
                        loaded[i] = Resources.Load<AnimationClip>(path);
                        if (loaded[i] == null) throw new InvalidOperationException("Missing runner clip " + path);
                    }
                    var preferred = Resources.Load<AnimationClip>("RunnerPrivate/Sprint");
                    sprintStride = preferred != null ? 2.38f : 3.00f;
                    if (preferred != null) loaded[(int)Move.Sprint] = preferred;
                    clips = loaded;
                }
                graph = PlayableGraph.Create("Freight runner locomotion");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                mixer = AnimationMixerPlayable.Create(graph, clips.Length);
                var output = AnimationPlayableOutput.Create(graph, "Body", animator);
                output.SetSourcePlayable(mixer);
                for (int i = 0; i < clips.Length; i++)
                {
                    players[i] = AnimationClipPlayable.Create(graph, clips[i]);
                    players[i].SetSpeed(0);
                    graph.Connect(players[i], 0, mixer, i);
                }
                graph.Play();
            }
            if (hasLookHeading) transform.rotation = Quaternion.Euler(0f, lookHeading, 0f);
            facing.Reset(transform.eulerAngles.y);
            lastPosition = transform.position;
            lastYaw = transform.eulerAngles.y;
            velocity = Vector3.zero;
            turnSpeed = 0f;
            phase = pivotPhase = 0f;
            travel = Travel.Still;
            leftPhaseOffset = rightPhaseOffset = 0f;
            Array.Clear(gaitWeights, 0, gaitWeights.Length);
            gaitWeights[(int)Move.Idle] = 1f - crouch;
            gaitWeights[(int)Move.CrouchIdle] = crouch;
            runBlend = travellingJump = slideTime = entryWeight = 0f;
            slideExit = landingTime = 1f;
            wasSliding = false;
            wasAirborne = airborne;
            airBlend = airborne ? 1f : 0f;
            Pose(.001f, Vector3.zero, 0f);
        }

        private void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }

        private void LateUpdate()
        {
            if (!graph.IsValid()) return;
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;
            Vector3 position = transform.position;
            Vector3 delta = position - lastPosition;
            delta.y = 0f;
            float longestStep = .45f + 10f * dt;
            if (delta.sqrMagnitude > longestStep * longestStep) { ActivateRig(); return; }
            float blend = 1f - Mathf.Exp(-dt * 14f);
            velocity = Vector3.Lerp(velocity, Vector3.ClampMagnitude(delta / dt, 10f), blend);
            if (hasLookHeading)
            {
                if (sliding && !wasSliding) slideYaw = velocity.sqrMagnitude > .09f
                    ? Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg : facing.FeetYaw;
                if (sliding && velocity.sqrMagnitude > .09f)
                    slideYaw = Mathf.LerpAngle(slideYaw, Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg, blend);
                facing.Advance(sliding ? slideYaw : lookHeading, velocity.magnitude, airborne, dt);
                transform.rotation = Quaternion.Euler(0f, facing.FeetYaw, 0f);
            }
            float yaw = transform.eulerAngles.y;
            float yawDelta = Mathf.DeltaAngle(lastYaw, yaw);
            turnSpeed = Mathf.Lerp(turnSpeed, Mathf.Clamp(yawDelta / dt, -420f, 420f), blend);
            if (!airborne && !sliding) pivotPhase = Mathf.Repeat(pivotPhase + Mathf.Abs(yawDelta) / 67f, 1f);
            lastPosition = position;
            lastYaw = yaw;
            Pose(dt, transform.InverseTransformDirection(velocity), delta.magnitude);
        }

        private void Pose(float dt, Vector3 motion, float distance)
        {
            Array.Clear(weights, 0, weights.Length);
            float speed = motion.magnitude;
            float pivot = SmoothRange(12f, 100f, Mathf.Abs(turnSpeed)) * (1f - SmoothRange(.06f,.65f,speed)) * (1f-crouch);
            float stepping = SmoothRange(.035f, .71f, speed);
            Vector3 direction = speed > .04f ? motion / speed : Vector3.zero;
            float sum = Mathf.Abs(direction.x) + Mathf.Abs(direction.z);
            if (sum > .001f) direction /= sum;
            float forward = Mathf.Max(0f, direction.z);
            float back = Mathf.Max(0f, -direction.z);
            float side = Mathf.Abs(direction.x);
            if (sum < .001f) forward = 1f;
            float requestedRun = sprinting ? SmoothRange(1.95f, 4.25f, speed) : 0f;
            runBlend = Mathf.Lerp(runBlend, requestedRun, 1f - Mathf.Exp(-dt * 13f));
            float run = runBlend;
            float jog = SmoothRange(1.90f, 3.65f, speed);
            // Travel per full two-foot cycle.
            float forwardStride = Mathf.Lerp(Mathf.Lerp(1.31f, 1.97f, jog), sprintStride, run);
            float stride = forward * forwardStride + back * 1.04f + side * 1.08f;
            stride = Mathf.Lerp(stride, forward * 1.65f + (back + side) * .86f, crouch);
            if (!airborne && !sliding)
            {
                SelectLeadingFoot(motion, speed);
                phase = Mathf.Repeat(phase + distance / Mathf.Max(stride, .75f), 1f);
            }
            else
            {
                travel = Travel.Longitudinal;
                leftPhaseOffset = rightPhaseOffset = 0f;
            }
            idleTime += dt;
            if (airborne && !wasAirborne)
            {
                travellingJump = SmoothRange(.8f, 3f, speed);
                leftLeads = Vector3.Dot(leftAnkle.position - rightAnkle.position, transform.forward) >= 0f;
                landingTime = 1f;
            }
            if (!airborne && wasAirborne)
            {
                landingTime = 0f;
                if (travellingJump > .5f) phase = leftLeads ? .07f : .57f;
            }
            wasAirborne = airborne;
            airBlend = Mathf.MoveTowards(airBlend, airborne ? 1f : 0f, dt * (airborne ? 12f : 16f));
            landingTime += dt;
            float land = (1f - SmoothRange(.055f, Mathf.Lerp(.32f,.18f,travellingJump), landingTime))
                * Mathf.Lerp(.91f,.24f,travellingJump) * (1f-airBlend);
            float slideWeight = PoseSlide(dt);
            float ground = (1f - land - airBlend) * (1f-slideWeight);
            float high = (1f - crouch) * (1f-pivot), low = crouch;
            Set(Move.TurnLeft, turnSpeed < 0 ? pivot * (1f-crouch) : 0f, pivotPhase);
            Set(Move.TurnRight, turnSpeed >= 0 ? pivot * (1f-crouch) : 0f, pivotPhase);
            Set(Move.Idle, high * (1f - stepping), idleTime / clips[(int)Move.Idle].length);
            Set(Move.CrouchIdle, low * (1f - stepping), idleTime / clips[(int)Move.CrouchIdle].length);
            high *= stepping; low *= stepping;
            Set(Move.Walk, high * forward * (1f - jog) * (1f - run), phase);
            Set(Move.Jog, high * forward * jog * (1f - run), phase);
            Set(Move.Sprint, high * forward * run, phase);
            Set(Move.Back, high * back, phase);
            Set(Move.Left, high * Mathf.Max(0f, -direction.x), phase + leftPhaseOffset);
            Set(Move.Right, high * Mathf.Max(0f, direction.x), phase + rightPhaseOffset);
            Set(Move.CrouchWalk, low * forward, phase);
            Set(Move.CrouchBack, low * back, phase);
            Set(Move.CrouchLeft, low * Mathf.Max(0f, -direction.x), phase + leftPhaseOffset);
            Set(Move.CrouchRight, low * Mathf.Max(0f, direction.x), phase + rightPhaseOffset);
            // Ballistic time follows the motor on both the ascending and descending arc.
            float flight = airborne ? Mathf.InverseLerp(RaidLocomotion.JumpImpulse,-RaidLocomotion.JumpImpulse,liftSpeed) : 1f;
            float airWeight = airBlend * (1f-slideWeight);
            Set(Move.Jump, airWeight * (1f-travellingJump), Mathf.Min(.9999f,flight));
            Set(leftLeads ? Move.RunJumpLeft : Move.RunJumpRight, airWeight * travellingJump, Mathf.Min(.9999f,flight));
            Set(Move.Land, land * (1f-slideWeight), Mathf.Min(.999f,landingTime/.39f));
            // Respond quickly to a lateral push-off while keeping the outgoing pose in motion.
            float transition = 1f - Mathf.Exp(-dt * Mathf.Lerp(15f, 22f, side));
            for (int i = 0; i < gaitWeights.Length; i++)
            {
                gaitWeights[i] = Mathf.Lerp(gaitWeights[i], weights[i], transition);
                weights[i] = ground * gaitWeights[i];
            }
            for (int i = 0; i < weights.Length; i++) mixer.SetInputWeight(i, weights[i]);
            graph.Evaluate(.001f);
            if (hasLookHeading)
            {
                float twist = sliding ? Mathf.Clamp(Mathf.DeltaAngle(facing.FeetYaw,lookHeading),-35f,35f) : facing.LookTwist;
                ApplyLookTwist(twist);
            }
        }

        private void SelectLeadingFoot(Vector3 motion, float speed)
        {
            if (speed < .10f) travel = Travel.Still;
            if (speed < .18f) return;
            var next = Mathf.Abs(motion.x) >= Mathf.Abs(motion.z)
                ? (motion.x < 0f ? Travel.Left : Travel.Right)
                : Travel.Longitudinal;
            if (next != travel && next != Travel.Longitudinal && travel != Travel.Longitudinal)
            {
                // In the standing and crouched strafe clips these are the outward swings:
                // the leading ankle passes its idle X while the opposite foot is planted.
                float opening = next == Travel.Left ? .80f : .30f;
                float offset = phase - opening;
                leftPhaseOffset = next == Travel.Left ? 0f : Mathf.Repeat(leftPhaseOffset + offset, 1f);
                rightPhaseOffset = next == Travel.Right ? 0f : Mathf.Repeat(rightPhaseOffset + offset, 1f);
                phase = opening;
            }
            travel = next;
            // Preserve the clock of the outgoing strafe during the crossfade. Once it
            // disappears it can share the main clock again for a later diagonal blend.
            if (next != Travel.Left && gaitWeights[(int)Move.Left] + gaitWeights[(int)Move.CrouchLeft] < .001f)
                leftPhaseOffset = 0f;
            if (next != Travel.Right && gaitWeights[(int)Move.Right] + gaitWeights[(int)Move.CrouchRight] < .001f)
                rightPhaseOffset = 0f;
        }

        private float PoseSlide(float dt)
        {
            if (sliding && !wasSliding) { slideTime = 0f; slideExit = 0f; }
            if (!sliding && wasSliding) { entryWeight = SmoothRange(0f,.10f,slideTime); slideExit = 0f; }
            wasSliding = sliding;
            if (sliding)
            {
                slideTime += dt;
                float enter = Mathf.Min(.9999f,slideTime / clips[(int)Move.SlideIn].length);
                float loop = SmoothRange(.70f,1f,enter);
                float weight = SmoothRange(0f,.10f,slideTime);
                Set(Move.SlideIn,weight*(1f-loop),enter);
                Set(Move.Slide,weight*loop,(slideTime-.42f)/clips[(int)Move.Slide].length);
                return weight;
            }
            float exitLength = clips[(int)Move.SlideOut].length;
            if (slideExit >= exitLength || entryWeight <= 0f) return 0f;
            slideExit += dt;
            float progress = Mathf.Clamp01(slideExit/exitLength);
            float departing = entryWeight*(1f-SmoothRange(.36f,.96f,progress))*(1f-airBlend);
            float recover = SmoothRange(0f,.3f,progress);
            // A slide interrupted during entry returns from the pose it actually reached.
            float held = Mathf.Min(.9999f,slideTime/clips[(int)Move.SlideIn].length);
            float exitShare = recover * SmoothRange(.25f,.76f,held);
            Set(Move.SlideIn,departing*(1f-exitShare),held);
            Set(Move.SlideOut,departing*exitShare,Mathf.Min(.9999f,progress));
            return departing;
        }

        private void ApplyLookTwist(float angle)
        {
            // Rotate around the animated spine, so a crouched runner can look
            // to the side without sweeping the shoulders around the pelvis.
            Vector3 axis = (neck.position - spine.position).normalized;
            spine.rotation = Quaternion.AngleAxis(angle * .14f, axis) * spine.rotation;
            ribs.rotation = Quaternion.AngleAxis(angle * .22f, axis) * ribs.rotation;
            chest.rotation = Quaternion.AngleAxis(angle * .28f, axis) * chest.rotation;
            neck.rotation = Quaternion.AngleAxis(angle * .21f, Vector3.up) * neck.rotation;
            head.rotation = Quaternion.AngleAxis(angle * .15f, Vector3.up) * head.rotation;
        }

        private static float SmoothRange(float from, float to, float value) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));

        private void Set(Move move, float weight, float normalizedTime)
        {
            int i = (int)move;
            weights[i] = weight;
            players[i].SetTime(Mathf.Repeat(normalizedTime, 1f) * clips[i].length);
        }
    }
}
