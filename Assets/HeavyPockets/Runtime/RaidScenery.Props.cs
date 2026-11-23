using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    internal sealed partial class RaidScenery
    {
        private readonly List<Mesh> propMeshes = new List<Mesh>();
        private GameObject runnerPrefab;
        private Material contactShadow;

        private Renderer GroundContact(Transform parent, float width, float depth)
        {
            if (contactShadow == null)
                contactShadow = new Material(Resources.Load<Shader>("GroundContact"));
            var shadow = Shape(parent, PrimitiveType.Quad, "Contact shadow", new Vector3(0, .011f, 0),
                new Vector3(width, depth, 1f), contactShadow);
            shadow.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var renderer = shadow.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private void DisposePropMeshes()
        {
            if (contactShadow != null) Object.Destroy(contactShadow);
            foreach (Mesh mesh in propMeshes)
                Object.Destroy(mesh);
        }

        // All pieces of the case are baked into material submeshes on one renderer.
        // Its exterior stays identical for every player regardless of the contents.
        private void BuildCrate()
        {
            var model = Child(root, "Transit case", new Vector3(RaidMap.Crate.x, 0, RaidMap.Crate.y));
            GroundContact(model, 1.65f, 1.15f);
            var p = new PropGeometry();
            p.Box(black, 0, .082f, 0, .99f, .12f, .63f, .035f);
            p.Box(amber, 0, .407f, 0, 1.034f, .62f, .658f, .036f);
            p.Box(steel, 0, .175f, 0, 1.047f, .037f, .668f, .009f);
            p.Box(black, 0, .674f, 0, 1.09f, .055f, .709f, .013f);
            p.Box(amber, 0, .737f, 0, 1.08f, .10f, .699f, .025f);
            p.Box(black, 0, .786f, 0, .98f, .019f, .605f, .009f);
            p.Box(amber, 0, .804f, 0, .948f, .039f, .574f, .018f);
            for (int end = -1; end <= 1; end += 2)
            {
                float x = end * .425f;
                // Reinforced raised lid rails and protected end handles.
                p.Box(black, x, .819f, 0, .087f, .053f, .644f, .018f);
                p.Box(steel, x, .847f, 0, .048f, .011f, .431f, .005f);
                p.Box(black, end * .516f, .478f, 0, .034f, .165f, .28f, .013f);
                p.Box(steel, end * .538f, .503f, 0, .022f, .032f, .205f, .009f);
                foreach (int side in new[] { -1, 1 })
                {
                    p.Box(black, end * .453f, .103f, side * .293f, .184f, .17f, .125f, .021f);
                    p.Box(steel, end * .486f, .418f, side * .302f, .095f, .512f, .081f, .014f);
                    p.Box(black, end * .467f, .732f, side * .286f, .162f, .14f, .142f, .027f);
                    float latchX = end * .297f;
                    p.Box(black, latchX, .653f, side * .333f, .115f, .196f, .033f, .012f);
                    p.Box(steel, latchX, .65f, side * .346f, .076f, .124f, .016f, .009f);
                    p.Box(black, latchX, .649f, side * .354f, .046f, .050f, .008f, .003f);
                    p.Box(steel, latchX, .606f, side * .354f, .084f, .018f, .011f, .005f);
                    p.Box(amber, end * .189f, .368f, side * .332f, .043f, .289f, .038f, .014f);
                }
            }

            // Recessed document pocket and paper edges on the lid, alongside two molded ribs.
            p.Box(black, .113f, .832f, .084f, .316f, .016f, .204f, .007f);
            p.Box(chalk, .118f, .842f, .088f, .26f, .007f, .148f, .002f);
            for (int i = 0; i < 4; i++)
                p.Box(steel, .091f + .008f * (i % 2), .847f, .045f + i * .023f, .17f - .022f * (i % 3), .002f, .008f, .0008f);
            for (int i = 0; i < 2; i++)
                p.Box(amber, -.236f + i * .078f, .834f, -.011f, .036f, .033f, .406f, .012f);
            p.Apply(model, propMeshes);
        }

        internal Transform InstantiatePlayer(ulong id, Vector3 position, float yaw)
        {
            if (runnerPrefab == null) runnerPrefab = Resources.Load<GameObject>("QuaterniusRunner");
            var actor = Child(root, "Runner " + id, position);
            actor.gameObject.SetActive(false);
            actor.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var model = Object.Instantiate(runnerPrefab, actor, false);
            var rig = new RunnerRig(model);
            QuaterniusAppearance.AttachHair(model);
            Renderer shadow = GroundContact(actor, 1.7f, 1.9f);
            shadow.gameObject.AddComponent<RunnerContactShadow>().Bind(rig);
            actor.gameObject.AddComponent<FreightRunnerGait>().Bind(rig);
            return actor;
        }

        private static Transform Child(Transform parent, string name, Vector3 position)
        {
            Transform child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = position;
            return child;
        }
    }
}
