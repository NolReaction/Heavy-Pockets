using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    [DefaultExecutionOrder(100)]
    internal sealed class RecruitSkin : MonoBehaviour
    {
        private static Mesh mesh;
        private static bool reportedError;
        private static Material[] materials;
        private static string[] jointNames;

        private static Dictionary<string, Transform> Hierarchy(Transform root)
        {
            var byName = new Dictionary<string, Transform>();
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
                byName[node.name] = node;
            return byName;
        }

        // Importers can rotate or scale the FBX root. Both rest rigs have the same fitted
        // shoulders and pelvis, so use those anatomical landmarks to align their spaces.
        private static Matrix4x4 RestFrame(Dictionary<string, Transform> joints)
        {
            var left = joints["upperarm_l"].position;
            var right = joints["upperarm_r"].position;
            var hips = joints["pelvis"].position;
            var across = left - right;
            var up = (left + right) * .5f - hips;
            return Matrix4x4.TRS(hips, Quaternion.LookRotation(Vector3.Cross(across, up), up),
                Vector3.one * across.magnitude);
        }

        private static void Prepare()
        {
            var template = Resources.Load<GameObject>("RaidRecruit");
            var source = template != null ? template.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
            if (source == null || source.sharedMesh == null || !source.sharedMesh.isReadable)
                throw new InvalidOperationException("Import RaidRecruit with a readable skinned mesh.");
            var original = Resources.Load<GameObject>(RunnerAppearance.ResourcePath);
            var rest = Hierarchy(original.transform);
            var alignment = RestFrame(rest) * RestFrame(Hierarchy(template.transform)).inverse;
            var meshToRest = alignment * source.transform.localToWorldMatrix;
            var bones = source.bones;
            var bindposes = new Matrix4x4[bones.Length];
            jointNames = new string[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) throw new InvalidOperationException("Missing imported skin bone at index " + i);
                jointNames[i] = bones[i].name;
                bindposes[i] = rest[jointNames[i]].worldToLocalMatrix * meshToRest;
            }
            materials = source.sharedMaterials;
            var rebound = UnityEngine.Object.Instantiate(source.sharedMesh);
            rebound.name = "Recruit on runner skeleton";
            rebound.bindposes = bindposes;
            mesh = rebound;
        }

        internal static SkinnedMeshRenderer Attach(Transform model)
        {
            Transform[] bones;
            Transform pelvis, headBase;
            Arm leftArm, rightArm;
            try
            {
                if (mesh == null) Prepare();
                var joints = Hierarchy(model);
                bones = new Transform[jointNames.Length];
                for (int i = 0; i < bones.Length; i++) bones[i] = joints[jointNames[i]];
                pelvis = joints["pelvis"];
                headBase = joints["neck_01"];
                leftArm = new Arm(joints, "_l", "_L");
                rightArm = new Arm(joints, "_r", "_R");
            }
            catch (Exception error) when (error is InvalidOperationException || error is KeyNotFoundException || error is UnityException)
            {
                if (!reportedError) Debug.LogWarning("Recruit skin unavailable. Using the modular runner. " + error.Message);
                reportedError = true;
                return null;
            }

            var body = new GameObject("Recruit");
            body.layer = model.gameObject.layer;
            body.transform.SetParent(model, false);
            var renderer = body.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.rootBone = model;
            renderer.sharedMaterials = materials;
            // Keep the entire upright, crouching and sliding runner in view.
            renderer.localBounds = new Bounds(Vector3.up * .9f, new Vector3(2.4f, 2.4f, 3f));
            var correctives = body.AddComponent<RecruitSkin>();
            correctives.skin = renderer;
            correctives.hips = pelvis;
            correctives.neck = headBase;
            correctives.left = leftArm;
            correctives.right = rightArm;
            return renderer;
        }

        private SkinnedMeshRenderer skin;
        private Transform hips, neck;
        private Arm left, right;

        private void LateUpdate()
        {
            Vector3 up = (neck.position - hips.position).normalized;
            left.Apply(skin, up);
            right.Apply(skin, up);
        }

        // FBX preserves the shape keys but does not execute the source Blender drivers.
        private struct Arm
        {
            private readonly Transform shoulder, elbow, wrist;
            private readonly int shoulderDown, axillaContact, elbowFlex;

            public Arm(Dictionary<string, Transform> joints, string boneSuffix, string shapeSuffix)
            {
                shoulder = joints["upperarm" + boneSuffix];
                elbow = joints["lowerarm" + boneSuffix];
                wrist = joints["hand" + boneSuffix];
                shoulderDown = Shape("ShoulderDown" + shapeSuffix);
                axillaContact = Shape("AxillaContact" + shapeSuffix);
                elbowFlex = Shape("ElbowFlex" + shapeSuffix);
            }

            private static int Shape(string name)
            {
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string key = mesh.GetBlendShapeName(i);
                    if (key == name || key.EndsWith("." + name, StringComparison.Ordinal)) return i;
                }
                return -1;
            }

            public void Apply(SkinnedMeshRenderer target, Vector3 up)
            {
                Vector3 upper = (elbow.position - shoulder.position).normalized;
                float down = -Vector3.Dot(upper, up);
                float lowered = Mathf.InverseLerp(.045f, .990f, down);
                float contact = Mathf.InverseLerp(1.20951316f, Mathf.PI * .5f,
                    Mathf.Asin(Mathf.Clamp01(1.00366f * down)));
                float bend = Mathf.InverseLerp(20f, 125f, Vector3.Angle(upper, wrist.position - elbow.position));
                if (shoulderDown >= 0) target.SetBlendShapeWeight(shoulderDown, Mathf.SmoothStep(0f, 100f, lowered));
                if (axillaContact >= 0) target.SetBlendShapeWeight(axillaContact, 100f * contact * contact);
                if (elbowFlex >= 0) target.SetBlendShapeWeight(elbowFlex, Mathf.SmoothStep(0f, 100f, bend));
            }
        }
    }
}
