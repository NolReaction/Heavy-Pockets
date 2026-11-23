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
        private readonly float[] gaitWeights = new float[(int)Move.Takeoff];
        private Transform[] upperBody;
        private Quaternion[] upperPose;
        private Transform spine, ribs, chest, neck, head;
        private RunnerFacing facing;
        private float lookHeading;
        private bool hasLookHeading;
        private Vector3 modelOrigin, lastPosition, velocity;
        private float lastYaw, turnSpeed, phase, cadence, idleTime, crouch, liftSpeed, runBlend, airBlend, airTime, landingTime;
        private float bodyOffset, bodyOffsetSpeed;
        private bool hasPose;
        private bool sprinting, airborne, wasAirborne;

        internal void Bind(SkinnedMeshRenderer skin)
        {
            Transform model = skin.transform;
            while (model.parent != transform) model = model.parent;
            animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.gameObject.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            modelOrigin = model.localPosition;
            Transform Bone(string suffix) =>
                Array.Find(skin.bones, bone => bone.name.EndsWith(":" + suffix, StringComparison.Ordinal));
            spine = Bone("Spine");
            ribs = Bone("Spine1");
            chest = Bone("Spine2");
            neck = Bone("Neck");
            head = Bone("Head");
            upperBody = Array.FindAll(skin.bones, bone => bone.name.Contains(":Spine") || bone.name.EndsWith(":Head") ||
                bone.name.EndsWith("Arm") || bone.name.EndsWith("Shoulder"));
            upperPose = new Quaternion[upperBody.Length];
            // Bake once in the bind pose. These sole points follow this model's actual mesh even when it rolls onto the toes.
            var mesh = new Mesh();
            skin.BakeMesh(mesh);
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = transform.InverseTransformPoint(skin.transform.TransformPoint(vertices[i]));
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
            for (int i = 0; i < feet.Length; i++)
            {
                string side = i == 0 ? "Left" : "Right";
                feet[i] = new PlantedFoot(transform, Bone(side + "UpLeg"), Bone(side + "Leg"), Bone(side + "Foot"), Bone(side + "ToeBase"), vertices);
            }
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
            if (hasLookHeading) transform.rotation = Quaternion.Euler(0f, lookHeading, 0f);
            facing.Reset(transform.eulerAngles.y);
            lastPosition = transform.position;
            lastYaw = transform.eulerAngles.y;
            velocity = Vector3.zero;
            turnSpeed = 0f;
            phase = cadence = bodyOffset = bodyOffsetSpeed = 0f;
            animator.transform.localPosition = modelOrigin;
            Array.Clear(gaitWeights, 0, gaitWeights.Length);
            gaitWeights[(int)Move.Idle] = 1f - crouch;
            gaitWeights[(int)Move.CrouchIdle] = crouch;
            hasPose = false;
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
            // The authored side-together steps cover considerably less ground
            // per cycle than a forward step. Use their measured stance travel.
            float forwardStride = Mathf.Lerp(Mathf.Lerp(1.47f, 2.50f, jog), 2.60f, run);
            float sideStride = Mathf.Lerp(.83f, .74f, Mathf.InverseLerp(-1f, 1f, direction.x));
            float stride = forward * forwardStride + back * 1.03f + side * sideStride;
            stride = Mathf.Lerp(stride, forward * 1.33f + back * 1.07f + side * .81f, crouch);
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
            // Both cycles use the same contact phase. Keep some of the weight-bearing
            // walking pose as the runner builds speed.
            Set(Move.Walk, high * forward * ((1f-jog)*(1f-run) + .10f*run), phase);
            Set(Move.Jog, high * forward * jog*(1f-run), phase + .964f);
            Set(Move.Sprint, high * forward * .90f * run, phase + .857f);
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
            animator.transform.localPosition = modelOrigin;
            graph.Evaluate(.001f);
            float follow = 1f - Mathf.Exp(-dt * 32f);
            for (int i = 0; i < upperBody.Length; i++)
            {
                Quaternion rotation = upperBody[i].localRotation;
                upperPose[i] = hasPose ? Quaternion.Slerp(upperPose[i], rotation, follow) : rotation;
                upperBody[i].localRotation = upperPose[i];
            }
            if (hasLookHeading) ApplyLookTwist(facing.LookTwist);
            GroundFeet(dt, run, !airborne, motion);
            hasPose = true;
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

        private void GroundFeet(float dt, float run, bool grounded, Vector3 motion)
        {
            float floor = transform.position.y;
            float low = Mathf.Min(feet[0].Measure(), feet[1].Measure()) - floor;
            // Preserve a short natural flight at a full sprint. For walking and
            // crouching at least one foot carries the body's weight throughout.
            float clearance = .002f + run * .027f * SmoothRange(.035f, .16f, low);
            float desired = grounded ? Mathf.Clamp(clearance - low, -.27f, .10f) : 0f;
            bodyOffset = hasPose
                ? Mathf.SmoothDamp(bodyOffset, desired, ref bodyOffsetSpeed, .083f, Mathf.Infinity, dt)
                : desired;
            // The skeleton can lower a planted ankle only as far as the leg can reach.
            if (grounded) bodyOffset = Mathf.Min(bodyOffset, desired + .040f);
            animator.transform.localPosition = modelOrigin + Vector3.up * bodyOffset;
            foreach (var foot in feet)
                foot.Solve(transform, floor + clearance, low + floor, bodyOffset, grounded, gaitWeights[(int)Move.Sprint], crouch, motion, dt);
        }

        private void Set(Move move, float weight, float normalizedTime)
        {
            int i = (int)move;
            weights[i] = weight;
            players[i].SetTime(Mathf.Repeat(normalizedTime, 1f) * clips[i].length);
        }

        // The lowest point moves from heel to ball to toe through a step. Remember
        // the point at contact so rolling around that point preserves the animation.
        private sealed class PlantedFoot
        {
            private readonly Transform hip, knee, ankle, toe;
            private readonly Vector3[] sole = new Vector3[15];
            private readonly Transform[] joints = new Transform[15];
            private readonly float outwardSign;
            private Vector3 anchor, previousLocalAnkle;
            private bool planted, havePreviousAnkle;
            private int lowestPoint, contactPoint;
            private float height, influence;

            internal PlantedFoot(Transform actor, Transform hip, Transform knee, Transform ankle, Transform toe, Vector3[] vertices)
            {
                this.hip = hip; this.knee = knee; this.ankle = ankle; this.toe = toe;
                Vector3 heel = actor.InverseTransformPoint(ankle.position);
                Vector3 ball = actor.InverseTransformPoint(toe.position);
                outwardSign = Mathf.Sign(heel.x - actor.InverseTransformPoint(hip.parent.position).x);
                float[] best = new float[sole.Length];
                for (int i = 0; i < sole.Length; i++) best[i] = float.PositiveInfinity;
                foreach (Vector3 vertex in vertices)
                {
                    if (Mathf.Sign(vertex.x) != Mathf.Sign(heel.x) || vertex.y > heel.y + .025f) continue;
                    for (int i = 0; i < sole.Length; i++)
                    {
                        // Lower silhouette for five pitches (heel strike through toe-off)
                        // and three sideways rolls, including both edges of the bare foot.
                        float score = vertex.y + (i / 3 - 2) * .68f * vertex.z + (i % 3 - 1) * .48f * vertex.x;
                        if (score >= best[i]) continue;
                        best[i] = score;
                        sole[i] = vertex;
                    }
                }
                for (int i = 0; i < sole.Length; i++)
                {
                    joints[i] = sole[i].z >= ball.z ? toe : ankle;
                    sole[i] = joints[i].InverseTransformPoint(actor.TransformPoint(sole[i]));
                }
            }

            internal void Reset() { planted = havePreviousAnkle = false; influence = 0f; }

            internal float Measure()
            {
                height = float.PositiveInfinity;
                for (int i = 0; i < sole.Length; i++)
                {
                    float y = joints[i].TransformPoint(sole[i]).y;
                    if (y >= height) continue;
                    height = y;
                    lowestPoint = i;
                }
                return height;
            }

            internal void Solve(Transform actor, float floor, float lowestSole, float offset, bool grounded, float sprint, float crouch, Vector3 motion, float dt)
            {
                if (!grounded) { Reset(); return; }
                float support = 1f - SmoothRange(.012f, .067f, height - lowestSole);
                // Release the contact as the ankle starts swinging forward relative to the hip.
                // Waiting for toe height alone holds a low swing in place, then pulls it ahead.
                Vector3 localAnkle = actor.InverseTransformPoint(ankle.position);
                float swing = havePreviousAnkle
                    ? Vector3.Dot((localAnkle - previousLocalAnkle) / dt, motion) / Mathf.Max(.36f, motion.sqrMagnitude)
                    : 0f;
                previousLocalAnkle = localAnkle;
                havePreviousAnkle = true;
                float stance = support * (1f - SmoothRange(.12f, .75f, swing));
                if (!planted && stance > .86f)
                {
                    contactPoint = lowestPoint;
                    anchor = joints[contactPoint].TransformPoint(sole[contactPoint]);
                    planted = true;
                }
                else if (stance < .15f) planted = false;
                Vector3 pivot = joints[contactPoint].TransformPoint(sole[contactPoint]);
                if (planted && pivot.y - (height + offset) > .016f)
                {
                    // Transfer support from the heel to the ball without moving the
                    // skeleton on this frame. Subsequent toe roll pivots at the ball.
                    contactPoint = lowestPoint;
                    Vector3 next = joints[contactPoint].TransformPoint(sole[contactPoint]);
                    anchor += next - pivot;
                    pivot = next;
                }
                influence = Mathf.Lerp(influence, planted ? stance : 0f, 1f - Mathf.Exp(-dt * (planted ? 40f : 24f)));
                Vector3 correction = Vector3.ProjectOnPlane(anchor - pivot, Vector3.up);
                correction = Vector3.ClampMagnitude(correction, .14f + .10f * crouch + .07f * sprint);
                anchor = pivot + correction;
                correction *= influence;
                float penetration = floor - (height + offset);
                correction.y = Mathf.Clamp(penetration * (penetration < 0f ? support : 1f), -.11f, .11f);
                // A planted foot can get dragged through the other ankle when side-stepping.
                // Ease it out of the lock before it reaches the centre of the pelvis.
                Vector3 outward = actor.right * outwardSign;
                float lane = Vector3.Dot(ankle.position + correction - hip.parent.position, outward);
                const float minimumLane = .058f, margin = .025f;
                if (lane < minimumLane + margin)
                {
                    float t = Mathf.Clamp01((lane - minimumLane + margin) / (2f * margin));
                    correction += outward * (minimumLane + margin * t * t - lane);
                }
                // In a low side-step, roll off the trailing foot before the pelvis
                // travels beyond its comfortable lateral reach.
                float sideStep = crouch * SmoothRange(.35f, .85f, Mathf.Abs(motion.x) / Mathf.Max(.1f, motion.magnitude));
                float outside = Mathf.Max(0f, lane - .321f);
                correction -= outward * (sideStep * outside * SmoothRange(0f, .045f, outside));
                if (correction.sqrMagnitude < .0000001f) return;

                Vector3 start = hip.position, joint = knee.position, end = ankle.position;
                Vector3 target = end + correction;
                float a = Vector3.Distance(start,joint), b = Vector3.Distance(joint,end);
                Vector3 direction = (target - start).normalized;
                float length = Mathf.Clamp(Vector3.Distance(start,target), Mathf.Abs(a-b)+.0001f, a+b-.001f);
                float along = (a*a - b*b + length*length) / (2f * length);
                Vector3 bend = Vector3.ProjectOnPlane(joint-start, direction);
                if (bend.sqrMagnitude < .000001f) bend = Vector3.ProjectOnPlane(actor.forward, direction);
                Vector3 mid = start + direction*along + bend.normalized*Mathf.Sqrt(Mathf.Max(0f,a*a-along*along));
                Quaternion rotation = ankle.rotation;
                hip.rotation = Quaternion.FromToRotation(joint-start,mid-start) * hip.rotation;
                knee.rotation = Quaternion.FromToRotation(ankle.position-knee.position,start+direction*length-knee.position) * knee.rotation;
                ankle.rotation = rotation;
            }
        }
    }
}
