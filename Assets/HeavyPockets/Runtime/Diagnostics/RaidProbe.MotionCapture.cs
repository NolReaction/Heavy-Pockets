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
            bool standingJump = false, runningJump = false, portraitSaved = false;
            int frame = 0;
            float start = Time.unscaledTime;
            var endOfFrame = new WaitForEndOfFrame();
            var rig = Client.ProbeLocalBody.GetComponent<FreightRunnerGait>().Rig;
            var skins = Client.ProbeLocalBody.GetComponentsInChildren<SkinnedMeshRenderer>();
            var hips = rig.Bone("pelvis");
            var chest = rig.Bone("spine_03");
            var leftHip = rig.Bone("thigh_l");
            var rightHip = rig.Bone("thigh_r");
            var leftKnee = rig.Bone("calf_l");
            var rightKnee = rig.Bone("calf_r");
            var leftFoot = rig.Bone("foot_l");
            var rightFoot = rig.Bone("foot_r");
            var leftArm = rig.Bone("upperarm_l");
            var rightArm = rig.Bone("upperarm_r");
            var leftHand = rig.Bone("hand_l");
            var rightHand = rig.Bone("hand_r");
            var baked = new Mesh();
            var vertices = new List<Vector3>();
            float nextSoleSample = 1.94f, maxGap = 0f, minGap = float.PositiveInfinity;
            int sprintSamples = 0;
            try
            {
                using (var times = new StreamWriter(Path.Combine(path, "frames.txt")))
                using (var poses = new StreamWriter(Path.Combine(path, "poses.csv")))
                {
                    poses.WriteLine("time,look,pelvisHeading,chestHeading,ankleWidth,kneeWidth,handWidth,torsoPitch,rootHeight,sliding");
                    while (Time.unscaledTime - start < 19.7f)
                    {
                        yield return endOfFrame;
                        float t = Time.unscaledTime - start;
                        Vector2 direction = Vector2.zero;
                        if (t >= .35f && t < 1.6f) direction = Vector2.right * .85f;
                        if (t >= 1.6f && t < 2.95f) direction = Vector2.right;
                        if (t >= 3.35f && t < 4.75f) direction = Vector2.left * .90f;
                        if (t >= 5f && t < 5.95f) direction = Vector2.up * .8f;
                        if (t >= 7.65f && t < 8.86f) direction = Vector2.right * .85f;
                        if (t >= 12.85f && t < 13.95f) direction = Vector2.down;
                        if (t >= 14.15f && t < 15.25f) direction = Vector2.up;
                        if (t >= 16.4f && t < 17.65f) direction = Vector2.left;
                        MoveButtons buttons = t >= 1.6f && t < 2.95f ? MoveButtons.Sprint :
                            t >= 4.95f && t < 5.95f ? MoveButtons.Crouch : MoveButtons.None;
                        if (t >= 7.65f && t < 8.86f || t >= 16.4f && t < 17.65f) buttons |= MoveButtons.Sprint;
                        if (t >= 17.43f && t < 18.8f) buttons |= MoveButtons.Crouch;
                        if (!standingJump && t >= 6.50f) { buttons |= MoveButtons.Jump; standingJump = true; }
                        if (!runningJump && t >= 8.15f) { buttons |= MoveButtons.Jump; runningJump = true; }
                        float heading = 90f + 18f * Ramp(9.85f, 10.19f, t) + 52f * Ramp(10.60f, 11.10f, t)
                            - 85f * Ramp(11.45f, 12.10f, t) + 15f * Ramp(12.1f, 12.7f, t)
                            - 180f * Ramp(15.3f, 16.16f, t);
                        if (buttons != previousButtons || direction != previous || t >= nextInput)
                        {
                            Client.SetProbeMotion(direction, heading, 3f + 13f * Ramp(12.1f, 12.8f, t), buttons);
                            previous = direction;
                            previousButtons = buttons;
                            nextInput = t + .06f;
                        }
                        if (t >= nextSoleSample && t < 2.82f && Client.OwnMotion.Sprinting)
                        {
                            // A running stride alternates a planted foot with a short flight phase.
                            float lowest = float.PositiveInfinity;
                            foreach (var skin in skins)
                            {
                                if (!skin.enabled) continue;
                                skin.BakeMesh(baked);
                                baked.GetVertices(vertices);
                                Matrix4x4 toWorld = skin.localToWorldMatrix;
                                foreach (Vector3 vertex in vertices)
                                    lowest = Mathf.Min(lowest, toWorld.MultiplyPoint3x4(vertex).y);
                            }
                            maxGap = Mathf.Max(maxGap, lowest);
                            minGap = Mathf.Min(minGap, lowest);
                            sprintSamples++;
                            nextSoleSample = t + .016f;
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
                            "{0:F4},{1:F2},{2:F2},{3:F2},{4:F4},{5:F4},{6:F4},{7:F2},{8:F4},{9}",
                            t, heading, HeadingFromRight(acrossHips), HeadingFromRight(acrossChest),
                            ankleWidth, kneeWidth, handWidth, torsoPitch, body.position.y, Client.OwnMotion.Sliding ? 1 : 0));
                        Vector3 focus = body.position;
                        focus.y = .96f + .72f * focus.y;
                        // Rear view shows foot lanes and turning; rotate to three-quarter profile for the backstep.
                        Vector3 rear = new Vector3(-2.70f, .66f, -.55f);
                        Vector3 profile = new Vector3(-.72f, .22f, -2.78f);
                        float sideView = Ramp(1.42f, 1.91f, t) * (1f - Ramp(5.85f, 6.4f, t));
                        sample.transform.position = focus + Vector3.Slerp(rear, profile, sideView);
                        sample.transform.LookAt(focus);
                        if (t >= 12.85f)
                        {
                            Camera gameplay = Camera.main;
                            sample.transform.SetPositionAndRotation(gameplay.transform.position, gameplay.transform.rotation);
                            sample.fieldOfView = gameplay.fieldOfView;
                        }
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
                        if (t >= 12.35f && !portraitSaved)
                        {
                            Vector3 portraitFocus = rig.Bone("Head").position - Vector3.up * .21f;
                            sample.transform.position = portraitFocus + body.forward * 1.36f + body.right * .47f + Vector3.up * .11f;
                            sample.transform.LookAt(portraitFocus);
                            try
                            {
                                sample.Render();
                                RenderTexture.active = target;
                                pixels.ReadPixels(rect, 0, 0, false);
                                File.WriteAllBytes(Path.Combine(directory, "character-portrait.jpg"), pixels.EncodeToJPG(95));
                                portraitSaved = true;
                            }
                            finally { RenderTexture.active = active; }
                        }
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
            if (sprintSamples < 12 || minGap < -.045f || minGap > .04f || maxGap < .045f || maxGap > .35f)
            {
                Fail($"Sprint stance and flight: {minGap:F3} to {maxGap:F3} m across {sprintSamples} samples.");
                yield break;
            }
            Signal("motion-contact", minGap.ToString("F4", CultureInfo.InvariantCulture));
            Signal("motion-flight", maxGap.ToString("F4", CultureInfo.InvariantCulture));
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
