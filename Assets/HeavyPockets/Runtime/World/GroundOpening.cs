using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    // Split the terrain grid at a foundation's exact edges. The building or its
    // distant shell owns the floor inside, even when it is flush with the ground.
    internal readonly struct GroundOpening
    {
        private readonly Rect bounds;

        internal GroundOpening(Vector3 origin, float width, float depth)
        {
            bounds = new Rect(origin.x - width * .5f, origin.z - depth * .5f, width, depth);
        }

        internal void Split(List<float> xs, List<float> zs)
        {
            if (bounds.width <= 0f || bounds.height <= 0f) return;
            InsertEdge(xs, bounds.xMin);
            InsertEdge(xs, bounds.xMax);
            InsertEdge(zs, bounds.yMin);
            InsertEdge(zs, bounds.yMax);
        }

        private static void InsertEdge(List<float> axis, float edge)
        {
            if (edge <= axis[0] || edge >= axis[axis.Count - 1]) return;
            int index = axis.BinarySearch(edge);
            if (index < 0) axis.Insert(~index, edge);
        }

        internal bool Covers(float x0, float x1, float z0, float z1)
            => bounds.Contains(new Vector2((x0 + x1) * .5f, (z0 + z1) * .5f));
    }
}
