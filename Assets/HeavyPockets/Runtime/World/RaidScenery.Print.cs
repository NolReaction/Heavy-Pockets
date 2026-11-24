using UnityEngine;
using UnityEngine.Rendering;

namespace HeavyPockets
{
    internal sealed partial class RaidScenery
    {
        private Material printMaterial;

        private void BuildHarborPrint() => BuildHarborPrint(ReceivingWarehouseLayout.Origin);

        private void BuildHarborPrint(Vector3 warehouse)
        {
            var print = Resources.Load<Texture2D>("HarborScreenprint");
            if (print == null) return;
            const float height = 1.68f;
            const float width = height * (16f / 9f);
            // The enamel port poster hangs on the east gable of the railhead shed.
            var position = warehouse + new Vector3(ReceivingWarehouseLayout.Width * .5f + .028f, 2.12f, 0f);
            Shape(root, PrimitiveType.Cube, "Enamel print backing", position,
                new Vector3(0.054f, height + 0.082f, width + 0.082f), steel);
            printMaterial = new Material(Resources.Load<Shader>("DepotPrint"));
            printMaterial.mainTexture = print;
            var panel = Shape(root, PrimitiveType.Quad, "Harbor screenprint", position + Vector3.right * 0.031f,
                new Vector3(width, height, 1f), printMaterial);
            panel.transform.localRotation = Quaternion.Euler(0, -90, 0);
            panel.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        private void DisposeHarborPrint()
        {
            if (printMaterial != null) Object.Destroy(printMaterial);
        }
    }
}
