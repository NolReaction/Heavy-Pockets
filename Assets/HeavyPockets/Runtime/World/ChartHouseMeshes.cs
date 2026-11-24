using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeavyPockets
{
    // Geometry is appended directly per finish. A drawer pull or a window divider never
    // becomes a transient GameObject when the cell is reloaded.
    internal sealed class ChartHouseMeshes
    {
        private sealed class Batch
        {
            internal readonly List<Vector3> Vertices = new List<Vector3>();
            internal readonly List<Vector3> Normals = new List<Vector3>();
            internal readonly List<Vector2> UV = new List<Vector2>();
            internal readonly List<Color32> Colors = new List<Color32>();
            internal readonly List<int> Triangles = new List<int>();
        }

        private readonly Dictionary<Material, Batch> batches = new Dictionary<Material, Batch>();
        private static readonly Color32 White = new Color32(255,255,255,255);
        private Batch For(Material material)
        {
            if (!batches.TryGetValue(material, out var batch)) batches.Add(material, batch = new Batch());
            return batch;
        }
        private readonly bool shadeInterior;
        internal ChartHouseMeshes(bool shadeInterior = true) => this.shadeInterior = shadeInterior;

        private static float PlanGap(Vector3 p, Bounds bounds)
        {
            Vector3 center=bounds.center, half=bounds.extents;
            float x=Mathf.Max(0,Mathf.Abs(p.x-center.x)-half.x);
            float z=Mathf.Max(0,Mathf.Abs(p.z-center.z)-half.z);
            return Mathf.Sqrt(x*x+z*z);
        }

        // Bake diffuse enclosure at vertices while building the cell. The falloff from
        // the three physical openings is separate from direct sunlight and practical lamps.
        // This also grounds furniture on the slab without a per-pixel AO pass.
        private static float IndoorAO(Vector3 p, Vector3 n)
        {
            const float edgeX=5.81f, edgeZ=3.82f;
            if(Mathf.Abs(p.x)>edgeX || Mathf.Abs(p.z)>edgeZ || p.y>3.925f) return 1f;
            float east=Mathf.Max(0,Mathf.Abs(p.z)-2.4f);
            float south=Mathf.Max(0,Mathf.Abs(p.x+2.575f)-2.13f);
            float door=Mathf.Max(0,Mathf.Abs(p.x-2.60f)-.86f);
            float windowY=Mathf.Max(0,1.07f-p.y)+Mathf.Max(0,p.y-3.19f);
            float sky = 1f / (1f + .115f*((edgeX-p.x)*(edgeX-p.x)+east*east+windowY*windowY))
                + .85f / (1f+.15f*((edgeZ+p.z)*(edgeZ+p.z)+south*south+windowY*windowY))
                + .34f / (1f+.23f*((edgeZ+p.z)*(edgeZ+p.z)+door*door));
            float ao=Mathf.Min(.94f,.31f+.48f*sky);
            float wallGap=Mathf.Min(edgeX-Mathf.Abs(p.x),edgeZ-Mathf.Abs(p.z));
            if(Mathf.Abs(n.y)>.65f)
                ao*=Mathf.Lerp(.57f,1f,Mathf.SmoothStep(0f,1f,wallGap/.64f));
            else
                ao*=Mathf.Lerp(.63f,1f,Mathf.SmoothStep(0f,1f,Mathf.Min(p.y,3.925f-p.y)/.43f));
            if(n.y>.55f && p.y<.10f)
            {
                float gap=Mathf.Min(PlanGap(p,ChartHouseLayout.Table),PlanGap(p,ChartHouseLayout.PlanChest));
                gap=Mathf.Min(gap,Mathf.Min(PlanGap(p,ChartHouseLayout.Bookcase),PlanGap(p,ChartHouseLayout.Radiator)));
                ao*=Mathf.Lerp(.24f,1f,Mathf.SmoothStep(0f,1f,gap/.72f));
            }
            return ao;
        }

        private void Vertex(Batch b, Vector3 p, Vector3 n, float u, float v, Color32 tint)
        {
            if(shadeInterior) tint.a=(byte)Mathf.RoundToInt(255f*IndoorAO(p,n));
            b.Vertices.Add(p); b.Normals.Add(n); b.UV.Add(new Vector2(u, v)); b.Colors.Add(tint);
        }

        // p0 is the bottom-left as seen on the finished face, including printed artwork.
        internal void Face(Material m, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 normal, Color32? tint = null)
        {
            var b = For(m);
            int i = b.Vertices.Count;
            Color32 pigment = tint ?? White;
            if(shadeInterior && ((p1-p0).sqrMagnitude>1.10f || (p3-p0).sqrMagnitude>1.10f))
            {
                GriddedFace(b,p0,p1,p2,p3,normal,pigment);
                return;
            }
            Vertex(b,p0,normal,0,0,pigment); Vertex(b,p1,normal,1,0,pigment);
            Vertex(b,p2,normal,1,1,pigment); Vertex(b,p3,normal,0,1,pigment);
            bool forward = Vector3.Dot(Vector3.Cross(p1-p0,p2-p0),normal) >= 0;
            b.Triangles.Add(i); b.Triangles.Add(i+(forward ? 1 : 2)); b.Triangles.Add(i+(forward ? 2 : 1));
            b.Triangles.Add(i); b.Triangles.Add(i+(forward ? 2 : 3)); b.Triangles.Add(i+(forward ? 3 : 2));
        }

        private void GriddedFace(Batch b,Vector3 p0,Vector3 p1,Vector3 p2,Vector3 p3,Vector3 normal,Color32 tint)
        {
            int columns=Mathf.Max(1,Mathf.CeilToInt((p1-p0).magnitude/.33f));
            int rows=Mathf.Max(1,Mathf.CeilToInt((p3-p0).magnitude/.33f));
            int start=b.Vertices.Count, stride=columns+1;
            bool forward=Vector3.Dot(Vector3.Cross(p1-p0,p2-p0),normal)>=0;
            for(int y=0;y<=rows;y++)
            {
                float v=y/(float)rows;
                Vector3 left=Vector3.Lerp(p0,p3,v), right=Vector3.Lerp(p1,p2,v);
                for(int x=0;x<=columns;x++)
                {
                    float u=x/(float)columns;
                    Vertex(b,Vector3.Lerp(left,right,u),normal,u,v,tint);
                    if(x==columns || y==rows) continue;
                    int a=start+y*stride+x, k=a+stride;
                    b.Triangles.Add(a); b.Triangles.Add(forward?a+1:k+1); b.Triangles.Add(forward?k+1:a+1);
                    b.Triangles.Add(a); b.Triangles.Add(forward?k+1:k); b.Triangles.Add(forward?k:k+1);
                }
            }
        }

        internal void Box(Material m, Bounds b, Color32? tint = null) =>
            Box(m,b.center,b.size,Quaternion.identity,tint);

        internal void Box(Material m, float x, float y, float z, float w, float h, float d, Color32? tint = null) =>
            Box(m,new Vector3(x,y,z),new Vector3(w,h,d),Quaternion.identity,tint);

        internal void Box(Material m, Vector3 p, Vector3 size, Quaternion rotation, Color32? tint = null)
        {
            Vector3 x = rotation * Vector3.right * (size.x*.5f);
            Vector3 y = rotation * Vector3.up * (size.y*.5f);
            Vector3 z = rotation * Vector3.forward * (size.z*.5f);
            Plane(m,p-x,y,z,-x.normalized,tint); Plane(m,p+x,y,z,x.normalized,tint);
            Plane(m,p-y,x,z,-y.normalized,tint); Plane(m,p+y,x,z,y.normalized,tint);
            Plane(m,p-z,x,y,-z.normalized,tint); Plane(m,p+z,x,y,z.normalized,tint);
        }

        internal void Plane(Material m, Vector3 center, Vector3 halfRight, Vector3 halfUp, Vector3 normal, Color32? tint = null) =>
            Face(m,center-halfRight-halfUp,center+halfRight-halfUp,center+halfRight+halfUp,center-halfRight+halfUp,normal,tint);

        internal void Slab(Material m, Vector3 center, Vector3 size, float edge)
        {
            float w=size.x*.5f, h=size.y*.5f, d=size.z*.5f;
            Vector3 Corner(int corner,float inset,float y)
            {
                float x=w-inset, z=d-inset, bevel=edge*.88f;
                Vector3 p = corner switch {
                    0 => new Vector3(-x+bevel,y,-z), 1 => new Vector3(x-bevel,y,-z),
                    2 => new Vector3(x,y,-z+bevel), 3 => new Vector3(x,y,z-bevel),
                    4 => new Vector3(x-bevel,y,z), 5 => new Vector3(-x+bevel,y,z),
                    6 => new Vector3(-x,y,z-bevel), _ => new Vector3(-x,y,-z+bevel)
                };
                return center+p;
            }
            for(int i=0;i<8;i++)
            {
                int j=(i+1)%8;
                var a=Corner(i,0,-h+edge); var b=Corner(j,0,-h+edge);
                var c=Corner(j,0,h-edge); var e=Corner(i,0,h-edge);
                Vector3 n = Vector3.Cross(Vector3.up,b-a).normalized;
                Face(m,a,b,c,e,n);
                var lowI=Corner(i,edge,-h); var lowJ=Corner(j,edge,-h);
                var topI=Corner(i,edge,h); var topJ=Corner(j,edge,h);
                Face(m,lowI,lowJ,b,a,(n-Vector3.up).normalized);
                Face(m,e,c,topJ,topI,(n+Vector3.up).normalized);
                // Fill opposite bevelled caps with triangles around the common centre.
                Vector3 cap=center+Vector3.up*h;
                Face(m,cap,topI,topJ,cap,Vector3.up);
                cap=center-Vector3.up*h;
                Face(m,cap,lowI,lowJ,cap,Vector3.down);
            }
        }

        internal void Beam(Material m, Vector3 a, Vector3 b, float w, float d, Color32? tint = null)
        {
            Vector3 delta = b-a;
            Box(m,(a+b)*.5f,new Vector3(w,delta.magnitude,d),Quaternion.FromToRotation(Vector3.up,delta),tint);
        }

        // Tapered bands share radial normals, giving spun metal, porcelain and cast fittings
        // a continuous highlight even when the profile uses few sides.
        internal void Tube(Material m, Vector3 a, Vector3 b, float r0, float r1, int sides = 16, Color32? tint = null)
        {
            Vector3 axis = b-a;
            float length = axis.magnitude;
            axis /= length;
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.forward).normalized;
            Vector3 v = Vector3.Cross(axis,u);
            var batch = For(m);
            Color32 color = tint ?? White;
            int first = batch.Vertices.Count;
            for (int i = 0; i <= sides; ++i)
            {
                float f = i/(float)sides, angle = f*Mathf.PI*2;
                Vector3 radial = u*Mathf.Cos(angle)+v*Mathf.Sin(angle);
                Vector3 normal = (radial*length + axis*(r0-r1)).normalized;
                Vertex(batch,a+radial*r0,normal,f,0,color); Vertex(batch,b+radial*r1,normal,f,1,color);
                Vertex(batch,a+radial*r0,-axis,.5f+Mathf.Cos(angle)*.5f,.5f+Mathf.Sin(angle)*.5f,color);
                Vertex(batch,b+radial*r1,axis,.5f+Mathf.Cos(angle)*.5f,.5f+Mathf.Sin(angle)*.5f,color);
            }
            int cap0 = batch.Vertices.Count;
            Vertex(batch,a,-axis,.5f,.5f,color); Vertex(batch,b,axis,.5f,.5f,color);
            for (int i = 0; i < sides; ++i)
            {
                int s = first+i*4, e=s+4;
                batch.Triangles.Add(s); batch.Triangles.Add(e); batch.Triangles.Add(s+1);
                batch.Triangles.Add(s+1); batch.Triangles.Add(e); batch.Triangles.Add(e+1);
                batch.Triangles.Add(cap0); batch.Triangles.Add(e+2); batch.Triangles.Add(s+2);
                batch.Triangles.Add(cap0+1); batch.Triangles.Add(s+3); batch.Triangles.Add(e+3);
            }
        }

        internal void Disc(Material m, Vector3 middle, Vector3 normal, float radius, float thickness, int sides = 32, Color32? tint = null) =>
            Tube(m,middle-normal*thickness*.5f,middle+normal*thickness*.5f,radius,radius,sides,tint);

        private static Mesh MakeMesh(Batch b, string name)
        {
            var mesh = new Mesh { name = name };
            if (b.Vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(b.Vertices); mesh.SetNormals(b.Normals); mesh.SetUVs(0,b.UV);
            mesh.SetColors(b.Colors); mesh.SetTriangles(b.Triangles,0); mesh.RecalculateTangents();
            return mesh;
        }

#if UNITY_EDITOR
        internal Mesh[] Bake(string layer)
        {
            var meshes=new List<Mesh>(batches.Count);
            foreach(var item in batches)
                meshes.Add(MakeMesh(item.Value,layer + "/" + item.Key.name));
            batches.Clear();
            return meshes.ToArray();
        }
#endif

        internal static Renderer Mount(Transform parent, Mesh mesh, Material material, bool castShadows)
        {
            var part = new GameObject(mesh.name,typeof(MeshFilter),typeof(MeshRenderer));
            part.transform.SetParent(parent,false);
            part.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            if (!castShadows) renderer.shadowCastingMode = ShadowCastingMode.Off;
            return renderer;
        }

        internal void FinishProxy(Transform parent, ChartHouseAssets owner) => Finish(parent, owner.Meshes);

        internal void Finish(Transform parent, List<Mesh> ownedMeshes)
        {
            foreach (var item in batches)
            {
                var mesh=MakeMesh(item.Value,item.Key.name);
                mesh.UploadMeshData(true);
                ownedMeshes.Add(mesh);
                Mount(parent,mesh,item.Key,true);
            }
            batches.Clear();
        }
    }
}
