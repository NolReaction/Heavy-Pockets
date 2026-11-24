using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    // Both clients and the dedicated server query this static yard geometry.
    // Group close obstacles together so a short camera sweep or a sightline
    // across an empty part of the yard can discard whole groups at once.
    internal static class YardOcclusion
    {
        private struct Node
        {
            internal Bounds Solid, Camera;
            internal int Left, Right;
            internal bool Leaf => Left < 0;
        }

        private static readonly Node[] nodes;
        private static Node[] placed = Array.Empty<Node>();
        private static readonly Bounds[] cameraGlass;
        private static readonly Bounds glazingBounds;

        static YardOcclusion()
        {
            nodes = Index(OutdoorLayout.Occluders);
            cameraGlass = (Bounds[])ChartHouseLayout.Glazing.Clone();
            Bounds envelope = default;
            for (int i = 0; i < cameraGlass.Length; i++)
            {
                cameraGlass[i].Expand(2f * RaidCameraBoom.Clearance);
                if (i == 0) envelope = cameraGlass[i];
                else envelope.Encapsulate(cameraGlass[i]);
            }
            envelope.Expand(.0002f);
            glazingBounds = envelope;
        }

        internal static void IndexPlan(Bounds[] walls) => placed = Index(walls);

        private static Node[] Index(Bounds[] source)
        {
            if (source.Length == 0) return Array.Empty<Node>();
            var boxes = (Bounds[])source.Clone();
            var tree = new Node[boxes.Length * 2 - 1];
            int next = 0;
            Build(tree, boxes, 0, boxes.Length, ref next);
            return tree;
        }

        private static int Build(Node[] tree, Bounds[] boxes, int start, int count, ref int next)
        {
            int index = next++;
            Bounds bounds = boxes[start];
            for (int i = start + 1; i < start + count; i++) bounds.Encapsulate(boxes[i]);
            // Rounded center/extents of a union must still include rays on a child's face.
            if (count > 1) bounds.Expand(.0002f);
            var node = new Node { Solid = bounds, Camera = bounds, Left = -1, Right = -1 };
            node.Camera.Expand(2f * RaidCameraBoom.Clearance);
            if (count > 1)
            {
                int axis = bounds.size.x >= bounds.size.z ? 0 : 2;
                Array.Sort(boxes, start, count, Comparer<Bounds>.Create((a, b) => a.center[axis].CompareTo(b.center[axis])));
                int split = count / 2;
                node.Left = Build(tree, boxes, start, split, ref next);
                node.Right = Build(tree, boxes, start + split, count - split, ref next);
            }
            tree[index] = node;
            return index;
        }

        internal static bool BlocksSight(Vector3 origin, Vector3 segment)
        {
            foreach (var passage in WorldMechanisms.Active)
                if (passage.Trace(origin, segment, 1f, 0f, out _)) return true;
            return BlocksStaticSight(origin, segment);
        }

        internal static bool BlocksStaticSight(Vector3 origin, Vector3 segment)
        {
            float length = 1f;
            if (placed.Length > 0 && Trace(placed, 0, origin, segment, ref length, false)) return true;
            if (TrainingGround.Active)
            {
                foreach (Bounds box in TrainingLayout.Opaque)
                    if (RaidMap.Intersects(box, origin, segment, 1f, out _)) return true;
                return false;
            }
            return nodes.Length > 0 && Trace(nodes, 0, origin, segment, ref length, false);
        }

        internal static float ClipCamera(Vector3 origin, Vector3 direction, float length)
        {
            foreach (var passage in WorldMechanisms.Active)
                if (passage.Trace(origin, direction, length, RaidCameraBoom.Clearance, out float hit)) length = hit;
            if (placed.Length > 0) Trace(placed, 0, origin, direction, ref length, true);
            if (TrainingGround.Active)
            {
                foreach (Bounds solid in TrainingLayout.Collision)
                {
                    Bounds padded = solid;
                    padded.Expand(2f * RaidCameraBoom.Clearance);
                    if (RaidMap.Intersects(padded, origin, direction, length, out float first)) length = first;
                }
                return length;
            }
            if (length > 0 && nodes.Length > 0) Trace(nodes, 0, origin, direction, ref length, true);
            if (length > 0 && cameraGlass.Length > 0 && RaidMap.Intersects(glazingBounds, origin, direction, length, out _))
                foreach (Bounds pane in cameraGlass)
                    if (RaidMap.Intersects(pane, origin, direction, length, out float first)) length = first;
            return length;
        }

        private static bool Trace(Node[] tree, int index, Vector3 origin, Vector3 direction, ref float length, bool camera)
        {
            ref readonly Node node = ref tree[index];
            if (!RaidMap.Intersects(camera ? node.Camera : node.Solid, origin, direction, length, out float first))
                return false;
            if (node.Leaf)
            {
                length = first;
                return true;
            }
            bool hit = Trace(tree, node.Left, origin, direction, ref length, camera);
            if (hit && (!camera || length == 0f)) return true;
            return Trace(tree, node.Right, origin, direction, ref length, camera) || hit;
        }
    }
}
