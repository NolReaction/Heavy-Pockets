using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    internal static class ReceivingWarehouseLayout
    {
        internal static readonly Vector3 Origin = new Vector3(-38f, 0f, 25f);
        internal const float Width = 14f, Height = 5f, Depth = 9f;
        internal const float DoorWidth = 1.08f, DoorHeight = 2.10f, DoorThickness = .045f;
        internal const string DoorLeafName = "Personnel door";
        internal static readonly Vector3 DoorHinge = Origin + new Vector3(4.66f, .04f, -4.43f);
        internal const string GateLeafName = "Loading shutter";
        internal const float GateWidth = 3.60f, GateHeight = 3.12f, GateThickness = .07f;
        internal static readonly Vector3 GateBottom = Origin + new Vector3(0, 0, -4.43f);

        // The builder and the physics world use the same piers and lintels.
        internal static readonly Bounds[] FrontMasonry = {
            Local(-7, 0, -4.5f, -GateWidth / 2, 4.65f, -4.24f),
            Local(-GateWidth / 2, GateHeight, -4.5f, GateWidth / 2, 4.65f, -4.24f),
            Local(GateWidth / 2, 0, -4.5f, 4.66f, 4.65f, -4.24f),
            Local(4.66f, 2.14f, -4.5f, 4.66f + DoorWidth, 4.65f, -4.24f),
            Local(4.66f + DoorWidth, 0, -4.5f, 7, 4.65f, -4.24f)
        };
        internal static readonly Bounds[] SideMasonry = {
            Local(-7,0,4.24f,7,4.65f,4.5f),
            Local(-7,0,-4.24f,-6.76f,4.65f,4.24f),
            Local(6.76f,0,-4.24f,7,4.65f,4.24f)
        };
        internal static readonly Bounds Roof = Local(-7,4.62f,-4.5f,7,5,4.5f);

        internal static readonly Bounds[] Racks = {
            Local(-6.48f, 0, 3.08f, -2.96f, 2.46f, 4.12f),
            Local(-2.66f, 0, 3.08f, .86f, 2.46f, 4.12f)
        };
        internal static readonly Bounds Desk = Local(5.47f, 0, .74f, 6.67f, .91f, 3.16f);
        internal static readonly Bounds[] Freight = {
            Local(-5.96f, 0, -.62f, -4.76f, .94f, .18f),
            Local(-4.52f, 0, -.55f, -3.32f, .61f, .25f),
            Local(-5.96f, 0, -1.60f, -4.76f, .55f, -.80f)
        };
        internal static readonly Bounds[] Collision = WorldCollision();

        private static Bounds Local(float x0, float y0, float z0, float x1, float y1, float z1)
        {
            Bounds bounds = new Bounds();
            bounds.SetMinMax(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1));
            return bounds;
        }

        private static Bounds[] WorldCollision()
        {
            // The common port slab supplies a continuous floor through the entrance.
            var boxes = new List<Bounds>(FrontMasonry);
            boxes.AddRange(SideMasonry);
            boxes.Add(Roof);
            boxes.Add(Desk);
            boxes.AddRange(Racks);
            boxes.AddRange(Freight);
            for (int i = 0; i < boxes.Count; ++i)
            {
                var b = boxes[i];
                b.center += Origin;
                boxes[i] = b;
            }
            boxes.Add(WarehouseFloorScale.ColumnBounds);
            boxes.Add(WarehouseFloorScale.HeadBounds);
            return boxes.ToArray();
        }
    }
}
