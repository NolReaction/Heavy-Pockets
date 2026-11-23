using UnityEngine;

namespace HeavyPockets
{
    internal sealed partial class RaidHud
    {
        // Outline stamps are drawn in the same 48 × 32 design space and stay legible in small cells.
        private static readonly byte[][] rifleStamp = {
            new byte[] { 2,12, 11,12, 13,10, 29,10, 30,12, 38,12, 38,14, 46,14, 46,16, 29,16, 27,19, 28,24, 23,24, 21,19, 17,19, 15,23, 12,22, 14,17, 8,17, 3,21, 2,21, 2,12 },
            new byte[] { 16,13, 26,13 }, new byte[] { 31,14, 35,14 }
        };
        private static readonly byte[][] sidearmStamp = {
            new byte[] { 10,10, 36,10, 38,12, 37,15, 23,15, 21,19, 17,19, 15,25, 9,24, 12,16, 9,15, 10,10 },
            new byte[] { 12,13, 34,13 }, new byte[] { 17,16, 16,19 }, new byte[] { 12,20, 15,21 }
        };
        private static readonly byte[][] armorStamp = {
            new byte[] { 18,3, 21,3, 22,7, 26,7, 27,3, 30,3, 31,9, 34,12, 33,26, 30,29, 18,29, 15,26, 14,12, 17,9, 18,3 },
            new byte[] { 18,13, 30,13, 29,20, 19,20, 18,13 }, new byte[] { 18,23, 30,23 }, new byte[] { 21,26, 27,26 }
        };
        private static readonly byte[][] packStamp = {
            new byte[] { 17,6, 20,4, 28,4, 31,6, 32,10, 34,12, 35,25, 32,28, 16,28, 13,25, 14,12, 16,10, 17,6 },
            new byte[] { 21,4, 21,2, 27,2, 27,4 }, new byte[] { 17,11, 31,11 },
            new byte[] { 17,17, 19,15, 29,15, 31,17, 30,24, 18,24, 17,17 }, new byte[] { 19,18, 29,18 }
        };
        private static readonly byte[][] pouchStamp = {
            new byte[] { 12,10, 14,7, 34,7, 36,10, 35,23, 32,26, 16,26, 13,23, 12,10 },
            new byte[] { 13,11, 14,15, 20,17, 28,17, 34,15, 35,11 }, new byte[] { 22,14, 26,14, 26,20, 22,20, 22,14 }
        };
        private static readonly Color StencilInk = new Color32(112, 132, 132, 255);

        private static void DrawGearStencil(Rect art, GearLocation slot)
        {
            byte[][] paths;
            switch (slot)
            {
                case GearLocation.Primary: paths = rifleStamp; break;
                case GearLocation.Secondary: paths = sidearmStamp; break;
                case GearLocation.Armor: paths = armorStamp; break;
                case GearLocation.Backpack: paths = packStamp; break;
                case GearLocation.Pouch: paths = pouchStamp; break;
                default:
                    var well = new Rect(art.center.x - 6, art.center.y - 6, 12, 12);
                    Corners(well, GridLine, 3, 1);
                    Block(art.center.x - 1, art.center.y - 1, 2, 2, GridLine);
                    return;
            }
            float s = Mathf.Min(art.width / 48, art.height / 32);
            float left = art.center.x - 24 * s, top = art.center.y - 16 * s;
            foreach (var path in paths)
                for (int i = 2; i < path.Length; i += 2)
                    Stroke(left + path[i - 2] * s, top + path[i - 1] * s,
                        left + path[i] * s, top + path[i + 1] * s, 1.15f, StencilInk);
        }
    }
}
