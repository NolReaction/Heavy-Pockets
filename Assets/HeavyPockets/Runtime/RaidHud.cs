using System;
using UnityEngine;

namespace HeavyPockets
{
    internal sealed partial class RaidHud
    {
        internal struct State
        {
            public bool Connected, Disconnected, Crate, Inventory;
            public RaidScreen Screen;
            public bool Queued, ChangingQueue;
            public LootItem[] BagItems, CrateItems, ExtractedItems;
            public LootItem[] Gear;
            public byte BasePouchLevel;
            public GearLocation ActiveGear;
            public LootFootprint[] Unknown;
            public Vector2 Position;
            public float Seconds;
            public float SearchFinishAt;
            public bool WorldLook;
        }

        private static readonly Color Ink = new Color32(16, 29, 34, 237);
        private static readonly Color Inset = new Color32(36, 51, 56, 248);
        private static readonly Color Rule = new Color32(133, 158, 158, 102);
        private static readonly Color Paper = new Color32(244, 241, 225, 255);
        private static readonly Color Muted = new Color32(190, 202, 198, 255);
        private static readonly Color Amber = new Color32(255, 190, 94, 255);
        private static readonly Color Mint = new Color32(119, 230, 187, 255);
        private GUIStyle label;

        internal void Draw(State state)
        {
            if (label == null)
                label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(), clipping = TextClipping.Clip };

            float scale = Mathf.Min(Screen.width / 840f, Screen.height / 500f, 2.2f);
            if (scale <= 0f) return;
            float width = Screen.width / scale, height = Screen.height / scale;
            var oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            const float edge = 18f;
            float mid = width * 0.5f;
            bool inventoryVisible = state.Connected && !state.Disconnected && state.Screen == RaidScreen.Play && state.Inventory;
            if (inventoryVisible && !showingInventory) openingTime = Time.unscaledTime;
            showingInventory = inventoryVisible;
            if (state.Disconnected || !state.Connected || state.Screen != RaidScreen.Play)
                DrawSessionScreen(state, width, height);
            else if (state.Inventory)
            {
                DrawInventory(state, width, height, scale);
            }
            else if (Event.current.type == EventType.Repaint)
            {
                DrawLocation(edge, edge);
                DrawBag(state, width - edge - 164f, edge);
                DrawFieldQuickbar(state, width - edge - 164f, edge + 51f);
                Crosshair(mid, height * 0.5f);
                if (RaidMap.InExit(state.Position))
                    DrawExit(mid, height - 122f, state.Seconds);
                else if (RaidMap.CanOpen(state.Position))
                    Hint(mid, height - 106f, "Обыскать ящик", Amber);
            }
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        private void DrawLocation(float x, float y)
        {
            Panel(x, y, 205, 48);
            PocketMark(x + 13, y + 15, Amber);
            Text(x + 41, y + 3, 158, 25, "Heavy Pockets", 19, Paper, true);
            Text(x + 42, y + 27, 154, 14, "ГРУЗОВОЙ ТЕРМИНАЛ", 10, Muted, true);
        }

        private static void PocketMark(float x, float y, Color color)
        {
            Stroke(x, y, x, y + 14, 3, color);
            Stroke(x, y + 14, x + 8, y + 19, 3, color);
            Stroke(x + 8, y + 19, x + 16, y + 14, 3, color);
            Stroke(x + 16, y + 14, x + 16, y, 3, color);
            Stroke(x, y, x + 8, y + 5, 3, color);
            Stroke(x + 8, y + 5, x + 16, y, 3, color);
        }

        private void DrawBag(State state, float x, float y)
        {
            Panel(x, y, 164, 48);
            CargoIcon(x + 10, y + 13, 0.82f, state.BagItems.Length > 0 ? Mint : Muted);
            Text(x + 40, y + 4, 101, 17, "РЮКЗАК", 10, Muted, true);
            Text(x + 40, y + 20, 96, 22, UsedCells(state.BagItems) + " / " + (LootCatalog.BagWidth * GearCatalog.BagRows(state.Gear)), 17, Paper, true);
        }

        private void DrawExit(float center, float y, float seconds)
        {
            float x = center - 125;
            Panel(x, y, 250, 51);
            Text(x + 12, y + 5, 167, 23, "Эвакуация", 16, Mint, true);
            Text(x + 195, y + 5, 50, 23, Mathf.Max(0f, 3f - seconds).ToString("0.0") + " с", 15, Paper);
            Block(x + 12, y + 35, 226, 4, Inset);
            Block(x + 12, y + 35, 226 * Mathf.Clamp01(seconds / 3f), 4, Mint);
        }


        private void Hint(float center, float y, string value, Color accent)
        {
            label.fontSize = 14;
            label.fontStyle = FontStyle.Normal;
            float w = label.CalcSize(new GUIContent(value)).x + 29f;
            Panel(center - w * 0.5f, y, w, 31);
            Block(center - w * 0.5f, y + 3, 2, 25, accent);
            Text(center - w * 0.5f + 14, y, w - 24, 31, value, 14, Paper);
        }

        private static void Crosshair(float x, float y)
        {
            Block(x - 2, y - 2, 4, 4, Ink);
            Block(x - 1, y - 1, 2, 2, Paper);
        }

        private static void CargoIcon(float x, float y, float s, Color col)
        {
            Block(x + 5*s, y + 3*s, 15*s, 2*s, col);
            Block(x, y + 7*s, 25*s, 2*s, col);
            Block(x, y + 20*s, 25*s, 2*s, col);
            Block(x, y + 7*s, 2*s, 14*s, col);
            Block(x + 23*s, y + 7*s, 2*s, 14*s, col);
            Block(x + 6*s, y + 10*s, 2*s, 7*s, col);
            Block(x + 17*s, y + 10*s, 2*s, 7*s, col);
        }

        private static void Panel(float x, float y, float w, float h)
        {
            Block(x, y, w, h, Ink);
            Block(x, y, w, 1, Rule);
        }

        private void Text(float x, float y, float width, float height, string value, int size, Color color,
            bool bold = false, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            label.fontSize = size;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            label.normal.textColor = color;
            label.alignment = alignment;
            GUI.Label(new Rect(x, y, width, height), value, label);
        }

        private static void Stroke(float x1, float y1, float x2, float y2, float thickness, Color col)
        {
            var matrix = GUI.matrix;
            var delta = new Vector2(x2 - x1, y2 - y1);
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            GUI.matrix = matrix * Matrix4x4.TRS(new Vector3(x1, y1, 0), Quaternion.Euler(0, 0, angle), Vector3.one);
            Block(-0.5f, -thickness * 0.5f, delta.magnitude + 1f, thickness, col);
            GUI.matrix = matrix;
        }

        private static void Block(float x, float y, float w, float h, Color color)
        {
            var old = GUI.color;
            GUI.color = old * color;
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
