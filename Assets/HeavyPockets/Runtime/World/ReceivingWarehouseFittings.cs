#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HeavyPockets
{
    // Author the fittings in metres. Only the baked meshes and two practical lights load in a raid.
    internal static class ReceivingWarehouseFittings
    {
        private const string Dir = "Assets/HeavyPockets/Art/DockWarehouse/";
        private enum Finish { Plaster, Concrete, Timber, Carton, Paint, Metal, Sign, Manifest }
        private static readonly Color32 Ivory = C(243,239,222), Teal = C(52,88,84),
            Iron = C(90,95,89), Ink = C(60,63,59), Ochre = C(207,160,79), White = C(255,255,255);
        private static Color32 C(byte r, byte g, byte b) => new Color32(r,g,b,255);
        private static Vector3 V(float x,float y,float z) => new Vector3(x,y,z);

        private sealed class Batch
        {
            private readonly List<Vector3> pos = new List<Vector3>(), normals = new List<Vector3>();
            private readonly List<Vector2> uv = new List<Vector2>(), finish = new List<Vector2>();
            private readonly List<Color32> colors = new List<Color32>();
            private readonly List<int> indices = new List<int>();

            public void Panel(Finish mat,Color32 tint,Vector3 c,Vector3 right,Vector3 up,Vector3 normal,bool print=false)
            {
                int first=pos.Count;
                float w=print?1:right.magnitude*2, h=print?1:up.magnitude*2;
                pos.Add(c-right-up); pos.Add(c+right-up); pos.Add(c+right+up); pos.Add(c-right+up);
                uv.Add(Vector2.zero); uv.Add(new Vector2(w,0)); uv.Add(new Vector2(w,h)); uv.Add(new Vector2(0,h));
                for(int i=0;i<4;i++){ normals.Add(normal); colors.Add(tint); finish.Add(new Vector2((int)mat,0)); }
                bool cw = Vector3.Dot(Vector3.Cross(right,up),normal)>=0f;
                indices.Add(first); indices.Add(first+(cw?1:2)); indices.Add(first+(cw?2:1));
                indices.Add(first); indices.Add(first+(cw?2:3)); indices.Add(first+(cw?3:2));
            }

            public void Box(Finish mat,Color32 col,float x,float y,float z,float w,float h,float d)
            {
                Vector3 c=V(x,y,z), r=Vector3.right*(w*.5f),u=Vector3.up*(h*.5f),f=Vector3.forward*(d*.5f);
                Panel(mat,col,c+r,-f,u,Vector3.right); Panel(mat,col,c-r,f,u,Vector3.left);
                Panel(mat,col,c+u,r,f,Vector3.up); Panel(mat,col,c-u,r,-f,Vector3.down);
                Panel(mat,col,c+f,-r,u,Vector3.forward); Panel(mat,col,c-f,r,u,Vector3.back);
            }

            public void Rod(Vector3 start,Vector3 end,float radius,Color32 tint, int count=8)
            {
                Vector3 axis=(end-start).normalized;
                Vector3 right=Vector3.Cross(axis,Math.Abs(axis.y)>.9f?Vector3.right:Vector3.up).normalized;
                Vector3 up=Vector3.Cross(axis,right);
                for(int i=0;i<count;i++)
                {
                    float a=2*Mathf.PI*i/count, b=2*Mathf.PI*(i+1)/count;
                    Vector3 rayA=right*Mathf.Cos(a)+up*Mathf.Sin(a), rayB=right*Mathf.Cos(b)+up*Mathf.Sin(b);
                    Vector3 middle=(start+end)*.5f+(rayA+rayB)*(radius*.5f);
                    Panel(Finish.Metal,tint,middle,(rayB-rayA)*(radius*.5f),(end-start)*.5f,(rayA+rayB).normalized);
                }
            }

            public Mesh Save(string name,string path)
            {
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (!mesh) {mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}
                mesh.Clear();
                mesh.name=name; mesh.SetVertices(pos); mesh.SetNormals(normals); mesh.SetColors(colors);
                mesh.SetUVs(0,uv); mesh.SetUVs(1,finish); mesh.SetTriangles(indices,0); mesh.RecalculateTangents();
                mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
                return mesh;
            }
        }

        [MenuItem("Heavy Pockets/Карта/Пересчитать интерьер приёмного склада")]
        public static void Build()
        {
            var shell=AssetDatabase.LoadAssetAtPath<Mesh>(Dir+"ReceivingWarehouse.asset");
            var outer=AssetDatabase.LoadAssetAtPath<Material>(Dir+"Dock brick and enamel.mat");
            if (!shell || !outer) throw new InvalidOperationException("Receiving warehouse shell or facade finish missing.");
            var paint=AssetDatabase.LoadAssetAtPath<Material>(Dir+"Receiving interior.mat");
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(Dir+"ReceivingFinishes.shader");
            if (!paint) { paint=new Material(shader); AssetDatabase.CreateAsset(paint,Dir+"Receiving interior.mat"); }
            paint.shader=shader;
            paint.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(Dir+"Receiving finishes.jpg"));
            paint.SetTexture("_Surface",AssetDatabase.LoadAssetAtPath<Texture2D>(Dir+"Receiving finishes detail.jpg"));
            EditorUtility.SetDirty(paint);
            var site=new GameObject("ReceivingWarehouse",typeof(MeshFilter),typeof(MeshRenderer));
            try
            {
                site.GetComponent<MeshFilter>().sharedMesh=shell;
                site.GetComponent<MeshRenderer>().sharedMaterial=outer;
                BuildRoofShadow(site.transform,outer);
                var room=new Batch(); Room(room); Fixtures(room);
                Mount(site.transform,"Receiving room",room.Save("Warehouse interior",Dir+"Receiving room.asset"),paint);
                var odds=new Batch(); Stock(odds); Desk(odds);
                var near=Mount(site.transform,"Cargo and paperwork",odds.Save("Freight and fittings",Dir+"Receiving stock.asset"),paint);
                var lod=near.gameObject.AddComponent<LODGroup>();
                lod.SetLODs(new[]{new LOD(.245f,new[]{near})}); lod.RecalculateBounds();
                var leaf=new Batch(); Door(leaf);
                var door=Mount(site.transform,ReceivingWarehouseLayout.DoorLeafName,leaf.Save("Service door leaf",Dir+"Receiving door.asset"),paint);
                door.transform.localPosition=ReceivingWarehouseLayout.DoorHinge-ReceivingWarehouseLayout.Origin;
                var shutter = new Batch();
                LoadingShutter(shutter);
                var curtain = Mount(site.transform, ReceivingWarehouseLayout.GateLeafName,
                    shutter.Save("Interlocking steel slats", Dir+"Receiving shutter.asset"), paint);
                curtain.transform.localPosition = ReceivingWarehouseLayout.GateBottom-ReceivingWarehouseLayout.Origin;
                foreach(float x in new[]{-3.6f,3.55f})
                {
                    var lamp=new GameObject("Подвесной светильник").AddComponent<Light>();
                    lamp.transform.SetParent(site.transform,false); lamp.transform.localPosition=V(x,3.67f,.25f);
                    lamp.transform.localRotation=Quaternion.Euler(90,0,0);
                    lamp.type=LightType.Spot; lamp.range=7.8f; lamp.spotAngle=132; lamp.innerSpotAngle=73;
                    lamp.color=new Color32(255,230,190,255); lamp.intensity=2.75f; lamp.shadows=LightShadows.None;
                }
                PrefabUtility.SaveAsPrefabAsset(site,Dir+"ReceivingWarehouse.prefab");
                AssetDatabase.SaveAssets();
            }
            finally {Object.DestroyImmediate(site);}
        }

        // The facade and interior ceiling meet at an eave. Give the shadow pass the
        // entire structural slab so a grazing sun cannot shine between those skins.
        private static void BuildRoofShadow(Transform parent, Material material)
        {
            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.name = "Structural roof shadow";
            roof.transform.SetParent(parent,false);
            roof.transform.localPosition = ReceivingWarehouseLayout.Roof.center;
            roof.transform.localScale = ReceivingWarehouseLayout.Roof.size;
            Object.DestroyImmediate(roof.GetComponent<Collider>());
            var renderer = roof.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            renderer.receiveShadows = false;
        }

        private static MeshRenderer Mount(Transform parent,string name,Mesh mesh,Material mat)
        {
            var part=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer)); part.transform.SetParent(parent,false);
            part.GetComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=part.GetComponent<MeshRenderer>(); renderer.sharedMaterial=mat;
            return renderer;
        }

        private static void Wall(Batch b,float x,float z,float width,float depth)
        {
            // The scanned windows recess into the masonry. A single
            // inward-facing lining keeps these exterior recesses visible at oblique angles.
            bool endWall = depth>width;
            Vector3 n = endWall ? -Mathf.Sign(x)*Vector3.right : -Mathf.Sign(z)*Vector3.forward;
            Vector3 r = Vector3.Cross(n,Vector3.up)*(Mathf.Max(width,depth)*.5f);
            Vector3 center = V(x,0,z)+n*(Mathf.Min(width,depth)*.5f);
            b.Panel(Finish.Plaster,Ivory,center+Vector3.up*2.38f,r,Vector3.up*2.38f,n);
            b.Panel(Finish.Paint,C(106,133,122),center+n*.006f+Vector3.up*.56f,r,Vector3.up*.56f,n);
            b.Panel(Finish.Paint,Teal,center+n*.008f+Vector3.up*1.126f,r,Vector3.up*.010f,n);
            b.Panel(Finish.Concrete,C(148,145,131),center+n*.013f+Vector3.up*.074f,r,Vector3.up*.074f,n);
        }

        private static void Room(Batch b)
        {
            b.Box(Finish.Concrete,C(201,197,182),0,-.045f,0,14,.090f,9);
            Wall(b,0,4.296f,13.66f,.10f); Wall(b,-6.807f,0,.10f,8.52f); Wall(b,6.807f,0,.10f,8.52f);
            foreach (Bounds span in ReceivingWarehouseLayout.FrontMasonry)
            {
                if (span.min.y > .01f)
                {
                    b.Panel(Finish.Plaster,Ivory,V(span.center.x,span.center.y,span.max.z),
                        Vector3.right*span.extents.x,Vector3.up*span.extents.y,Vector3.forward);
                }
                else
                {
                    Wall(b,span.center.x,-4.29f,span.size.x,.10f);
                    b.Box(Finish.Concrete,Iron,span.center.x,.076f,-4.43f,span.size.x,.152f,.14f);
                }
            }
            b.Box(Finish.Concrete,Iron,0,.073f,4.431f,14,.146f,.117f);
            foreach(float x in new[]{-6.93f,6.93f}) b.Box(Finish.Concrete,Iron,x,.073f,0,.14f,.146f,8.87f);
            // The dark jamb and two slim stops frame a 1.08 × 2.10 m moving leaf.
            foreach(float x in new[]{4.64f,5.76f})
                b.Box(Finish.Paint,Teal,x,1.093f,-4.386f,.04f,2.186f,.185f);
            b.Box(Finish.Paint,Teal,5.2f,2.166f,-4.386f,1.16f,.050f,.185f);
            b.Box(Finish.Metal,Iron,5.2f,2.211f,-4.455f,1.28f,.040f,.14f);
            b.Box(Finish.Plaster,C(188,189,169),0,4.64f,0,13.73f,.045f,8.65f);
            foreach(float x in new[]{-5.0f,-1.7f,1.7f,5.0f})
            {
                b.Box(Finish.Paint,Teal,x,4.44f,0,.09f,.35f,8.5f);
                b.Box(Finish.Paint,Teal,x,4.27f,0,.17f,.035f,8.5f);
            }
            // Inward faces of the same steel-framed opaque glass blocks as the scanned exterior.
            Window(b,V(-5.20f,1.9f,-4.205f),Vector3.right,2.16f,1.57f);
            Window(b,V(5.20f,2.33f,4.21f),Vector3.left,1.43f,.78f);
            Window(b,V(-1.73f,1.90f,4.21f),Vector3.left,2.28f,1.61f);
            Window(b,V(-6.73f,1.90f,2.96f),Vector3.back,1.85f,1.56f);
            Window(b,V(-6.73f,1.90f,-2.96f),Vector3.back,1.77f,1.56f);
            Window(b,V(6.73f,2.11f,-2.96f),Vector3.forward,.65f,1.18f);
            Window(b,V(6.73f,2.39f,2.96f),Vector3.forward,1.16f,.70f);
            ShutterFrame(b);
        }

        private static void ShutterFrame(Batch b)
        {
            const float width=ReceivingWarehouseLayout.GateWidth, height=ReceivingWarehouseLayout.GateHeight;
            float z = ReceivingWarehouseLayout.GateBottom.z-ReceivingWarehouseLayout.Origin.z;
            foreach (float side in new[]{-1f, 1f})
            {
                float x = side*(width*.5f+.045f);
                b.Box(Finish.Paint,Teal,x,height*.5f,z+.015f,.090f,height,.40f);
                b.Box(Finish.Metal,Iron,x,height*.5f,z-.17f,.056f,height,.038f);
                for(int bolt=0; bolt<4; ++bolt)
                    b.Box(Finish.Metal,Ivory,x,.31f+bolt*.75f,z-.195f,.018f,.018f,.006f);
            }
            b.Box(Finish.Metal,Teal,0,height+.05f,z,width+.18f,.10f,.40f);
            b.Box(Finish.Paint,C(109,127,122),0,height+.23f,z+.06f,width+.33f,.34f,.49f);
            b.Box(Finish.Metal,Iron,0,height+.101f,z-.188f,width+.34f,.035f,.055f);
            // Two reachable control stations, one on each face of the entrance.
            foreach (float face in new[]{-1f,1f})
            {
                float stationZ = face<0 ? -4.54f : -4.185f;
                b.Box(Finish.Paint,Ink,2.08f,1.18f,stationZ,.13f,.23f,.075f);
                b.Box(Finish.Metal,Iron,2.08f,1.18f,stationZ+face*.041f,.110f,.20f,.014f);
                foreach (float button in new[]{-.044f,.044f})
                {
                    Vector3 pin = V(2.08f,1.18f+button,stationZ+face*.05f);
                    Color32 tint = button>0 ? C(115,170,135) : C(181,108,87);
                    b.Rod(pin,pin+Vector3.forward*(face*.013f),.020f,tint);
                    b.Panel(Finish.Paint,tint,pin+Vector3.forward*(face*.014f),
                        Vector3.right*.014f,Vector3.up*.014f,Vector3.forward*face);
                }
            }
        }

        private static void LoadingShutter(Batch b)
        {
            const float w=ReceivingWarehouseLayout.GateWidth, h=ReceivingWarehouseLayout.GateHeight;
            const float t=ReceivingWarehouseLayout.GateThickness, rail=.056f;
            const int slats=32;
            float pitch=(h-rail)/slats;
            Color32 enamel=C(155,176,168);
            b.Box(Finish.Metal,Iron,0,h*.5f,0,w,h,t*.50f);
            for(int i=0;i<slats;i++)
            {
                float y=rail+i*pitch;
                foreach(float side in new[]{-1f,1f})
                {
                    Vector3 normal=Vector3.forward*side;
                    b.Panel(Finish.Paint,enamel,V(0,y+.54f*pitch,side*.025f),Vector3.right*(w*.5f),
                        V(0,.40f*pitch,side*.006f),normal);
                    b.Panel(Finish.Metal,Iron,V(0,y+.09f*pitch,side*.025f),Vector3.right*(w*.5f),
                        V(0,.055f*pitch,-side*.006f),normal);
                }
            }
            b.Box(Finish.Paint,Teal,0,rail*.58f,0,w,rail*.84f,t);
            b.Box(Finish.Metal,Ink,0,rail*.12f,0,w,.014f,t*.66f);
        }

        private static void Window(Batch b,Vector3 c,Vector3 right,float width,float height)
        {
            Vector3 n=Vector3.Cross(Vector3.up,right);
            // Right is oriented as seen from outside for the two lengthwise walls.
            n=Vector3.Dot(n,-c)>0?n:-n;
            Vector3 r=right*(width/2),u=Vector3.up*(height/2);
            b.Panel(Finish.Paint,Ink,c,r+right*.060f,u+Vector3.up*.067f,n);
            var daylight=C(145,173,170); daylight.a=90;
            b.Panel(Finish.Paint,daylight,c+n*.006f,r,u,n);
            for(int i=1;i<4;i++)
                b.Panel(Finish.Paint,Teal,c+n*.013f+right*(width*(i*.25f-.5f)),right*.014f,u,n);
            b.Panel(Finish.Paint,Teal,c+n*.016f+Vector3.up*(height*.09f),r,Vector3.up*.016f,n);
            b.Panel(Finish.Metal,Iron,c-u+n*.075f,r+right*.075f,n*.083f,Vector3.up);
        }

        private static void Door(Batch b)
        {
            const float w=ReceivingWarehouseLayout.DoorWidth, h=ReceivingWarehouseLayout.DoorHeight;
            const float t=ReceivingWarehouseLayout.DoorThickness;
            b.Box(Finish.Paint,C(122,140,126),w*.5f,h*.5f,0,w,h,t);
            foreach(float side in new[]{-1f,1f})
            {
                float z=side*(t*.5f+.005f);
                b.Box(Finish.Paint,Iron,.77f,1.54f,z,.30f,.65f,.012f);
                b.Box(Finish.Metal,C(148,163,153),.77f,1.54f,z+side*.012f,.254f,.598f,.012f);
                b.Box(Finish.Metal,Ink,.933f,1.02f,z+side*.011f,.069f,.165f,.016f);
                Vector3 stem=V(.933f,1.035f,z);
                b.Rod(stem,stem+V(0,0,side*.071f),.016f,Iron);
                b.Rod(stem+V(0,0,side*.071f),stem+V(-.119f,0,side*.071f),.016f,Iron);
                for(int slat=0;slat<6;slat++)
                    b.Box(Finish.Paint,Iron,.52f,.26f+.03f*slat,z,.37f,.012f,.010f);
            }
            foreach(float y in new[]{.22f,1.08f,1.88f}) b.Rod(V(0,y,-.021f),V(0,y+.105f,-.021f),.012f,Iron);
        }

        private static void Fixtures(Batch b)
        {
            foreach(float x in new[]{-3.6f,3.55f})
            {
                foreach(float dx in new[]{-.47f,.47f})
                    b.Rod(V(x+dx,4.612f,.25f),V(x+dx,3.87f,.25f),.007f,Iron,5);
                b.Box(Finish.Paint,Iron,x,3.84f,.25f,1.43f,.10f,.25f);
                var opal=C(255,241,192); opal.a=45;
                b.Box(Finish.Paint,opal,x,3.77f,.25f,1.32f,.048f,.177f);
            }
            // Receiving stripe is painted on the floor, outside the clear pedestrian route.
            foreach(float x in new[]{-6.14f,-3.15f}) b.Box(Finish.Paint,Ochre,x,.001f,-.68f,.05f,.001f,2.24f);
            foreach(float z in new[]{-1.8f,.44f}) b.Box(Finish.Paint,Ochre,-4.645f,.001f,z,3.04f,.001f,.05f);
            b.Panel(Finish.Sign,White,V(3.2f,2.32f,4.22f),Vector3.right*.78f,Vector3.up*.30f,Vector3.back,true);
            foreach(Bounds rack in ReceivingWarehouseLayout.Racks)
            {
                foreach(float x in new[]{rack.min.x,rack.center.x,rack.max.x})
                    foreach(float z in new[]{rack.min.z,rack.max.z})
                        b.Box(Finish.Paint,Teal,x,1.23f,z,.062f,2.46f,.064f);
                foreach(float y in new[]{.14f,.89f,1.65f,2.43f})
                {
                    b.Box(Finish.Timber,White,rack.center.x,y,rack.center.z,rack.size.x,.045f,rack.size.z);
                    foreach(float z in new[]{rack.min.z,rack.max.z})
                        b.Box(Finish.Paint,Ochre,rack.center.x,y-.049f,z,rack.size.x,.067f,.058f);
                }
                b.Rod(V(rack.min.x,.17f,rack.max.z),V(rack.max.x,2.41f,rack.max.z),.018f,Iron);
                b.Rod(V(rack.max.x,.17f,rack.max.z),V(rack.min.x,2.41f,rack.max.z),.018f,Iron);
            }
            Bounds desk=ReceivingWarehouseLayout.Desk;
            b.Box(Finish.Timber,White,desk.center.x,.877f,desk.center.z,desk.size.x,.066f,desk.size.z);
            foreach(float x in new[]{desk.min.x+.085f,desk.max.x-.085f})
                foreach(float z in new[]{desk.min.z+.10f,desk.max.z-.10f})
                    b.Box(Finish.Paint,Teal,x,.42f,z,.065f,.84f,.065f);
            b.Box(Finish.Paint,Teal,desk.center.x,.63f,desk.max.z-.41f,1.02f,.42f,.64f);
            foreach(float y in new[]{.56f,.76f}) b.Box(Finish.Metal,Iron,desk.min.x+.075f,y,desk.max.z-.41f,.035f,.023f,.16f);
        }

        private static void Carton(Batch b,Vector3 center,Vector3 size,Color32 tint)
        {
            b.Box(Finish.Carton,tint,center.x,center.y,center.z,size.x,size.y,size.z);
            b.Box(Finish.Paint,C(183,156,111),center.x,center.y+.0005f,center.z,.044f,size.y+.003f,size.z+.003f);
            Vector3 face=center+V(size.x*.22f,0,-size.z*.5f-.002f);
            b.Panel(Finish.Manifest,White,face,Vector3.right*.066f,Vector3.up*.052f,Vector3.back,true);
        }

        private static void Stock(Batch b)
        {
            var cartonTints=new[]{C(236,211,172),C(210,186,159),C(222,224,205),C(230,197,154)};
            int serial=0;
            foreach(Bounds rack in ReceivingWarehouseLayout.Racks)
                for(int shelf=0;shelf<3;shelf++)
                    for(int i=0;i<4;i++,serial++)
                    {
                        if((serial+1)%7==0) continue;
                        float y=new[]{.164f,.914f,1.674f}[shelf];
                        Vector3 size=V(.59f+.041f*(serial%4),.37f+.05f*(serial%3),.65f);
                        Carton(b,V(rack.min.x+.47f+.82f*i,y+size.y*.5f,rack.center.z-.054f),size,cartonTints[serial%4]);
                    }
            foreach(Bounds load in ReceivingWarehouseLayout.Freight)
            {
                for(int row=0;row<3;row++)
                    b.Box(Finish.Timber,Ivory,load.min.x+.095f+row*.505f,.064f,load.center.z,.13f,.128f,.80f);
                for(int board=0;board<5;board++)
                    b.Box(Finish.Timber,White,load.center.x,.134f,load.min.z+.076f+board*.161f,1.20f,.032f,.13f);
                float h=load.size.y-.15f;
                for(int box=0;box<2;box++,serial++)
                    Carton(b,V(load.min.x+.30f+box*.60f,.15f+h*.5f,load.center.z),V(.579f,h,.76f),cartonTints[serial%4]);
            }
        }

        private static void Desk(Batch b)
        {
            b.Box(Finish.Paint,Teal,6.01f,.925f,1.53f,.52f,.028f,.40f);
            b.Panel(Finish.Manifest,White,V(6.01f,.942f,1.53f),Vector3.forward*.175f,Vector3.right*.244f,Vector3.up,true);
            b.Box(Finish.Timber,White,6.42f,.935f,2.20f,.15f,.045f,.095f);
            b.Rod(V(6.42f,.96f,2.20f),V(6.42f,1.025f,2.20f),.025f,Iron);
            b.Rod(V(5.87f,.925f,1.93f),V(5.93f,.925f,2.09f),.005f,Ochre);
            for(int i=0;i<3;i++)
                b.Box(Finish.Paint,i%2==0?Teal:Ivory,6.08f,.939f+i*.050f,2.69f,.38f,.046f,.46f);
        }
    }
}
#endif
