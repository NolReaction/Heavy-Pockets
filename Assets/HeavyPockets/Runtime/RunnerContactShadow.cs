using UnityEngine;

namespace HeavyPockets
{
    // Sample the final leg pose after the gait's LateUpdate, including foot IK.
    [DefaultExecutionOrder(100)]
    internal sealed class RunnerContactShadow : MonoBehaviour
    {
        private static readonly int Body = Shader.PropertyToID("_Body");
        private static readonly int LeftHeel = Shader.PropertyToID("_LeftHeel");
        private static readonly int LeftToe = Shader.PropertyToID("_LeftToe");
        private static readonly int RightHeel = Shader.PropertyToID("_RightHeel");
        private static readonly int RightToe = Shader.PropertyToID("_RightToe");
        private MaterialPropertyBlock properties;
        private Renderer decal;
        private Sole left, right;

        internal void Bind(RunnerRig rig)
        {
            properties = new MaterialPropertyBlock();
            decal = GetComponent<Renderer>();
            left = new Sole(rig.Bone("foot_l"), rig.Bone("ball_l"), transform.parent);
            right = new Sole(rig.Bone("foot_r"), rig.Bone("ball_r"), transform.parent);
        }

        private void LateUpdate()
        {
            Vector3 position = transform.parent.position;
            float flight = Mathf.Clamp01(position.y);
            transform.position = new Vector3(position.x, .011f, position.z);
            // The broad body shadow softens during flight. The two narrower shadows
            // fade independently as the heel rolls up and the toe leaves the floor.
            properties.SetVector(Body, new Vector4(2.6f - flight * .4f, 3.2f - flight * .5f,
                Mathf.Lerp(.21f, .07f, flight), 0f));
            left.Write(properties, LeftHeel, LeftToe);
            right.Write(properties, RightHeel, RightToe);
            decal.SetPropertyBlock(properties);
        }

        private sealed class Sole
        {
            private readonly Transform ankle, toe;
            private readonly Vector3 heelLocal, toeLocal;

            internal Sole(Transform ankle, Transform toe, Transform actor)
            {
                this.ankle = ankle;
                this.toe = toe;
                Vector3 forward = Vector3.ProjectOnPlane(toe.position - ankle.position, Vector3.up).normalized;
                Vector3 heel = ankle.position - forward * .035f;
                Vector3 tip = toe.position + forward * .048f;
                heel.y = tip.y = actor.position.y;
                heelLocal = ankle.InverseTransformPoint(heel);
                toeLocal = toe.InverseTransformPoint(tip);
            }

            internal void Write(MaterialPropertyBlock block, int heelId, int toeId)
            {
                block.SetVector(heelId, Project(ankle.TransformPoint(heelLocal)));
                block.SetVector(toeId, Project(toe.TransformPoint(toeLocal)));
            }

            private static Vector4 Project(Vector3 point)
            {
                float lift = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.012f, .24f, point.y));
                return new Vector4(point.x, point.z, Mathf.Lerp(.082f, .14f, lift), .46f * (1f - lift));
            }
        }
    }
}
