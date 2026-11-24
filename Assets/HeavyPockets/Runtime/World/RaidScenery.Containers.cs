using UnityEngine;

namespace HeavyPockets
{
    internal sealed partial class RaidScenery
    {
        private sealed class Lid
        {
            internal Transform Hinge;
            internal Quaternion Closed;
            internal float Angle;
            internal double Started = double.NegativeInfinity;
            internal bool Animating;

            internal void Pose(float fraction)
            {
                if (Hinge) Hinge.localRotation = Closed * Quaternion.Euler(Angle * Mathf.SmoothStep(0f,1f,fraction),0f,0f);
            }

            internal void Sample(double now)
            {
                if (double.IsNegativeInfinity(Started)) { Pose(0f); return; }
                float time = (float)(now - Started) / RaidContainers.LidSeconds;
                Pose(Mathf.Clamp01(time));
                Animating = time < 1f;
            }
        }

        private readonly Lid[] containerLids = { new Lid(),new Lid(),new Lid() };
        private readonly Transform[] casePrototypes = new Transform[RaidContainers.Count];

        private void BindContainerLid(byte id,Transform hinge,float degrees)
        {
            var lid = containerLids[id];
            lid.Hinge = hinge;
            lid.Closed = hinge.localRotation;
            lid.Angle = degrees;
            lid.Sample(WorldMechanisms.Now);
        }

        internal void OpenContainerLid(byte id,double started)
        {
            var lid = containerLids[id];
            lid.Started = started;
            lid.Sample(WorldMechanisms.Now);
        }

        internal void ResetContainerLids()
        {
            foreach (var lid in containerLids)
            {
                lid.Started = double.NegativeInfinity;
                lid.Animating = false;
                lid.Pose(0);
            }
        }

        internal void TickContainerLids()
        {
            foreach (var lid in containerLids)
                if (lid.Animating) lid.Sample(WorldMechanisms.Now);
        }

        private void MountFreightCases(Transform warehouse)
        {
            for (byte id=1; id<RaidContainers.Count; id++)
            {
                if (!casePrototypes[id]) casePrototypes[id] = MakeFreightCase(id);
                Transform model = Object.Instantiate(casePrototypes[id], warehouse, false);
                var center = RaidContainers.WarehouseBoxes[id-1].center;
                model.localPosition = new Vector3(center.x,0f,center.z);
                model.gameObject.SetActive(true);
                BindContainerLid(id,model.Find("Lid"),87f);
            }
        }

        private Transform MakeFreightCase(byte id)
        {
            bool tools = id == 1;
            var model = Child(root,tools ? "Joiner's tool chest" : "Dispatch chest",Vector3.zero);
            model.gameObject.SetActive(false);
            Material oak = Resources.Load<Material>("ArchitecturalFloors/Oiled oak");
            Material enamel = tools ? teal : oxide;
            var body = new PropGeometry();
            for (int side=-1;side<=1;side+=2)
            {
                body.Box(black,side*.37f,.042f,0,.105f,.084f,.70f,.012f);
                body.Box(oak,side*.549f,.343f,0,.066f,.505f,.738f,.008f);
                for (int row=0;row<3;row++)
                    body.Box(oak,0,.182f+row*.16f,side*.354f,1.04f,.157f,.065f,.004f);
                for (int end=-1;end<=1;end+=2)
                {
                    body.Box(enamel,end*.525f,.337f,side*.381f,.085f,.518f,.018f,.004f);
                    body.Box(enamel,end*.582f,.337f,side*.338f,.014f,.518f,.09f,.003f);
                    body.Box(steel,end*.527f,.157f,side*.392f,.026f,.026f,.009f,.006f);
                    body.Box(steel,end*.527f,.523f,side*.392f,.026f,.026f,.009f,.006f);
                }
                body.Box(steel,side*.586f,.389f,0,.016f,.040f,.210f,.007f);
                body.Box(steel,side*.585f,.351f,-.099f,.023f,.081f,.024f,.005f);
                body.Box(steel,side*.585f,.351f,.099f,.023f,.081f,.024f,.005f);
                body.Box(steel,side*.29f,.529f,-.389f,.074f,.106f,.014f,.006f);
                body.Box(black,side*.29f,.525f,-.398f,.032f,.037f,.009f,.004f);
            }
            body.Box(oak,0,.123f,0,1.05f,.07f,.65f,.008f);
            body.Box(black,0,.161f,0,1.028f,.012f,.63f,.004f);
            body.Box(enamel,0,.373f,-.389f,.77f,.15f,.014f,.007f);
            body.Apply(model,propMeshes);
            WallText(model,tools ? "ИНСТРУМЕНТ" : "ОТПРАВКА", new Vector3(0,.373f,-.398f),0,.017f,Rgb(0xEFE8D6));

            var lid = Child(model,"Lid",new Vector3(0,.603f,.357f));
            var cover = new PropGeometry();
            for (int i=0;i<5;i++)
                cover.Box(oak,0,.020f,-.667f+i*.155f,1.175f,.071f,.151f,.004f);
            for (int side=-1;side<=1;side+=2)
            {
                cover.Box(enamel,side*.43f,.064f,-.357f,.063f,.019f,.773f,.005f);
                cover.Box(steel,side*.29f,.031f,-.700f,.079f,.019f,.092f,.004f);
                cover.Box(steel,side*.29f,.005f,-.742f,.078f,.070f,.015f,.004f);
                cover.Box(steel,side*.38f,.024f,.023f,.133f,.045f,.055f,.010f);
                cover.Box(enamel,side*.37f,-.024f,-.351f,.071f,.028f,.65f,.003f);
            }
            cover.Box(chalk,.15f,.059f,-.286f,.223f,.005f,.146f,.001f);
            for(int i=0;i<4;i++) cover.Box(black,.124f,.063f,-.321f+i*.018f,.124f-i*.009f,.002f,.004f,.0005f);
            cover.Apply(lid,propMeshes);
            return model;
        }
    }
}
