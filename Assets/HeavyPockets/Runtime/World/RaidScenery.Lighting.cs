using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeavyPockets
{
    internal sealed partial class RaidScenery
    {
        private enum SurfaceFinish { Paint, Concrete, Steel, Rubber, Fabric, Glass, Lamp, Signal }

        private readonly List<Material> surfaces = new List<Material>();
        private Shader depotShader;
        private Texture2D grainTexture;

        private Material Surface(string name, Color color, SurfaceFinish finish = SurfaceFinish.Paint, float glow = 0f)
        {
            if (depotShader == null)
            {
                depotShader = Resources.Load<Shader>("DepotSurface");
                grainTexture = Resources.Load<Texture2D>("DepotGrain");
            }
            var material = new Material(depotShader) { name = name, color = color, enableInstancing = true };
            // Pigment contrast, gloss, specular reflectance, normal relief.
            var response = new Vector4(0.17f, 0.39f, 0.075f, 0.14f);
            var frequency = new Vector4(0.85f, 0.85f, 0, 0);
            float wear = 0.66f;
            switch (finish)
            {
                case SurfaceFinish.Concrete:
                    response = new Vector4(0.15f, 0.105f, 0.021f, 0.14f);
                    frequency = new Vector4(0.39f, 0.39f, 0, 0);
                    wear = 0.15f;
                    break;
                case SurfaceFinish.Steel:
                    response = new Vector4(0.23f, 0.53f, 0.20f, 0.20f);
                    frequency = new Vector4(0.68f, 1.90f, 0, 0);
                    wear = 0.53f;
                    break;
                case SurfaceFinish.Rubber:
                    response = new Vector4(0.26f, 0.24f, 0.068f, 0.27f);
                    frequency = new Vector4(1.7f, 1.7f, 0, 0);
                    wear = 0.09f;
                    break;
                case SurfaceFinish.Fabric:
                    response = new Vector4(0.44f, 0.13f, 0.029f, 0.56f);
                    frequency = new Vector4(2.2f, 2.8f, 0, 0);
                    wear = 0.06f;
                    break;
                case SurfaceFinish.Glass:
                    response = new Vector4(0.075f, 0.58f, 0.13f, 0.028f);
                    wear = 0f;
                    break;
                case SurfaceFinish.Lamp:
                case SurfaceFinish.Signal:
                    response = new Vector4(0.08f, 0.41f, 0.09f, 0.045f);
                    wear = 0.02f;
                    if (glow == 0f) glow = finish == SurfaceFinish.Lamp ? 1f : 0.58f;
                    break;
            }
            material.SetTexture("_Grain", grainTexture);
            material.SetVector("_Finish", response);
            material.SetVector("_GrainScale", frequency);
            material.SetFloat("_Wear", wear);
            material.SetFloat("_Glow", glow);
            surfaces.Add(material);
            return material;
        }

        private void DisposeSurfaces()
        {
            foreach (Material surface in surfaces)
                Object.Destroy(surface);
            surfaces.Clear();
        }

        private Light NewLight(string name, LightType type, Color color, float intensity)
        {
            var fixture = new GameObject(name);
            fixture.transform.SetParent(root, false);
            var light = fixture.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            return light;
        }
    }
}
