#if (DEVELOPMENT_BUILD || UNITY_EDITOR) && !UNITY_SERVER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace HeavyPockets
{
    public sealed partial class RaidClient
    {
        internal Transform ProbeLocalBody => ownModel;
    }

    public sealed partial class RaidProbe
    {
        // Film the same network-driven character and rig as the playable client.
        // A separate camera keeps the feet in frame without changing the player's view.
        private IEnumerator CaptureLocomotion()
        {
            if (!Array.Exists(Environment.GetCommandLineArgs(), a => a == "-probeMotion")) yield break;
            // Keep the entire stride and its floor contact in view, clear of the second player.
            yield return Walk(new Vector2(-4.95f, -2.6f), 90f);
            Phase("recording locomotion");
            string path = Path.Combine(directory, "motion");
            Directory.CreateDirectory(path);
            var cameraObject = new GameObject("Locomotion review camera");
            Camera sample = cameraObject.AddComponent<Camera>();
            sample.CopyFrom(Camera.main);
            sample.enabled = false;
            const int width = 768, height = 640;
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            sample.targetTexture = target;
            sample.aspect = (float)width / height;
            sample.fieldOfView = 46f;
            var rect = new Rect(0, 0, width, height);
            Vector2 previous = new Vector2(9f, 9f);
            float nextInput = 0f;
            MoveButtons previousButtons = MoveButtons.None;
            bool standingJump = false, runningJump = false;
            int frame = 0;
            float start = Time.unscaledTime;
            var endOfFrame = new WaitForEndOfFrame();
            var skin = Client.ProbeLocalBody.GetComponentInChildren<SkinnedMeshRenderer>();
            Transform Bone(string name) => Array.Find(skin.bones, b => b.name.EndsWith(":" + name, StringComparison.Ordinal));
            var hips = Bone("Hips");
            var chest = Bone("Spine2");
            var leftHip = Bone("LeftUpLeg");
            var rightHip = Bone("RightUpLeg");
            var leftKnee = Bone("LeftLeg");
            var rightKnee = Bone("RightLeg");
            var leftFoot = Bone("LeftFoot");
            var rightFoot = Bone("RightFoot");
            var leftArm = Bone("LeftArm");
            var rightArm = Bone("RightArm");
            var leftHand = Bone("LeftHand");
            var rightHand = Bone("RightHand");
            var baked = new Mesh();
            var vertices = new List<Vector3>(skin.sharedMesh.vertexCount);
            float nextSoleSample = 2.12f, maxGap = 0f;
            int sprintSamples = 0;
            try
            {
                using (var times = new StreamWriter(Path.Combine(path, "frames.txt")))
                using (var poses = new StreamWriter(Path.Combine(path, "poses.csv")))
                {
                    poses.WriteLine("time,look,pelvisHeading,chestHeading,ankleWidth,kneeWidth,handWidth,torsoPitch,rootHeight");
                    while (Time.unscaledTime - start < 12.85f)
                    {
                        yield return endOfFrame;
                        float t = Time.unscaledTime - start;
                        Vector2 direction = Vector2.zero;
                        if (t >= .35f && t < 1.6f) direction = Vector2.right * .85f;
                        if (t >= 1.6f && t < 2.95f) direction = Vector2.right;
                        if (t >= 3.35f && t < 4.75f) direction = Vector2.left * .90f;
                        if (t >= 5f && t < 5.95f) direction = Vector2.up * .8f;
                        if (t >= 7.65f && t < 9.18f) direction = Vector2.right * .85f;
                        MoveButtons buttons = t >= 1.6f && t < 2.95f ? MoveButtons.Sprint :
                            t >= 4.95f && t < 5.95f ? MoveButtons.Crouch : MoveButtons.None;
                        if (!standingJump && t >= 6.50f) { buttons |= MoveButtons.Jump; standingJump = true; }
                        if (!runningJump && t >= 8.15f) { buttons |= MoveButtons.Jump; runningJump = true; }
                        float heading = 90f + 18f * Ramp(9.50f, 9.84f, t) + 52f * Ramp(10.27f, 10.77f, t)
                            - 85f * Ramp(11.45f, 12.10f, t);
                        if (buttons != previousButtons || direction != previous || t >= nextInput)
                        {
                            Client.SetProbeMotion(direction, heading, 3f, buttons);
                            previous = direction;
                            previousButtons = buttons;
                            nextInput = t + .06f;
                        }
                        if (t >= nextSoleSample && t < 2.82f && Client.OwnMotion.Sprinting)
                        {
                            // Measure the rendered skin: a valid motor height can still leave both soles in the air.
                            skin.BakeMesh(baked);
                            baked.GetVertices(vertices);
                            float lowest = float.PositiveInfinity;
                            Matrix4x4 toWorld = skin.localToWorldMatrix;
                            foreach (Vector3 vertex in vertices)
                                lowest = Mathf.Min(lowest, toWorld.MultiplyPoint3x4(vertex).y);
                            maxGap = Mathf.Max(maxGap, lowest);
                            sprintSamples++;
                            nextSoleSample = t + .033f;
                        }
                        Transform body = Client.ProbeLocalBody;
                        Vector3 acrossHips = rightHip.position - leftHip.position;
                        Vector3 acrossChest = rightArm.position - leftArm.position;
                        Vector3 torso = chest.position - hips.position;
                        float ankleWidth = Vector3.Dot(rightFoot.position - leftFoot.position, body.right);
                        float kneeWidth = Vector3.Dot(rightKnee.position - leftKnee.position, body.right);
                        float handWidth = Vector3.Dot(rightHand.position - leftHand.position, body.right);
                        float torsoPitch = Mathf.Atan2(Vector3.Dot(torso, body.forward), torso.y) * Mathf.Rad2Deg;
                        poses.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "{0:F4},{1:F2},{2:F2},{3:F2},{4:F4},{5:F4},{6:F4},{7:F2},{8:F4}",
                            t, heading, HeadingFromRight(acrossHips), HeadingFromRight(acrossChest),
                            ankleWidth, kneeWidth, handWidth, torsoPitch, body.position.y));
                        Vector3 focus = body.position;
                        focus.y = .96f + .72f * focus.y;
                        // Rear view shows foot lanes and turning; rotate to three-quarter profile for the backstep.
                        Vector3 rear = new Vector3(-2.70f, .66f, -.55f);
                        Vector3 profile = new Vector3(-.72f, .22f, -2.78f);
                        float sideView = Ramp(2.98f, 3.34f, t) * (1f - Ramp(5.85f, 6.4f, t));
                        sample.transform.position = focus + Vector3.Slerp(rear, profile, sideView);
                        sample.transform.LookAt(focus);
                        RenderTexture active = RenderTexture.active;
                        try
                        {
                            sample.Render();
                            RenderTexture.active = target;
                            pixels.ReadPixels(rect, 0, 0, false);
                        }
                        finally { RenderTexture.active = active; }
                        string name = "frame-" + frame.ToString("D3") + ".jpg";
                        File.WriteAllBytes(Path.Combine(path, name), pixels.EncodeToJPG(90));
                        times.WriteLine(t.ToString("F4", CultureInfo.InvariantCulture));
                        frame++;
                    }
                }
            }
            finally
            {
                Client.SetProbeMotion(Vector2.zero, 90f, 3f);
                sample.targetTexture = null;
                RenderTexture.ReleaseTemporary(target);
                Destroy(pixels);
                Destroy(baked);
                Destroy(cameraObject);
            }
            if (sprintSamples < 6 || !(maxGap < .09f))
            {
                Fail($"Sprint floor contact: {maxGap:F3} m maximum gap across {sprintSamples} samples.");
                yield break;
            }
            Signal("motion-contact", maxGap.ToString("F4", CultureInfo.InvariantCulture));
            // Rejoin the rest of the two-client test on the other side of the partition.
            yield return Walk(new Vector2(-3.35f, -1.8f));
            yield return Walk(new Vector2(-3.35f, 2.35f));
        }

        private static float Ramp(float from, float to, float t) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, t));

        private static float HeadingFromRight(Vector3 lateral) => Mathf.Atan2(-lateral.z, lateral.x) * Mathf.Rad2Deg;
    }
}
#endif
