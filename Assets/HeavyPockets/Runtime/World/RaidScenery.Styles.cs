using UnityEngine;
using UnityEngine.Rendering;

namespace HeavyPockets
{
    internal sealed partial class RaidScenery
    {
        private const string StylePreference = "HeavyPockets.Harbor.Style";
        private static RaidScenery activeHarbor;
        private int currentStyle;
        private Material harborSky, previousSky;
        private Light sun, previousSun;
        private readonly Light[] floodlights = new Light[3];
        private float previousShadowDistance;

        // Colors are authored in sRGB to match the swatches seen in the editor.
        private static Color32 Rgb(int rgb) =>
            new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        private sealed class Look
        {
            internal string Name;
            internal int Sky, Horizon, CloudLight, CloudShade, Disk;
            internal int AmbientSky, AmbientEquator, AmbientGround, Sun, Fog;
            internal Vector3 SunDirection;
            internal float Strength, Shadow, FogStart, FogEnd;
            internal int Sea, Grass, Land;
        }

        private static readonly Look[] Looks =
        {
            new Look
            {
                Name = "Северный порт", Sky = 0x528BB6, Horizon = 0xD4DDD9,
                CloudLight = 0xFAF8ED, CloudShade = 0xA8BAC3, Disk = 0xFFF0CB,
                AmbientSky = 0xB1CADC, AmbientEquator = 0x98AAB2, AmbientGround = 0x847C70,
                Sun = 0xFFF0D6, SunDirection = new Vector3(-.72f,.68f,.39f),
                Strength = .96f, Shadow = .76f, Fog = 0xACBFC5, FogStart = 73f, FogEnd = 325f,
                Sea = 0x4B848C, Grass = 0x626F59, Land = 0x77796E
            },
            new Look
            {
                Name = "Грузовой плакат", Sky = 0x48979B, Horizon = 0xF5D99A,
                CloudLight = 0xFFF1BC, CloudShade = 0xE2B878, Disk = 0xFF693D,
                AmbientSky = 0xB7D3BE, AmbientEquator = 0xACBDA9, AmbientGround = 0x9E8D71,
                Sun = 0xFFE2A3, SunDirection = new Vector3(.43f,.28f,.86f),
                Strength = 1.1f, Shadow = .88f, Fog = 0xDDCA99, FogStart = 55f, FogEnd = 230f,
                Sea = 0x388F8F, Grass = 0xAA9E58, Land = 0xC6A46F
            },
            new Look
            {
                Name = "Синий час", Sky = 0x263967, Horizon = 0xB597A3,
                CloudLight = 0xAE94AE, CloudShade = 0x586180, Disk = 0xFFD8A8,
                AmbientSky = 0x8394BE, AmbientEquator = 0x747E9A, AmbientGround = 0x686575,
                Sun = 0xB9CCFC, SunDirection = new Vector3(-.49f,.13f,.87f),
                Strength = .74f, Shadow = .71f, Fog = 0x898A9F, FogStart = 39f, FogEnd = 208f,
                Sea = 0x4C6687, Grass = 0x6C746C, Land = 0x777481
            }
        };

        internal static int EnvironmentStyle => activeHarbor != null ? activeHarbor.currentStyle : 0;
        internal static string EnvironmentStyleName(int index) => Looks[index].Name;

        internal static void SetEnvironmentStyle(int index, bool persist = true)
        {
            if (activeHarbor == null || index < 0 || index >= Looks.Length) return;
            activeHarbor.ApplyLook(index);
            if (persist)
            {
                PlayerPrefs.SetInt(StylePreference, index);
                PlayerPrefs.Save();
            }
        }

        private void BuildLighting()
        {
            previousSky = RenderSettings.skybox;
            previousSun = RenderSettings.sun;
            previousShadowDistance = QualitySettings.shadowDistance;
            harborSky = new Material(Resources.Load<Shader>("HarborSky")) { name = "Harbor cloud deck" };
            RenderSettings.skybox = harborSky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            QualitySettings.shadowDistance = Mathf.Max(previousShadowDistance, 65f);

            sun = NewLight("Harbor daylight", LightType.Directional, Color.white, 1f);
            sun.shadows = LightShadows.Soft;
            sun.shadowBias = .032f;
            sun.shadowNormalBias = .22f;
            RenderSettings.sun = sun;
            for (int i = 0; i < floodlights.Length; ++i)
            {
                Vector3 position;
                float range;
                switch (i)
                {
                    case 0: position = new Vector3(0, 2.65f, -.55f); range = 9f; break;
                    case 1: position = new Vector3(47f, 4.12f, 34.67f); range = 15f; break;
                    default: position = new Vector3(38f, 10.2f, 14.4f); range = 24f; break;
                }
                Light lamp = NewLight("Quayside work light", LightType.Point, Rgb(0xFFBA75), i == 2 ? 5.2f : 3.5f);
                lamp.transform.localPosition = position;
                lamp.range = range;
                floodlights[i] = lamp;
            }
            activeHarbor = this;
            ApplyLook(Mathf.Clamp(PlayerPrefs.GetInt(StylePreference, 0), 0, Looks.Length - 1));
        }

        private void ApplyLook(int index)
        {
            currentStyle = index;
            Look look = Looks[index];
            bool poster = index == 1, dusk = index == 2;
            Shader.SetGlobalFloat("_HarborPoster", poster ? 1f : 0f);
            Shader.SetGlobalFloat("_HarborDusk", dusk ? 1f : 0f);
            SetSurfaceTint("_HarborSeaColor", look.Sea);
            SetSurfaceTint("_HarborGrassColor", look.Grass);
            SetSurfaceTint("_HarborLandColor", look.Land);
            harborSky.SetColor("_Zenith", Rgb(look.Sky));
            harborSky.SetColor("_Horizon", Rgb(look.Horizon));
            harborSky.SetColor("_CloudLit", Rgb(look.CloudLight));
            harborSky.SetColor("_CloudShade", Rgb(look.CloudShade));
            harborSky.SetColor("_Disk", Rgb(look.Disk));
            harborSky.SetVector("_SunDirection", look.SunDirection.normalized);

            RenderSettings.ambientSkyColor = Rgb(look.AmbientSky);
            RenderSettings.ambientEquatorColor = Rgb(look.AmbientEquator);
            RenderSettings.ambientGroundColor = Rgb(look.AmbientGround);
            RenderSettings.fogColor = Rgb(look.Fog);
            RenderSettings.fogStartDistance = look.FogStart;
            RenderSettings.fogEndDistance = look.FogEnd;
            sun.transform.rotation = Quaternion.LookRotation(-look.SunDirection);
            sun.color = Rgb(look.Sun);
            sun.intensity = look.Strength;
            sun.shadowStrength = look.Shadow;
            sun.shadows = poster ? LightShadows.Hard : LightShadows.Soft;
            foreach (Light lamp in floodlights) lamp.enabled = dusk;
        }

        private static void SetSurfaceTint(string property, int rgb)
        {
            Color value = Rgb(rgb);
            Shader.SetGlobalVector(property, QualitySettings.activeColorSpace == ColorSpace.Linear ? value.linear : value);
        }

        private void DisposeLighting()
        {
            if (activeHarbor == this)
            {
                activeHarbor = null;
                Shader.SetGlobalFloat("_HarborPoster", 0);
                Shader.SetGlobalFloat("_HarborDusk", 0);
                QualitySettings.shadowDistance = previousShadowDistance;
                if (RenderSettings.skybox == harborSky) RenderSettings.skybox = previousSky;
                if (RenderSettings.sun == sun) RenderSettings.sun = previousSun;
            }
            Object.Destroy(harborSky);
        }
    }
}
