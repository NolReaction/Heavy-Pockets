#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    internal sealed partial class RaidScenery
    {
        private sealed class PlantMesh
        {
            internal readonly List<Vector3> Vertices = new List<Vector3>();
            internal readonly List<int> Triangles = new List<int>();
            internal readonly List<Color32> Colors = new List<Color32>();
            internal List<Vector2> CardUVs;
            internal readonly System.Random Random;

            internal PlantMesh(int seed) { Random = new System.Random(seed); }
            internal float Range(float min, float max) => Mathf.Lerp(min, max, (float)Random.NextDouble());

            internal int Vertex(Vector3 point, Color tint)
            {
                Vertices.Add(point);
                Colors.Add(tint);
                CardUVs?.Add(Vector2.zero);
                return Vertices.Count - 1;
            }

            internal int CardVertex(Vector3 point, Color tint, float u, float v)
            {
                if (CardUVs == null) CardUVs = new List<Vector2>(new Vector2[Vertices.Count]);
                tint.a = 0f;
                int index = Vertex(point, tint);
                CardUVs[index] = new Vector2(u, v);
                return index;
            }

            internal void Triangle(int a, int b, int c)
            {
                Triangles.Add(a); Triangles.Add(b); Triangles.Add(c);
            }

            internal void Stem(Vector3 foot, Vector3 tip, float radius, Color bark, int sides = 6)
            {
                int first = Vertices.Count;
                for (int i = 0; i < sides; i++)
                {
                    Vector3 rim = Compass(i * 2f * Mathf.PI / sides) * radius;
                    Vertex(foot + rim, bark * .77f);
                    Vertex(tip + rim * .42f, bark);
                }
                for (int i = 0; i < sides; i++)
                {
                    int a = first + 2*i, b = first + 2*((i+1) % sides);
                    Triangle(a, a+1, b); Triangle(b, a+1, b+1);
                }
            }

            // Uneven lobes with soft normals. Overlapping crowns break up the tree's outline.
            internal void Lobe(Vector3 center, Vector3 size, Color shade, Color sunlit, int sides)
            {
                int bottom = Vertex(center - Vector3.up * size.y * .72f, shade);
                float phase = Range(0, 6.28f);
                for (int tier = 0; tier < 2; tier++)
                    for (int i = 0; i < sides; i++)
                    {
                        Vector3 ring = Compass(phase + (i + tier * .28f) * 2f * Mathf.PI / sides);
                        float radius = Range(.72f, 1.15f) * (tier == 0 ? 1f : .89f);
                        var offset = new Vector3(ring.x * size.x * radius, size.y *
                            (tier * .61f + Range(-.28f, .07f)), ring.z * size.z * radius);
                        Vertex(center + offset, Color.Lerp(shade, sunlit, tier * .52f + Range(.04f, .43f)));
                    }
                int top = Vertex(center + Vector3.up * size.y * .82f, sunlit);
                for (int i = 0; i < sides; i++)
                {
                    int a = bottom + 1+i, b = bottom + 1+(i+1)%sides;
                    Triangle(bottom, a, b);
                    Triangle(a, a+sides, b); Triangle(b, a+sides, b+sides);
                    Triangle(top, b+sides, a+sides);
                }
            }
        }

        private static Vector3 Compass(float radians) =>
            new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians));

        private void BakeVegetation(CellBake bake)
        {
            // Three near belts frame the yard. Gaps on the seaward end keep the bay and headlands visible.
            var groves = new[]
            {
                new Vector2(-88,-31), new Vector2(-107,9), new Vector2(-79,39),
                new Vector2(-91,71), new Vector2(-47,82), new Vector2(-16,77),
                new Vector2(17,94), new Vector2(59,90), new Vector2(-84,-83),
                new Vector2(-42,-79), new Vector2(4,-85), new Vector2(43,-79),
                new Vector2(-132,-49), new Vector2(-147,49), new Vector2(-76,119)
            };
            for (int i = 0; i < groves.Length; i++)
            {
                var placement = new PlantMesh(6821 + i * 31);
                for (int t = 0; t < 19; t++)
                {
                    float x = groves[i].x + placement.Range(-17f,17f);
                    float z = groves[i].y + placement.Range(-12f,12f);
                    if (Mathf.Abs(x) < RaidMap.RoomX + 4f && Mathf.Abs(z) < RaidMap.RoomZ + 4f) continue;
                    float y = CoastHeight(x,z);
                    if (y < 0f) continue;
                    var foot = new Vector3(x,y-.13f,z);
                    float height = placement.Range(5.2f,11.3f);
                    var tile = bake.At(x,z);
                    int variant = (t + i * 3) % 4;
                    float angle = t * 2.39996f + i * .79f;
                    BuildShelterTree(tile.Woods, foot, height, variant, angle, true);
                    BuildShelterTree(tile.Woodline, foot, height, variant, angle, false);
                }
                for (int n = 0; n < 7; n++)
                {
                    float x = groves[i].x + placement.Range(-21,21), z = groves[i].y + placement.Range(-15,15);
                    if (Mathf.Abs(x) < RaidMap.RoomX + 3f && Mathf.Abs(z) < RaidMap.RoomZ + 3f) continue;
                    float y = CoastHeight(x,z);
                    if (y < 0f) continue;
                    float scale = placement.Range(.45f,1.8f);
                    bake.At(x,z).Woods.Lobe(new Vector3(x,y-.06f,z), new Vector3(1.25f,.63f,.83f)*scale,
                        Rgb(0x515854), Rgb(0x969586), 8);
                }
            }
            BakeMeadowPlants(bake);
        }

        private static void BuildShelterTree(PlantMesh mesh, Vector3 foot, float height,
            int variant, float phase, bool detailed)
        {
            float radius = height * (.254f + .038f * Mathf.Sin(phase));
            Vector3 lean = new Vector3(.045f + .018f * Mathf.Cos(phase), 0f, .022f) * height;
            Vector3 top = foot + Vector3.up * height + lean;
            Color tint = Color.Lerp(Rgb(0xefecce), Rgb(0xd0ded1), .5f + .5f * Mathf.Sin(phase * 2.8f));
            Color bottom = Color.Lerp(tint, Rgb(0x8e9f8c), .34f);
            if (detailed) mesh.Stem(foot, Vector3.Lerp(foot,top,.41f), height * .012f, Rgb(0x766954), 5);
            for (int side = 0; side < 3; side++)
            {
                Vector3 width = Compass(phase + side * Mathf.PI / 3f) * radius;
                Vector3 fold = detailed ? new Vector3(width.z, 0, -width.x) * .28f : Vector3.zero;
                float u0 = (variant + (side == 1 ? 1f : 0f)) * .25f;
                float u1 = (variant + (side == 1 ? 0f : 1f)) * .25f;
                int a = mesh.CardVertex(foot - width + fold, bottom, u0, 0f);
                int b = mesh.CardVertex(top - width + fold, tint, u0, 1f);
                int c = mesh.CardVertex(top + width + fold, tint, u1, 1f);
                int d = mesh.CardVertex(foot + width + fold, bottom, u1, 0f);
                if (detailed)
                {
                    float mid = (u0 + u1) * .5f;
                    int e = mesh.CardVertex(foot, bottom, mid, 0f);
                    int f = mesh.CardVertex(top, tint, mid, 1f);
                    mesh.Triangle(a,b,f); mesh.Triangle(a,f,e);
                    mesh.Triangle(e,f,c); mesh.Triangle(e,c,d);
                }
                else { mesh.Triangle(a,b,c); mesh.Triangle(a,c,d); }
            }
        }

        private static bool AllowsMeadow(float x, float z)
        {
            Vector3 house = ChartHouseLayout.Origin;
            if (Mathf.Abs(x-house.x)<6.65f && Mathf.Abs(z-house.z)<4.7f ||
                Mathf.Abs(x-house.x-2.6f)<1.3f && z > house.z-9f && z < house.z-3f) return false;
            if (Mathf.Abs(x)<15.2f && Mathf.Abs(z+1f)<10.3f || Mathf.Abs(x-11f)<3.8f ||
                Mathf.Abs(x-8f)<58.4f && Mathf.Abs(z-31f)<3.5f ||
                Mathf.Abs(x-47f)<3.35f && Mathf.Abs(z-40f)<8.9f ||
                Mathf.Abs(x+54f)<2.9f || Mathf.Abs(x-38f)<10f && Mathf.Abs(z-20f)<9.5f || x>64.5f)
                return false;
            foreach (var solid in OutdoorLayout.Solids)
            {
                Vector3 mid = solid.Bounds.center, half = solid.Bounds.extents;
                if (Mathf.Abs(x-mid.x)<half.x+1f && Mathf.Abs(z-mid.z)<half.z+1f) return false;
            }
            return true;
        }

        private void BakeMeadowPlants(CellBake bake)
        {
            for (int sector = 0; sector < 4; sector++)
            {
                var scatter = new PlantMesh(5412 + 11*sector);
                for (int p = 0; p < 460; p++)
                {
                    float x = scatter.Range(0f,RaidMap.RoomX-.9f) * (sector%2==0 ? -1f : 1f);
                    float z = scatter.Range(0f,RaidMap.RoomZ-.9f) * (sector<2 ? -1f : 1f);
                    if (!AllowsMeadow(x,z) || Meadow(x,z)<scatter.Range(.28f,.72f)) continue;
                    Color baseTone = Color.Lerp(Rgb(0x535f3c),Rgb(0x838348),scatter.Range(0,1));
                    Color tips = Color.Lerp(Rgb(0x858f54),Rgb(0xc0ad76),scatter.Range(0,1));
                    bool reeds = scatter.Random.Next(8)==0;
                    for (int tuft = 0; tuft < 5; tuft++)
                    {
                        float tx = x+scatter.Range(-1.3f,1.3f), tz=z+scatter.Range(-.94f,.94f);
                        if (!AllowsMeadow(tx,tz)) continue;
                        var mesh = bake.At(tx,tz).Sedge;
                        var origin = new Vector3(tx,CoastHeight(tx,tz)-.02f,tz);
                        int count = reeds ? 8 : 6;
                        for (int blade = 0; blade < count; blade++)
                        {
                            Vector3 outwards = Compass(scatter.Range(0,6.28f));
                            Vector3 across = new Vector3(-outwards.z,0,outwards.x) * scatter.Range(.021f,.043f);
                            float h = reeds ? scatter.Range(.37f,.65f) : scatter.Range(.18f,.39f);
                            Vector3 start = origin + outwards * scatter.Range(.02f,.13f);
                            Vector3 middle = start + Vector3.up*(h*.62f) + outwards*(h*.22f);
                            Vector3 end = start + Vector3.up*h + outwards*(h*.50f);
                            int a = mesh.Vertex(start - across,baseTone);
                            int b = mesh.Vertex(start + across,baseTone);
                            int c = mesh.Vertex(middle + across*.64f,tips);
                            int d = mesh.Vertex(middle - across*.64f,tips);
                            int e = mesh.Vertex(end,tips);
                            mesh.Triangle(a,b,c); mesh.Triangle(a,c,d); mesh.Triangle(d,c,e);
                            if (reeds && blade<2)
                            {
                                Vector3 head = end - outwards*.025f;
                                int s = mesh.Vertex(head - across*.5f,Rgb(0xa19768));
                                int t = mesh.Vertex(head + across*.5f,Rgb(0xbdb084));
                                int u = mesh.Vertex(head + Vector3.up*.085f + outwards*.055f,Rgb(0xcabe91));
                                mesh.Triangle(s,t,u);
                            }
                        }
                    }
                }
            }
        }
    }
}
#endif
