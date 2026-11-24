using UnityEngine;

namespace HeavyPockets
{
    internal enum OutdoorKind { Container, Shed, Barrier, Pier, SignalPost, LocomotiveDeck, DieselHood, DriverCab, WagonDeck, TimberLoad, FreightCrate }

    internal readonly struct OutdoorSolid
    {
        internal readonly OutdoorKind Kind;
        internal readonly Bounds Bounds;

        internal OutdoorSolid(OutdoorKind kind, float x, float z, float width, float height, float depth)
        {
            Kind = kind;
            Bounds = new Bounds(new Vector3(x, height * .5f, z), new Vector3(width, height, depth));
        }
    }

    // Both the rendered structures and the dedicated server read these footprints.
    // Heights are measured from the level working surface of the quay at y = 0.
    internal static class OutdoorLayout
    {
        internal static readonly OutdoorSolid[] Solids =
        {
            new OutdoorSolid(OutdoorKind.Shed, -38, 25, 14, 5, 9),
            new OutdoorSolid(OutdoorKind.Shed, 54, -26, 8, 3.5f, 6),

            new OutdoorSolid(OutdoorKind.Container, -19, 12, 12, 2.6f, 2.5f),
            new OutdoorSolid(OutdoorKind.Container, -19, 16, 12, 2.6f, 2.5f),
            new OutdoorSolid(OutdoorKind.Container, -34, -20, 12, 2.6f, 2.5f),
            new OutdoorSolid(OutdoorKind.Container, -20, -20, 12, 2.6f, 2.5f),
            new OutdoorSolid(OutdoorKind.Container, 17, 36, 12, 2.6f, 2.5f),
            new OutdoorSolid(OutdoorKind.Container, 31, 36, 12, 2.6f, 2.5f),
            new OutdoorSolid(OutdoorKind.Container, 26, -20, 12, 2.6f, 2.5f),
            new OutdoorSolid(OutdoorKind.Container, 16, -9, 2.5f, 2.6f, 12),
            new OutdoorSolid(OutdoorKind.Container, 21, -9, 2.5f, 2.6f, 12),
            new OutdoorSolid(OutdoorKind.Container, -49, 5, 2.5f, 2.6f, 12),

            // Separate the low running boards from the machinery and timber for sightlines.
            new OutdoorSolid(OutdoorKind.LocomotiveDeck, -54, -1.60f, 2.92f, 1.48f, 11.6f),
            new OutdoorSolid(OutdoorKind.DieselHood, -54, -.28f, 1.93f, 3.12f, 6.66f),
            new OutdoorSolid(OutdoorKind.DriverCab, -54, -5.02f, 2.64f, 4.08f, 2.84f),
            new OutdoorSolid(OutdoorKind.WagonDeck, -54, -13.13f, 2.83f, 1.40f, 10.4f),
            new OutdoorSolid(OutdoorKind.TimberLoad, -54, -13.13f, 2.24f, 3.05f, 8.9f),

            new OutdoorSolid(OutdoorKind.Barrier, -12, -3, 5, 1.05f, .85f),
            new OutdoorSolid(OutdoorKind.Barrier, 8, 17, 5, 1.05f, .85f),
            new OutdoorSolid(OutdoorKind.Barrier, 41, -9, 5, 1.05f, .85f),
            new OutdoorSolid(OutdoorKind.Barrier, -25, 38, 5, 1.05f, .85f),

            // Staged freight next to the first spawn. Each rise is reachable with one standing jump.
            new OutdoorSolid(OutdoorKind.FreightCrate, -7, -6, 1.8f, .44f, 1.6f),
            new OutdoorSolid(OutdoorKind.FreightCrate, -9.1f, -6, 1.8f, .94f, 1.6f),
            new OutdoorSolid(OutdoorKind.FreightCrate, -11.2f, -6, 1.8f, 1.44f, 1.6f),

            new OutdoorSolid(OutdoorKind.Pier, 32, 14, 1.3f, 11, 1.3f),
            new OutdoorSolid(OutdoorKind.Pier, 44, 14, 1.3f, 11, 1.3f),
            new OutdoorSolid(OutdoorKind.Pier, 32, 26, 1.3f, 11, 1.3f),
            new OutdoorSolid(OutdoorKind.Pier, 44, 26, 1.3f, 11, 1.3f),

            new OutdoorSolid(OutdoorKind.SignalPost, 44.4f, 35, .30f, 4.1f, .30f),
            new OutdoorSolid(OutdoorKind.SignalPost, 49.6f, 35, .30f, 4.1f, .30f)
        };

        internal static readonly Bounds ExitSign = new Bounds(new Vector3(47f, 4.49f, 34.94f),
            new Vector3(3.90f, 1.13f, .19f));
        internal static readonly Bounds ExitCrossbar = new Bounds(new Vector3(47f, 4.04f, 35f),
            new Vector3(5.55f, .31f, .29f));

        // The crossbar and sign admit players below them but obstruct a raised shoulder camera.
        internal static readonly Bounds[] Occluders = BuildOccluders();

        private static Bounds[] BuildOccluders()
        {
            var boxes = new Bounds[Solids.Length + 2];
            for (int i = 0; i < Solids.Length; i++)
                boxes[i] = Solids[i].Bounds;
            boxes[Solids.Length] = ExitSign;
            boxes[Solids.Length + 1] = ExitCrossbar;
            return boxes;
        }
    }
}
