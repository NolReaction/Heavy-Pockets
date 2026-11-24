using System;
using UnityEngine;

namespace HeavyPockets
{
    // One independently loadable visual tile. Mesh coordinates are relative to the port origin.
    [PreferBinarySerialization]
    internal sealed class PortCellAsset : ScriptableObject
    {
        [Serializable]
        internal struct Surface
        {
            public Mesh Mesh;
            public string Material;
            public bool CastShadows;
        }

        [Serializable]
        internal struct Lettering
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public string Text;
            public float Size;
            public Color Color;
        }

        public Vector2Int Cell;
        public Surface[] Surfaces;
        public Lettering[] Signs;
        public Mesh[] ChartHouseShell;
        public Mesh[] ChartHouseDetail;
        // Only the distant catalog fills this array. No persistent asset references the near tiles.
        public PortCellAsset[] HorizonCells;

        internal const int Width = 24;
        internal static Vector2Int Key(float x, float z) =>
            new Vector2Int(Mathf.FloorToInt(x / Width), Mathf.FloorToInt(z / Width));
        internal static string TileName(Vector2Int key) => $"PortCells/Tile_{key.x}_{key.y}";
    }
}
