using UnityEngine;

namespace HeavyPockets
{
    // A small product-photo studio lives beyond the raid camera's far plane.
    internal sealed class WardrobeStudio : System.IDisposable
    {
        private const int Layer = 30;
        private readonly GameObject room, actor;
        private readonly Transform turntable;
        private readonly Camera camera;
        internal RenderTexture Image { get; }
        internal Texture2D Contact { get; }
        internal Texture2D Backdrop { get; }
        internal float FloorY => 1f - camera.WorldToViewportPoint(turntable.position).y;
        internal bool CloseUp { get; private set; }

        internal WardrobeStudio(int style)
        {
            room = new GameObject("Wardrobe studio");
            room.transform.position = new Vector3(80f, 85f, 80f);
            turntable = Child("Turntable");
            actor = RunnerAppearance.CreatePreview(turntable, style);
            PrepareLayers();
            turntable.localRotation = Quaternion.Euler(0, -16f, 0);
            camera = Child("Portrait camera").gameObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0, 0, 0, 0);
            camera.cullingMask = 1 << Layer;
            camera.orthographic = true;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 8f;
            camera.allowHDR = false;
            camera.depth = -5f;
            Image = new RenderTexture(640, 768, 24, RenderTextureFormat.ARGB32) {
                name = "Wardrobe portrait", antiAliasing = 2, filterMode = FilterMode.Bilinear };
            camera.targetTexture = Image;
            Backdrop = new Texture2D(256, 1, TextureFormat.RGBA32, false) { name = "Studio backdrop", wrapMode = TextureWrapMode.Clamp };
            var ramp = new Color32[256];
            for (int i = 0; i < ramp.Length; i++)
            {
                float t = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Abs(i / 255f - .49f) * 1.7f);
                ramp[i] = Color.Lerp(new Color32(38, 58, 64, 255), new Color32(78, 105, 107, 255), t);
            }
            Backdrop.SetPixels32(ramp);
            Backdrop.Apply(false, true);
            Contact = new Texture2D(96, 32, TextureFormat.RGBA32, false) { name = "Wardrobe contact", wrapMode = TextureWrapMode.Clamp };
            var shade = new Color32[96 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 96; x++)
                {
                    float dx = (x - 47.5f) / 47.5f, dy = (y - 15.5f) / 15.5f;
                    float fade = Mathf.Clamp01(1f - dx * dx - dy * dy);
                    shade[y * 96 + x] = new Color32(11, 25, 30, (byte)(100 * fade * fade));
                }
            Contact.SetPixels32(shade);
            Contact.Apply(false, true);
            SetCloseUp(false);
            Lamp("Softbox", new Vector3(1.6f, 2.8f, 2.1f), new Color(1f, .90f, .78f), 2.7f);
            Lamp("Fill", new Vector3(-1.5f, 1.65f, 1.6f), new Color(.73f, .88f, 1f), 1.25f);
            Lamp("Rim", new Vector3(-.9f, 2.2f, -1.3f), new Color(1f, .77f, .48f), 2f);
        }

        private Transform Child(string name)
        {
            var node = new GameObject(name).transform;
            node.SetParent(room.transform, false);
            return node;
        }

        private void Lamp(string name, Vector3 position, Color color, float intensity)
        {
            var light = Child(name).gameObject.AddComponent<Light>();
            light.transform.localPosition = position;
            light.type = LightType.Point;
            light.range = 6f;
            light.cullingMask = 1 << Layer;
            light.color = color;
            light.intensity = intensity;
        }

        private void PrepareLayers()
        {
            foreach (Transform child in actor.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = Layer;
        }

        internal void Dress(int style)
        {
            RunnerAppearance.Apply(actor, style);
            PrepareLayers();
        }

        internal void Turn(float degrees) => turntable.Rotate(0, degrees, 0, Space.Self);

        internal void SetCloseUp(bool zoom)
        {
            CloseUp = zoom;
            camera.orthographicSize = zoom ? .55f : 1.08f;
            float y = zoom ? 1.47f : .91f;
            camera.transform.localPosition = new Vector3(0, y + .09f, 3.3f);
            camera.transform.LookAt(room.transform.position + Vector3.up * y);
        }

        public void Dispose()
        {
            room.SetActive(false);
            camera.targetTexture = null;
            Object.Destroy(room);
            Image.Release();
            Object.Destroy(Image);
            Object.Destroy(Contact);
            Object.Destroy(Backdrop);
        }
    }
}
