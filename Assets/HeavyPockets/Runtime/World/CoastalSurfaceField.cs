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

        internal static GameObject Create(Transform parent, Vector2 center, Vector2 size)
        {
            var field = new GameObject("Coastal surface garden").AddComponent<CoastalSurfaceField>();
            field.transform.SetParent(parent, false);
            field.transform.localPosition = new Vector3(center.x, 0, center.y);
            field.Build(size);
            return field.gameObject;
        }

        internal void BendAt(Vector3 feet)
        {
            if (blades != null) blades.SetVector("_CoastalFeet", new Vector4(feet.x, feet.y, feet.z, 1f));
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

        private void Build(Vector2 size)
        {
            int nx = Mathf.Max(1, Mathf.CeilToInt(size.x / 1.35f));
            int nz = Mathf.Max(1, Mathf.CeilToInt(size.y / 1.35f));
            var vertices = new Vector3[(nx + 1) * (nz + 1)];
            var colors = new Color32[vertices.Length];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[nx * nz * 6];
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    int k = j * (nx + 1) + i;
                    float u = i / (float)nx - .5f, v = j / (float)nz - .5f;
                    vertices[k] = new Vector3(u * size.x, 0, v * size.y);
                    normals[k] = Vector3.up;
                    colors[k] = Layers(u * 144f, v * 112f);
                    if (i == nx || j == nz) continue;
                    int t = 6 * (j * nx + i);
                    triangles[t] = k; triangles[t + 1] = k + nx + 1; triangles[t + 2] = k + nx + 2;
                    triangles[t + 3] = k; triangles[t + 4] = k + nx + 2; triangles[t + 5] = k + 1;
                }
            var ground = new Mesh { name = "Coastal surface paint", indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            ground.vertices = vertices;
            ground.normals = normals;
            ground.colors32 = colors;
            ground.triangles = triangles;
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
                    BuildGrassTile(origin, span, size, random);
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

        private void BuildGrassTile(Vector2 origin, Vector2 span, Vector2 fieldSize, System.Random rng)
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
                float h = Mathf.Lerp(.12f, .30f, longGrass) * Mathf.Lerp(.64f, 1.28f, Sample(rng));
                Color tip = Color.Lerp(new Color(.575f, .605f, .413f), new Color(.50f, .56f, .356f), layers.a);
                tip = Color.Lerp(tip, new Color(.68f, .625f, .44f), layers.b) * Mathf.Lerp(.89f, 1.045f, Sample(rng));
                Color low = Color.Lerp(tip, new Color(.31f, .365f, .255f), .60f);
                int bladeCount = 4 + (int)(longGrass * 2.5f);
                for (int n = 0; n < bladeCount; n++)
                {
                    float angle = Sample(rng) * Mathf.PI * 2;
                    var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    var spread = new Vector3(-direction.z, 0, direction.x) * Mathf.Lerp(.007f, .0145f, Sample(rng));
                    Vector3 foot = new Vector3(p.x, -.008f, p.y) + direction * h * Sample(rng) * .7f;
                    Vector3 end = foot + Vector3.up * h * Mathf.Lerp(.74f, 1.15f, Sample(rng)) + direction * h * Mathf.Lerp(.31f, .81f, Sample(rng));
                    grass.Blade(foot, end, spread, low, tip);
                }
                if (layers.a > .65f && layers.b < .5f && patch > .44f && Sample(rng) < .047f)
                {
                    // A small group of pale flowerheads, at runner-ankle height.
                    Vector3 foot = new Vector3(p.x, 0, p.y), crown = foot + new Vector3(.045f, h * 1.35f, .036f);
                    grass.Blade(foot, crown, new Vector3(.005f, 0, 0), low, tip);
                    for (int petal = 0; petal < 4; petal++)
                    {
                        float angle = petal * 1.5708f;
                        var radial = new Vector3(Mathf.Cos(angle), .25f, Mathf.Sin(angle));
                        grass.Blade(crown, crown + radial * .036f, new Vector3(-radial.z, 0, radial.x) * .011f,
                            new Color(.84f, .72f, .43f), new Color(.94f, .90f, .74f));
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
