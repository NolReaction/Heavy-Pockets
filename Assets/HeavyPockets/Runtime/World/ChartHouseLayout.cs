using UnityEngine;

namespace HeavyPockets
{
    internal static class ChartHouseLayout
    {
        internal static readonly Vector3 Origin = new Vector3(-4f, 0f, 16f);
        internal const float WallHeight = 3.92f, Sill = 1.07f, WindowTop = 3.19f;
        internal const float DoorLeft = 1.75f, DoorRight = 3.45f, DoorTop = 2.55f;
        internal const float FrontLeft = -4.70f, FrontRight = -.45f, EastStart = -2.44f, EastEnd = 2.44f;

        // Local wall pieces are shared by the rendered shell and authoritative collision.
        internal static readonly Bounds[] Walls = {
            Span(-6, 0, 3.78f, 6, WallHeight, 4), Span(-6, 0, -3.78f, -5.78f, WallHeight, 3.78f),
            Span(-6, 0, -4, FrontLeft, WallHeight, -3.78f), Span(FrontRight, 0, -4, DoorLeft, WallHeight, -3.78f),
            Span(DoorRight, 0, -4, 6, WallHeight, -3.78f), Span(DoorLeft, DoorTop, -4, DoorRight, WallHeight, -3.78f),
            Span(FrontLeft, 0, -4, FrontRight, Sill, -3.78f), Span(FrontLeft, WindowTop, -4, FrontRight, WallHeight, -3.78f),
            Span(5.78f, 0, -3.78f, 6, WallHeight, EastStart), Span(5.78f, 0, EastEnd, 6, WallHeight, 3.78f),
            Span(5.78f, 0, EastStart, 6, Sill, EastEnd), Span(5.78f, WindowTop, EastStart, 6, WallHeight, EastEnd)
        };
        internal static readonly Bounds Roof = Span(-6.19f, WallHeight, -4.18f, 6.19f, 4.12f, 4.18f);
        internal static readonly Bounds Table = Span(-2.87f, 0, -.77f, .43f, 1.065f, .78f);
        internal static readonly Bounds PlanChest = Span(-2.55f, 0, 3.04f, 2.83f, .86f, 3.76f);
        internal static readonly Bounds Bookcase = Span(-5.77f, 0, -.64f, -5.00f, 2.63f, 2.56f);
        internal static readonly Bounds Radiator = Span(5.34f, 0, -1.32f, 5.78f, .88f, 1.32f);
        internal static readonly Bounds Stool = Span(2.94f, 0, 2.25f, 3.47f, .55f, 2.78f);
        internal static readonly Bounds Telescope = Span(3.83f,0,-.05f,4.39f,1.45f,.52f);

        internal static readonly Bounds[] Glazing = {
            World(Span(FrontLeft,Sill,-3.90f,FrontRight,WindowTop,-3.85f)),
            World(Span(5.85f,Sill,EastStart,5.90f,WindowTop,EastEnd))
        };

        internal static readonly Bounds[] Collision = BuildCollision();

        internal static Bounds Span(float x0, float y0, float z0, float x1, float y1, float z1)
        {
            var b = new Bounds();
            b.SetMinMax(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1));
            return b;
        }

        private static Bounds World(Bounds b)
        {
            b.center += Origin;
            return b;
        }

        private static Bounds[] BuildCollision()
        {
            Bounds[] contents = { Roof, Table, PlanChest, Bookcase, Radiator, Stool, Telescope,
                Span(1.265f,2.69f,-4.795f,3.935f,2.83f,-3.845f) };
            var boxes = new Bounds[Walls.Length + contents.Length];
            for (int i = 0; i < Walls.Length; ++i) boxes[i] = World(Walls[i]);
            for (int i = 0; i < contents.Length; ++i) boxes[Walls.Length + i] = World(contents[i]);
            return boxes;
        }
    }
}
