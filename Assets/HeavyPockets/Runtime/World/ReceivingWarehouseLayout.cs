using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    internal static class ReceivingWarehouseLayout
    {
        internal static readonly Vector3 Origin = new Vector3(-38f, 0f, 25f);
        internal const float Width = 14f, Height = 5f, Depth = 9f;
        internal const float DoorWidth = 1.08f, DoorHeight = 2.10f, DoorThickness = .045f, OpenAngle = -90f;
        internal const string DoorLeafName = "Personnel door";
        internal static readonly Vector3 DoorHinge = Origin + new Vector3(4.66f, .04f, -4.43f);

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
            var boxes = new List<Bounds> {
                Local(-7,0,-4.5f,4.66f,4.65f,-4.24f), Local(5.74f,0,-4.5f,7,4.65f,-4.24f),
                Local(4.66f,2.14f,-4.5f,5.74f,4.65f,-4.24f),
                Local(-7,0,4.24f,7,4.65f,4.5f), Local(-7,0,-4.24f,-6.76f,4.65f,4.24f),
                Local(6.76f,0,-4.24f,7,4.65f,4.24f), Local(-7,4.62f,-4.5f,7,5,4.5f),
                Local(-1.88f,0,-4.24f,1.88f,3.12f,-3.76f), Desk
            };
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
