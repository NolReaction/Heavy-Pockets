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

            // Vertex alpha selects the tree impostor (0), understory atlas (64), bark (128) or untextured mesh (255).
            internal int CardVertex(Vector3 point, Color tint, float u, float v, byte surface = 0)
            {
                if (CardUVs == null) CardUVs = new List<Vector2>(new Vector2[Vertices.Count]);
                tint.a = surface / 255f;
                int index = Vertex(point, tint);
                CardUVs[index] = new Vector2(u, v);
                return index;
            }

            internal void Spray(Vector3 start, Vector3 end, Vector3 wing, Vector3 fold, int column, Color tint)
            {
                // A folded card has two surface directions even when seen from directly above a shrub.
                Vector3 sideRoot = wing * .56f;
                Color shaded = tint * .74f;
                float u = column * .25f;
                int a = CardVertex(start - sideRoot, shaded, u, 0, 64);
                int b = CardVertex(end - wing, tint, u, 1, 64);
                int c = CardVertex(start + fold * .22f, shaded, u + .125f, 0, 64);
                int d = CardVertex(end + fold, tint, u + .125f, 1, 64);
                int e = CardVertex(start + sideRoot, shaded, u + .25f, 0, 64);
                int f = CardVertex(end + wing, tint, u + .25f, 1, 64);
                Triangle(a,b,d); Triangle(a,d,c); Triangle(c,d,f); Triangle(c,f,e);
            }

            internal void Bark(Vector3 start, Vector3 end, float baseRadius, float tipRadius, int sides)
            {
                float repeats = Vector3.Distance(start,end) * 1.3f;
                int first = Vertices.Count;
                for (int i = 0; i <= sides; i++)
                {
                    float fraction = i/(float)sides;
                    Vector3 rim = Compass(fraction * 2f * Mathf.PI);
                    CardVertex(start + rim * baseRadius, Rgb(0xb7b6ac), fraction, 0, 128);
                    CardVertex(end + rim * tipRadius, Rgb(0xfff0d7), fraction, repeats, 128);
                    if (i == 0) continue;
                    int n = first + i*2;
                    Triangle(n-2,n-1,n); Triangle(n,n-1,n+1);
                }
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
            for (int i = 0; i < YardPlanting.Trunks.Length; i++)
            {
                Bounds trunk = YardPlanting.Trunks[i];
                Vector3 foot = new Vector3(trunk.center.x, -.035f, trunk.center.z);
                var tile = bake.At(foot.x,foot.z);
                float height = trunk.size.y * 1.36f;
                BuildYardPine(tile.Woods, foot, height, trunk.extents.x, i * 2.4f);
                BuildShelterTree(tile.Woodline, foot, height, i%4, i*2.4f, false);
            }
            BakeUndergrowth(bake);
            BakeMeadowPlants(bake);
        }

        private static void BuildYardPine(PlantMesh mesh, Vector3 foot, float height, float radius, float phase)
        {
            Vector3 shoulder = foot + Vector3.up * (height * .69f);
            Vector3 top = foot + new Vector3(height * .025f, height, height * .013f);
            mesh.Bark(foot, shoulder, radius, radius * .62f, 9);
            mesh.Bark(shoulder, top, radius * .62f, .021f, 9);
            Color needles = Rgb(0xe4e5cf);
            for (int tier=0; tier<10; tier++)
            {
                float t = tier/10f;
                float y = height * (.27f + t * .73f);
                Vector3 hub = foot + Vector3.up*y + (top-foot-Vector3.up*height)*Mathf.Max(0f,(t-.58f)*1.8f);
                int count = 8 - tier/2;
                for (int branch=0; branch<count; branch++)
                {
                    float angle = phase + tier*2.4f + (branch + mesh.Range(-.15f,.15f))*2f*Mathf.PI/count;
                    Vector3 outward = Compass(angle), across = new Vector3(-outward.z,0,outward.x);
                    float reach = height * (.30f - t*.253f) * mesh.Range(.80f,1.11f);
                    Vector3 joint = hub + outward*reach*.43f + Vector3.up*reach*.10f;
                    Vector3 tip = hub + outward*reach + Vector3.up*reach*mesh.Range(-.14f,.20f);
                    mesh.Stem(hub, joint, radius*.16f*(1f-t*.65f),Rgb(0x756951),4);
                    Color tint = Color.Lerp(Rgb(0xa6b29a),needles,.23f + .56f*t + mesh.Range(0f,.19f));
                    mesh.Spray(Vector3.Lerp(hub,joint,.32f),tip,across*reach*.36f,Vector3.up*reach*.30f,2,tint);
                    for (int side=-1; side<=1; side+=2)
                    {
                        Vector3 spray = joint + (outward*.34f + across*(side*.27f) -
                            Vector3.up*mesh.Range(.19f,.42f))*reach;
                        Vector3 wing = (across*.13f + Vector3.up*(side*.23f))*reach;
                        mesh.Spray(Vector3.Lerp(hub,joint,.62f),spray,wing,outward*reach*.12f,2,tint);
                    }
                }
            }
            mesh.Spray(top-Vector3.up*height*.11f,top,Vector3.right*height*.039f,Vector3.forward*height*.025f,2,needles);
        }

        private static void BakeUndergrowth(CellBake bake)
        {
            // Patches follow the rail verge, the shore path and the receiving yard. The loading apron stays clear.
            Vector2[] patches = {
                new Vector2(-29,11), new Vector2(-46,16), new Vector2(-28,26), new Vector2(-62,20),
                new Vector2(-38,-31), new Vector2(-2,-26), new Vector2(23,13), new Vector2(57,-13),
                new Vector2(35,-34), new Vector2(28,44), new Vector2(-16,43), new Vector2(-23,-10)
            };
            for (int p=0; p<patches.Length; p++)
            {
                var spacing = new PlantMesh(2719 + p*61);
                for (int j=0; j<12; j++)
                {
                    float x=patches[p].x + spacing.Range(-4.4f,4.4f);
                    float z=patches[p].y + spacing.Range(-3.2f,3.2f);
                    if (!AllowsMeadow(x,z)) continue;
                    var foot = new Vector3(x,CoastHeight(x,z)-.035f,z);
                    var tile = bake.At(x,z);
                    float h=spacing.Range(.38f, .98f), phase=spacing.Range(0f,6.28f);
                    Color tint=Color.Lerp(Rgb(0xc8d3b5),Rgb(0xffedc4),spacing.Range(0,1));
                    for(int sprig=0;sprig<7;sprig++)
                    {
                        Vector3 dir=Compass(phase+sprig*2.40f);
                        Vector3 wing=new Vector3(-dir.z,0,dir.x)*h*.34f;
                        Vector3 start=foot+dir*(h*.15f);
                        Vector3 end=foot+(dir*spacing.Range(.32f,.74f)+Vector3.up*spacing.Range(.68f,1.0f))*h;
                        tile.Woods.Spray(start,end,wing,(Vector3.up+dir)*h*.13f,1,tint);
                    }
                    // Small plants drop away with the detailed cell; taller shrubs keep crossed sprays on the horizon.
                    if (h < .70f) continue;
                    for(int side=0;side<2;side++)
                    {
                        Vector3 wing=Compass(phase+side*1.57f)*h*.76f;
                        tile.Woodline.Spray(foot,foot+Vector3.up*h,wing,Vector3.zero,1,tint);
                    }
                }
            }
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
            Bounds warehouse = OutdoorLayout.ReceivingWarehouse.Bounds;
            if (Mathf.Abs(x-warehouse.center.x) < warehouse.extents.x + .9f &&
                z > warehouse.min.z-3.5f && z < warehouse.max.z+.75f) return false;
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
                    bool herbs = scatter.Random.Next(5) == 0;
                    Color tint = Color.Lerp(Rgb(0xb9c9ae),Rgb(0xffedc3),scatter.Range(0,1));
                    for (int tuft=0; tuft<6; tuft++)
                    {
                        float tx = x + scatter.Range(-1.6f,1.6f), tz = z + scatter.Range(-1.1f,1.1f);
                        if (!AllowsMeadow(tx,tz)) continue;
                        var mesh = bake.At(tx,tz).Sedge;
                        var basePoint = new Vector3(tx,CoastHeight(tx,tz)-.035f,tz);
                        float h = herbs ? scatter.Range(.32f,.62f) : scatter.Range(.14f,.38f);
                        float phase = scatter.Range(0f,6.28f);
                        for (int side=0; side<3; side++)
                        {
                            Vector3 outwards=Compass(phase + side*2.08f);
                            Vector3 across=new Vector3(-outwards.z,0,outwards.x);
                            Vector3 start=basePoint+outwards*h*.09f;
                            Vector3 tip=start+(Vector3.up + outwards * scatter.Range(.17f,.52f))*h;
                            mesh.Spray(start,tip,across*h*(herbs?.44f:.68f),outwards*h*.15f,herbs?3:0,tint);
                        }
                    }
                }
            }
        }
    }
}
#endif
