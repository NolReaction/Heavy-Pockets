using UnityEngine;

namespace HeavyPockets
{
    internal sealed partial class RaidScenery
    {
        private const string FloorMaterials = "ArchitecturalFloors/";

        // Share the original floor vertices (and their baked indoor occlusion) with the timber finish.
        private static void BuildChartFloorFinish(Transform site)
        {
            var owner = site.GetComponent<ChartHouseAssets>();
            var oak = Resources.Load<Material>(FloorMaterials + "Oiled oak");
            foreach (var renderer in site.GetComponentsInChildren<MeshRenderer>())
                if (renderer.sharedMaterial == owner.Finishes.Floor) renderer.sharedMaterial = oak;
        }

        private static void BuildWarehouseFloorFinish(Transform site)
        {
            // A 14 mm glazed finish defines the receiving clerk's bay and clears the industrial slab.
            // Reuse Unity's quad: a streamed building never allocates a new mesh for this insert.
            var finish = Resources.Load<Material>(FloorMaterials + "Octagon and dot");
            var floor = Shape(site, PrimitiveType.Quad, "Clerk's tiled bay",
                new Vector3(5.04f, .014f, 1.56f), new Vector3(3.42f,5.48f,1f),finish);
            floor.transform.localRotation = Quaternion.Euler(90f,0f,0f);
            floor.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // All walkable landing faces are partitioned into disjoint regions, including painted stripes.
        // Their elevation is exactly the top returned by the motor and the PhysX collider.
        private static void BuildTrainingLanding(ChartHouseMeshes mesh, Bounds b, Material body, Material tread, Material paint)
        {
            var lo = b.min;
            var hi = b.max;
            var c = b.center;
            var e = b.extents;
            mesh.Plane(body, new Vector3(c.x,c.y,lo.z), Vector3.right*e.x,Vector3.up*e.y,Vector3.back);
            mesh.Plane(body, new Vector3(c.x,c.y,hi.z), Vector3.right*e.x,Vector3.up*e.y,Vector3.forward);
            mesh.Plane(body, new Vector3(lo.x,c.y,c.z), Vector3.forward*e.z,Vector3.up*e.y,Vector3.left);
            mesh.Plane(body, new Vector3(hi.x,c.y,c.z), Vector3.forward*e.z,Vector3.up*e.y,Vector3.right);
            if (lo.y > 0) mesh.Plane(body,new Vector3(c.x,lo.y,c.z),Vector3.right*e.x,Vector3.forward*e.z,Vector3.down);
            float stripeStart = lo.z + .04f, stripeEnd = lo.z + .11f;
            Top(mesh, tread, lo.x,lo.z,hi.x,stripeStart,hi.y);
            Top(mesh, tread, lo.x,stripeEnd,hi.x,hi.z,hi.y);
            Top(mesh, tread, lo.x,stripeStart,lo.x+.06f,stripeEnd,hi.y);
            Top(mesh, paint, lo.x+.06f,stripeStart,hi.x-.06f,stripeEnd,hi.y);
            Top(mesh, tread, hi.x-.06f,stripeStart,hi.x,stripeEnd,hi.y);
        }

        private static void Top(ChartHouseMeshes mesh, Material material, float x0,float z0,float x1,float z1,float height)
        {
            mesh.Plane(material, new Vector3((x0+x1)*.5f,height,(z0+z1)*.5f),
                Vector3.right*((x1-x0)*.5f), Vector3.forward*((z1-z0)*.5f), Vector3.up);
        }

        private void BuildPracticePromenade()
        {
            var mesh = new ChartHouseMeshes(false);
            const float top = .014f;
            // Each run ends at the next run's edge, including the apron around the corner of the chart house.
            foreach (var run in TrainingLayout.Paving)
            {
                var area = run.Area;
                var material = Resources.Load<Material>(FloorMaterials + run.Material);
                Top(mesh,material,area.xMin,area.yMin,area.xMax,area.yMax,top);
            }

            // Walk-on material library. The exterior and interior finishes here are the same assets used on site.
            for (int i = 0; i < TrainingLayout.FloorSamples.Length; i++)
            {
                float left = TrainingLayout.SampleLeft(i);
                var sample = Resources.Load<Material>(FloorMaterials + TrainingLayout.FloorSamples[i].Material);
                Top(mesh,sample,left,TrainingLayout.SampleSouth,left + TrainingLayout.SampleWidth,TrainingLayout.SampleNorth,top);
            }
            mesh.Finish(Panel(root,"Pavement and finish samples",0,0,0,0),shellMeshes);
        }
    }
}
