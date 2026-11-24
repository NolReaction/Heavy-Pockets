using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeavyPockets
{
    internal sealed partial class RaidScenery
    {
        internal CoastalSurfaceField TrainingSurfaces { get; private set; }

        private void BuildTrainingRange()
        {
            var approaches = TrainingApproach.Layout(WorldPlan.Load(true));
            TrainingSurfaces = CoastalSurfaceField.Create(root, Vector2.zero,
                new Vector2(RaidMap.RoomX * 2f, RaidMap.RoomZ * 2f), (p, margin) =>
                    TrainingLayout.IsPaved(p,margin) || TrainingApproach.Covers(approaches,p,margin),
                ReceivingWarehouseLayout.Foundation(TrainingBuildings.Warehouse)).GetComponent<CoastalSurfaceField>();
            TrainingGrove.Plant(TrainingSurfaces);
            var mint = Surface("Seafoam enamel", Rgb(0x71B3A4));
            var brick = Surface("Terracotta rubber", Rgb(0xB96947), SurfaceFinish.Rubber);
            var sand = Surface("Pale limestone", Rgb(0xE2CC9F), SurfaceFinish.Concrete);
            var welcome = new Material(Resources.Load<Shader>("DepotPrint")) { name = "Range directory" };
            welcome.mainTexture = Resources.Load<Texture2D>("TrainingDirectory");
            surfaces.Add(welcome);

            var landings = new ChartHouseMeshes(false);
            foreach (RangePart part in TrainingLayout.Parts)
            {
                Bounds b = part.Box;
                Vector3 c = b.center, s = b.size;
                Material finish = part.Kind == RangeFixture.Board ? teal : part.Kind == RangeFixture.Platform ? sand :
                    part.Kind == RangeFixture.Frame ? mint : part.Kind == RangeFixture.Beam ? amber :
                    part.Kind == RangeFixture.CrawlRoof ? brick : steel;
                if (part.Kind == RangeFixture.Platform || part.Kind == RangeFixture.Beam)
                {
                    BuildTrainingLanding(landings,b,finish,brick,chalk);
                    if (part.Label != null)
                        WallText(architecture,part.Label,new Vector3(c.x,c.y,b.min.z-.008f),0,.054f,Rgb(0x3B4D48));
                    continue;
                }
                Part(architecture, part.Label ?? part.Kind.ToString(), c.x,c.y,c.z,s.x,s.y,s.z,finish);
                if (part.Kind != RangeFixture.Board) continue;
                if (part.Label == "Полигон")
                {
                    // Quad UVs preserve the map artwork's aspect ratio on the entrance panel.
                    var face = Shape(root,PrimitiveType.Quad,"Training ground plan",
                        new Vector3(c.x,c.y,b.min.z-.003f),new Vector3(s.x-.06f,s.y-.06f,1),welcome);
                    face.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                    continue;
                }
                var front = new Vector3(c.x,c.y,b.min.z-.006f);
                WallText(architecture,part.Label,front,0,.053f,Rgb(0xFFF0CB));
                WallText(architecture,part.Label,new Vector3(c.x,c.y,b.max.z+.006f),180,.053f,Rgb(0xFFF0CB));
            }

            // Survey markers make the full extent of the level field legible from the running lanes.
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = -6; i <= 6; i++)
                    RangeMarker(i*11.8f,side*55.85f);
                for (int i = -4; i <= 4; i++)
                    RangeMarker(side*71.85f,i*11.2f);
            }
            landings.Finish(Panel(root,"Parkour landings",0,0,0,0),shellMeshes);
            BuildPracticePromenade(approaches);
            BuildRangeHorizon();
            BuildExtraction();
            foreach (var post in TrainingGround.ExitPosts)
                BuildYardSolid(architecture, post, 0);
            BakeArchitecture();
            BuildSampleLeaf(WorldMechanisms.TrainingDoor,mint,brick);
            BuildSampleLeaf(WorldMechanisms.TrainingGate,mint,brick);
            InitializeTrainingBuildings();
        }

        private void RangeMarker(float x, float z)
        {
            // Flush survey tiles leave the running surface clear.
            Part(architecture,"Boundary marker",x,.001f,z,.30f,.007f,.30f,chalk);
            Part(architecture,"Marker centre",x,.006f,z,.12f,.004f,.12f,teal);
        }

        private void BuildSampleLeaf(WorldMechanism mechanism, Material mint, Material brick)
        {
            Vector3 p = mechanism.Pivot, s = mechanism.Size;
            Transform leaf = Panel(root,mechanism.IsShutter ? "Practice roller shutter" : "Practice swing door",p.x,p.y,p.z,0);
            var shape = new PropGeometry();
            if (mechanism.IsShutter)
            {
                shape.Box(teal,0,s.y*.5f,0,s.x,s.y,s.z,.014f);
                const int slats = 19;
                for (int i=0;i<slats;i++)
                    shape.Box(i < 2 ? brick : mint,0,(i+.5f)*s.y/slats,0,s.x-.018f,s.y/slats-.013f,s.z+.011f,.013f);
                float postX = p.x + s.x*.5f + .18f;
                foreach (int side in new[] {-1,1})
                {
                    Transform panel = Panel(root,"Gate switch",postX,1.10f,p.z+side*.17f,0);
                    var box = new PropGeometry();
                    box.Box(teal,0,0,0,.17f,.27f,.065f,.018f);
                    box.Box(chalk,0,0,side*.037f,.13f,.23f,.025f,.012f);
                    box.Box(mint,0,.047f,side*.054f,.07f,.06f,.018f,.012f);
                    box.Box(brick,0,-.045f,side*.054f,.07f,.06f,.018f,.012f);
                    box.Apply(panel,propMeshes);
                }
            }
            else
            {
                shape.Box(mint,s.x*.5f,s.y*.5f,0,s.x,s.y,s.z,.012f);
                foreach (int side in new[] {-1,1})
                {
                    float face = side*s.z*.5f;
                    shape.Box(teal,s.x*.62f,1.49f,face,.31f,.54f,.012f,.026f);
                    shape.Box(glass,s.x*.62f,1.49f,face+side*.012f,.25f,.48f,.012f,.016f);
                    shape.Box(steel,s.x-.16f,1.00f,face+side*.06f,.17f,.032f,.037f,.012f);
                    shape.Box(steel,s.x-.098f,1.00f,face+side*.033f,.042f,.060f,.065f,.012f);
                    shape.Box(teal,s.x*.5f,.19f,face,s.x-.07f,.26f,.012f,.005f);
                }
            }
            shape.Apply(leaf,propMeshes);
            mechanism.Bind(leaf);
        }

    }
}
