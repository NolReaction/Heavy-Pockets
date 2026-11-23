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
            internal readonly System.Random Random;

            internal PlantMesh(int seed) { Random = new System.Random(seed); }
            internal float Range(float min, float max) => Mathf.Lerp(min, max, (float)Random.NextDouble());

            internal int Vertex(Vector3 point, Color tint)
            {
                Vertices.Add(point);
                Colors.Add(tint);
                return Vertices.Count - 1;
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

        private void BuildVegetation(Material plants)
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
                var woods = new PlantMesh(6821 + i * 31);
                for (int t = 0; t < 19; t++)
                {
                    float x = groves[i].x + woods.Range(-17f,17f);
                    float z = groves[i].y + woods.Range(-12f,12f);
                    if (Mathf.Abs(x) < RaidMap.RoomX + 4f && Mathf.Abs(z) < RaidMap.RoomZ + 4f) continue;
                    float y = CoastHeight(x,z);
                    if (y < 0f) continue;
                    var foot = new Vector3(x,y-.13f,z);
                    float height = woods.Range(5.2f,11.3f);
                    if ((t+i)%4 == 0)
                        BuildCoastalBirch(woods, foot, height);
                    else
                        BuildShorePine(woods, foot, height);
                }
                for (int n = 0; n < 7; n++)
                {
                    float x = groves[i].x + woods.Range(-21,21), z = groves[i].y + woods.Range(-15,15);
                    if (Mathf.Abs(x) < RaidMap.RoomX + 3f && Mathf.Abs(z) < RaidMap.RoomZ + 3f) continue;
                    float y = CoastHeight(x,z);
                    if (y < 0f) continue;
                    float scale = woods.Range(.45f,1.8f);
                    woods.Lobe(new Vector3(x,y-.06f,z), new Vector3(1.25f,.63f,.83f)*scale,
                        Rgb(0x515854), Rgb(0x969586), 8);
                }
                if (woods.Vertices.Count > 0)
                    AddCoastMesh("Shelterbelt " + (i+1), woods.Vertices, woods.Triangles, woods.Colors, plants, true);
            }
            BuildMeadowPlants(plants);
        }

        private static void BuildShorePine(PlantMesh mesh, Vector3 foot, float height)
        {
            Vector3 lean = new Vector3(mesh.Range(-.7f,.7f),0,mesh.Range(-.65f,.65f));
            Vector3 top = foot + Vector3.up * height + lean;
            mesh.Stem(foot,top,height*.023f,Rgb(0x786654));
            Color shade = Color.Lerp(Rgb(0x263e39), Rgb(0x405941), mesh.Range(0,1));
            Color sunlit = Color.Lerp(Rgb(0x536e53), Rgb(0x78835b), mesh.Range(0,1));
            float radius = height * mesh.Range(.19f,.29f);
            float angle = mesh.Range(0,6.28f);
            for (int tier = 0; tier < 6; tier++)
            {
                float t = tier/6f;
                Vector3 whorl = Vector3.Lerp(foot, top, .26f + .70f*t);
                int count = 8 - tier/2;
                angle += 2.4f;
                for (int i = 0; i < count; i++)
                {
                    Vector3 dir = Compass(angle + (i + mesh.Range(-.16f,.16f))*2f*Mathf.PI/count);
                    Vector3 span = new Vector3(-dir.z,0,dir.x);
                    float reach = radius * (1f - .86f*t) * mesh.Range(.76f,1.2f);
                    Vector3 tip = whorl + dir*reach - Vector3.up*(reach*mesh.Range(.06f,.30f));
                    Vector3 shoulder = whorl + dir*(reach*.38f);
                    int a = mesh.Vertex(whorl + Vector3.up*height*.11f, Color.Lerp(shade,sunlit,.59f));
                    int left = mesh.Vertex(shoulder - span*reach*.49f, shade);
                    int ridge = mesh.Vertex(shoulder + Vector3.up*(reach*.19f), sunlit);
                    int point = mesh.Vertex(tip, Color.Lerp(shade,sunlit,mesh.Range(.37f,.94f)));
                    int right = mesh.Vertex(shoulder + span*reach*.46f, shade);
                    mesh.Triangle(a,ridge,left); mesh.Triangle(left,ridge,point);
                    mesh.Triangle(ridge,right,point); mesh.Triangle(a,right,ridge);
                }
            }
            mesh.Lobe(top - Vector3.up*height*.09f, new Vector3(radius*.17f,height*.12f,radius*.14f),shade,sunlit,6);
        }

        private static void BuildCoastalBirch(PlantMesh mesh, Vector3 foot, float height)
        {
            Color bark = Rgb(0xaaa18a), shade = Rgb(0x435440);
            Color sunlit = Color.Lerp(Rgb(0x788549),Rgb(0xadb064),mesh.Range(0,1));
            var top = foot + new Vector3(mesh.Range(-.75f,.75f),height,mesh.Range(-.75f,.75f));
            mesh.Stem(foot,top,height*.018f,bark);
            float angle = mesh.Range(0,6.28f);
            for (int i = 0; i < 6; i++)
            {
                Vector3 split = Vector3.Lerp(foot,top, .24f + i*.073f);
                var branch = Compass(angle + i*2.39f) * height*mesh.Range(.12f,.28f);
                Vector3 crown = split + branch + Vector3.up*height*mesh.Range(.25f,.37f);
                mesh.Stem(split,crown,height*.008f,bark,4);
                float r = height*mesh.Range(.15f,.23f);
                mesh.Lobe(crown,new Vector3(r,1.22f*r,.94f*r),shade,sunlit,8);
            }
            mesh.Lobe(top-Vector3.up*height*.11f, new Vector3(.16f,.17f,.17f)*height, shade,sunlit,8);
        }

        private static bool AllowsMeadow(float x, float z)
        {
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

        private void BuildMeadowPlants(Material plants)
        {
            for (int sector = 0; sector < 4; sector++)
            {
                var mesh = new PlantMesh(5412 + 11*sector);
                for (int p = 0; p < 460; p++)
                {
                    float x = mesh.Range(0f,RaidMap.RoomX-.9f) * (sector%2==0 ? -1f : 1f);
                    float z = mesh.Range(0f,RaidMap.RoomZ-.9f) * (sector<2 ? -1f : 1f);
                    if (!AllowsMeadow(x,z) || Meadow(x,z)<mesh.Range(.28f,.72f)) continue;
                    Color baseTone = Color.Lerp(Rgb(0x535f3c),Rgb(0x838348),mesh.Range(0,1));
                    Color tips = Color.Lerp(Rgb(0x858f54),Rgb(0xc0ad76),mesh.Range(0,1));
                    bool reeds = mesh.Random.Next(8)==0;
                    for (int tuft = 0; tuft < 5; tuft++)
                    {
                        float tx = x+mesh.Range(-1.3f,1.3f), tz=z+mesh.Range(-.94f,.94f);
                        if (!AllowsMeadow(tx,tz)) continue;
                        var origin = new Vector3(tx,CoastHeight(tx,tz)-.02f,tz);
                        int count = reeds ? 8 : 6;
                        for (int blade = 0; blade < count; blade++)
                        {
                            Vector3 outwards = Compass(mesh.Range(0,6.28f));
                            Vector3 across = new Vector3(-outwards.z,0,outwards.x) * mesh.Range(.021f,.043f);
                            float h = reeds ? mesh.Range(.37f,.65f) : mesh.Range(.18f,.39f);
                            Vector3 start = origin + outwards * mesh.Range(.02f,.13f);
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
                if (mesh.Vertices.Count > 0)
                    AddCoastMesh("Quayside sedge " + (sector+1), mesh.Vertices, mesh.Triangles, mesh.Colors, plants, false);
            }
        }
    }
}
