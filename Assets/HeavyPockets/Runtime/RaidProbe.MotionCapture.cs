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
            yield return Walk(new Vector2(-5.65f, -2.6f), 90f);
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
            bool jumped = false;
            int frame = 0;
            float start = Time.unscaledTime;
            var endOfFrame = new WaitForEndOfFrame();
            var skin = Client.ProbeLocalBody.GetComponentInChildren<SkinnedMeshRenderer>();
            var baked = new Mesh();
            var vertices = new List<Vector3>(skin.sharedMesh.vertexCount);
            float nextSoleSample = 2.12f, maxGap = 0f;
            int sprintSamples = 0;
            try
            {
                using (var times = new StreamWriter(Path.Combine(path, "frames.txt")))
                {
                    while (Time.unscaledTime - start < 8.3f)
                    {
                        yield return endOfFrame;
                        float t = Time.unscaledTime - start;
                        Vector2 direction = t < .35f ? Vector2.zero :
                            t < 1.7f ? Vector2.right * .83f : t < 2.85f ? Vector2.right :
                            t < 3.65f ? Vector2.zero : t < 4.7f ? Vector2.left :
                            t < 5.55f ? new Vector2(-.45f, .55f) : t < 6.1f ? Vector2.up * .7f : Vector2.zero;
                        MoveButtons buttons = t >= 1.7f && t < 2.85f ? MoveButtons.Sprint :
                            t >= 3.05f && t < 5.55f ? MoveButtons.Crouch : MoveButtons.None;
                        if (!jumped && t >= 6.75f) { buttons |= MoveButtons.Jump; jumped = true; }
                        if (buttons != previousButtons || direction != previous || t >= nextInput)
                        {
                            Client.SetProbeMotion(direction, 90f, 3f, buttons);
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
                        Vector3 focus = Client.ProbeLocalBody.position;
                        focus.y = .96f + .72f * focus.y;
                        sample.transform.position = focus + new Vector3(.9f, .18f, -2.95f);
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
    }
}
#endif
