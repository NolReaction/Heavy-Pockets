#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeavyPockets
{
    // Observe the actual visible rig during the two-client corner traversal.
    public sealed partial class RaidClient
    {
        internal bool ProbeRemotePresent => runners.Count == 1;
        private int probeStarts, probeHides, probeImmediate;
        private float probeStartYaw, probeSpawnError, probeSpawnTurn, probeStartYawDrift;
        private bool probeActiveAfterHide;

        partial void ObserveRunnerStart(Runner runner, Vector3 position, float expectedYaw)
        {
            if (!ScriptedInput) return;
            probeStarts++;
            probeSpawnError = Mathf.Max(probeSpawnError, Vector3.Distance(runner.Model.position, position));
            probeSpawnTurn = Mathf.Max(probeSpawnTurn, Mathf.Abs(Mathf.DeltaAngle(runner.Model.eulerAngles.y, expectedYaw)));
            if (probeStarts == 1) probeStartYaw = expectedYaw;
            else probeStartYawDrift = Mathf.Max(probeStartYawDrift, Mathf.Abs(Mathf.DeltaAngle(probeStartYaw, expectedYaw)));
            bool ready = runner.Model.gameObject.activeInHierarchy;
            bool sawBody = false;
            foreach (Renderer part in runner.Model.GetComponentsInChildren<Renderer>())
            {
                if (!part.enabled) continue;
                ready &= !part.forceRenderingOff;
                foreach (Material mat in part.sharedMaterials)
                {
                    if (mat.shader.name == "HeavyPockets/GroundContact") continue;
                    sawBody = true;
                    ready &= mat.shader.isSupported && mat.GetTag("RenderType", false) == "Opaque";
                }
            }
            if (ready && sawBody) probeImmediate++;
        }

        partial void ObserveRunnerHide(Runner runner)
        {
            if (!ScriptedInput) return;
            probeHides++;
            probeActiveAfterHide |= runner.Model.gameObject.activeInHierarchy;
        }

        internal bool ProbeRemoteReveal(float stationaryYaw, out string evidence)
        {
            float endError = 180f;
            foreach (Runner runner in runners.Values)
                endError = Mathf.Abs(Mathf.DeltaAngle(runner.Model.eulerAngles.y, stationaryYaw));
            evidence = string.Format(CultureInfo.InvariantCulture,
                "{0}/{1} immediately opaque activations, {2} hides. Spawn {3:F4} m, turn {4:F2} deg. First yaw {5:F1}, final error {6:F2} deg.",
                probeImmediate, probeStarts, probeHides, probeSpawnError, probeSpawnTurn, probeStartYaw, endError);
            return runners.Count == 1 && probeStarts >= 2 && probeHides >= 1 && probeImmediate == probeStarts
                && probeSpawnError < 0.001f && probeSpawnTurn < 0.2f && probeStartYawDrift < 0.2f
                && Mathf.Abs(Mathf.DeltaAngle(probeStartYaw, stationaryYaw)) < 0.2f && endError < 0.2f
                && !probeActiveAfterHide;
        }

        // Measure the actual on-screen body behind the depot's meshes. All three
        // renders use the same camera pose and geometry in a single simulation frame.
        internal bool ProbeRemoteSilhouette(out int exposed, out int silhouette)
        {
            exposed = silhouette = 0;
            if (!ScriptedInput || view == null || runners.Count != 1) return false;
            var body = runners.Values.First().Model.GetComponentsInChildren<Renderer>()
                .Where(r => r.enabled && r.sharedMaterial.shader.name != "HeavyPockets/GroundContact").ToArray();
            if (body.Length == 0) return false;
            const int sampleLayer = 31; // Unused by depot geometry.
            int[] layers = body.Select(r => r.gameObject.layer).ToArray();
            var shadows = body.Select(r => r.shadowCastingMode).ToArray();
            const int width = 512;
            int height = Mathf.Max(1, Mathf.RoundToInt(width / view.aspect));
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var cameraObject = new GameObject("Corner probe camera");
            Camera sample = cameraObject.AddComponent<Camera>();
            RenderTexture previousTarget = RenderTexture.active;
            try
            {
                sample.CopyFrom(view);
                sample.enabled = false;
                sample.transform.SetPositionAndRotation(view.transform.position, view.transform.rotation);
                sample.targetTexture = target;
                sample.aspect = view.aspect;
                // A cast shadow on the wall is not exposed body geometry.
                for (int i = 0; i < body.Length; i++)
                {
                    body[i].gameObject.layer = sampleLayer;
                    body[i].shadowCastingMode = ShadowCastingMode.Off;
                }

                int bodyMask = 1 << sampleLayer;
                sample.cullingMask = view.cullingMask | bodyMask;
                Color32[] withActor = ReadProbePixels(sample, pixels);
                sample.cullingMask &= ~bodyMask;
                Color32[] background = ReadProbePixels(sample, pixels);
                sample.cullingMask = bodyMask;
                sample.clearFlags = CameraClearFlags.SolidColor;
                sample.backgroundColor = Color.magenta;
                Color32[] isolated = ReadProbePixels(sample, pixels);
                for (int i = 0; i < isolated.Length; i++)
                {
                    Color32 c = isolated[i];
                    if (c.r >= 250 && c.g <= 5 && c.b >= 250) continue;
                    silhouette++;
                    Color32 a = withActor[i], b = background[i];
                    if (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > 6)
                        exposed++;
                }
                return silhouette > 0;
            }
            finally
            {
                for (int i = 0; i < body.Length; i++)
                {
                    body[i].gameObject.layer = layers[i];
                    body[i].shadowCastingMode = shadows[i];
                }
                sample.targetTexture = null;
                RenderTexture.active = previousTarget;
                RenderTexture.ReleaseTemporary(target);
                Destroy(pixels);
                Destroy(cameraObject);
            }
        }

        private static Color32[] ReadProbePixels(Camera sample, Texture2D pixels)
        {
            sample.Render();
            RenderTexture.active = sample.targetTexture;
            pixels.ReadPixels(new Rect(0, 0, pixels.width, pixels.height), 0, 0, false);
            return pixels.GetPixels32();
        }
    }
}
#endif
