using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeavyPockets
{
    internal sealed partial class RaidScenery
    {
        private static Vector3 P(float x, float y, float z) => new Vector3(x,y,z);
        private static readonly Color32 OxBlood = new Color32(139,67,48,255);
        private static readonly Color32 Navy = new Color32(60,77,80,255);
        private static readonly Color32 Bone = new Color32(239,227,195,255);

        private Transform ChartHouseRoot(Transform parent, string name, Vector3 origin, out ChartHouseAssets owner)
        {
            var site = new GameObject(name).transform;
            site.SetParent(parent,false);
            site.localPosition = origin;
            owner = site.gameObject.AddComponent<ChartHouseAssets>();
            return site;
        }

        private void BuildChartHouseProxy(Transform parent, Vector3 origin)
        {
            var site = ChartHouseRoot(parent,"Штурманская • вдали",origin,out var owner);
            var shell = new ChartHouseMeshes(false);
            foreach (var wall in ChartHouseLayout.Walls) shell.Box(oxide,wall);
            shell.Box(black,ChartHouseLayout.Roof);
            shell.Box(teal,2.60f,2.73f,-4.32f,2.65f,.14f,.94f);
            shell.Box(teal,-2.58f,1.08f,-3.93f,4.42f,.12f,.29f);
            shell.Box(teal,5.91f,1.08f,0,.29f,.12f,4.98f);
            shell.FinishProxy(site,owner);
        }

        private void BuildChartHouse(Transform parent,Mesh[] shell,Mesh[] detail,Vector3 origin)
        {
            if(shell == null || shell.Length == 0 || detail == null || detail.Length == 0)
                throw new System.InvalidOperationException("The chart house tile has no baked interior meshes.");
            var site = ChartHouseRoot(parent,"Штурманская № 04",origin,out var owner);
            var f = owner.Finishes = new HarborInteriorMaterials();
            owner.BorrowedMeshes=true;
            MountChartHouse(site,shell,owner);
            var nearRoot = new GameObject("Фурнитура и надписи").transform;
            nearRoot.SetParent(site,false);
            var fine = MountChartHouse(nearRoot,detail,owner);

            WallText(nearRoot,"ШТУРМАНСКАЯ",P(2.6f,3.32f,-4.065f),0,.052f,Bone);
            WallText(nearRoot,"04",P(4.5f,1.87f,-4.068f),0,.078f,Bone);
            foreach (var label in nearRoot.GetComponentsInChildren<TextMesh>()) fine.Add(label.GetComponent<Renderer>());
            var lod = nearRoot.gameObject.AddComponent<LODGroup>();
            lod.SetLODs(new[]{ new LOD(.245f, fine.ToArray()) });
            lod.localReferencePoint = P(0,1.88f,0);
            lod.size = 12.6f;

            ChartHouseLight(site,"Свет над картой",LightType.Spot,P(.25f,3.38f,3.07f),P(.25f,1.78f,3.70f),
                new Color32(255,219,168,255),2.6f,4.7f,129f,true);
            ChartHouseLight(site,"Свет чертёжного стола",LightType.Spot,P(-1.27f,2.77f,-.15f),P(-1.27f,.85f,.04f),
                new Color32(255,223,174,255),3.7f,5.25f,107f,false);
            ChartHouseLight(site,"Свет архивного пролёта",LightType.Point,P(.45f,2.77f,1.32f),Vector3.zero,
                new Color32(255,216,169,255),2.02f,7.8f,0f,false);
            ChartHouseLight(site,"Отражение света от штукатурки",LightType.Point,P(2f,2.15f,.7f),Vector3.zero,
                new Color32(198,213,225,255),.76f,5.9f,0f,false);
            chartHouseReflection = f.InstallReflection(site);
            if (TrainingGround.Active) BuildChartFloorFinish(site);
        }

        private static List<Renderer> MountChartHouse(Transform parent,Mesh[] meshes,ChartHouseAssets owner)
        {
            var result=new List<Renderer>(meshes.Length);
            var finishes=owner.Finishes;
            foreach(Mesh mesh in meshes)
            {
                string key=mesh.name;
                var material=finishes.GetFinish(key.Substring(key.IndexOf('/')+1));
                result.Add(ChartHouseMeshes.Mount(parent,mesh,material,material!=finishes.Glass && material!=finishes.Lamp));
                owner.Meshes.Add(mesh);
            }
            return result;
        }

        private static void ChartHouseLight(Transform site,string name,LightType type,Vector3 position,Vector3 target,
            Color32 color,float intensity,float range,float cone,bool shadows)
        {
            var mount = new GameObject(name,typeof(Light)).transform;
            mount.SetParent(site,false);
            mount.localPosition = position;
            var light = mount.GetComponent<Light>();
            light.type = type; light.color = color; light.intensity = intensity; light.range = range;
            light.cullingMask &= ~(1 << AnimationStudio.Layer);
            if (type == LightType.Spot)
            {
                mount.localRotation = Quaternion.LookRotation(target-position,Vector3.up);
                light.spotAngle = cone; light.innerSpotAngle = cone*.64f;
            }
            if (shadows)
            {
                light.shadows = LightShadows.Soft; light.shadowStrength = .72f;
                light.shadowResolution = LightShadowResolution.Low; light.shadowBias = .018f;
                light.shadowNormalBias = .08f;
            }
        }

#if UNITY_EDITOR
        partial void BakeChartHouseMeshes(PortCellAsset tile,string path) => BakeChartHouseTile(tile,path);

        internal static void BakeChartHouseTile(PortCellAsset tile,string path)
        {
            using (var finishes=new HarborInteriorMaterials())
            {
                var shell=new ChartHouseMeshes();
                ChartHouseShell(shell,finishes);
                ChartHouseJoinery(shell,finishes);
                var detail=new ChartHouseMeshes();
                ChartHouseTrim(detail,finishes);
                ChartHouseInstruments(detail,finishes);
                ChartHouseArchive(detail,finishes);
                tile.ChartHouseShell=shell.Bake("Structure");
                tile.ChartHouseDetail=detail.Bake("Instruments");
                foreach(Mesh mesh in tile.ChartHouseShell) UnityEditor.AssetDatabase.AddObjectToAsset(mesh,path);
                foreach(Mesh mesh in tile.ChartHouseDetail) UnityEditor.AssetDatabase.AddObjectToAsset(mesh,path);
                UnityEditor.EditorUtility.SetDirty(tile);
            }
        }

        private static void ChartHouseShell(ChartHouseMeshes b, HarborInteriorMaterials f)
        {
            foreach (Bounds wall in ChartHouseLayout.Walls)
            {
                b.Box(f.Brick,wall);
                Vector3 c = wall.center, s = wall.size;
                Vector3 inner = s.x < s.z ? P(-Mathf.Sign(c.x),0,0) : P(0,0,-Mathf.Sign(c.z));
                // The inset lining leaves brick visible in the window and door reveals.
                var finish = new Bounds(c+inner*.120f, s.x < s.z ? P(.025f,s.y,s.z) : P(s.x,s.y,.025f));
                b.Box(f.Plaster,finish);
            }
            b.Box(f.Steel,ChartHouseLayout.Roof);
            b.Box(f.Plaster,0,3.896f,0,11.58f,.05f,7.58f);
            b.Box(f.Floor,0,-.061f,0,12.05f,.16f,8.05f);
            b.Box(f.Floor,2.6f,-.062f,-4.48f,2.6f,.144f,.97f);
            b.Box(f.Steel,2.6f,2.76f,-4.32f,2.67f,.14f,.95f);
            b.Box(f.Timber,2.6f,2.682f,-4.32f,2.58f,.027f,.89f);
            foreach (int side in new[]{-1,1})
            {
                float x = 2.6f+side*1.03f;
                b.Beam(f.Steel,P(x,3.22f,-4.0f),P(x,2.82f,-4.73f),.034f,.037f);
                b.Box(f.Paint,2.6f+side*.88f,1.29f,-3.90f,.10f,2.58f,.25f);
                b.Box(f.Steel,0,4.115f,side*4.12f,12.44f,.14f,.10f);
                b.Box(f.Steel,side*6.13f,4.115f,0,.10f,.14f,8.22f);
            }
            b.Box(f.Paint,2.6f,2.55f,-3.90f,1.84f,.12f,.25f);
            b.Box(f.Paint,2.6f,3.30f,-4.03f,2.66f,.49f,.055f);
            b.Box(f.Paint,4.5f,1.86f,-4.03f,.64f,.59f,.055f);
            Window(b,f,false);
            Window(b,f,true);

            // Timber pilasters and plate rails carry the same horizontal datum through the room.
            b.Box(f.Paint,0,.45f,3.748f,11.61f,.84f,.035f);
            b.Box(f.Paint,-5.748f,.45f,0,.035f,.84f,7.53f);
            b.Box(f.Timber,0,.90f,3.70f,11.59f,.082f,.11f);
            b.Box(f.Timber,-5.70f,.90f,0,.11f,.082f,7.51f);
            b.Box(f.Timber,0,.083f,3.71f,11.64f,.15f,.095f);
            b.Box(f.Timber,-5.71f,.083f,0,.095f,.15f,7.54f);
            for (float x=-5.4f; x<5.7f; x+=.66f) b.Box(f.Paint,x,.46f,3.714f,.047f,.77f,.045f);
            for (float z=-3.43f;z<3.7f;z+=.66f) b.Box(f.Paint,-5.714f,.46f,z,.045f,.77f,.047f);
            foreach (float x in new[]{-4.42f,-1.28f,1.86f,5.0f})
            {
                b.Box(f.Timber,x,3.775f,0,.19f,.24f,7.54f);
                b.Box(f.Timber,x,3.644f,0,.245f,.043f,7.54f);
            }
        }

        private static void Window(ChartHouseMeshes b,HarborInteriorMaterials f,bool east)
        {
            float left = east ? ChartHouseLayout.EastStart : ChartHouseLayout.FrontLeft;
            float right = east ? ChartHouseLayout.EastEnd : ChartHouseLayout.FrontRight;
            float y0=ChartHouseLayout.Sill,y1=ChartHouseLayout.WindowTop;
            float middle=(left+right)*.5f, width=right-left;
            void Bar(Material m,float along,float y,float inset,float w,float h,float depth)
            {
                if(east) b.Box(m,5.885f+inset,y,along,depth,h,w);
                else b.Box(m,along,y,-3.885f-inset,w,h,depth);
            }
            Bar(f.Glass,middle,(y0+y1)*.5f,0,width-.05f,y1-y0-.06f,.018f);
            Bar(f.Timber,middle,y0+.02f,-.038f,width+.23f,.096f,.41f);
            Bar(f.Paint,middle,y1-.048f,0,width+.05f,.085f,.17f);
            int lights = east ? 5 : 4;
            for (int i=0;i<=lights;++i)
            {
                float at=Mathf.Lerp(left,right,i/(float)lights);
                Bar(f.Paint,at,(y0+y1)*.5f,0,i==0||i==lights?.116f:.067f,y1-y0,.135f);
            }
            Bar(f.Paint,middle,2.64f,0,width,.06f,.14f);
        }
#endif
    }
}
