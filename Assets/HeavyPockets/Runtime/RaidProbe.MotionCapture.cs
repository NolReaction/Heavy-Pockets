#if (DEVELOPMENT_BUILD || UNITY_EDITOR) && !UNITY_SERVER
using System;
using System.Collections;
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
            // Leave room for the review camera between the running lane and the north wall.
            yield return Walk(new Vector2(-3.7f, 2.45f), 90f);
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
            sample.fieldOfView = 41f;
            var rect = new Rect(0, 0, width, height);
            Vector2 previous = new Vector2(9f, 9f);
            float nextInput = 0f;
            MoveButtons previousButtons = MoveButtons.None;
            bool jumped = false;
            int frame = 0;
            float start = Time.unscaledTime;
            var endOfFrame = new WaitForEndOfFrame();
            try
            {
                using (var times = new StreamWriter(Path.Combine(path, "frames.txt")))
                {
                    while (Time.unscaledTime - start < 6.45f)
                    {
                        yield return endOfFrame;
                        float t = Time.unscaledTime - start;
                        Vector2 direction = t < .35f || t >= 4.75f ? Vector2.zero :
                            t < 2.15f ? Vector2.right : t < 2.85f ? Vector2.zero :
                            t < 3.75f ? Vector2.left : Vector2.down * .7f;
                        MoveButtons buttons = t >= 1.32f && t < 2.15f ? MoveButtons.Sprint :
                            t >= 2.35f && t < 3.75f ? MoveButtons.Crouch : MoveButtons.None;
                        if (!jumped && t >= 5.02f) { buttons |= MoveButtons.Jump; jumped = true; }
                        if (buttons != previousButtons || direction != previous || t >= nextInput)
                        {
                            Client.SetProbeMotion(direction, 90f, 3f, buttons);
                            previous = direction;
                            previousButtons = buttons;
                            nextInput = t + .06f;
                        }
                        if (t < frame / 24f) continue;
                        Vector3 focus = Client.ProbeLocalBody.position;
                        focus.y = 1.04f + .5f * Client.OwnMotion.Height;
                        sample.transform.position = focus + new Vector3(1.7f, .20f, 2.85f);
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
                Destroy(cameraObject);
            }
        }
    }
}
#endif
