using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HeavyPockets
{
    // The four vertex channels paint asphalt, sand, dry grass and long meadow respectively.
    // The same material can be painted onto any mesh at the port's metre scale.
    internal sealed class CoastalSurfaceField : MonoBehaviour
    {
        private readonly List<Mesh> ownedMeshes = new List<Mesh>();
        private Material blades;
        internal Material GrovePaint { private get; set; }

        internal static GameObject Create(Transform parent, Vector2 center, Vector2 size,
            Func<Vector2, float, bool> excludeGrass = null, GroundOpening foundation = default)
        {
            var field = new GameObject("Coastal surface garden").AddComponent<CoastalSurfaceField>();
            field.transform.SetParent(parent, false);
            field.transform.localPosition = new Vector3(center.x, 0, center.y);
            field.Build(size, center, excludeGrass, foundation);
            return field.gameObject;
        }

        internal void BendAt(Vector3 feet)
        {
            if (blades != null) blades.SetVector("_CoastalFeet", new Vector4(feet.x, feet.y, feet.z, 1f));
            if (GrovePaint != null) GrovePaint.SetVector("_CoastalFeet", new Vector4(feet.x, feet.y, feet.z, 1f));
        }

        private static float Blend(float a, float b, float value) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, value));

        internal static Color Layers(float x, float z)
        {
            // Authored in the 144 x 112 m garden's local coordinates.
            float drift = Mathf.PerlinNoise(34 + x * .079f, 62 + z * .079f) - .5f;
            float swale = Mathf.Sin(z * .11f + x * .036f);
            float edge = Mathf.Max((Mathf.Abs(z) - 46f) / 10f, (x - 57f) / 14f);
            float east = Blend(1, 13, x) * (1f - Blend(.06f,.94f,edge + drift*.29f));
            float lane = -14f + 2.8f * Mathf.Sin((z + 20f) * .078f) + .061f * (z + 20f);
            float road = 1f - Blend(2.85f, 4.7f, Mathf.Abs(x - lane) + drift * .8f);
            float dune = Mathf.Sqrt(Mathf.Pow((x + 49f) / 30f, 2) + Mathf.Pow((z + 35f) / 24f, 2));
            float sand = 1f - Blend(.82f, 1.18f, dune + swale * .049f + drift * .095f);
            float dry = Blend(21f, 33f, z + 4f * Mathf.Sin(x * .07f) + drift * 4f);
            float meadow = Blend(-1f, 13f, z + swale * 2.7f + drift * 3f) * (1f - Blend(-31f, -21f, x));
            return new Color(Mathf.Lerp(road, .62f, east), Mathf.Lerp(sand, .89f, east),
                Mathf.Lerp(dry, .84f, east), meadow * (1f - east));
        }

        private static List<float> GroundAxis(float center, float size)
        {
            int cells = Mathf.Max(1, Mathf.CeilToInt(size / 1.35f));
            var axis = new List<float>(cells + 3);
            for (int i = 0; i <= cells; i++) axis.Add(center + (i / (float)cells - .5f) * size);
            return axis;
        }

        private void Build(Vector2 size, Vector2 center, Func<Vector2, float, bool> excludeGrass, GroundOpening foundation)
        {
            var xs = GroundAxis(center.x, size.x);
            var zs = GroundAxis(center.y, size.y);
            foundation.Split(xs,zs);
            int nx = xs.Count - 1, nz = zs.Count - 1;
            var vertices = new Vector3[(nx + 1) * (nz + 1)];
            var colors = new Color32[vertices.Length];
            var normals = new Vector3[vertices.Length];
            var triangles = new List<int>(nx * nz * 6);
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    int k = j * (nx + 1) + i;
                    float x = xs[i] - center.x, z = zs[j] - center.y;
                    vertices[k] = new Vector3(x, 0, z);
                    normals[k] = Vector3.up;
                    colors[k] = Layers(x * 144f / size.x, z * 112f / size.y);
                    if (i == nx || j == nz || foundation.Covers(xs[i],xs[i+1],zs[j],zs[j+1])) continue;
                    triangles.Add(k); triangles.Add(k + nx + 1); triangles.Add(k + nx + 2);
                    triangles.Add(k); triangles.Add(k + nx + 2); triangles.Add(k + 1);
                }
            var ground = new Mesh { name = "Coastal surface paint", indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            ground.vertices = vertices;
            ground.normals = normals;
            ground.colors32 = colors;
            ground.SetTriangles(triangles,0);
            Mount(gameObject, ground, Resources.Load<Material>("SurfaceGarden/Coastal surface"));
            blades = new Material(Resources.Load<Material>("SurfaceGarden/Meadow blades")) { name = "Grass underfoot" };
            var random = new System.Random(27147);
            int cx = Mathf.CeilToInt(size.x / 14f), cz = Mathf.CeilToInt(size.y / 14f);
            for (int j = 0; j < cz; j++)
                for (int i = 0; i < cx; i++)
                {
                    Vector2 span = new Vector2(size.x / cx, size.y / cz);
                    Vector2 origin = new Vector2(-size.x * .5f + i * span.x, -size.y * .5f + j * span.y);
                    if ((origin.x / size.x) * 144f > 10f) continue;
                    BuildGrassTile(origin, span, size, center, random, excludeGrass);
                }
        }

        private sealed class GrassMesh
        {
            internal readonly List<Vector3> Points = new List<Vector3>();
            internal readonly List<Color32> Colors = new List<Color32>();
            internal readonly List<Vector2> Flex = new List<Vector2>();
            internal readonly List<int> Triangles = new List<int>();
            internal void Blade(Vector3 root, Vector3 tip, Vector3 spread, Color low, Color high)
            {
                int k = Points.Count;
                Vector3 mid = Vector3.Lerp(root, tip, .58f) + Vector3.up * (tip.y - root.y) * .15f;
                Points.Add(root - spread); Points.Add(root + spread);
                Points.Add(mid - spread * .59f); Points.Add(mid + spread * .59f); Points.Add(tip);
                Colors.Add(low); Colors.Add(low); Colors.Add(high); Colors.Add(high); Colors.Add(high);
                Flex.Add(Vector2.zero); Flex.Add(Vector2.zero);
                Flex.Add(Vector2.up * .58f); Flex.Add(Vector2.up * .58f); Flex.Add(Vector2.up);
                Triangles.Add(k); Triangles.Add(k + 2); Triangles.Add(k + 1);
                Triangles.Add(k + 1); Triangles.Add(k + 2); Triangles.Add(k + 3);
                Triangles.Add(k + 2); Triangles.Add(k + 4); Triangles.Add(k + 3);
            }
        }
        private static float Sample(System.Random r) => (float)r.NextDouble();

        private void BuildGrassTile(Vector2 origin, Vector2 span, Vector2 fieldSize, Vector2 center,
            System.Random rng, Func<Vector2, float, bool> excludeGrass)
        {
            var grass = new GrassMesh();
            int count = Mathf.CeilToInt(span.x * span.y * 3.4f);
            for (int i = 0; i < count; i++)
            {
                Vector2 p = new Vector2(Sample(rng) * span.x, Sample(rng) * span.y);
                Vector2 field = origin + p;
                float x = field.x / fieldSize.x * 144f, z = field.y / fieldSize.y * 112f;
                Color layers = Layers(x, z);
                float cover = (1f - layers.r) * (1f - layers.g) * (1f - Blend(-1, 10, x));
                float patch = Mathf.PerlinNoise(x * .21f + 12, z * .21f + 31);
                if (Sample(rng) > cover * Mathf.Lerp(.12f, .96f, Blend(.21f, .70f, patch))) continue;
                float longGrass = Mathf.Max(layers.a, layers.b * .85f);
                float h = Mathf.Lerp(.105f, .36f, longGrass) * Mathf.Lerp(.64f, 1.28f, Sample(rng));
                // Include the furthest tip plus the space it can bend under a nearby foot.
                if (excludeGrass != null && excludeGrass(center + field, h * 1.52f + .28f)) continue;
                Color tip = Color.Lerp(new Color(.575f, .605f, .413f), new Color(.50f, .56f, .356f), layers.a);
                tip = Color.Lerp(tip, new Color(.68f, .625f, .44f), layers.b) * Mathf.Lerp(.89f, 1.045f, Sample(rng));
                Color low = Color.Lerp(tip, new Color(.31f, .365f, .255f), .60f);
                int bladeCount = 4 + (int)(longGrass * 3.5f);
                for (int n = 0; n < bladeCount; n++)
                {
                    float angle = Sample(rng) * Mathf.PI * 2;
                    var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    var spread = new Vector3(-direction.z, 0, direction.x) * Mathf.Lerp(.009f, .023f, Sample(rng));
                    Vector3 foot = new Vector3(p.x, -.008f, p.y) + direction * h * Sample(rng) * .7f;
                    Vector3 end = foot + Vector3.up * h * Mathf.Lerp(.74f, 1.15f, Sample(rng)) + direction * h * Mathf.Lerp(.31f, .81f, Sample(rng));
                    grass.Blade(foot, end, spread, low, tip);
                }
                if (longGrass > .51f && patch > .49f && Sample(rng) < .18f)
                {
                    // Soft oat panicles in the dry plots; small flowering yarrow and
                    // broad lance-shaped leaves at the feet of the taller meadow grasses.
                    bool flowers=layers.a>.50f && layers.b<.50f;
                    Vector3 foot = new Vector3(p.x,0,p.y);
                    Vector3 crown = foot + new Vector3(.049f,h*(flowers ? 1.12f : 1.42f),.036f);
                    grass.Blade(foot,crown,new Vector3(.005f,0,.003f),low,tip);
                    Color head = flowers ? new Color(.89f,.86f,.67f) : new Color(.77f,.68f,.47f);
                    for (int n=0;n<5;n++)
                    {
                        float angle=n*2.39996f+p.x;
                        Vector3 dir=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                        Vector3 side=new Vector3(-dir.z,0,dir.x);
                        if(flowers)
                        {
                            grass.Blade(foot,foot+dir*h*.53f+Vector3.up*h*.31f,side*.024f,low,tip);
                            Vector3 bud=crown+dir*.035f+Vector3.up*(.008f*(n%2));
                            grass.Blade(crown,bud,side*.009f,tip,head);
                            grass.Blade(bud,bud+dir*.028f+Vector3.up*.016f,side*.018f,head,head*1.08f);
                        }
                        else
                        {
                            Vector3 node=crown-Vector3.up*.11f+Vector3.up*(n*.018f);
                            grass.Blade(node,node+dir*(.036f+.006f*(4-n))+Vector3.up*.056f,side*.007f,tip,head);
                        }
                    }
                }
            }
            if (grass.Points.Count == 0) return;
            var patchRoot = new GameObject("Coastal grass patch");
            patchRoot.transform.SetParent(transform, false);
            patchRoot.transform.localPosition = new Vector3(origin.x, 0, origin.y);
            var mesh = new Mesh { name = "Fine blades and seed heads" };
            mesh.SetVertices(grass.Points);
            mesh.SetColors(grass.Colors);
            mesh.SetUVs(0, grass.Flex);
            mesh.SetTriangles(grass.Triangles, 0);
            var up = new Vector3[grass.Points.Count];
            Array.Fill(up, Vector3.up);
            mesh.normals = up;
            var renderer = Mount(patchRoot, mesh, blades);
            var lod = patchRoot.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(.24f, new Renderer[] { renderer }) });
            lod.RecalculateBounds();
        }

        private MeshRenderer Mount(GameObject go, Mesh mesh, Material mat)
        {
            ownedMeshes.Add(mesh);
            mesh.UploadMeshData(true);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            return renderer;
        }

        private void OnDestroy()
        {
            foreach (var mesh in ownedMeshes) Release(mesh);
            Release(blades);
        }
        private static void Release(Object item)
        {
            if (Application.isPlaying) Destroy(item);
            else DestroyImmediate(item);
        }
    }
}
