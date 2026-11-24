using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    internal sealed class ChartHouseAssets : MonoBehaviour
    {
        internal readonly List<Mesh> Meshes = new List<Mesh>();
        internal bool BorrowedMeshes;
        internal HarborInteriorMaterials Finishes;
        private void OnDestroy()
        {
            if(!BorrowedMeshes) foreach (Mesh mesh in Meshes) Destroy(mesh);
            Finishes?.Dispose();
        }
    }
}
