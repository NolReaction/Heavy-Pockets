using UnityEngine;
using UnityEngine.Rendering;

namespace HeavyPockets
{
    // Surveyed trees on the playable west bank. The surface garden, PhysX and the motor
    // use exactly these trunk centres; shrubs and meadow plants are permeable.
    internal sealed class TrainingGrove : MonoBehaviour
    {
        internal readonly struct Tree
        {
            internal readonly Vector2 Ground;
            internal readonly float Scale;
            internal readonly int Form;
            internal float TrunkRadius => Scale * (Form == 1 ? .13f : .18f);
            internal Tree(float x, float z, float scale, int form)
            {
                Ground = new Vector2(x, z);
                Scale = scale;
                Form = form;
            }
        }

        internal static readonly Tree[] Trees =
        {
            new Tree(-6.8f,-17.5f,.95f,0), new Tree(-7f,-38f,.76f,1),
            new Tree(-22f,-13f,1.10f,2), new Tree(-27f,-10.7f,.73f,1),
            new Tree(-35.6f,1.4f,1.08f,0), new Tree(-40.6f,4.7f,.85f,1),
            new Tree(-46f,8.2f,.96f,2), new Tree(-59f,5.4f,.80f,1),
            new Tree(-61f,11.3f,.96f,0), new Tree(-58f,29.3f,1.13f,2),
            new Tree(-62f,35f,.83f,1), new Tree(-45f,38f,.90f,0),
            new Tree(-36.6f,42.5f,1.1f,2), new Tree(-28.5f,48f,.80f,1),
            new Tree(-19.1f,31f,.85f,1), new Tree(-12f,19f,1.04f,0),
            new Tree(-7f,25f,.76f,1), new Tree(-59f,-13.8f,.90f,1),
            new Tree(-64f,-18.0f,.84f,2), new Tree(-21f,-38.5f,.79f,1),
            new Tree(-26f,-43f,.94f,0), new Tree(-48f,-47f,.80f,2)
        };

        internal static readonly Bounds[] Trunks = System.Array.ConvertAll(Trees, tree =>
        {
            float diameter = 2f * tree.TrunkRadius;
            float height = 4.65f * tree.Scale;
            return new Bounds(new Vector3(tree.Ground.x,height*.5f,tree.Ground.y),new Vector3(diameter,height,diameter));
        });

        private readonly Mesh[,] meshes = new Mesh[5,3];
        private Material paint;

        internal static void Plant(CoastalSurfaceField surface)
        {
            var grove = new GameObject("Coastal broadleaves and hedgerows").AddComponent<TrainingGrove>();
            grove.transform.SetParent(surface.transform,false);
            grove.paint = new Material(Resources.Load<Shader>("SurfaceGarden/CoastalGrove")) {
                name = "Coastal foliage", mainTexture = Resources.Load<Texture2D>("SurfaceGarden/Coastal Branches"),
                enableInstancing = true
            };
            surface.GrovePaint = grove.paint;
            for (int form=0; form<5; form++)
                for(int lod=0; lod<3; lod++)
                    grove.meshes[form,lod]=new CoastalPlantMeshes(form,lod).Build();

            var random = new System.Random(31972);
            foreach (Tree tree in Trees)
            {
                grove.Specimen(tree.Ground,tree.Scale,tree.Form,(float)random.NextDouble()*360f);
                // Shrubs grow in loose arcs beside the trees and out along the open sunny edge.
                int count=random.Next(2,6);
                float heading=(float)random.NextDouble()*Mathf.PI*2;
                for (int i=0;i<count;i++)
                {
                    float angle=heading+i*.84f;
                    float radius=2.9f+(float)random.NextDouble()*2.2f;
                    Vector2 ground=tree.Ground+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
                    var habitat=CoastalSurfaceField.Layers(ground.x,ground.y);
                    if (habitat.r>.2f || habitat.g>.80f || ground.x>-4f) continue;
                    grove.Specimen(ground,Mathf.Lerp(.70f,1.30f,(float)random.NextDouble()),3+i%2,(float)random.NextDouble()*360f);
                }
            }
        }

        private void Specimen(Vector2 ground,float scale,int form,float yaw)
        {
            bool shrub=form>=3;
            var plant=new GameObject(shrub ? "Coastal shrub" : "Broadleaf tree");
            plant.transform.SetParent(transform,false);
            plant.transform.localPosition=new Vector3(ground.x,0f,ground.y);
            plant.transform.localRotation=Quaternion.Euler(0,yaw,0);
            plant.transform.localScale=Vector3.one*scale;
            var lods=new LOD[3];
            for(int i=0;i<3;i++)
            {
                var leaf=new GameObject("LOD"+i,typeof(MeshFilter),typeof(MeshRenderer));
                leaf.transform.SetParent(plant.transform,false);
                leaf.GetComponent<MeshFilter>().sharedMesh=meshes[form,i];
                var renderer=leaf.GetComponent<MeshRenderer>();
                renderer.sharedMaterial=paint;
                renderer.lightProbeUsage=LightProbeUsage.Off;
                renderer.shadowCastingMode=i==2 ? ShadowCastingMode.Off : ShadowCastingMode.On;
                float threshold=i==0 ? (shrub ? .105f : .155f) : i==1 ? (shrub ? .045f : .060f) : (shrub ? .012f : .009f);
                lods[i]=new LOD(threshold,new Renderer[]{renderer});
            }
            var group=plant.AddComponent<LODGroup>();
            group.SetLODs(lods);
            group.RecalculateBounds();
        }

        private void OnDestroy()
        {
            foreach (var mesh in meshes) if(mesh) Release(mesh);
            if(paint) Release(paint);
        }
        private static void Release(Object item)
        {
            if(Application.isPlaying) Destroy(item);
            else DestroyImmediate(item);
        }
    }
}
