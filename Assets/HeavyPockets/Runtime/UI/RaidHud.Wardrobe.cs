using System;
using UnityEngine;

namespace HeavyPockets
{
    internal sealed partial class RaidHud
    {
        private WardrobeStudio studio;
        private Action<bool> finishWardrobe;
        private int wardrobeRow, wardrobePress = -1;
        private bool turningModel;
        internal int WardrobeStyle { get; private set; }

        internal void OpenWardrobe(int style, bool graphics, Action<bool> finish)
        {
            CloseWardrobe();
            WardrobeStyle = style;
            wardrobeRow = 3;
            finishWardrobe = finish;
            if (graphics) studio = new WardrobeStudio(style);
        }

        internal void CloseWardrobe()
        {
            studio?.Dispose();
            studio = null;
            finishWardrobe = null;
            turningModel = false;
            wardrobePress = -1;
        }

        internal void WardrobeKey(int rowStep, int change, bool enter)
        {
            if (finishWardrobe == null) return;
            int count = RunnerAppearance.Categories.Length;
            wardrobeRow = (wardrobeRow + rowStep + count) % count;
            if (change != 0) CycleOption(wardrobeRow, change);
            if (enter) finishWardrobe(true);
        }

        private void CycleOption(int row, int step)
        {
            wardrobeRow = row;
            int count = RunnerAppearance.Options(row).Length;
            int value = (RunnerAppearance.Get(WardrobeStyle, row) + step + count) % count;
            WardrobeStyle = RunnerAppearance.With(WardrobeStyle, row, value);
            studio?.Dress(WardrobeStyle);
        }

        private static readonly Color Slate = new Color32(38, 58, 64, 255);

        private void DrawWardrobe(float width, float height)
        {
            var ev = Event.current;
            bool paint = ev.type == EventType.Repaint;
            float rail = Mathf.Clamp(width * .405f, 333f, 450f);
            float margin = 26f;
            float top = Mathf.Clamp(height * .15f, 76f, 132f);
            float rowHeight = Mathf.Clamp((height - top - 75f) / RunnerAppearance.Categories.Length, 48, 69);
            Rect stage = new Rect(rail, 0, width - rail, height);
            if (paint)
            {
                Block(0, 0, rail, height, StationInk);
                Block(rail, 0, stage.width, height, Slate);
                float imageHeight = Mathf.Min(height - 43f, stage.width * 1.2f);
                float imageWidth = imageHeight * (5f / 6f);
                var portrait = new Rect(stage.center.x - imageWidth * .5f, (height - imageHeight) * .5f - 10f, imageWidth, imageHeight);
                if (studio != null)
                {
                    GUI.color = Color.white;
                    GUI.DrawTexture(stage, studio.Backdrop, ScaleMode.StretchToFill);
                    if (!studio.CloseUp)
                    {
                        float y = portrait.y + portrait.height * studio.FloorY;
                        GUI.DrawTexture(new Rect(portrait.center.x - portrait.width * .29f, y - 10,
                            portrait.width * .58f, 20), studio.Contact);
                    }
                    GUI.DrawTexture(portrait, studio.Image, ScaleMode.StretchToFill, true);
                }
                Block(rail, 0, 2, height, Amber);
                PocketMark(margin, 18, Amber);
                Text(margin + 28, 12, rail - margin * 2 - 28, 30, "Heavy Pockets", 16, Paper, true);
                Text(margin - 1, top - 39, rail - margin * 2, 39, "Гардероб", 31, Paper, true);
                Block(margin, top + 5, rail - margin * 2, 1, Rule);
            }

            for (int i = 0; i < RunnerAppearance.Categories.Length; i++)
            {
                float y = top + 9 + i * rowHeight;
                var row = new Rect(margin, y, rail - margin * 2, rowHeight - 4);
                float button = 34f;
                Rect prev = new Rect(row.xMax - button * 2 - 1, row.y, button, row.height);
                Rect next = new Rect(row.xMax - button, row.y, button, row.height);
                bool selected = wardrobeRow == i;
                if (paint)
                {
                    Block(row.x, row.y, row.width, row.height, selected ? Slate : StationRow);
                    if (selected) Block(row.x, row.y, 3f, row.height, Mint);
                    float pad = row.x + 12;
                    Text(pad, y + 3, prev.x - pad, 16, RunnerAppearance.Categories[i], 11, selected ? Mint : Muted, true);
                    string value = RunnerAppearance.Options(i)[RunnerAppearance.Get(WardrobeStyle, i)];
                    Text(pad, y + 16, prev.x - pad - 3, row.height - 17, value, 16, Paper, true);
                }
                if (WardrobeButton(prev, i * 3, "‹")) { CycleOption(i, -1); return; }
                if (WardrobeButton(next, i * 3 + 1, "›")) { CycleOption(i, 1); return; }
                if (HitWardrobe(new Rect(row.x, row.y, prev.x - row.x, row.height), i * 3 + 2))
                { wardrobeRow = i; return; }
            }

            float half = (rail - margin * 2 - 8) * .5f;
            if (WardrobeButton(new Rect(margin, height - 52, half, 36), 100, "Назад")) { finishWardrobe(false); return; }
            if (WardrobeButton(new Rect(margin + half + 8, height - 52, half, 36), 101, "Применить", true)) { finishWardrobe(true); return; }
            const float tools = 200;
            float x = stage.center.x - tools * .5f, yTools = height - 42;
            if (WardrobeButton(new Rect(x, yTools, 37, 30), 102, "‹")) { studio?.Turn(-30); return; }
            if (WardrobeButton(new Rect(x + 40, yTools, tools - 80, 30), 103, studio != null && studio.CloseUp ? "В полный рост" : "Приблизить"))
            { if (studio != null) studio.SetCloseUp(!studio.CloseUp); return; }
            if (WardrobeButton(new Rect(x + tools - 37, yTools, 37, 30), 104, "›")) { studio?.Turn(30); return; }
            if (!Application.isFocused) { turningModel = false; wardrobePress = -1; return; }
            if (ev.type == EventType.MouseDown && ev.button == 0 && stage.Contains(ev.mousePosition))
            { turningModel = true; ev.Use(); }
            else if (turningModel && ev.type == EventType.MouseDrag)
            { studio?.Turn(-ev.delta.x * .65f); ev.Use(); }
            else if (ev.type == EventType.MouseUp) { turningModel = false; wardrobePress = -1; }
        }

        private bool WardrobeButton(Rect rect, int id, string caption, bool primary = false)
        {
            var ev = Event.current;
            if (ev.type == EventType.Repaint)
            {
                bool hover = rect.Contains(ev.mousePosition);
                Color fill = primary ? Mint : StationRow;
                if (hover) fill = Color.Lerp(fill, primary ? Paper : Mint, primary ? .25f : .2f);
                Block(rect.x, rect.y, rect.width, rect.height, fill);
                Text(rect.x, rect.y - 1, rect.width, rect.height, caption,
                    caption.Length == 1 ? 23 : 14, primary ? StationInk : Paper, true, TextAnchor.MiddleCenter);
            }
            return HitWardrobe(rect, id);
        }

        private bool HitWardrobe(Rect rect, int id)
        {
            var ev = Event.current;
            if (!Application.isFocused || ev.button != 0 || !rect.Contains(ev.mousePosition)) return false;
            if (ev.type == EventType.MouseDown) { wardrobePress = id; ev.Use(); }
            else if (ev.type == EventType.MouseUp)
            {
                bool click = wardrobePress == id;
                wardrobePress = -1;
                ev.Use();
                return click;
            }
            return false;
        }
    }
}
