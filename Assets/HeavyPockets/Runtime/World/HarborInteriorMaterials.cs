using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeavyPockets
{
    // Chart-house scans are private to this cell. A returning player can take over
    // an in-flight load; other cells and shared harbor resources are never unloaded here.
    internal sealed class HarborInteriorMaterials : IDisposable
    {
        private const string Folder = "HarborInterior/";
        private sealed class Map
        {
            public readonly string Name;
            public Texture Texture;
            public ResourceRequest Request;
            public bool Failed { get; private set; }

            public Map(string name) { Name = name; }

            public void Start()
            {
                if (Texture || Request != null) return;
                Failed = false;
                Request = Resources.LoadAsync<Texture>(Folder + Name);
                Request.completed += Complete;
            }

            private void Complete(AsyncOperation operation)
            {
                Texture = ((ResourceRequest)operation).asset as Texture;
                Failed = !Texture;
                Request = null;
                if (owners == 0 && reservations == 0) Release();
            }

            public Texture Get()
            {
                if (!Texture)
                    Texture = Request != null && Request.isDone ? Request.asset as Texture :
                        Resources.Load<Texture>(Folder + Name);
                if (!Texture) throw new InvalidOperationException("Missing chart-house texture " + Name);
                return Texture;
            }

            public void Release()
            {
                // A request owns its result until its main-thread completion callback.
                if (Request != null || !Texture) return;
                Resources.UnloadAsset(Texture);
                Texture = null;
            }
        }

        private static readonly Map brickColor = new Map("BrickColor"), brickDetail = new Map("BrickDetail");
        private static readonly Map floorColor = new Map("FloorColor"), floorDetail = new Map("FloorDetail");
        private static readonly Map plasterColor = new Map("PlasterColor"), plasterDetail = new Map("PlasterDetail");
        private static readonly Map woodColor = new Map("TimberColor"), woodDetail = new Map("TimberDetail");
        private static readonly Map satin = new Map("SatinDetail"), chart = new Map("NorthernHarborChart");
        private static readonly Map logbook = new Map("RadioWatchLog");
        private static readonly Map reflection = new Map("ChartHouseReflection");
        private static readonly Map[] Maps = { brickColor, brickDetail, floorColor, floorDetail, plasterColor,
            plasterDetail, woodColor, woodDetail, satin, chart, logbook, reflection };
        private static int reservations, owners;
        private readonly List<Material> materials = new List<Material>(14);
        private readonly Shader shader;
        private ReflectionProbe installedProbe;
        private bool disposed;
        internal readonly Material Brick, Plaster, Floor, Timber, Paint, Steel, Brass, Ceramic, Rubber, Paper, Lamp, Chart, Logbook, Glass;

        internal static void Prefetch()
        {
            ++reservations;
            foreach (var map in Maps) map.Start();
        }

        internal static bool Ready
        {
            get
            {
                foreach (var map in Maps)
                    if (!map.Texture && !map.Failed) return false;
                return true;
            }
        }

        internal static void CancelPrefetch()
        {
            if (reservations == 0) return;
            --reservations;
            ReleaseUnused();
        }

        private static void ReleaseUnused()
        {
            if (owners != 0 || reservations != 0) return;
            foreach (var map in Maps) map.Release();
        }

        internal HarborInteriorMaterials()
        {
            ++owners;
            try
            {
                shader = Resources.Load<Shader>(Folder + "ChartHouseSurface");
                Brick = Finish("Fired harbor brick", 0xFFFFFF, brickColor, brickDetail, .715f, .88f, 0f, .75f, .02f);
                Plaster = Finish("Limewashed plaster", 0xFFF4D9, plasterColor, plasterDetail, .50f, .60f, 0f, .55f, .05f);
                Floor = Finish("Sealed terrazzo", 0xDFE5E1, floorColor, floorDetail, .38f, .48f, 0f, .56f, .15f);
                Timber = Finish("Oiled hardwood", 0xFFFFFF, woodColor, woodDetail, .68f, .80f, 0f, .82f, .16f);
                Paint = Finish("Petrol enamel", 0x244A47, null, satin, 2.8f, .47f, .08f, .65f, .11f);
                Steel = Finish("Gunmetal steel", 0x68716D, null, satin, 4f, .72f, .76f, .85f, .06f);
                Brass = Finish("Patinated brass", 0xCF9C60, null, satin, 4.1f, .35f, .76f, .64f, .22f);
                Ceramic = Finish("Ivory glazed porcelain", 0xE2DFCE, null, satin, 3f, .18f, 0f, .25f, .59f);
                Rubber = Finish("Graphite rubber", 0x353B3A, null, satin, 3.1f, .70f, 0f, .43f, .06f);
                Paper = Finish("Rag stock", 0xE8DCC2, null, satin, 5f, .37f, 0f, .20f, .07f);
                Lamp = Finish("Opal glass diffuser", 0xFFDC9D, null, satin, 4f, .1f, 0f, .2f, .43f);
                Lamp.SetFloat("_Light", 1.35f);
                Chart = Finish("Northern harbor pilot chart", 0xFFFFFF, chart, satin, 3.2f, .24f, 0f, .25f, .04f);
                Chart.SetFloat("_Print", 1f);
                Logbook = Finish("Radio watch log", 0xFFFFFF, logbook, satin, 5f, .12f, 0f, .14f, .03f);
                Logbook.SetFloat("_Print", 1f);
                Glass = new Material(Resources.Load<Shader>(Folder + "ChartHouseGlass")) { name = "Harbor window glass" };
                materials.Add(Glass);
            }
            catch { Dispose(); throw; }
        }

        private Material Finish(string name, int pigment, Map color, Map detail, float scale, float relief,
            float metallic, float smoothnessMultiplier, float smoothnessOffset)
        {
            var material = new Material(shader) { name = name };
            materials.Add(material);
            material.SetColor("_Color", new Color32((byte)(pigment >> 16), (byte)(pigment >> 8), (byte)pigment, 255));
            if (color != null) material.SetTexture("_MainTex", color.Get());
            material.SetTexture("_Detail", detail.Get());
            material.SetFloat("_Scale", scale);
            material.SetFloat("_Relief", relief);
            material.SetFloat("_Metallic", metallic);
            material.SetVector("_Gloss", new Vector4(smoothnessMultiplier, smoothnessOffset, 0f, 0f));
            return material;
        }

        // The panorama was rendered once at this point inside the finished chart house.
        // Unity imports its specular convolution as an HDR cubemap with glossy mip levels.
        internal ReflectionProbe InstallReflection(Transform site)
        {
            var mount = new GameObject("Отражения штурманской").transform;
            mount.SetParent(site, false);
            mount.localPosition = new Vector3(-.4f, 1.90f, .45f);
            var probe = mount.gameObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.customBakedTexture = reflection.Get();
            probe.boxProjection = true;
            probe.size = new Vector3(11.9f, 3.8f, 7.9f);
            probe.center = new Vector3(.4f, 0f, -.45f);
            probe.blendDistance = .15f;
            probe.importance = 2;
            probe.intensity = ReflectionLevel(Shader.GetGlobalFloat("_HarborDusk"));
            installedProbe = probe;
            return probe;
        }

        internal static float ReflectionLevel(float dusk) => Mathf.Lerp(.85f, .30f, dusk);

        internal Material GetFinish(string name)
        {
            foreach (var material in materials)
                if (material.name == name) return material;
            throw new InvalidOperationException("Unknown chart-house finish '" + name + "'.");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (installedProbe)
            {
                installedProbe.enabled = false;
                installedProbe.customBakedTexture = null;
            }
            foreach (var material in materials)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(material);
                else UnityEngine.Object.DestroyImmediate(material);
            }
            materials.Clear();
            --owners;
            ReleaseUnused();
        }
    }
}
