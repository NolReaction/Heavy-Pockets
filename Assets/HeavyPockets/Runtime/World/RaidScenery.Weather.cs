using UnityEngine;
using UnityEngine.Rendering;

namespace HeavyPockets
{
    internal sealed partial class RaidScenery
    {
        private struct SceneLighting
        {
            public AmbientMode Mode;
            public Color Sky, Equator, Ground, FogColor;
            public bool Fog;
            public FogMode FogMode;
            public DefaultReflectionMode Reflections;
            public float Ambient, Reflection, FogStart, FogEnd, ShadowDistance;

            public static SceneLighting Capture() => new SceneLighting
            {
                Mode = RenderSettings.ambientMode, Sky = RenderSettings.ambientSkyColor,
                Equator = RenderSettings.ambientEquatorColor, Ground = RenderSettings.ambientGroundColor,
                Ambient = RenderSettings.ambientIntensity, Reflections = RenderSettings.defaultReflectionMode,
                Reflection = RenderSettings.reflectionIntensity, Fog = RenderSettings.fog,
                FogMode = RenderSettings.fogMode, FogColor = RenderSettings.fogColor,
                FogStart = RenderSettings.fogStartDistance, FogEnd = RenderSettings.fogEndDistance,
                ShadowDistance = QualitySettings.shadowDistance
            };

            public void Restore()
            {
                RenderSettings.ambientMode = Mode;
                RenderSettings.ambientSkyColor = Sky;
                RenderSettings.ambientEquatorColor = Equator;
                RenderSettings.ambientGroundColor = Ground;
                RenderSettings.ambientIntensity = Ambient;
                RenderSettings.defaultReflectionMode = Reflections;
                RenderSettings.reflectionIntensity = Reflection;
                RenderSettings.fog = Fog;
                RenderSettings.fogMode = FogMode;
                RenderSettings.fogColor = FogColor;
                RenderSettings.fogStartDistance = FogStart;
                RenderSettings.fogEndDistance = FogEnd;
                QualitySettings.shadowDistance = ShadowDistance;
            }
        }

        private static float Blend(float low, float high, float value) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(low, high, value));

        private static Color SkyTone(int night, int sunset, int day, float twilight, float daylight) =>
            Color.Lerp(Color.Lerp(Rgb(night), Rgb(sunset), twilight), Rgb(day), daylight);

        private void ApplyWeather()
        {
            Look look = Looks[currentStyle];
            var profile = settings[currentStyle];
            float hour = profile[HarborLightParameter.Time];
            float sunlight = profile[HarborLightParameter.Sun];
            float clouds = profile[HarborLightParameter.Clouds];
            float haze = profile[HarborLightParameter.Haze];

            // Art-directed east-to-west arc; the same vector drives the disk, water and shadows.
            float orbit = (hour - 6f) * Mathf.PI / 12f;
            float elevation = Mathf.Sin(orbit) * 55f * Mathf.Deg2Rad;
            float height = Mathf.Sin(elevation);
            float azimuth = orbit + .18f;
            var dir = new Vector3(Mathf.Cos(azimuth) * Mathf.Cos(elevation), height,
                Mathf.Sin(azimuth) * Mathf.Cos(elevation));
            float twilight = Blend(-.24f, -.018f, height);
            float daylight = Blend(-.035f, .52f, height);
            float warm = twilight * (1f - daylight);
            float overcast = Blend(.53f, 1f, clouds);
            float dusk = 1f - Blend(-.16f, .18f, height);
            Shader.SetGlobalFloat("_HarborDusk", dusk);
            RoomReflection.SetDusk(dusk);
            InteriorDaylight.SetDaylight(daylight * (.48f + .52f * Mathf.Clamp01(sunlight)) * (1f - .28f * overcast));
            Shader.SetGlobalVector("_HarborSunDirection", dir);
            Shader.SetGlobalFloat("_HarborCloudCoverage", clouds);

            Color zenith = SkyTone(0x18263F, 0x67759B, look.Sky, twilight, daylight);
            Color horizon = SkyTone(0x343F54, currentStyle == 2 ? 0xC19AA8 : 0xF5AF82, look.Horizon, twilight, daylight);
            zenith = Color.Lerp(zenith, horizon, (overcast * .42f + haze * .12f) * twilight);
            Color shade = SkyTone(0x27354D, 0x806F89, look.CloudShade, twilight, daylight);
            Color lit = SkyTone(0x526279, 0xFFD4AD, look.CloudLight, twilight, daylight);
            lit = Color.Lerp(lit, shade, Mathf.Max(.30f * overcast, (1f - Mathf.Clamp01(sunlight)) * twilight * .65f));
            harborSky.SetColor("_Zenith", zenith);
            harborSky.SetColor("_Horizon", horizon);
            harborSky.SetColor("_CloudLit", lit);
            harborSky.SetColor("_CloudShade", shade);
            harborSky.SetColor("_Disk", Color.Lerp(Rgb(look.Disk), Rgb(0xFFAA66), warm));
            harborSky.SetVector("_SunDirection", dir);
            harborSky.SetFloat("_SunDiameter", currentStyle == 1 ? 7.6f : Mathf.Lerp(1.05f, 1.65f, warm));
            harborSky.SetFloat("_SunVisibility", Blend(-.025f, .018f, height) * Mathf.Clamp01(sunlight));
            harborSky.SetFloat("_Haze", haze);
            SetSurfaceTint("_HarborSkyZenith", zenith);
            SetSurfaceTint("_HarborSkyHorizon", horizon);

            RenderSettings.ambientSkyColor = SkyTone(0x677FA4, 0xA8A9BF, look.AmbientSky, twilight, daylight);
            RenderSettings.ambientEquatorColor = SkyTone(0x4C5C79, 0xB6A1A1, look.AmbientEquator, twilight, daylight);
            RenderSettings.ambientGroundColor = SkyTone(0x333C4E, 0x756964, look.AmbientGround, twilight, daylight);
            RenderSettings.fogColor = Color.Lerp(SkyTone(0x354258, 0xBE9C9C, look.Fog, twilight, daylight), horizon, .32f);
            RenderSettings.fog = haze > 0f;
            float clearDistance = (1f - Blend(0f, .15f, haze)) * RaidCameraBoom.FarClip;
            RenderSettings.fogStartDistance = clearDistance + Mathf.Lerp(look.FogStart * 1.85f, look.FogStart * .32f, haze);
            RenderSettings.fogEndDistance = clearDistance + Mathf.Lerp(look.FogEnd * 1.7f, look.FogEnd * .62f, haze);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = Mathf.Lerp(.42f, .72f, twilight);

            // One shadow-casting directional light. The moon takes over after sunset.
            bool moon = height < -.065f;
            Vector3 key = moon ? -dir : dir;
            key.y = Mathf.Max(key.y, .025f);
            sun.transform.rotation = Quaternion.LookRotation(-key.normalized);
            sun.color = moon ? (Color)Rgb(0x9DB9EC) : Color.Lerp(Rgb(look.Sun), Rgb(0xFFA269), warm);
            sun.intensity = moon ? .20f * (1f - Blend(-.29f, -.065f, height)) :
                sunlight * look.Strength * Blend(-.065f, .12f, height) * (1f - .54f * overcast);
            sun.shadowStrength = look.Shadow * Mathf.Lerp(1f, .44f, overcast) * (moon ? .64f : 1f);
            for (int i = 0; i < floodlights.Length; ++i)
            {
                var lamp = floodlights[i];
                lamp.enabled = dusk > .015f;
                lamp.intensity = dusk * (i == 2 ? 5.2f : 3.5f);
            }
            // Rebuild the environment reflection once after a group of slider changes.
            refreshSkyAt = Time.unscaledTime + .32f;
        }

        internal void TickLighting(float deltaTime)
        {
            float wind = settings[currentStyle][HarborLightParameter.Wind];
            if (deltaTime > 0f)
                windTravel += wind * (double)deltaTime;
            // XY is the repeating cloud offset. Z and W give vegetation the wind strength and gust phase.
            const double period = 22600.0;
            Shader.SetGlobalVector(WindOffset, new Vector4((float)(windTravel * .86 % period),
                (float)(windTravel * .51 % period), Mathf.Sqrt(wind / 15f),
                (float)(windTravel * .31 % (2.0 * System.Math.PI))));
            if (refreshSkyAt > 0f && Time.unscaledTime >= refreshSkyAt)
            {
                refreshSkyAt = 0f;
                DynamicGI.UpdateEnvironment();
            }
        }
    }
}
