using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace HeavyPockets
{
    // The clips contain poses on the actual BaseBody bones. The motor owns the
    // root, including its jump height; the graph only animates the skeleton.
    internal sealed class FreightRunnerGait : MonoBehaviour
    {
        private enum Move { Idle, Walk, Back, Left, Right, Jog, Sprint, CrouchIdle, CrouchWalk,
            CrouchBack, CrouchLeft, CrouchRight, Takeoff, Air, Land, Count }

        private static AnimationClip[] clips;
        private readonly AnimationClipPlayable[] players = new AnimationClipPlayable[(int)Move.Count];
        private readonly float[] weights = new float[(int)Move.Count];
        private readonly PlantedFoot[] feet = new PlantedFoot[2];
        private Animator animator;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private Transform shadow;
        private Vector3 shadowScale, lastPosition, velocity;
        private float lastYaw, turnSpeed, phase, idleTime, crouch, liftSpeed, runBlend, airBlend, airTime, landingTime;
        private bool sprinting, airborne, wasAirborne;

        internal void Bind(SkinnedMeshRenderer skin)
        {
            Transform model = skin.transform;
            while (model.parent != transform) model = model.parent;
            animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.gameObject.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Transform Bone(string suffix) =>
                Array.Find(skin.bones, bone => bone.name.EndsWith(":" + suffix, StringComparison.Ordinal));
            for (int i = 0; i < feet.Length; i++)
            {
                string side = i == 0 ? "Left" : "Right";
                feet[i] = new PlantedFoot(transform, Bone(side + "UpLeg"), Bone(side + "Leg"), Bone(side + "Foot"), Bone(side + "ToeBase"));
            }
            shadow = transform.Find("Contact shadow");
            if (shadow != null) shadowScale = shadow.localScale;
            if (isActiveAndEnabled) ActivateRig();
        }

        internal void SetLocomotion(float crouch, bool sprinting, bool airborne, float verticalSpeed)
        {
            this.crouch = Mathf.Clamp01(crouch);
            this.sprinting = sprinting;
            this.airborne = airborne;
            liftSpeed = verticalSpeed;
        }

        private void OnEnable() { if (animator != null) ActivateRig(); }

        private void ActivateRig()
        {
            if (!graph.IsValid())
            {
                if (clips == null)
                {
                    clips = new AnimationClip[(int)Move.Count];
                    for (int i = 0; i < clips.Length; i++)
                        clips[i] = Resources.Load<AnimationClip>("FreightMotion/" + (Move)i);
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
            lastPosition = transform.position;
            lastYaw = transform.eulerAngles.y;
            velocity = Vector3.zero;
            turnSpeed = 0f;
            runBlend = 0f;
            landingTime = 1f;
            wasAirborne = airborne;
            airBlend = airborne ? 1f : 0f;
            foreach (var foot in feet) foot.Reset();
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
            float yaw = transform.eulerAngles.y;
            if (delta.sqrMagnitude > 1f || dt > .3f) { ActivateRig(); return; }
            float blend = 1f - Mathf.Exp(-dt * 14f);
            velocity = Vector3.Lerp(velocity, Vector3.ClampMagnitude(delta / dt, 6f), blend);
            turnSpeed = Mathf.Lerp(turnSpeed, Mathf.Clamp(Mathf.DeltaAngle(lastYaw, yaw) / dt, -270f, 270f), blend);
            lastPosition = position;
            lastYaw = yaw;
            Pose(dt, transform.InverseTransformDirection(velocity), delta.magnitude);
        }

        private void Pose(float dt, Vector3 motion, float distance)
        {
            Array.Clear(weights, 0, weights.Length);
            float speed = motion.magnitude;
            float pivot = Mathf.InverseLerp(30f, 160f, Mathf.Abs(turnSpeed)) * (1f - Mathf.Clamp01(speed));
            float stepping = Mathf.Max(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.05f, .85f, speed)), pivot * .76f);
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
            float jog = Mathf.InverseLerp(1.75f, 3.8f, speed);
            float stride = Mathf.Lerp(Mathf.Lerp(1.43f, 1.98f, jog), 2.67f, run);
            stride = Mathf.Lerp(stride, 1.05f, crouch);
            if (!airborne) phase = Mathf.Repeat(phase + distance / stride + dt * pivot * 1.05f, 1f);
            idleTime += dt;
            if (airborne && !wasAirborne) { airTime = 0f; landingTime = 1f; }
            if (!airborne && wasAirborne) landingTime = 0f;
            wasAirborne = airborne;
            airBlend = Mathf.MoveTowards(airBlend, airborne ? 1f : 0f, dt * (airborne ? 12f : 16f));
            airTime += dt;
            landingTime += dt;
            float land = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.06f,.28f,landingTime))) * .70f * (1f-airBlend);
            float ground = 1f - land - airBlend;
            float high = ground * (1f - crouch), low = ground * crouch;
            Set(Move.Idle, high * (1f - stepping), idleTime / clips[(int)Move.Idle].length);
            Set(Move.CrouchIdle, low * (1f - stepping), idleTime / clips[(int)Move.CrouchIdle].length);
            high *= stepping; low *= stepping;
            Set(Move.Walk, high * forward * (1f-jog)*(1f-run), phase);
            Set(Move.Jog, high * forward * jog*(1f-run), phase + .964f);
            Set(Move.Sprint, high * forward * run, phase + .857f);
            Set(Move.Back, high * back, phase);
            Set(direction.x < 0f ? Move.Left : Move.Right, high * side, phase);
            Set(Move.CrouchWalk, low * forward, phase);
            Set(Move.CrouchBack, low * back, phase);
            Set(direction.x < 0f ? Move.CrouchLeft : Move.CrouchRight, low * side, phase);
            float flight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.28f,.52f,airTime));
            Set(Move.Takeoff, airBlend * (1f - flight), Mathf.Lerp(.08f,.50f,Mathf.Clamp01(airTime/.36f)));
            Set(Move.Air, airBlend * flight, Mathf.Lerp(.1f,.68f,Mathf.InverseLerp(1.5f,-5f,liftSpeed)));
            Set(Move.Land, land, Mathf.Lerp(.03f, .54f, Mathf.Clamp01(landingTime / .28f)));
            for (int i = 0; i < weights.Length; i++) mixer.SetInputWeight(i, weights[i]);
            graph.Evaluate(.001f);
            foreach (var foot in feet)
                foot.Update(transform, !airborne && airBlend < .15f && landingTime > .09f, dt);
            if (shadow != null)
            {
                var p = shadow.position;
                shadow.position = new Vector3(p.x, .011f, p.z);
                shadow.localScale = shadowScale * Mathf.Lerp(1f, .56f, Mathf.Clamp01(transform.position.y));
            }
        }

        private void Set(Move move, float weight, float normalizedTime)
        {
            int i = (int)move;
            weights[i] = weight;
            if (weight > 0) players[i].SetTime(Mathf.Repeat(normalizedTime, 1f) * clips[i].length);
        }

        // Hold the authored support pose in world space between replicated root
        // samples. The animated knee supplies the bend plane and the original
        // ankle rotation preserves heel strike and toe-off.
        private sealed class PlantedFoot
        {
            private readonly Transform hip, knee, ankle, toe;
            private readonly Vector3 heelSole, toeSole, forefoot;
            private Vector3 anchor;
            private bool planted, useToe;
            private float influence;

            internal PlantedFoot(Transform actor, Transform hip, Transform knee, Transform ankle, Transform toe)
            {
                this.hip = hip; this.knee = knee; this.ankle = ankle; this.toe = toe;
                Vector3 heel = actor.InverseTransformPoint(ankle.position);
                Vector3 ball = actor.InverseTransformPoint(toe.position);
                heel.y = ball.y = .004f;
                heelSole = ankle.InverseTransformPoint(actor.TransformPoint(heel + Vector3.back * .044f));
                forefoot = ankle.InverseTransformPoint(actor.TransformPoint(ball));
                toeSole = toe.InverseTransformPoint(actor.TransformPoint(ball + Vector3.forward * .065f));
            }

            internal void Reset() { planted = false; influence = 0f; }

            internal void Update(Transform actor, bool grounded, float dt)
            {
                if (!grounded) { Reset(); return; }
                Vector3 heel = ankle.TransformPoint(heelSole);
                Vector3 tip = toe.TransformPoint(toeSole);
                float height = Mathf.Min(Mathf.Min(heel.y, tip.y), ankle.TransformPoint(forefoot).y) - actor.position.y;
                if (!planted && height < .018f)
                {
                    useToe = tip.y < heel.y;
                    anchor = useToe ? toe.position : ankle.position;
                    planted = true;
                }
                Vector3 correction = anchor - (useToe ? toe.position : ankle.position);
                correction.y = 0;
                if (height > .055f || correction.sqrMagnitude > .030f) planted = false;
                influence = Mathf.MoveTowards(influence, planted ? 1f : 0f, dt * 14f);
                float lift = Mathf.Clamp(.005f - height, 0f, .08f);
                if (influence <= 0f && lift <= 0f) return;
                Vector3 start = hip.position, joint = knee.position, end = ankle.position;
                Vector3 target = end + Vector3.ClampMagnitude(correction, .12f) * influence + Vector3.up * lift;
                float a = Vector3.Distance(start,joint), b = Vector3.Distance(joint,end);
                Vector3 direction = (target - start).normalized;
                float length = Mathf.Clamp(Vector3.Distance(start,target), Mathf.Abs(a-b)+.0001f, a+b-.0001f);
                float along = (a*a - b*b + length*length) / (2f * length);
                Vector3 bend = Vector3.ProjectOnPlane(joint-start, direction).normalized;
                Vector3 mid = start + direction*along + bend*Mathf.Sqrt(Mathf.Max(0f,a*a-along*along));
                Quaternion rotation = ankle.rotation;
                hip.rotation = Quaternion.FromToRotation(joint-start,mid-start) * hip.rotation;
                knee.rotation = Quaternion.FromToRotation(ankle.position-knee.position,start+direction*length-knee.position) * knee.rotation;
                ankle.rotation = rotation;
            }
        }
    }
}
