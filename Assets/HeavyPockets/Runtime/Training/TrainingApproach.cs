using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    // One cross-section of a path. Consecutive pairs bound both the finished surfacing
    // and its clearance in the grass, so moving a map placement keeps the two together.
    internal readonly struct PavementSection
    {
        internal readonly Vector2 Left, Right;
        internal PavementSection(Vector2 left, Vector2 right) { Left = left; Right = right; }
        internal Vector2 At(float t) => Vector2.LerpUnclamped(Left, Right, t);
    }

    internal readonly struct TrainingApproach
    {
        internal readonly PavementSection[] Sections;
        internal readonly Matrix4x4 TextureFrame;
        private TrainingApproach(PavementSection[] sections, Matrix4x4 textureFrame)
        {
            Sections = sections;
            TextureFrame = textureFrame;
        }

        internal static List<TrainingApproach> Layout(WorldPlan plan)
        {
            var paths = new List<TrainingApproach>();
            if (!plan) return paths;
            foreach (var item in plan.Objects)
                if (item.Piece && item.Piece.WalkwayWidth > .1f && item.Piece.WalkwayOut.sqrMagnitude > .01f)
                    paths.Add(Connect(item));
            return paths;
        }

        private static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);
        private static Vector2 LeftOf(Vector2 v) => new Vector2(-v.y, v.x);

        private static TrainingApproach Connect(WorldPlan.Placement item)
        {
            var piece = item.Piece;
            Vector2 start = Flat(item.Position + item.Rotation * new Vector3(piece.WalkwayEdge.x, 0, piece.WalkwayEdge.y));
            Vector2 outward = Flat(item.Rotation * new Vector3(piece.WalkwayOut.x, 0, piece.WalkwayOut.y)).normalized;
            float half = piece.WalkwayWidth * .5f;
            Vector2 mouth = default, edgeNormal = default;
            float streetHalf = 0, best = float.PositiveInfinity;
            void Edge(Vector2 a, Vector2 b, Vector2 exterior)
            {
                if (Vector2.Dot(start - a, exterior) < -.03f) return;
                Vector2 line = b - a, along = line.normalized;
                float width = Mathf.Min(half * 1.65f, line.magnitude * .45f);
                Vector2 guide = start + outward * (piece.WalkwayWidth * .85f);
                Vector2 point = a + along * Mathf.Clamp(Vector2.Dot(guide - a, along), width, line.magnitude - width);
                Vector2 delta = point - start;
                float distance = delta.magnitude;
                float score = distance + 8f * (1f - Vector2.Dot(outward, delta.normalized)) + Mathf.Max(0, half-width)*3f;
                if (score >= best) return;
                best = score; mouth = point; edgeNormal = exterior; streetHalf = width;
            }
            foreach (var run in TrainingLayout.Paving)
            {
                var r = run.Area;
                var sw = r.min; var ne = r.max; var nw = new Vector2(sw.x,ne.y); var se = new Vector2(ne.x,sw.y);
                Edge(sw,se,Vector2.down); Edge(nw,ne,Vector2.up);
                Edge(sw,nw,Vector2.left); Edge(se,ne,Vector2.right);
            }
            Vector2 grain = LeftOf(outward);
            Quaternion orientation = Quaternion.LookRotation(new Vector3(-grain.y, 0, grain.x));
            var textureFrame = Matrix4x4.TRS(new Vector3(start.x,0,start.y),orientation,Vector3.one).inverse;
            if (float.IsInfinity(best) || Vector2.Distance(start,mouth) < .35f)
                return new TrainingApproach(System.Array.Empty<PavementSection>(),textureFrame);

            var points = new List<Vector2> {start};
            // For a reversed placement go around the building instead of running the path through its ground floor.
            if (Vector2.Dot(outward,(mouth - start).normalized) < -.10f)
            {
                Bounds bounds = item.Place(piece.Envelope);
                Vector2 centre = Flat(bounds.center), radial = (start - centre).normalized;
                float radius = Flat(bounds.extents).magnitude + half + .8f;
                float angle = Vector2.SignedAngle(radial, mouth - centre);
                int arcs = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(angle) / 22f));
                for (int i = 0; i <= arcs; i++)
                {
                    float t = Mathf.Deg2Rad * angle * i / arcs;
                    points.Add(centre + (radial * Mathf.Cos(t) + LeftOf(radial) * Mathf.Sin(t)) * radius);
                }
            }
            points.Add(mouth);

            var sections = new List<PavementSection>();
            for (int k = 0; k < points.Count - 1; k++)
            {
                Vector2 a = points[k], b = points[k+1];
                Vector2 from = k == 0 ? outward : (b - points[k-1]).normalized;
                Vector2 to = k == points.Count-2 ? -edgeNormal : (points[k+2] - a).normalized;
                float startWidth = k == 0 ? half : half * .83f;
                float endWidth = k == points.Count-2 ? streetHalf : half * .83f;
                float length = Vector2.Distance(a,b), handle = Mathf.Min(4.5f,length*.33f);
                int steps = Mathf.Max(4,Mathf.CeilToInt(length/1.1f));
                var left0 = a+LeftOf(from)*startWidth; var right0 = a-LeftOf(from)*startWidth;
                var left1 = b+LeftOf(to)*endWidth; var right1 = b-LeftOf(to)*endWidth;
                for (int j = k == 0 ? 0 : 1; j <= steps; j++)
                {
                    float t = j/(float)steps, u = 1-t;
                    Vector2 Row(Vector2 p,Vector2 q)
                    {
                        float towardStart = handle, towardEnd = handle;
                        if (points.Count == 2)
                            {
                            // Keep the two edge tangents between the terrace and the kerb even at oblique junctions.
                            float gap = Mathf.Max(0,Vector2.Dot(p-mouth,edgeNormal));
                            float entering = -Vector2.Dot(from,edgeNormal);
                            if (entering > .001f) towardStart = Mathf.Min(handle,gap*.45f/entering);
                            towardEnd = Mathf.Min(handle,gap*.45f);
                        }
                        Vector2 point = u*u*u*p + 3*u*u*t*(p+from*towardStart) + 3*u*t*t*(q-to*towardEnd) + t*t*t*q;
                        // Close a residual sliver at the kerb where a terrace almost touches the street.
                        if (points.Count == 2 && Mathf.Abs(Vector2.Dot(p-mouth,edgeNormal)) < .65f)
                            point -= edgeNormal*Vector2.Dot(point-mouth,edgeNormal);
                        return point;
                    }
                    sections.Add(new PavementSection(Row(left0,left1),Row(right0,right1)));
                }
            }
            return new TrainingApproach(sections.ToArray(),textureFrame);
        }

        internal static bool Covers(List<TrainingApproach> paths, Vector2 point, float margin)
        {
            foreach (var approach in paths)
            {
                var path = approach.Sections;
                for (int i = 1; i < path.Length; i++)
                {
                    var a = path[i-1]; var b = path[i];
                    bool Inside(Vector2 p, Vector2 q)
                    {
                        Vector2 edge = q-p, delta = point-p;
                        return edge.x*delta.y-edge.y*delta.x >= -margin*edge.magnitude;
                    }
                    if (Inside(a.Left,a.Right) && Inside(a.Right,b.Right) &&
                        Inside(b.Right,b.Left) && Inside(b.Left,a.Left)) return true;
                }
            }
            return false;
        }
    }
}
