using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    // Bakes disconnected hard-surface parts and tapered fabric sections into one mesh
    // per rigid piece. The crate and avatar never need colliders or per-detail objects.
    internal sealed class PropGeometry
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector2> uv = new List<Vector2>();
        private readonly List<Material> materials = new List<Material>();
        private readonly List<List<int>> submeshes = new List<List<int>>();

        private List<int> Triangles(Material material)
        {
            int index = materials.IndexOf(material);
            if (index >= 0) return submeshes[index];
            materials.Add(material);
            var triangles = new List<int>();
            submeshes.Add(triangles);
            return triangles;
        }

        internal void Box(Material material, float x, float y, float z, float w, float h, float d,
            float bevel, float pitch = 0, float yaw = 0, float roll = 0)
        {
            Vector3 center = new Vector3(x, y, z);
            Quaternion rotation = Quaternion.Euler(pitch, yaw, roll);
            float rim = Mathf.Min(bevel, Mathf.Min(h, Mathf.Min(w, d)) * .34f);
            float hw = w * .5f, hh = h * .5f, hd = d * .5f;
            float cut = Mathf.Min(bevel * 1.65f, Mathf.Min(hw, hd) * .54f);
            var rings = new[] {
                BoxRing(hw - rim, -hh, hd - rim, cut * .62f, center, rotation),
                BoxRing(hw, -hh + rim, hd, cut, center, rotation),
                BoxRing(hw, hh - rim, hd, cut, center, rotation),
                BoxRing(hw - rim, hh, hd - rim, cut * .62f, center, rotation)
            };
            var triangles = Triangles(material);
            for (int band = 1; band < rings.Length; band++)
                for (int i = 0; i < 8; i++)
                {
                    int j = (i + 1) % 8;
                    FlatQuad(triangles, rings[band - 1][i], rings[band][i], rings[band][j], rings[band - 1][j]);
                }
            Cap(triangles, rings[0], false);
            Cap(triangles, rings[3], true);
        }

        private static Vector3[] BoxRing(float w, float y, float d, float c, Vector3 center, Quaternion rotation)
        {
            var ring = new[] {
                new Vector3(w,y,-d+c), new Vector3(w,y,d-c), new Vector3(w-c,y,d), new Vector3(-w+c,y,d),
                new Vector3(-w,y,d-c), new Vector3(-w,y,-d+c), new Vector3(-w+c,y,-d), new Vector3(w-c,y,-d)
            };
            for (int i = 0; i < ring.Length; i++)
                ring[i] = center + rotation * ring[i];
            return ring;
        }

        private void FlatQuad(List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            uv.Add(Vector2.zero); uv.Add(Vector2.up); uv.Add(Vector2.one); uv.Add(Vector2.right);
            t.Add(start); t.Add(start + 1); t.Add(start + 2);
            t.Add(start); t.Add(start + 2); t.Add(start + 3);
        }

        private void Cap(List<int> t, Vector3[] ring, bool top)
        {
            int start = vertices.Count;
            foreach (Vector3 v in ring)
            {
                vertices.Add(v);
                uv.Add(new Vector2(v.x, v.z));
            }
            for (int i = 1; i < ring.Length - 1; i++)
            {
                t.Add(start);
                t.Add(start + (top ? i + 1 : i));
                t.Add(start + (top ? i : i + 1));
            }
        }

        internal void Oval(Material material, float x, float y, float z, float width, float height, float depth, Vector3[] profile)
        {
            const int sides = 12;
            var t = Triangles(material);
            int start = vertices.Count;
            var bottom = new Vector3[sides];
            var top = new Vector3[sides];
            for (int band = 0; band < profile.Length; band++)
            {
                var section = profile[band];
                for (int side = 0; side < sides; side++)
                {
                    float angle = 2f * Mathf.PI * side / sides;
                    var point = new Vector3(x + width * .5f * section.y * Mathf.Cos(angle),
                        y + height * section.x, z + depth * .5f * section.z * Mathf.Sin(angle));
                    vertices.Add(point);
                    uv.Add(new Vector2(side / (float)sides, section.x));
                    if (band == 0) bottom[side] = point;
                    if (band == profile.Length - 1) top[side] = point;
                    if (band == 0) continue;
                    int upper = start + band * sides;
                    int next = (side + 1) % sides;
                    t.Add(upper - sides + side); t.Add(upper + side); t.Add(upper + next);
                    t.Add(upper - sides + side); t.Add(upper + next); t.Add(upper - sides + next);
                }
            }
            Cap(t, bottom, false);
            Cap(t, top, true);
        }

        internal void Apply(Transform target, List<Mesh> ownedMeshes)
        {
            var mesh = new Mesh { name = target.name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = submeshes.Count;
            for (int i = 0; i < submeshes.Count; i++)
                mesh.SetTriangles(submeshes[i], i, false);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            target.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            target.gameObject.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
            ownedMeshes.Add(mesh);
        }
    }
}
