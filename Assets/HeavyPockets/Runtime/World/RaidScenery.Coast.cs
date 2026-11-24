using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeavyPockets
{
    internal sealed partial class RaidScenery
    {
        private Material shorePlants;

        private void BuildCoast()
        {
            var earth = new Material(Resources.Load<Shader>("CoastalGround")) { name = "Harbour gravel and meadow" };
            earth.SetTexture("_Grain", Resources.Load<Texture2D>("DepotGrain"));
            shorePlants = new Material(earth) { name = "Coastal vegetation and granite" };
            shorePlants.SetFloat("_Habitat", 1f);
            earth.SetTexture("_CoastColor", Resources.Load<Texture2D>("CoastRockColor"));
            earth.SetTexture("_CoastNormals", Resources.Load<Texture2D>("CoastRockNormals"));
            var sea = new Material(Resources.Load<Shader>("CoastalWater")) { name = "Tidal water" };
            surfaces.Add(earth);
            surfaces.Add(shorePlants);
            surfaces.Add(sea);

            // The central flat is the networked walking plane. The hills start beyond its boundary.
            // Keep the 4 m shoreline samples at the quay. Spacing grows to 16 m in the distant hills.
            var xs = CoastSamples(-340, 420, -96, 116);
            var zs = CoastSamples(-364, 364, -80, 80);
            int columns = xs.Count, rows = zs.Count;
            var vertices = new List<Vector3>(columns * rows);
            var colors = new List<Color32>(columns * rows);
            var indices = new List<int>((columns - 1) * (rows - 1) * 6);
            for (int row = 0; row < rows; row++)
            {
                float z = zs[row];
                for (int column = 0; column < columns; column++)
                {
                    float x = xs[column];
                    vertices.Add(new Vector3(x, CoastHeight(x,z) - .035f, z));
                    colors.Add(new Color(.54f + .46f * Noise(x, z, .043f), Meadow(x,z), 0));
                    if (row == 0 || column == 0) continue;
                    int d = row * columns + column, a = d - columns - 1;
                    indices.Add(a); indices.Add(d - 1); indices.Add(d);
                    indices.Add(a); indices.Add(d); indices.Add(a + 1);
                }
            }
            AddCoastMesh("Glacial shore and marsh", vertices, indices, colors, earth, true);
            AddCoastMesh("Harbour inlet", new List<Vector3> {
                new Vector3(69,-1.6f,-420), new Vector3(69,-1.6f,420),
                new Vector3(430,-1.6f,420), new Vector3(430,-1.6f,-420)
            }, new List<int> { 0,1,2,0,2,3 }, null, sea, false);
        }

        private static List<float> CoastSamples(int start, int end, int denseFrom, int denseTo)
        {
            var coordinates = new List<float>();
            for (int p = start; p < end; )
            {
                coordinates.Add(p);
                int distance = Mathf.Max(denseFrom - p, p - denseTo);
                p += distance < 8 ? 4 : distance < 100 ? 8 : 16;
            }
            coordinates.Add(end);
            return coordinates;
        }

        private static float Noise(float x, float z, float frequency)
        {
            return Mathf.PerlinNoise(83.21f + x * frequency, 31.7f + z * frequency);
        }

        private static float Meadow(float x, float z)
        {
            float grass = .08f + .71f * Noise(x,z,.043f) + .38f * Noise(x + 115,z,.12f);
            // A worn unpaved route between the western railhead and the customs apron.
            float track = z - .32f*x - 3.2f*Mathf.Sin(x*.063f) + 1.8f;
            float wheelRuts = Mathf.Abs(Mathf.Abs(track) - 1.04f);
            float trailMask = Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.2f,1.5f,wheelRuts));
            return Mathf.Clamp01(grass * Mathf.Lerp(trailMask,1f,Mathf.InverseLerp(6,24,x)));
        }

        private static float CoastHeight(float x, float z)
        {
            float boundary = Mathf.Max(Mathf.Abs(z) - RaidMap.RoomZ, -x - RaidMap.RoomX);
            float rise = Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,92,boundary));
            float ridges = 8f + 36f*Noise(x,z,.0084f) + 13f*Noise(x,z,.022f)
                + 6f*Mathf.Sin(x*.019f-z*.013f);
            float nearShore = 73f + 11f*rise + 26f*rise*Mathf.Sin(z*.021f);
            float nearLand = rise*ridges;
            if (x <= nearShore) return nearLand;

            float inland = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0, 9f, x - nearShore));
            float seabed = Mathf.Lerp(nearLand, -5.8f, inland);
            // Far islands frame the shipping channel while leaving open water down its centre.
            float farShore = 192f + 48f*Mathf.Cos(z*.018f) + 21f*Mathf.Sin(z*.042f);
            float island = Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(0,61,x - farShore));
            return Mathf.Lerp(seabed, 5f + 37f*Noise(x,z,.012f) + 7f*Noise(x,z,.035f), island);
        }

        private void AddCoastMesh(string name, List<Vector3> vertices, List<int> indices,
            List<Color32> colors, Material material, bool castShadows)
        {
            if (vertices.Count == 0) return;
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            if (colors != null) mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            mesh.UploadMeshData(true);
            shellMeshes.Add(mesh);
            var obj = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            obj.transform.SetParent(root, false);
            obj.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }
    }
}
