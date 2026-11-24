using UnityEngine;

namespace HeavyPockets
{
    internal static class LootArt
    {
        private static Texture2D salvageAtlas, equipmentAtlas, dispatchAtlas;

        // Pixel bounds of each illustration in the source artwork (top-left origin).
        private static readonly RectInt[] Bounds =
        {
            new RectInt(31,71,303,257), new RectInt(395,67,299,259), new RectInt(749,133,307,123),
            new RectInt(1141,37,255,319), new RectInt(33,407,293,269), new RectInt(399,395,281,299),
            new RectInt(737,427,329,239), new RectInt(1109,425,317,245), new RectInt(39,741,285,307),
            new RectInt(375,861,331,101), new RectInt(753,787,307,235)
        };

        private static readonly RectInt[] EquipmentBounds =
        {
            new RectInt(13, 109, 374, 182), new RectInt(444, 95, 311, 209), new RectInt(854, 13, 292, 374),
            new RectInt(27, 413, 346, 374), new RectInt(445, 462, 309, 276), new RectInt(813, 468, 374, 264),
            new RectInt(13, 849, 374, 302), new RectInt(413, 861, 374, 277), new RectInt(834, 813, 331, 374)
        };

        private static Texture2D Atlas(byte type)
        {
            if (type >= LootCatalog.FoldingRule)
            {
                if (dispatchAtlas == null) dispatchAtlas = Resources.Load<Texture2D>("DispatchLoot");
                return dispatchAtlas;
            }
            if (type <= Bounds.Length)
            {
                if (salvageAtlas == null) salvageAtlas = Resources.Load<Texture2D>("HeavyPocketsLoot");
                return salvageAtlas;
            }
            if (equipmentAtlas == null) equipmentAtlas = Resources.Load<Texture2D>("HeavyPocketsGear");
            return equipmentAtlas;
        }

        private static Rect GetUV(byte type)
        {
            if (type >= LootCatalog.FoldingRule)
            {
                float column = type - LootCatalog.FoldingRule;
                bool wide = type == LootCatalog.FoldingRule;
                return new Rect(column / 3f, wide ? .25f : 0f, 1f / 3f, wide ? .50f : 1f);
            }
            bool salvage = type <= Bounds.Length;
            RectInt bounds = salvage ? Bounds[type - 1] : EquipmentBounds[type - 1 - Bounds.Length];
            float width = salvage ? 1448f : 1200f, height = salvage ? 1086f : 1200f;
            return new Rect(bounds.x / width, 1f - bounds.yMax / height, bounds.width / width, bounds.height / height);
        }

        internal static Rect RenderedBounds(Rect rect, byte type, bool rotated, out int quarterTurns)
        {
            quarterTurns = 0;
            if (type < 1 || type > LootCatalog.TypeCount || rect.width <= 0 || rect.height <= 0) return Rect.zero;
            Texture2D atlas = Atlas(type);
            if (atlas == null) return Rect.zero;

            Rect uv = GetUV(type);
            float width = uv.width * atlas.width, height = uv.height * atlas.height;
            int nominalWidth = LootCatalog.Width(type, false), nominalHeight = LootCatalog.Height(type, false);
            bool turnToFit = nominalWidth != nominalHeight && (nominalWidth > nominalHeight) != (width > height);
            quarterTurns = (turnToFit ? 1 : 0) + (rotated ? 1 : 0);
            Vector2 size = quarterTurns == 1 ? new Vector2(height, width) : new Vector2(width, height);
            size *= Mathf.Min(rect.width / size.x, rect.height / size.y);
            return new Rect(rect.center - size * .5f, size);
        }

        internal static void Draw(Rect rect, byte type, bool rotated)
        {
            Rect fitted = RenderedBounds(rect, type, rotated, out int quarterTurns);
            if (fitted.width <= 0 || fitted.height <= 0) return;
            Vector2 size = quarterTurns == 1 ? new Vector2(fitted.height, fitted.width) : fitted.size;
            var oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, oldColor.a);
            GUI.matrix = oldMatrix * Matrix4x4.TRS(fitted.center,
                Quaternion.Euler(0, 0, 90 * quarterTurns), Vector3.one);
            GUI.DrawTextureWithTexCoords(new Rect(-size * .5f, size), Atlas(type), GetUV(type), true);
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }
    }
}
