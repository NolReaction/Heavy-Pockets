using System;
using UnityEngine;

namespace HeavyPockets
{
    // Geometry is stored separately so the authority can load the layout without rendering assets.
    internal sealed class MapPiece : ScriptableObject
    {
        public string Title;
        public string Tile;
        public Bounds Envelope;
        public Bounds[] Solids = Array.Empty<Bounds>();
    }
}
