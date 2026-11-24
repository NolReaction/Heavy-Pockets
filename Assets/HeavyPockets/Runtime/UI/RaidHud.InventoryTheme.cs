using UnityEngine;

namespace HeavyPockets
{
    internal sealed partial class RaidHud
    {
        private sealed class InventoryPalette
        {
            public readonly string Name;
            public readonly Color Panel, Header, Text, Caption, Accent, Grid, Empty, Locked;
            public readonly Texture2D[] ItemFills = new Texture2D[4];

            public InventoryPalette(string name, uint panel, uint header, uint text, uint caption, uint accent,
                uint grid, uint empty, uint locked)
            {
                Name = name;
                Panel = Rgb(panel);
                Header = Rgb(header);
                Text = Rgb(text);
                Caption = Rgb(caption);
                Accent = Rgb(accent);
                Grid = Rgb(grid);
                Empty = Rgb(empty);
                Locked = Rgb(locked);
            }
        }

        private static Color32 Rgb(uint rgb) =>
            new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        private static readonly InventoryPalette[] palettes = {
            new InventoryPalette("Графит", 0x242931, 0x303641, 0xF5F1E9, 0xC0C6CC, 0xF5BA73, 0x424C56, 0x29323B, 0x1B222A),
            new InventoryPalette("Прилив", 0x173738, 0x204749, 0xEFF8EB, 0xBCD4CC, 0x93E5BE, 0x375755, 0x233F41, 0x152D30),
            new InventoryPalette("Шалфей", 0xBAC5AE, 0xCED5C4, 0x172D2B, 0x3D5348, 0x366254, 0x53645C, 0x36453F, 0x28362F)
        };
        private const string InventoryStyleKey = "HeavyPockets.Inventory.Style";
        private static int styleIndex = -1;
        private static int StyleIndex
        {
            get
            {
                if (styleIndex < 0)
                    styleIndex = Mathf.Clamp(PlayerPrefs.GetInt(InventoryStyleKey, 0), 0, palettes.Length - 1);
                return styleIndex;
            }
        }

        private static InventoryPalette Palette => palettes[StyleIndex];
        private static Color EmptyCell => Palette.Empty;
        private static Color LockedCell => Palette.Locked;
        private static Color Surface => Palette.Grid;
        private static Color GridLine => Color.Lerp(Palette.Empty, Color.white, .24f);
        private static Color FieldLabel => Color.Lerp(Palette.Empty, Color.white, .76f);
        private static Color Unsearched => Color.Lerp(Palette.Empty, Palette.Grid, .63f);
        private static Color PrintInk => Palette.Text;
        private static Color Umber => Palette.Caption;

        private static Rect StyleTab(Board board) =>
            new Rect(board.BagPanel.xMax - Side - 76, board.BagPanel.y + 5, 76, 20);

        // One click previews the complete next skin on the currently open container.
        private static bool HandleStylePointer(Board board, Vector2 point, Event ev)
        {
            if (ev.button != 0 || !StyleTab(board).Contains(point)) return false;
            if (ev.type == EventType.MouseDown)
            {
                styleIndex = (StyleIndex + 1) % palettes.Length;
                PlayerPrefs.SetInt(InventoryStyleKey, styleIndex);
                PlayerPrefs.Save();
            }
            return ev.type == EventType.MouseDown || ev.type == EventType.MouseUp;
        }

        private void DrawStyleTab(Board board)
        {
            Rect tab = StyleTab(board);
            bool hover = pointerActive && tab.Contains(pointer);
            Fill(tab, Color.Lerp(Palette.Header, Palette.Text, hover ? .15f : .07f));
            float x = tab.x + 5, y = tab.center.y;
            Block(x, y - 3, 2, 6, Palette.Accent);
            Block(x + 3, y - 4, 2, 8, Palette.Caption);
            Block(x + 6, y - 2, 2, 4, Palette.Text);
            Text(x + 11, tab.y, tab.width - 22, tab.height, Palette.Name, 10, PrintInk, true);
            Stroke(tab.xMax - 8, y - 2, tab.xMax - 6, y, 1, Palette.Caption);
            Stroke(tab.xMax - 6, y, tab.xMax - 8, y + 2, 1, Palette.Caption);
        }

        private static readonly Color32[] tierColors = {
            Rgb(0xB9C9C9), Rgb(0x73C9FF), Rgb(0xDEA1FF), Rgb(0xFFCA72)
        };
        private static readonly Color32[] tierShadows = {
            Rgb(0x4C6166), Rgb(0x166296), Rgb(0x713C96), Rgb(0x9C5C20)
        };
        private static Color RarityColor(byte type) => tierColors[(int)LootCatalog.Rarity(type)];

        private static void ItemWash(Rect rect, byte type)
        {
            int tier = (int)LootCatalog.Rarity(type);
            InventoryPalette skin = Palette;
            if (skin.ItemFills[tier] == null)
            {
                const int size = 24;
                var gradient = new Texture2D(size, size, TextureFormat.RGB24, false) {
                    wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = x / (size - 1f), v = y / (size - 1f);
                        float wash = Mathf.Clamp01(1f - .54f * u - .75f * v);
                        Color bed = Color.Lerp(skin.Empty, tierShadows[tier], .24f + .72f * wash);
                        pixels[y * size + x] = Color.Lerp(bed, tierColors[tier], .26f * wash * wash);
                    }
                gradient.SetPixels32(pixels);
                gradient.Apply(false, true);
                skin.ItemFills[tier] = gradient;
            }
            GUI.DrawTexture(rect, skin.ItemFills[tier]);
            // Width and number of marks both encode rarity at the smallest grid size.
            int marks = tier + 1;
            for (int i = 0; i < marks; i++)
                Block(rect.x + 2 + i * 5, rect.yMax - 3, 4, 2, tierColors[tier]);
        }
    }
}
