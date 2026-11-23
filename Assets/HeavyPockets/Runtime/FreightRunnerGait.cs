using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace HeavyPockets
{
    // The visual skeleton plays in-place Quaternius clips. The raid motor moves the actor.
    internal sealed class FreightRunnerGait : MonoBehaviour
    {
        private enum Move { Idle, Walk, Back, Left, Right, Jog, Sprint, CrouchIdle, CrouchWalk,
            CrouchBack, CrouchLeft, CrouchRight, Takeoff, Air, Land, Count }

        private static AnimationClip[] clips;
        private readonly AnimationClipPlayable[] players = new AnimationClipPlayable[(int)Move.Count];
        private readonly float[] weights = new float[(int)Move.Count];
        private Animator animator;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private readonly float[] gaitWeights = new float[(int)Move.Takeoff];
        private Transform spine, ribs, chest, neck, head;
        private RunnerFacing facing;
        private float lookHeading;
        private bool hasLookHeading;
        private Vector3 lastPosition, velocity;
        private float lastYaw, turnSpeed, phase, cadence, idleTime, crouch, liftSpeed, runBlend, airBlend, airTime, landingTime;
        private bool sprinting, airborne, wasAirborne;

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
            if (isActiveAndEnabled) ActivateRig();
        }

        internal void SetLocomotion(float crouch, bool sprinting, bool airborne, float verticalSpeed)
        {
            this.crouch = Mathf.Clamp01(crouch);
            this.sprinting = sprinting;
            this.airborne = airborne;
            liftSpeed = verticalSpeed;
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
            phase = cadence = 0f;
            Array.Clear(gaitWeights, 0, gaitWeights.Length);
            gaitWeights[(int)Move.Idle] = 1f - crouch;
            gaitWeights[(int)Move.CrouchIdle] = crouch;
            runBlend = 0f;
            landingTime = 1f;
            wasAirborne = airborne;
            // A runner can first become visible partway through a jump.
            airTime = airborne ? Mathf.Max(0f, (RaidLocomotion.JumpImpulse - liftSpeed) / RaidLocomotion.Gravity) : 0f;
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
            float longestStep = .45f + 6f * dt;
            if (delta.sqrMagnitude > longestStep * longestStep) { ActivateRig(); return; }
            float blend = 1f - Mathf.Exp(-dt * 14f);
            velocity = Vector3.Lerp(velocity, Vector3.ClampMagnitude(delta / dt, 6f), blend);
            if (hasLookHeading)
            {
                facing.Advance(lookHeading, velocity.magnitude, airborne, dt);
                transform.rotation = Quaternion.Euler(0f, facing.FeetYaw, 0f);
            }
            float yaw = transform.eulerAngles.y;
            turnSpeed = Mathf.Lerp(turnSpeed, Mathf.Clamp(Mathf.DeltaAngle(lastYaw, yaw) / dt, -270f, 270f), blend);
            lastPosition = position;
            lastYaw = yaw;
            Pose(dt, transform.InverseTransformDirection(velocity), delta.magnitude);
        }

        private void Pose(float dt, Vector3 motion, float distance)
        {
            Array.Clear(weights, 0, weights.Length);
            float speed = motion.magnitude;
            float pivot = Mathf.InverseLerp(40f, 185f, Mathf.Abs(turnSpeed)) * (1f - Mathf.Clamp01(speed));
            float stepping = Mathf.Max(SmoothRange(.035f, .92f, speed), pivot * .62f);
            Vector3 direction = speed > .04f ? motion / speed : Vector3.zero;
            direction.x += Mathf.Sign(turnSpeed) * pivot;
            float sum = Mathf.Abs(direction.x) + Mathf.Abs(direction.z);
            if (sum > .001f) direction /= sum;
            float forward = Mathf.Max(0f, direction.z);
            float back = Mathf.Max(0f, -direction.z);
            float side = Mathf.Abs(direction.x);
            if (sum < .001f) forward = 1f;
            float requestedRun = sprinting ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, 4.65f, speed)) : 0f;
            runBlend = Mathf.Lerp(runBlend, requestedRun, 1f - Mathf.Exp(-dt * 13f));
            float run = runBlend;
            float jog = SmoothRange(1.90f, 3.65f, speed);
            // Travel per full two-foot cycle. All locomotion clips start on the same foot.
            float forwardStride = Mathf.Lerp(Mathf.Lerp(1.31f, 1.97f, jog), 3.00f, run);
            float stride = forward * forwardStride + back + side * 1.14f;
            stride = Mathf.Lerp(stride, forward * 1.65f + back * 1.22f + side * 1.30f, crouch);
            float targetCadence = Mathf.Min(distance / dt, 6f) / Mathf.Max(stride, .75f);
            cadence = Mathf.Lerp(cadence, targetCadence + pivot * .80f, 1f - Mathf.Exp(-dt * 16f));
            if (!airborne) phase = Mathf.Repeat(phase + cadence * dt, 1f);
            idleTime += dt;
            if (airborne && !wasAirborne) { airTime = 0f; landingTime = 1f; }
            if (!airborne && wasAirborne) landingTime = 0f;
            wasAirborne = airborne;
            airBlend = Mathf.MoveTowards(airBlend, airborne ? 1f : 0f, dt * (airborne ? 12f : 16f));
            airTime += dt;
            landingTime += dt;
            float land = (1f - SmoothRange(.075f, .35f, landingTime)) * .72f * (1f-airBlend);
            // Carry part of the current stride into a travelling jump. Its lead
            // leg and opposing arm then remain continuous across toe-off.
            float strideCarry = airBlend * SmoothRange(.35f, 3.2f, speed) * .23f;
            float jumpWeight = airBlend - strideCarry;
            float ground = 1f - land - jumpWeight;
            float high = 1f - crouch, low = crouch;
            Set(Move.Idle, high * (1f - stepping), idleTime / clips[(int)Move.Idle].length);
            Set(Move.CrouchIdle, low * (1f - stepping), idleTime / clips[(int)Move.CrouchIdle].length);
            high *= stepping; low *= stepping;
            Set(Move.Walk, high * forward * (1f - jog) * (1f - run), phase);
            Set(Move.Jog, high * forward * jog * (1f - run), phase);
            Set(Move.Sprint, high * forward * run, phase);
            Set(Move.Back, high * back, phase);
            Set(Move.Left, high * Mathf.Max(0f, -direction.x), phase);
            Set(Move.Right, high * Mathf.Max(0f, direction.x), phase);
            Set(Move.CrouchWalk, low * forward, phase);
            Set(Move.CrouchBack, low * back, phase);
            Set(Move.CrouchLeft, low * Mathf.Max(0f, -direction.x), phase);
            Set(Move.CrouchRight, low * Mathf.Max(0f, direction.x), phase);
            float flight = SmoothRange(.18f, .29f, airTime);
            // Reach towards the floor before contact; roll down and absorb the
            // impact through ankles, knees and pelvis once the motor lands.
            float approach = !airborne ? 1f : liftSpeed < -.1f
                ? 1f - SmoothRange(.015f, .17f, Mathf.Max(0f, transform.position.y) / -liftSpeed) : 0f;
            float airbornePose = jumpWeight * (1f - approach);
            Set(Move.Takeoff, airbornePose * (1f - flight), Mathf.Min(.999f, airTime / .29f));
            Set(Move.Air, airbornePose * flight, .999f * Mathf.InverseLerp(.75f, -4.1f, liftSpeed));
            Set(Move.Land, land + jumpWeight * approach, airborne ? 0f : Mathf.Min(.999f, landingTime / .42f));
            // Direction changes keep both anatomical feet on a shared phase. Fade the
            // outgoing clips as they continue to play, even across a full reversal.
            float transition = 1f - Mathf.Exp(-dt * 15f);
            for (int i = 0; i < gaitWeights.Length; i++)
            {
                gaitWeights[i] = Mathf.Lerp(gaitWeights[i], weights[i], transition);
                weights[i] = ground * gaitWeights[i];
            }
            for (int i = 0; i < weights.Length; i++) mixer.SetInputWeight(i, weights[i]);
            graph.Evaluate(.001f);
            if (hasLookHeading) ApplyLookTwist(facing.LookTwist);
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
