using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    // One deterministic mesh per botanical form and LOD, shared by all placed specimens.
    // The branch structure and crown centres are identical at each distance.
    internal sealed class CoastalPlantMeshes
    {
        private readonly List<Vector3> position = new List<Vector3>(), normal = new List<Vector3>();
        private readonly List<Color32> color = new List<Color32>();
        private readonly List<Vector2> uv = new List<Vector2>(), flex = new List<Vector2>();
        private readonly List<int> indices = new List<int>();
        private readonly int form, level;
        private readonly bool shrub;
        private readonly Color green, bark;
        private static readonly Vector2 Flat = new Vector2(.984f,.016f);

        internal CoastalPlantMeshes(int form, int level)
        {
            this.form = form;
            this.level = level;
            shrub = form >= 3;
            green = form == 0 ? new Color(.43f,.57f,.32f) : form == 1 ? new Color(.47f,.63f,.45f) :
                form == 2 ? new Color(.49f,.57f,.30f) : form == 3 ? new Color(.37f,.50f,.34f) : new Color(.50f,.57f,.32f);
            bark = form == 1 ? new Color(.69f,.67f,.54f) : new Color(.49f,.43f,.34f);
        }

        private static float Range(System.Random r, float low, float high) => Mathf.Lerp(low,high,(float)r.NextDouble());

        internal Mesh Build()
        {
            var random = new System.Random(892 + form*413);
            float h = form == 1 ? 6.45f : form == 2 ? 5.48f : 5.92f;
            if (shrub) h = form == 3 ? 1.48f : 1.07f;
            float spread = shrub ? h*.90f : h*(form == 1 ? .29f : form == 2 ? .46f : .39f);
            float r = shrub ? .048f : form == 1 ? .13f : .18f;
            Vector3 foot = new Vector3(0,-.045f,0), fork = new Vector3(.04f,h*(shrub?.14f:.34f),.055f);
            Vector3 leader = new Vector3(-.25f*spread,h*.84f,.10f*spread);
            Limb(foot,fork,r,r*.79f, 0,.05f);
            Limb(fork,leader,r*.79f,r*.16f,.05f,.43f);
            int boughs = shrub ? 6 : 8;
            for (int i=0;i<boughs;i++)
            {
                float t = i/(float)(boughs-1);
                float angle = 2.399963f*i + Range(random,-.22f,.22f);
                Vector3 radial = new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                Vector3 side = new Vector3(-radial.z,0,radial.x);
                Vector3 join = Vector3.Lerp(fork,leader,t*.9f);
                float off = Mathf.Lerp(.82f,.24f,t)*spread*Range(random,.86f,1.10f);
                Vector3 end = radial*off + new Vector3(0,h*Mathf.Lerp(shrub?.43f:.55f,.89f,t),0);
                Vector3 elbow = Vector3.Lerp(join,end,.52f) - Vector3.up*.034f*h;
                Limb(join,elbow,r*Mathf.Lerp(.54f,.22f,t),r*.27f,.10f,.26f);
                Limb(elbow,end,r*.27f,r*.065f,.26f,.63f);
                for (int j=0;j<3;j++)
                {
                    Vector3 centre = end + side*((j-1)*spread*.33f) + radial*(j==1 ? spread*.18f : 0f);
                    centre.y += Range(random,-.028f,.040f)*h;
                    Limb(Vector3.Lerp(elbow,end,.40f),centre,r*.15f,r*.032f,.30f,.70f);
                    Vector3 radius = new Vector3(spread*Range(random,.42f,.55f),h*Range(random,shrub?.22f:.11f,shrub?.32f:.17f),spread*Range(random,.41f,.53f));
                    if(level!=2 || j==1) Foliage(centre,level==2 ? radius*1.23f : radius,new System.Random(4901+form*701+i*43+j*7));
                }
            }
            var mesh = new Mesh { name = (shrub ? "Coastal shrub " : "Broadleaf ") +form+" LOD"+level };
            mesh.SetVertices(position); mesh.SetNormals(normal); mesh.SetColors(color);
            mesh.SetUVs(0,uv); mesh.SetUVs(1,flex); mesh.SetTriangles(indices,0); mesh.RecalculateBounds();
            var bounds=mesh.bounds;
            bounds.Expand(shrub ? .45f : .35f);
            mesh.bounds=bounds;
            mesh.UploadMeshData(true);
            return mesh;
        }

        private int Vertex(Vector3 p,Vector3 n,Color c,Vector2 tex,float wind)
        {
            int k=position.Count;
            position.Add(p); normal.Add(n.normalized); c.a=1; color.Add(c);
            uv.Add(tex); flex.Add(new Vector2(wind,shrub ? 1f : 0f));
            return k;
        }

        private void Quad(int a,int b,int c,int d)
        {
            indices.Add(a); indices.Add(b); indices.Add(c);
            indices.Add(a); indices.Add(c); indices.Add(d);
        }

        private void Limb(Vector3 from,Vector3 to,float ra,float rb,float w0,float w1)
        {
            Vector3 axis=(to-from).normalized;
            if (axis.sqrMagnitude<.1f || (level==2 && ra<.065f)) return;
            Vector3 right=Vector3.Cross(axis, Mathf.Abs(axis.z)>.94f ? Vector3.up : Vector3.forward).normalized;
            Vector3 front=Vector3.Cross(right,axis);
            int sides=level==0 ? 8 : level==1 ? 6 : 4;
            int k=position.Count;
            for(int i=0;i<=sides;i++)
            {
                float fraction=i/(float)sides, a=fraction*Mathf.PI*2;
                Vector3 radial=right*Mathf.Cos(a)+front*Mathf.Sin(a);
                float u=.507f+.46f*fraction;
                Vertex(from+radial*ra,radial,bark*.93f,new Vector2(u,.02f),w0);
                Vertex(to+radial*rb,radial,bark,new Vector2(u,.488f),w1);
                if (i<sides) Quad(k+2*i,k+2*i+1,k+2*i+3,k+2*i+2);
            }
        }

        private void Foliage(Vector3 centre,Vector3 radius,System.Random rng)
        {
            float tint=Range(rng,.90f,1.08f);
            int tile=form==1 ? 1 : shrub ? 2 : 0;
            if(level==2)
            {
                const int sides=6;
                int k=position.Count;
                for(int band=0;band<=3;band++)
                    for(int j=0;j<sides;j++)
                    {
                        float y=band==0 ? -.86f : band==1 ? -.21f : band==2 ? .46f : .88f;
                        float a=2*Mathf.PI*(j+band*.17f)/sides;
                        float ring=Mathf.Sqrt(1f-y*y)*Range(rng,.82f,1.12f);
                        Vector3 unit=new Vector3(ring*Mathf.Cos(a),y,ring*Mathf.Sin(a));
                        Vector3 p=centre+Vector3.Scale(radius,unit);
                        float value=Mathf.Lerp(.75f,1.10f,(y+1)*.5f)*tint;
                        Vertex(p,unit+Vector3.up*.55f,green*value,Flat,.68f);
                        if (band>0)
                        {
                            int lower=k+(band-1)*sides+j, next=k+(band-1)*sides+(j+1)%sides;
                            Quad(lower,lower+sides,next+sides,next);
                        }
                    }
                for(int i=1;i<sides-1;i++)
                {
                    indices.Add(k);indices.Add(k+i);indices.Add(k+i+1);
                    indices.Add(k+3*sides);indices.Add(k+3*sides+i+1);indices.Add(k+3*sides+i);
                }
                return;
            }
            if(form==4 && rng.Next(4)==0) Blossom(centre+Vector3.up*radius.y*.93f,rng);
            int count=level==0 ? 28 : 14;
            Vector2 tileStart=new Vector2(tile%2*.5f+.004f,tile<2 ? .504f : .004f);
            for(int j=0;j<count;j++)
            {
                float a=j*2.399963f+Range(rng,-.38f,.38f);
                float y=Range(rng,-.89f,.97f);
                float ring=Mathf.Sqrt(1f-y*y);
                Vector3 outward=new Vector3(Mathf.Cos(a)*ring,y,Mathf.Sin(a)*ring);
                Vector3 skin=centre+Vector3.Scale(radius,outward)*Range(rng,.70f,1f);
                Vector3 side=new Vector3(-Mathf.Sin(a),Range(rng,-.45f,.45f),Mathf.Cos(a)).normalized;
                Vector3 dir=(new Vector3(outward.x,0,outward.z)*.62f+Vector3.up*(form==1 ? Range(rng,-1.6f,-.25f) : y*.67f+Range(rng,-.9f,.2f))).normalized;
                float length=radius.x*Range(rng,.64f,1.08f)*(level==1 ? 1.32f : 1f);
                Vector3 basePoint=skin-dir*length*.42f;
                Vector3 mid=basePoint+dir*length*.50f+Vector3.up*.09f*length;
                Vector3 end=basePoint+dir*length-Vector3.up*length*.14f;
                float width=length*(form==1 ? 1.12f : 1.54f);
                Color shade=green*(tint*Mathf.Lerp(.93f,1.33f,(y+1)*.5f));
                Vector3 n=(outward*.71f+Vector3.up*.90f).normalized;
                int first=position.Count;
                for(int row=0;row<3;row++)
                {
                    Vector3 c=row==0 ? basePoint : row==1 ? mid : end;
                    for(int edge=0;edge<2;edge++)
                        Vertex(c+side*width*(edge-.5f),n,shade, tileStart+new Vector2(.492f*edge,.246f*row),.64f+row*.16f);
                }
                Quad(first,first+2,first+3,first+1); Quad(first+2,first+4,first+5,first+3);
            }
        }

        private void Blossom(Vector3 centre,System.Random rng)
        {
            // Flat cream corymbs sit above the wider-leaf elder scrub. Each is a small
            // group of cupped florets, legible close up without a transparent sprite.
            int units=level==0 ? 11 : 6;
            for(int i=0;i<units;i++)
            {
                float a=i*2.39996f, distance=.082f*Mathf.Sqrt(i/(float)units);
                Vector3 p=centre+new Vector3(Mathf.Cos(a)*distance,.055f-distance*.32f,Mathf.Sin(a)*distance);
                float size=Range(rng,.015f,.034f)*(level==0 ? 1f : 1.25f);
                int k=Vertex(p,Vector3.up,new Color(.81f,.70f,.41f),Flat,.94f);
                for(int l=0;l<5;l++)
                {
                    float angle=a+l*1.256637f;
                    var rim=p+new Vector3(size*Mathf.Cos(angle),size*.25f,size*Mathf.Sin(angle));
                    Vertex(rim,Vector3.up,new Color(.90f,.88f,.71f),Flat,.97f);
                }
                for(int l=0;l<5;l++){indices.Add(k);indices.Add(k+1+(l+1)%5);indices.Add(k+l+1);}
            }
        }
    }
}
