using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    internal sealed partial class RaidScenery
    {
        private static float RangeEdge(float x, float z) =>
            Mathf.Max(Mathf.Abs(x) - RaidMap.RoomX, Mathf.Abs(z) - RaidMap.RoomZ);

        private static float DuneLobe(float x, float z, float cx, float cz, float sx, float sz)
        {
            float dx = (x-cx)/sx, dz = (z-cz)/sz;
            return Mathf.Exp(-(dx*dx + dz*dz));
        }

        private static float DuneHeight(float x, float z)
        {
            float rise = Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(2f,58f,RangeEdge(x,z)));
            float hills = 2.4f + 13.5f * DuneLobe(x,z,-76,113,49,32)
                + 6.8f * DuneLobe(x,z,38,96,44,54) + 22f * DuneLobe(x,z,132,20,40,59)
                + 11f * DuneLobe(x,z,-95,-48,32,49) + 17f * DuneLobe(x,z,-152,16,49,54)
                + 19f * DuneLobe(x,z,24,-133,75,38);
            return rise * (hills + (Noise(x,z,.024f)-.48f)*5.2f + (Noise(x,z,.055f)-.5f)*1.5f);
        }

        private void BuildRangeHorizon()
        {
            // Share the edge geometry and layer recipe of the flat garden. The road and
            // sand continue out into the first swale before yielding to dry coastal grass.
            const int step = 4, limit = 232, row = limit*2/step+1;
            var points = new List<Vector3>(row*row);
            var indices = new List<int>();
            var pigment = new List<Color32>(row*row);
            for (int z = -limit; z <= limit; z += step)
            for (int x = -limit; x <= limit; x += step)
            {
                int k = points.Count;
                points.Add(new Vector3(x,DuneHeight(x,z),z));
                float dist = RangeEdge(x,z);
                Color rim = CoastalSurfaceField.Layers(x,z);
                float meadow = Noise(x,z,.018f);
                float dry = Mathf.SmoothStep(.10f,.83f,Noise(x+57,z-25,.035f));
                float oldDune = DuneLobe(x,z,-83,-78,34,32) + DuneLobe(x,z,110,24,30,50)*.5f;
                Color outside = new Color(0f, Mathf.Clamp01(oldDune*(.2f+.62f*dry)), dry, meadow);
                pigment.Add(Color.Lerp(rim,outside,Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(3f,29f,dist))));
                if (x==limit || z==limit || RangeEdge(x+step*.5f,z+step*.5f)<0f) continue;
                indices.Add(k); indices.Add(k+row); indices.Add(k+row+1);
                indices.Add(k); indices.Add(k+row+1); indices.Add(k+1);
            }
            AddCoastMesh("Sheltered training basin",points,indices,pigment,
                Resources.Load<Material>("SurfaceGarden/Coastal surface"),false);
            BuildRangeWindbreaks();
        }

        private void BuildRangeWindbreaks()
        {
            var points = new List<Vector3>();
            var faces = new List<int>();
            var tints = new List<Color32>();
            var r = new System.Random(4129);
            float Rand(float lo,float hi) => Mathf.Lerp(lo,hi,(float)r.NextDouble());
            // Windswept stone pines form uneven clusters at the edge of the sand.
            // Separate stands leave sightlines open between the learning stations.
            Vector2[] stands = {
                new Vector2(-60,73), new Vector2(-26,96), new Vector2(57,88), new Vector2(91,44),
                new Vector2(102,-6), new Vector2(63,-83), new Vector2(5,-88), new Vector2(-61,-85),
                new Vector2(-89,-46), new Vector2(-99,18), new Vector2(-79,57),new Vector2(22,116)
            };
            foreach (Vector2 stand in stands)
            {
                int trees = r.Next(6,15);
                float angle = Rand(0f,6.28f);
                var axis = new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                for (int j=0; j<trees; j++)
                {
                    Vector2 p = stand + axis*Rand(-14,14) + new Vector2(-axis.y,axis.x)*Rand(-5,5);
                    if (RangeEdge(p.x,p.y)<6f) continue;
                    Vector3 foot = new Vector3(p.x,DuneHeight(p.x,p.y)-.18f,p.y);
                    float h=Rand(4.0f,8.5f);
                    float youth = Rand(0f,1f);
                    Vector3 branch = foot+new Vector3(Rand(-.35f,.48f),h*Mathf.Lerp(.48f,.65f,youth),Rand(-.32f,.32f));
                    Color bark = Color.Lerp(Rgb(0x665d49),Rgb(0x807155),Rand(0,1));
                    Limb(foot,branch,h*.032f,h*.018f,bark);
                    int boughs = r.Next(3,6);
                    Color greens = Color.Lerp(Rgb(0x677e65),Rgb(0x87946b),Rand(0,1));
                    for (int arm=0; arm<boughs; arm++)
                    {
                        float a = angle+arm*2.39996f+Rand(-.35f,.35f);
                        float offset = arm==0 ? 0f : h*Rand(.17f,.36f);
                        Vector3 radial=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                        Vector3 crown = branch + radial*offset + Vector3.up*h*(arm==0?.33f:Rand(.05f,.24f));
                        Limb(branch-Vector3.up*h*Rand(.10f,.20f),crown,h*.016f,h*.007f,bark);
                        float radius=h*Rand(.20f,.30f);
                        Color lit=Color.Lerp(greens,Rgb(0xa6b18a),Rand(.22f,.46f));
                        Crown(crown,new Vector3(radius,h*Rand(.10f,.19f),radius*Rand(.80f,1.12f)),greens*.83f,lit,9);
                        Crown(crown+radial*radius*.65f-Vector3.up*radius*.21f,
                            new Vector3(radius*.69f,radius*.35f,radius*.56f),greens*.85f,lit,7);
                    }
                }
                // Patchy scrub grows under the windbreak; a few gaps still reveal its forked trunks.
                for(int shrub=0;shrub<trees*3;shrub++)
                {
                    Vector2 p=stand+axis*Rand(-18,18)+new Vector2(-axis.y,axis.x)*Rand(-7,7);
                    if(RangeEdge(p.x,p.y)<4) continue;
                    float size=Rand(.65f,2.1f);
                    Vector3 shrubBase=new Vector3(p.x,DuneHeight(p.x,p.y)+size*.15f-.12f,p.y);
                    Color green=Color.Lerp(Rgb(0x758467),Rgb(0x9c9b73),Rand(0f,1f));
                    Crown(shrubBase,new Vector3(size,size*.47f,size*.76f),green*.81f,green,8);
                }
            }
            var leaf = new Material(Resources.Load<Shader>("CoastalGround")) { name="Windbreak foliage" };
            leaf.SetTexture("_Grain",Resources.Load<Texture2D>("DepotGrain"));
            surfaces.Add(leaf);
            AddCoastMesh("Maritime pine windbreaks",points,faces,tints,leaf,true);

            void Limb(Vector3 from,Vector3 to,float baseRadius,float endRadius,Color bark)
            {
                Vector3 up = (to-from).normalized;
                Vector3 right=Vector3.Cross(up,Vector3.forward).normalized, front=Vector3.Cross(right,up);
                int first=points.Count;
                const int sides=5;
                bark.a=1;
                for(int i=0;i<sides;i++)
                {
                    float a=2*Mathf.PI*i/sides;
                    Vector3 radial=right*Mathf.Cos(a)+front*Mathf.Sin(a);
                    points.Add(from+radial*baseRadius); tints.Add(bark);
                    points.Add(to+radial*endRadius); tints.Add(bark);
                    int k=first+i*2, next=first+2*((i+1)%sides);
                    faces.Add(k); faces.Add(k+1); faces.Add(next+1);
                    faces.Add(k); faces.Add(next+1); faces.Add(next);
                }
            }

            void Crown(Vector3 centre,Vector3 size,Color shade,Color light,int sides)
            {
                int first=points.Count;
                float phase=Rand(0f,6.28f);
                for (int level=0;level<4;level++)
                {
                    float rFactor=level==0?.33f:level==3?.36f:1f;
                    float y=level==0?-.92f:level==1?-.30f:level==2?.29f:.85f;
                    for(int i=0;i<sides;i++)
                    {
                        float a = phase + 2f*Mathf.PI*(i+.22f*level)/sides;
                        float radial = rFactor*Rand(.82f,1.15f);
                        points.Add(centre+Vector3.Scale(size,new Vector3(radial*Mathf.Cos(a),y+Rand(-.10f,.10f),radial*Mathf.Sin(a))));
                        Color tone = Color.Lerp(shade,light,Mathf.Clamp01(level*.31f+Rand(-.09f,.12f)));
                        tone.a=1; tints.Add(tone);
                        if(level==0) continue;
                        int k=first+(level-1)*sides+i, next=first+(level-1)*sides+(i+1)%sides;
                        faces.Add(k); faces.Add(k+sides); faces.Add(next+sides);
                        faces.Add(k); faces.Add(next+sides); faces.Add(next);
                    }
                }
                for(int i=1;i<sides-1;i++)
                {
                    faces.Add(first);faces.Add(first+i);faces.Add(first+i+1);
                    int top=first+3*sides;
                    faces.Add(top);faces.Add(top+i+1);faces.Add(top+i);
                }
            }
        }
    }
}
