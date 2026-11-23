using System;
using UnityEngine;

namespace HeavyPockets
{
    internal sealed partial class RaidHud
    {
        internal Action<RaidMenuAction> MenuAction;
        private static readonly RaidMenuAction[] PauseButtons = { RaidMenuAction.Resume, RaidMenuAction.Wardrobe, RaidMenuAction.Bag, RaidMenuAction.Quit };
        private static readonly RaidMenuAction[] ReceiptButtons = { RaidMenuAction.Base };
        private static readonly RaidMenuAction[] BaseButtons = { RaidMenuAction.Join, RaidMenuAction.Quit,
            RaidMenuAction.Pouch1, RaidMenuAction.Pouch2, RaidMenuAction.Pouch3, RaidMenuAction.Pouch4 };
        private static readonly RaidMenuAction[] QueueButtons = { RaidMenuAction.CancelQueue, RaidMenuAction.Quit };
        private static readonly RaidMenuAction[] DisconnectButtons = { RaidMenuAction.Quit };
        private RaidMenuAction? pressedButton;
        private RaidMenuAction[] currentButtons;
        private int menuFocus = -1;
        private Vector2 lastMenuPointer;
        private bool menuPointerKnown;
        private static readonly Color StationInk = new Color32(21, 38, 43, 251);
        private static readonly Color StationRow = new Color32(37, 55, 59, 255);

        private RaidMenuAction[] Buttons(State s)
        {
            var actions = s.Disconnected ? DisconnectButtons : !s.Connected ? Array.Empty<RaidMenuAction>() :
                s.Screen == RaidScreen.Menu ? PauseButtons : s.Screen == RaidScreen.Receipt ? ReceiptButtons :
                s.Screen == RaidScreen.Base ? (s.Queued ? QueueButtons : BaseButtons) : Array.Empty<RaidMenuAction>();
            if (actions != currentButtons)
            {
                ResetMenuInput();
                currentButtons = actions;
            }
            return actions;
        }

        internal void ResetMenuInput()
        {
            currentButtons = null;
            pressedButton = null;
            menuFocus = -1;
            menuPointerKnown = false;
        }

        private static bool Enabled(State s, RaidMenuAction action) =>
            !s.ChangingQueue || action == RaidMenuAction.Quit;

        internal void MenuKey(State s, int step, bool enter)
        {
            var actions = Buttons(s);
            if (actions.Length == 0) return;
            if (step != 0)
            {
                for (int i = 0; i < actions.Length; i++)
                {
                    menuFocus = menuFocus < 0 ? (step > 0 ? 0 : actions.Length - 1) :
                        (menuFocus + step + actions.Length) % actions.Length;
                    if (Enabled(s, actions[menuFocus])) break;
                }
            }
            int index = menuFocus < 0 ? 0 : menuFocus;
            if (enter && Enabled(s, actions[index])) MenuAction(actions[index]);
        }

        private static string Caption(RaidMenuAction action, State state)
        {
            switch (action)
            {
                case RaidMenuAction.Resume: return "Продолжить";
                case RaidMenuAction.Bag: return "Открыть рюкзак";
                case RaidMenuAction.Wardrobe: return "Гардероб";
                case RaidMenuAction.Base: return "Вернуться на базу";
                case RaidMenuAction.Join: return state.ChangingQueue ? "Входим…" : "Начать новый рейд";
                case RaidMenuAction.CancelQueue: return state.ChangingQueue ? "Отменяем…" : "Отменить ожидание";
                default: return "Выйти из игры";
            }
        }

        private readonly struct StationLayout
        {
            internal readonly float Width, Height, Rail, Margin, HeaderY, HeaderHeight, ActionY;
            private readonly bool hasPouches;

            internal StationLayout(float width, float height, bool receipt, int count)
            {
                Width = width;
                Height = height;
                Rail = Mathf.Round(Mathf.Clamp(width * .36f, 304f, 372f));
                Margin = Mathf.Round(Rail * .079f);
                HeaderY = Mathf.Round(Mathf.Clamp(height * .20f, 78f, 150f));
                HeaderHeight = receipt ? 96f : 76f;
                hasPouches = count == BaseButtons.Length;
                ActionY = Mathf.Min(HeaderY + HeaderHeight + 55f, height - 108f - (hasPouches ? 2 : count) * 44f);
            }

            internal Rect Button(int index)
            {
                if (!hasPouches || index < 2)
                    return new Rect(Margin, ActionY + index * 44f, Rail - Margin * 2f, 38f);
                float tile = Mathf.Min(150f, (Width - Rail - Margin * 2f - 10f) * .5f);
                int slot = index - 2;
                return new Rect(Rail + Margin + (slot % 2) * (tile + 8f),
                    HeaderY + 45f + (slot / 2) * 63f, tile, 56f);
            }

            internal Rect Manifest(int count, out int columns)
            {
                float gap = Mathf.Clamp(Width * .035f, 20f, 54f);
                float width = Mathf.Min(Width - Rail - gap * 2f, count == 1 ? 336f : 530f);
                columns = count > 1 && width >= 432f ? 2 : 1;
                int rows = (count + columns - 1) / columns;
                float height = 50f + rows * 48f;
                float y = Mathf.Min(HeaderY, Height - height - 20f);
                return new Rect(Rail + gap, y, width, height);
            }
        }

        private bool MenuPointer(State s, StationLayout layout, RaidMenuAction[] actions, Vector2 p, bool down)
        {
            for (int i = 0; i < actions.Length; i++)
            {
                var action = actions[i];
                if (!layout.Button(i).Contains(p) || !Enabled(s, action)) continue;
                if (down) { pressedButton = action; menuFocus = -1; }
                else
                {
                    bool click = pressedButton == action;
                    pressedButton = null;
                    if (click) MenuAction(action);
                }
                return true;
            }
            pressedButton = null;
            return false;
        }

        private void DrawSessionScreen(State s, float width, float height)
        {
            var actions = Buttons(s);
            bool receipt = s.Connected && !s.Disconnected && s.Screen == RaidScreen.Receipt;
            bool atBase = s.Connected && !s.Disconnected && s.Screen == RaidScreen.Base;
            bool paused = s.Connected && !s.Disconnected && s.Screen == RaidScreen.Menu;
            var layout = new StationLayout(width, height, receipt, actions.Length);
            Vector3 mouse = Input.mousePosition;
            Vector2 point = new Vector2(mouse.x, Screen.height - mouse.y) * (width / Screen.width);
            var ev = Event.current;
            if (!Application.isFocused) pressedButton = null;
            if ((ev.type == EventType.MouseDown || ev.type == EventType.MouseUp) && ev.button == 0)
            {
                if (Application.isFocused) MenuPointer(s, layout, actions, point, ev.type == EventType.MouseDown);
                ev.Use();
                return;
            }
            if (ev.type != EventType.Repaint) return;
            if (menuPointerKnown && (lastMenuPointer - point).sqrMagnitude > .5f) menuFocus = -1;
            menuPointerKnown = true;
            lastMenuPointer = point;

            Color accent = receipt || atBase ? Mint : Amber;
            Block(layout.Rail, 0, width - layout.Rail, height, new Color32(9, 17, 20, 72));
            Block(0, 0, layout.Rail, height, StationInk);
            Block(layout.Rail - 1, 0, 1, height, Rule);
            float margin = layout.Margin;
            PocketMark(margin + 1, 28, accent);
            Text(margin + 30, 20, layout.Rail - margin * 2 - 30, 32, "Heavy Pockets", 21, Paper, true);
            Block(margin, 65, layout.Rail - margin * 2, 1, Rule);

            // The title is printed on a clipped stock label that protrudes over the rail.
            float y = layout.HeaderY, h = layout.HeaderHeight;
            float edge = layout.Rail + 8;
            Block(0, y, edge - 8, h, Paper);
            for (int i = 0; i < 8; i++) Block(edge - 8 + i, y + i + 1, 1, h - i - 1, Paper);
            Block(0, y, 5, h, accent);
            string title = s.Disconnected ? "Связь потеряна" : !s.Connected ? "Подключение…" :
                receipt ? "Эвакуация" : atBase ? "База" : "Меню рейда";
            if (receipt)
            {
                Text(margin, y + 6, layout.Rail - margin, 48, title, 36, StationInk, true);
                Text(margin + 1, y + 49, layout.Rail - margin, 35, "завершена", 25, StationInk);
            }
            else
                Text(margin, y, layout.Rail - margin, h, title, atBase ? 44 : 33, StationInk, true);

            string status = s.Disconnected ? "Соединение с сервером закрыто." : !s.Connected ? "Ожидаем ответ сервера." :
                receipt ? (s.ExtractedItems.Length == 0 ? "Рюкзак пуст." : "") :
                paused ? "Сетевой рейд продолжается." :
                s.Queued ? "Ждём завершения рейда других игроков." : "Готов к новому рейду.";
            if (status.Length > 0) Text(margin, y + h + 8, layout.Rail - margin - 10, 28, status, 13, Muted);
            if (receipt && s.ExtractedItems.Length > 0) DrawManifest(s.ExtractedItems, layout);
            if (atBase && !s.Queued)
                Text(layout.Rail + margin, layout.HeaderY, 310, 35, "Подсумок для рейда", 21, Paper, true);
            for (int i = 0; i < actions.Length; i++)
                DrawMenuButton(s, actions[i], i, layout.Button(i), point, accent);

        }

        private void DrawMenuButton(State s, RaidMenuAction action, int index, Rect r, Vector2 point, Color accent)
        {
            bool enabled = Enabled(s, action);
            bool hover = enabled && (menuFocus >= 0 ? menuFocus == index : r.Contains(point));
            bool pressed = enabled && pressedButton == action && r.Contains(point);
            bool primary = index == 0 && enabled;
            bool pouch = action >= RaidMenuAction.Pouch1 && action <= RaidMenuAction.Pouch4;
            int level = 1 + (int)action - (int)RaidMenuAction.Pouch1;
            Color fill = primary ? accent : StationRow;
            if (hover) fill = Color.Lerp(fill, primary ? Paper : accent, primary ? .35f : .22f);
            if (pressed) fill = Color.Lerp(fill, StationInk, primary ? .22f : .38f);
            Block(r.x, r.y, r.width, r.height, fill);
            if (pouch)
            {
                bool selected = level == s.BasePouchLevel;
                Block(r.x, r.y, 3f, r.height, selected || hover ? accent : Rule);
                Text(r.x + 11, r.y + 3, r.width - 18, 27, "Уровень " + level, 16, selected ? accent : Paper, true);
                string cells = level == 1 ? "1 ячейка" : level + " ячейки";
                Text(r.x + 11, r.y + 29, r.width - 14, 20, cells + (selected ? "  ·  Надет" : ""), 12, selected ? accent : Muted);
                return;
            }
            if (!primary || pressed)
                Block(r.x, r.yMax - 1, r.width, 1, hover ? accent : Rule);
            if (hover) Block(r.x, r.y, 3, r.height, primary ? StationInk : accent);
            Color foreground = primary ? StationInk : enabled ? Paper : Muted;
            float offset = pressed ? 1f : 0f;
            Text(r.x + 14 + offset, r.y + offset, r.width - 53, r.height, Caption(action, s),
                primary ? 16 : 14, foreground, primary);
            if (primary || hover)
            {
                float x = r.xMax - 20 + offset, y = r.center.y + offset;
                Block(x - 9, y - 1, 13, 2, foreground);
                Stroke(x - 1, y - 5, x + 4, y, 2, foreground);
                Stroke(x + 4, y, x - 1, y + 5, 2, foreground);
            }
        }

        private void DrawManifest(LootItem[] items, StationLayout layout)
        {
            Rect r = layout.Manifest(items.Length, out int columns);
            Block(r.x, r.y, r.width, r.height, StationInk);
            Block(r.x, r.y, r.width, 3, Mint);
            Text(r.x + 14, r.y + 5, r.width - 80, 36, "Вынесено предметов", 17, Paper, true);
            Text(r.xMax - 57, r.y + 4, 42, 37, items.Length.ToString(), 22, Mint, true, TextAnchor.MiddleRight);
            float w = (r.width - 24f) / columns;
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i];
                float x = r.x + 12 + i % columns * w;
                float y = r.y + 44 + (i / columns) * 48;
                Block(x, y, w - 4, 1, Rule);
                Block(x, y + 5, 37, 37, StationRow);
                LootArt.Draw(new Rect(x + 2, y + 7, 33, 33), item.Type, item.Rotated);
                Text(x + 45, y + 3, w - 51, 22, LootCatalog.Name(item.Type), 12, Paper, true);
                Text(x + 45, y + 23, w - 51, 17, LootCatalog.RarityName(item.Type), 11, RarityColor(item.Type));
            }
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        internal bool ProbeMenuClick(State s, RaidMenuAction action)
        {
            var actions = Buttons(s);
            int index = Array.IndexOf(actions, action);
            if (index < 0) return false;
            float scale = Mathf.Min(Screen.width / 840f, Screen.height / 500f, 2.2f);
            float width = scale > 0f ? Screen.width / scale : 840f;
            float height = scale > 0f ? Screen.height / scale : 500f;
            var layout = new StationLayout(width, height, s.Screen == RaidScreen.Receipt && !s.Disconnected, actions.Length);
            var point = layout.Button(index).center;
            return MenuPointer(s, layout, actions, point, true) && MenuPointer(s, layout, actions, point, false);
        }
#endif
    }
}
