using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    internal enum RangeFixture { Frame, Platform, Beam, CrawlRoof, Board, Post }

    internal readonly struct RangePart
    {
        internal readonly RangeFixture Kind;
        internal readonly Bounds Box;
        internal readonly string Label;
        internal RangePart(RangeFixture kind, Bounds box, string label = null)
        {
            Kind = kind;
            Box = box;
            Label = label;
        }
    }

    internal static class TrainingLayout
    {
        internal static readonly RangePart[] Parts = Layout();
        internal static readonly Bounds[] Collision = System.Array.ConvertAll(Parts, p => p.Box);

        private static RangePart[] Layout()
        {
            var parts = new List<RangePart>();
            void Box(RangeFixture kind, float x, float bottom, float z, float w, float h, float d, string label = null)
                => parts.Add(new RangePart(kind, new Bounds(new Vector3(x, bottom + h * .5f, z), new Vector3(w,h,d)), label));
            void Frame(Vector3 pivot, float width, float height, bool gate)
            {
                float x = pivot.x + (gate ? 0f : width * .5f);
                float post = gate ? .20f : .12f;
                float head = gate ? .34f : .15f;
                float z = pivot.z;
                for (int side = -1; side <= 1; side += 2)
                    Box(RangeFixture.Frame, x + side * (width + post) * .5f, 0, z, post, height + pivot.y, .29f);
                Box(RangeFixture.Frame, x, height + pivot.y, z, width + post * 2, head, .34f);
            }
            var door = WorldMechanisms.TrainingDoor;
            var gate = WorldMechanisms.TrainingGate;
            Frame(door.Pivot, door.Size.x, door.Size.y, false);
            Frame(gate.Pivot, gate.Size.x, gate.Size.y, true);

            // A standing jump can gain just under a metre; the rise and gap are independent.
            Box(RangeFixture.Platform, 17, 0, 7, 2.4f, .40f, 2f, "40 см");
            Box(RangeFixture.Platform, 17, 0, 9.40f, 2.4f, .80f, 2f, "80 см");
            Box(RangeFixture.Platform, 17, 0, 11.90f, 2.4f, 1.20f, 2f, "120 см");
            Box(RangeFixture.Platform, 17, 0, 14.45f, 2.4f, 1.60f, 2f, "160 см");
            Box(RangeFixture.Beam, 17, 1.42f, 18.45f, .48f, .18f, 6f);
            Box(RangeFixture.Platform, 17, 0, 22.45f, 2.4f, 1.60f, 2f);
            Box(RangeFixture.CrawlRoof, 24.5f, 1.30f, 13f, 2.65f, .16f, 4.2f);
            foreach (float x in new[] { 23.25f, 25.75f })
                foreach (float z in new[] { 10.97f, 15.03f })
                    Box(RangeFixture.Frame, x, 0, z, .15f, 1.30f, .15f);

            void Sign(float x, float z, float width, float height, float bottom, string label)
            {
                Box(RangeFixture.Board, x, bottom, z, width, height, .09f, label);
                Box(RangeFixture.Post, x, 0, z, .10f, bottom, .10f);
            }
            Sign(1f,-22f, 3.60f,1.34f,.78f, "Полигон");
            Sign(-16,-20,1.35f,.34f,.58f, "Асфальт");
            Sign(-49,-29,1.35f,.34f,.58f, "Песок");
            Sign(-33,-5,1.65f,.34f,.58f, "Газон");
            Sign(-53,18,1.65f,.34f,.58f, "Луговая трава");
            Sign(-32,37,1.72f,.34f,.58f, "Сухая трава");
            Sign(23,-9.6f,1.55f,.34f,.69f, "Механизмы");
            Sign(21,5f,1.5f,.34f,.69f, "Паркур");
            return parts.ToArray();
        }
    }
}
