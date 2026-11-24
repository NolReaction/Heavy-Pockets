using System;
using UnityEngine;

namespace HeavyPockets
{
    // Geometry is stored separately so the authority can load the layout without rendering assets.
    internal sealed class MapPiece : ScriptableObject
    {
        public string Title;
        public string Tile;
        [Tooltip("Local X/Z centre of the seam between the entrance paving and the public footpath.")]
        public Vector2 WalkwayEdge;
        public Vector2 WalkwayOut;
        [Min(0)] public float WalkwayWidth;
        public Bounds Envelope;
        public Bounds[] Solids = Array.Empty<Bounds>();
        public Bounds[] Glazing = Array.Empty<Bounds>();
    }
}
