#if UNITY_EDITOR
using UnityEngine;

namespace HeavyPockets
{
    internal sealed partial class RaidScenery
    {
        private static void ChartHouseJoinery(ChartHouseMeshes b, HarborInteriorMaterials f)
        {
            // Main chart is set into a deep shadow frame with a thin brass sight edge.
            const float cx=.25f, cy=2.24f, width=3.82f, height=1.91f;
            b.Box(f.Timber,cx,cy,3.664f,width+.24f,height+.24f,.13f);
            b.Box(f.Brass,cx,cy,3.583f,width+.080f,height+.080f,.048f);
            b.Box(f.Rubber,cx,cy,3.555f,width+.018f,height+.018f,.015f);
            b.Plane(f.Chart,P(cx,cy,3.544f),Vector3.right*(width*.5f),Vector3.up*(height*.5f),Vector3.back);

            Bounds chest=ChartHouseLayout.PlanChest;
            b.Box(f.Timber,chest.center, chest.size-P(.10f,.12f,.08f),Quaternion.identity);
            b.Slab(f.Timber,P(chest.center.x,.806f,chest.center.z),P(chest.size.x,.108f,chest.size.z),.018f);
            b.Box(f.Paint,chest.center.x,.093f,chest.center.z,chest.size.x-.12f,.16f,chest.size.z-.12f);
            for (int bay=0;bay<3;bay++)
            {
                float x=-1.60f + 1.76f*bay;
                for (int row=0;row<4;row++)
                {
                    float y=.214f+row*.153f;
                    b.Box(f.Timber,x,y,3.06f,1.688f,.134f,.053f);
                    b.Box(f.Brass,x,y+.002f,3.024f,.19f,.064f,.016f);
                    b.Box(f.Paper,x,y+.005f,3.013f,.135f,.035f,.008f,Bone);
                }
            }

            Bounds table=ChartHouseLayout.Table;
            float tx=table.center.x;
            b.Box(f.Paint,tx,.34f,.005f,2.77f,.56f,1.24f);
            b.Box(f.Timber,tx,.78f,.005f,2.96f,.32f,1.36f);
            b.Slab(f.Timber,P(tx,.993f,.005f),P(table.size.x,.142f,table.size.z),.027f);
            b.Box(f.Brass,tx,.981f,-.771f,3.12f,.012f,.008f);
            foreach (int side in new[]{-1,1})
            {
                b.Box(f.Timber,tx+side*1.28f,.12f,0,.19f,.23f,1.30f);
                b.Box(f.Brass,tx+side*1.60f,1.032f,-.741f,.087f,.058f,.07f);
                b.Box(f.Brass,tx+side*1.60f,1.032f,.750f,.087f,.058f,.07f);
            }
            for (int i=0;i<5;i++)
            {
                float x=tx+(i-2)*.544f;
                b.Box(f.Paint,x,.36f,-.630f,.497f,.447f,.025f);
                b.Box(f.Timber,x,.768f,-.678f,.498f,.219f,.021f);
                b.Box(f.Brass,x,.782f,-.704f,.113f,.029f,.039f);
            }
            Quaternion turn=Quaternion.Euler(0,-7f,0);
            b.Plane(f.Chart,P(tx-.13f,1.068f,.039f),turn*Vector3.right*.919f,turn*Vector3.forward*.461f,Vector3.up);

            // An open archive with recessed back, solid end panels and projecting shelf noses.
            b.Box(f.Paint,-5.717f,1.36f,.96f,.086f,2.53f,3.17f);
            foreach(float z in new[]{-.602f,.97f,2.52f})
                b.Box(f.Paint,-5.38f,1.34f,z,.74f,2.56f,.071f);
            for(int i=0;i<5;++i)
            {
                float y=.14f+i*.60f;
                b.Box(f.Timber,-5.38f,y,.96f,.75f,.07f,3.17f);
                b.Box(f.Timber,-5.007f,y-.025f,.96f,.034f,.092f,3.18f);
            }
            b.Box(f.Paint,-5.37f,2.59f,.96f,.78f,.084f,3.20f);

            // Cast-iron radiator and a low, turned stool.
            b.Box(f.Paint,5.635f,.45f,0,.15f,.64f,2.43f);
            b.Tube(f.Steel,P(5.58f,.72f,-1.30f),P(5.58f,.72f,1.30f),.055f,.055f);
            b.Tube(f.Steel,P(5.58f,.22f,-1.30f),P(5.58f,.22f,1.30f),.055f,.055f);
            for(int i=0;i<17;i++)
            {
                float z=-1.16f+i*.145f;
                b.Tube(f.Paint,P(5.54f,.18f,z),P(5.54f,.74f,z),.096f,.096f,10);
                b.Tube(f.Paint,P(5.54f,.74f,z),P(5.54f,.80f,z),.096f,.050f,10);
            }
            Vector3 stool=ChartHouseLayout.Stool.center; stool.y=0;
            b.Disc(f.Timber,stool+Vector3.up*.506f,Vector3.up,.269f,.067f);
            b.Disc(f.Rubber,stool+Vector3.up*.540f,Vector3.up,.248f,.019f);
            for(int i=0;i<4;i++)
            {
                float angle=i*Mathf.PI*.5f+.25f;
                Vector3 v=P(Mathf.Sin(angle),0,Mathf.Cos(angle));
                b.Tube(f.Steel,stool+v*.232f+Vector3.up*.03f,stool+v*.145f+Vector3.up*.47f,.018f,.015f,10);
            }
        }

        private static void ChartHouseTrim(ChartHouseMeshes b, HarborInteriorMaterials f)
        {
            // Drain fittings and ring straps read from the approach; inside, a pair of spun
            // shades and the damped enamel switchgear frame the main chart.
            b.Tube(f.Steel,P(-5.72f,.24f,-4.10f),P(-5.72f,4.03f,-4.10f),.061f,.061f);
            foreach(float y in new[]{.72f,2.10f,3.40f})
                b.Disc(f.Brass,P(-5.72f,y,-4.10f),Vector3.up,.073f,.043f);
            for(int k=0;k<2;k++)
            {
                float z = k==0 ? -.16f : 1.32f;
                float x = k==0 ? -1.26f : .45f;
                float bottom=k==0 ? 2.81f : 3.13f;
                b.Disc(f.Paint,P(x,3.86f,z),Vector3.up,.145f,.065f);
                b.Tube(f.Rubber,P(x,bottom+.26f,z),P(x,3.84f,z),.012f,.012f,10);
                b.Disc(f.Brass,P(x,bottom+.239f,z),Vector3.up,.057f,.096f);
                b.Tube(f.Paint,P(x,bottom+.026f,z),P(x,bottom+.206f,z),.275f,.103f,32);
                b.Disc(f.Brass,P(x,bottom+.016f,z),Vector3.up,.28f,.028f);
                b.Disc(f.Lamp,P(x,bottom+.000f,z),Vector3.up,.255f,.015f);
            }
            // Surface-mounted conduit follows the wall and terminates at the instrument panel.
            b.Tube(f.Steel,P(-5.60f,3.43f,3.59f),P(5.58f,3.43f,3.59f),.019f,.019f,10);
            b.Tube(f.Steel,P(-4.22f,1.12f,3.59f),P(-4.22f,3.43f,3.59f),.021f,.021f,10);
            for(float x=-5.0f;x<=5.2f;x+=1.02f)
                b.Box(f.Brass,x,3.43f,3.605f,.024f,.080f,.041f);
            b.Box(f.Paint,-4.14f,1.90f,3.65f,1.61f,1.32f,.17f);
            b.Box(f.Rubber,-4.14f,1.90f,3.558f,1.47f,1.20f,.032f);
            b.Box(f.Paint,-4.14f,1.90f,3.537f,1.43f,1.16f,.030f);
            foreach(float x in new[]{-4.60f,-4.15f,-3.70f})
            {
                Dial(b,f,P(x,2.11f,3.46f),.151f,false);
                b.Disc(f.Brass,P(x,1.60f,3.50f),Vector3.back,.075f,.036f,20);
                b.Disc(f.Rubber,P(x,1.60f,3.473f),Vector3.back,.051f,.05f,16);
            }
            Dial(b,f,P(4.04f,2.65f,3.59f),.43f,true);
            foreach (float x in new[]{-.63f,1.13f})
            {
                b.Disc(f.Brass,P(x,3.51f,3.705f),Vector3.back,.066f,.047f,16);
                b.Beam(f.Brass,P(x,3.51f,3.68f),P(x,3.46f,3.05f),.021f,.024f);
            }
            b.Tube(f.Brass,P(-1.11f,3.44f,3.05f),P(1.61f,3.44f,3.05f),.041f,.041f,20);
            b.Box(f.Lamp,.25f,3.406f,3.069f,2.54f,.017f,.037f);

            // A slim sill on either side makes the glazing's thickness visible close up.
            foreach(float x in new[]{-4.03f,-2.61f,-1.18f})
            {
                b.Box(f.Brass,x,1.17f,-3.785f,.14f,.038f,.032f);
                b.Box(f.Brass,x+.04f,1.19f,-3.757f,.035f,.064f,.030f);
            }
            for(int i=0;i<2;i++)
            {
                float x=1.88f+i*1.43f;
                b.Disc(f.Brass,P(x,3.31f,-4.069f),Vector3.back,.018f,.015f,12);
            }
        }

        private static void Dial(ChartHouseMeshes b,HarborInteriorMaterials f,Vector3 p,float r,bool clock)
        {
            Vector3 n=Vector3.back;
            b.Disc(f.Rubber,p+n*.012f,n,r*1.085f,.092f);
            b.Disc(f.Brass,p+n*.063f,n,r,.055f);
            b.Disc(f.Paper,p+n*.095f,n,r*.87f,.016f,40,Bone);
            int steps=clock?60:24;
            for(int j=0;j<steps;j++)
            {
                float angle=j*2*Mathf.PI/steps;
                Vector3 ray=P(Mathf.Sin(angle),Mathf.Cos(angle),0);
                bool major=j%(clock?5:4)==0;
                b.Beam(f.Rubber,p+n*.108f+ray*r*(major?.64f:.73f),
                    p+n*.108f+ray*r*.80f,r*(major?.038f:.016f),.006f);
            }
            b.Beam(f.Rubber,p+n*.12f,p+n*.12f+P(.43f,.36f,0)*r,r*.052f,.009f);
            if(clock) b.Beam(f.Rubber,p+n*.13f,p+n*.13f+P(-.52f,.42f,0)*r,r*.039f,.009f);
            b.Disc(f.Brass,p+n*.145f,n,r*.066f,.014f,16);
        }

        private static void ChartHouseInstruments(ChartHouseMeshes b,HarborInteriorMaterials f)
        {
            DeskLamp(b,f);
            DockTelescope(b,f);
            // Small ship compass assembled from a stepped brass bowl and glass-free dial.
            Vector3 p=P(-.06f,1.072f,.32f);
            b.Tube(f.Brass,p,p+Vector3.up*.042f,.159f,.175f,32);
            b.Tube(f.Paint,p+Vector3.up*.043f,p+Vector3.up*.121f,.148f,.18f,32);
            b.Disc(f.Brass,p+Vector3.up*.131f,Vector3.up,.185f,.028f);
            b.Disc(f.Paper,p+Vector3.up*.150f,Vector3.up,.157f,.013f,40,Bone);
            for(int i=0;i<16;i++)
            {
                float a=i*Mathf.PI/8f;
                Vector3 ray=P(Mathf.Sin(a),0,Mathf.Cos(a));
                b.Beam(f.Rubber,p+Vector3.up*.159f+ray*.115f,p+Vector3.up*.159f+ray*.147f,.006f,.005f);
            }
            b.Beam(f.Paint,p+P(-.049f,.164f,-.078f),p+P(.066f,.164f,.103f),.026f,.009f);
            b.Disc(f.Brass,p+Vector3.up*.174f,Vector3.up,.026f,.011f,16);
            // Parallel dividers and two pencils on the plotting sheet.
            b.Tube(f.Brass,P(-1.61f,1.08f,-.05f),P(-1.31f,1.08f,.23f),.012f,.007f,10);
            b.Tube(f.Brass,P(-1.61f,1.08f,-.05f),P(-1.45f,1.08f,.29f),.012f,.006f,10);
            b.Disc(f.Brass,P(-1.61f,1.088f,-.05f),Vector3.up,.028f,.018f,16);
            for(int i=0;i<2;i++)
            {
                Vector3 p0=P(-1.71f+.04f*i,1.08f,-.47f+.06f*i), p1=p0+P(.28f,0,.053f);
                b.Tube(f.Paper,p0,p1,.008f,.008f,6,i==0?OxBlood:Navy);
                b.Tube(f.Timber,p1,p1+P(.026f,0,.005f),.008f,.001f,6);
            }
            // Rolled charts have a visible hollow end, held down with brass rings.
            for(int i=0;i<3;i++)
            {
                Vector3 start=P(-2.65f,1.134f+(i==2?.09f:0),.24f+(i==1?.135f:.018f*i));
                Vector3 end=start+P(.98f,0,.045f);
                b.Tube(f.Paper,start,end,.061f,.061f,18,Bone);
                b.Disc(f.Timber,end+P(.001f,0,0),Vector3.right,.032f,.003f,16);
                b.Disc(f.Rubber,end+P(.003f,0,0),Vector3.right,.016f,.003f,16);
                b.Disc(f.Brass,Vector3.Lerp(start,end,.67f),Vector3.right,.062f,.025f,18);
            }
        }

        private static void DeskLamp(ChartHouseMeshes b,HarborInteriorMaterials f)
        {
            var p=P(-2.39f,1.068f,.58f);
            b.Tube(f.Brass,p,p+Vector3.up*.034f,.132f,.137f,32);
            b.Tube(f.Paint,p+Vector3.up*.035f,p+Vector3.up*.075f,.117f,.080f,24);
            b.Tube(f.Brass,p+Vector3.up*.062f,p+Vector3.up*.325f,.026f,.018f);
            Vector3 axis=p+P(0,.37f,-.062f);
            foreach(int side in new[]{-1,1})
            {
                b.Tube(f.Brass,p+Vector3.up*.295f,axis+P(side*.179f,-.016f,0),.010f,.010f,12);
                b.Disc(f.Brass,axis+P(side*.181f,0,0),Vector3.right,.028f,.023f,14);
            }
            const int bands=14;
            Vector3 last=Vector3.zero;
            for(int i=0;i<=bands;i++)
            {
                float a=i*Mathf.PI/bands;
                Vector3 edge=P(0,Mathf.Sin(a)*.075f,Mathf.Cos(a)*.088f);
                if(i>0)
                {
                    Vector3 n=(last+edge).normalized;
                    b.Face(f.Paint,axis+last-Vector3.right*.17f,axis+last+Vector3.right*.17f,
                        axis+edge+Vector3.right*.17f,axis+edge-Vector3.right*.17f,n);
                    foreach(int side in new[]{-1,1})
                    {
                        Vector3 end=axis+Vector3.right*.17f*side;
                        b.Face(f.Paint,end,end+last,end+edge,end,Vector3.right*side);
                    }
                }
                last=edge;
            }
            b.Plane(f.Lamp,axis-Vector3.up*.002f,Vector3.right*.161f,Vector3.forward*.079f,Vector3.down);
        }

        private static void DockTelescope(ChartHouseMeshes b,HarborInteriorMaterials f)
        {
            Vector3 p=P(4.09f,0f,.24f);
            for(int i=0;i<3;i++)
            {
                float a=i*2*Mathf.PI/3+.21f;
                Vector3 radial=P(Mathf.Sin(a),0,Mathf.Cos(a));
                Vector3 foot=p+radial*.38f+Vector3.up*.028f;
                Vector3 top=p+radial*.086f+Vector3.up*1.14f;
                b.Beam(f.Timber,foot,top,.048f,.047f);
                b.Tube(f.Brass,foot,Vector3.Lerp(foot,top,.11f),.037f,.031f,14);
                b.Beam(f.Steel,Vector3.Lerp(foot,top,.40f),p+Vector3.up*.55f,.015f,.015f);
            }
            b.Disc(f.Brass,p+Vector3.up*1.15f,Vector3.up,.165f,.056f);
            b.Disc(f.Paint,p+Vector3.up*1.20f,Vector3.up,.12f,.067f);
            b.Tube(f.Brass,p+Vector3.up*1.215f,p+Vector3.up*1.395f,.047f,.037f,20);
            Vector3 axis=P(.94f,.205f,.28f).normalized, mid=p+Vector3.up*1.41f;
            b.Tube(f.Brass,mid-axis*.31f,mid+axis*.35f,.047f,.083f,24);
            b.Disc(f.Paint,mid+axis*.36f,axis,.087f,.090f,24);
            b.Disc(f.Brass,mid+axis*.406f,axis,.086f,.021f,24);
            b.Disc(f.Rubber,mid+axis*.419f,axis,.069f,.010f,24);
            b.Tube(f.Paint,mid-axis*.41f,mid-axis*.31f,.031f,.044f,16);
            b.Disc(f.Rubber,mid-axis*.413f,axis,.046f,.028f,20);
            b.Disc(f.Brass,mid+Vector3.back*.07f,Vector3.back,.064f,.079f,20);
        }

        private static void ChartHouseArchive(ChartHouseMeshes b,HarborInteriorMaterials f)
        {
            // The open watch log sits beside the receiver with its printed spread facing the operator.
            Vector3 log = P(.99f, ChartHouseLayout.PlanChest.max.y, 3.345f);
            Quaternion turn = Quaternion.Euler(0, -8f, 0);
            b.Box(f.Paint, log + Vector3.up * .006f, P(.56f, .012f, .36f), turn);
            b.Box(f.Paper, log + Vector3.up * .016f, P(.548f, .013f, .348f), turn, Bone);
            b.Plane(f.Logbook, log + Vector3.up * .023f, turn * Vector3.right * .274f,
                turn * Vector3.forward * .174f, Vector3.up);

            var bindings=new[]{Navy,OxBlood,new Color32(131,116,76,255),new Color32(100,109,91,255)};
            for(int shelf=0;shelf<4;shelf++)
            {
                for(int bay=0;bay<2;bay++)
                {
                    int count=4+(shelf+bay)%3;
                    for(int i=0;i<count;++i)
                    {
                        float z= -.44f+bay*1.56f +i*.22f;
                        float h=.34f+.043f*((i*7+shelf)%4);
                        float y=.178f+shelf*.6f;
                        var tint=bindings[(i+shelf*3+bay)%bindings.Length];
                        b.Box(f.Paper,-5.25f,y+h*.5f,z,.412f,h,.158f,tint);
                        b.Box(f.Brass,-5.039f,y+h*.67f,z,.013f,.086f,.11f);
                        b.Box(f.Paper,-5.029f,y+h*.67f,z,.010f,.055f,.073f,Bone);
                        b.Disc(f.Rubber,P(-5.032f,y+.080f,z),Vector3.right,.027f,.009f,12);
                    }
                }
            }
            for(int pile=0;pile<2;++pile)
            {
                for(int i=0;i<4;i++)
                {
                    Vector3 middle=P(-1.80f+3.68f*pile,.90f+i*.057f,3.39f);
                    Quaternion rotation=Quaternion.Euler(0,(i%3-1)*7f,0);
                    b.Box(f.Paper,middle,P(.59f,.053f,.38f),rotation,bindings[(i+pile)%4]);
                    b.Box(f.Paper,middle+Vector3.up*.005f+rotation*Vector3.back*.187f,P(.55f,.028f,.013f),rotation,Bone);
                }
            }
        }
    }
}
#endif
