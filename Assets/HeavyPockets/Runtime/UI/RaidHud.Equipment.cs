using System;
using UnityEngine;

namespace HeavyPockets
{
    internal sealed partial class RaidHud
    {
        internal Action<uint, GearLocation, GearLocation, int, int, bool> MoveGear;
        internal Action<GearLocation> SelectGear;

        private static readonly GearLocation[] wornSlots = {
            GearLocation.Primary, GearLocation.Secondary, GearLocation.Armor, GearLocation.Pouch };
        private static readonly GearLocation[] quickSlots = {
            GearLocation.Primary, GearLocation.Secondary, GearLocation.PouchCell, GearLocation.PouchCell2 };
        private static readonly GearLocation[] pouchCells = {
            GearLocation.PouchCell, GearLocation.PouchCell2, GearLocation.PouchCell3, GearLocation.PouchCell4 };

        // Bag is the zero value and marks an interaction with one of the ordinary grids.
        private GearLocation heldGear, selectedGear;
        private ulong tipKey, shownTip;
        private float tipSince, lastTipSample, cardAmount, cardOpacity;
        private Rect visibleCard, shownAnchor;
        private LootItem shownItem;
        private string shownDescription;

        private static bool IsGearSlot(GearLocation slot) => GearCatalog.IsSlot(slot);
        private static LootItem GearItem(State state, GearLocation slot) =>
            IsGearSlot(slot) && state.Gear != null && (int)slot < state.Gear.Length ? state.Gear[(int)slot] : default;

        private static Rect GearRect(Board board, GearLocation slot)
        {
            const float pouchCell = Cell;
            if (slot == GearLocation.Backpack)
                return new Rect(board.BagPanel.x + Side, board.BagPanel.y + 35, 38, 38);
            int i = Array.IndexOf(pouchCells, slot);
            if (i >= 0)
                return new Rect(board.Bag.x + (i % 2) * (pouchCell + 3), board.Bag.yMax + 25 + (i / 2) * (pouchCell + 3),
                    pouchCell, pouchCell);
            i = Array.IndexOf(wornSlots, slot);
            if (i < 0) return Rect.zero;
            Rect pane = board.EquipmentPanel;
            const float gap = 4;
            float height = (pane.height - 37 - Side - gap * 3) / 4f;
            return new Rect(pane.x + Side, pane.y + 37 + i * (height + gap), pane.width - 2 * Side, height);
        }

        private static bool GearHit(Board board, Vector2 point, out GearLocation slot)
        {
            if (board.Frame.Contains(point))
                for (int i = 1; i < GearCatalog.Slots; i++)
                {
                    slot = (GearLocation)i;
                    if (GearRect(board, slot).Contains(point)) return true;
                }
            slot = GearLocation.Bag;
            return false;
        }

        private void ResetGearInteraction()
        {
            heldGear = selectedGear = GearLocation.Bag;
            tipKey = shownTip = 0;
            cardOpacity = cardAmount = 0;
            lastTipSample = Time.unscaledTime;
            shownDescription = null;
            visibleCard = Rect.zero;
        }

        private void ValidateGearSelection(State state)
        {
            if (IsGearSlot(heldGear) && GearItem(state, heldGear).Id != held.Id) CancelDrag();
            if (IsGearSlot(selectedGear) && GearItem(state, selectedGear).Id != selectedId)
            {
                selectedGear = GearLocation.Bag;
                selectedId = 0;
            }
        }

        internal void FocusGear(GearLocation slot, LootItem item)
        {
            if (held.Id != 0 || !IsGearSlot(slot) || item.Id == 0) return;
            selectedGear = slot;
            selectedId = item.Id;
            keyboardFocus = true;
        }

        private bool CanMoveGear(State state, GearLocation to, int x = -1, int y = -1)
        {
            var from = IsGearSlot(heldGear) ? heldGear : heldBag ? GearLocation.Bag : GearLocation.Crate;
            return held.Id != 0 && GearCatalog.PlanMove(held, from, to, x, y, rotated, state.BagItems,
                state.CrateItems, state.Gear, state.Unknown, out _, out _) == RaidStatus.Placed;
        }

        private bool HandleGearPointer(State state, Board board, Vector2 p, EventType type, bool quick)
        {
            if (!GearHit(board, p, out var slot)) return false;
            if (type == EventType.MouseDown)
            {
                CancelDrag();
                LootItem item = GearItem(state, slot);
                selectedGear = slot;
                selectedId = item.Id;
                if (item.Id == 0 || slot == GearLocation.Pouch || !GearCatalog.Available(slot, state.Gear)) return true;
                held = item;
                heldGear = slot;
                heldBag = false;
                rotated = item.Rotated;
                if (quick)
                {
                    if (CanMoveGear(state, GearLocation.Bag))
                        MoveGear?.Invoke(item.Id, slot, GearLocation.Bag, -1, -1, item.Rotated);
                    CancelDrag();
                    return true;
                }
                Rect box = GearRect(board, slot);
                Vector2 offset = box.center - p;
                grabToCenter = new Vector2(offset.x * HeldSize.x / box.width, offset.y * HeldSize.y / box.height);
                pressPoint = p;
                return true;
            }
            if (type != EventType.MouseUp || held.Id == 0) return false;
            if (dragging && CanMoveGear(state, slot))
            {
                var from = IsGearSlot(heldGear) ? heldGear : heldBag ? GearLocation.Bag : GearLocation.Crate;
                MoveGear?.Invoke(held.Id, from, slot, -1, -1, rotated);
            }
            else if (!dragging && slot == heldGear)
            {
                keyboardFocus = true;
                if (GearCatalog.Selectable(slot)) SelectGear?.Invoke(slot);
            }
            CancelDrag();
            return true;
        }

        private void DrawGear(State state, Board board)
        {
            var backpack = GearRect(board, GearLocation.Backpack);
            DrawGearSlot(state, board, GearLocation.Backpack);
            var equipped = GearItem(state, GearLocation.Backpack);
            float infoX = backpack.xMax + 6, infoWidth = board.BagPanel.xMax - Side - infoX;
            string packName = equipped.Id == 0 ? "Без рюкзака" : LootCatalog.Name(equipped.Type);
            Text(infoX, backpack.y, infoWidth, 20, packName, packName.Length > 16 ? 10 : 12, PrintInk, true);
            Text(infoX, backpack.y + 18, infoWidth, 17, UsedCells(state.BagItems) + " / " +
                (GearCatalog.BagRows(state.Gear) * LootCatalog.BagWidth) + " ячеек", 11, Umber);

            float left = board.Bag.x, top = board.Bag.yMax + 6;
            Text(left, top - 1, 89, 17, "Подсумок", 12, PrintInk, true);
            for (int i = 0; i < pouchCells.Length; i++)
                DrawGearSlot(state, board, pouchCells[i]);
            for (int i = 0; i < wornSlots.Length; i++)
                DrawGearSlot(state, board, wornSlots[i]);
        }

        private void DrawGearSlot(State state, Board board, GearLocation slot)
        {
            Rect r = GearRect(board, slot);
            LootItem item = GearItem(state, slot);
            bool pouchCell = Array.IndexOf(pouchCells, slot) >= 0;
            bool small = pouchCell || slot == GearLocation.Backpack;
            bool available = GearCatalog.Available(slot, state.Gear);
            bool under = pointerActive && !state.WorldLook && r.Contains(pointer);
            bool active = state.ActiveGear == slot && item.Id != 0;
            Color edge = active ? Mint : Surface;
            if (dragging && under) edge = CanMoveGear(state, slot) ? Mint : Bad;
            else if (under || (keyboardFocus && selectedGear == slot)) edge = FieldLabel;
            bool hasArt = item.Id != 0 && !(dragging && heldGear == slot);
            if (!available) Fill(r, LockedCell);
            else if (hasArt) ItemWash(r, item.Type);
            else Fill(r, EmptyCell);
            Outline(r, edge, 1);
            if (!available)
            {
                Hatch(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2));
                DrawLock(r.center, GridLine);
                return;
            }
            if (active) Corners(r, Mint, 5f, 1.5f);
            float x = r.x + 6, y = r.y + 4;
            if (!small)
            {
                string name = slot == GearLocation.Primary ? "Основное" : slot == GearLocation.Secondary ? "Запасное" : GearCatalog.Name(slot);
                Block(r.x + 1, r.y + 1, r.width - 2, 19, new Color32(24, 37, 39, 130));
                Text(x, y - 2, r.xMax - x - 3, 18, name, 12, Paper, true);
            }
            bool pouch = slot == GearLocation.Pouch;
            Rect art = small ? new Rect(r.x + 4, r.y + 4, r.width - 8, r.height - 8) :
                new Rect(r.x + 7, r.y + 21, r.width - 14, r.height - 26);

            if (hasArt) LootArt.Draw(art, item.Type, item.Rotated);
            else DrawGearStencil(art, slot);
            if (pouch)
                DrawLock(new Vector2(r.xMax - 11, y + 6), FieldLabel);
        }

        private void DrawFieldQuickbar(State state, float x, float y)
        {
            if (state.Gear == null || state.Gear.Length != GearCatalog.Slots) return;
            for (int i = 0; i < quickSlots.Length; i++)
            {
                var r = new Rect(x + i * 41, y, 40, 23);
                LootItem item = GearItem(state, quickSlots[i]);
                bool active = item.Id != 0 && state.ActiveGear == quickSlots[i];
                if (item.Id != 0)
                {
                    ItemWash(r, item.Type);
                    LootArt.Draw(new Rect(r.x + 4, r.y + 2, r.width - 8, 18), item.Type, item.Rotated);
                }
                else Fill(r, Ink);
                if (active) Corners(r, Mint, 3f, 1f);
            }
        }

        private void DrawItemCard(State state, Board board, float width, float height)
        {
            Rect anchor = Rect.zero;
            LootItem item = default;
            string description = null;
            ulong key = 0;
            if (!state.WorldLook && held.Id == 0)
            {
                bool gearFocus = keyboardFocus && IsGearSlot(selectedGear);
                GearLocation slot = selectedGear;
                if (gearFocus || (!keyboardFocus && pointerActive && GearHit(board, pointer, out slot)))
                {
                    item = GearItem(state, slot);
                    if (item.Id != 0 && (gearFocus || hoverId == item.Id)) anchor = GearRect(board, slot);
                    else item = default;
                }
                else
                {
                    item = FocusedItem(state, board, out bool inBag);
                    if (item.Id != 0) anchor = TileRect(inBag ? board.Bag : board.Crate, item.X, item.Y, item, item.Rotated);
                    else if (!keyboardFocus && HoveredFootprint(state, board, out var f))
                    {
                        anchor = new Rect(board.Crate.x + f.X * Cell, board.Crate.y + f.Y * Cell, f.Width * Cell, f.Height * Cell);
                        key = 0x100000000UL | (uint)(1 + f.Y * LootCatalog.CrateWidth + f.X);
                        description = "Содержимое станет известно после обыска.";
                    }
                }
            }
            if (item.Id != 0)
            {
                key = item.Id;
                description = LootCatalog.Description(item.Type);
            }
            float now = Time.unscaledTime;
            float elapsed = Mathf.Max(0, now - lastTipSample);
            lastTipSample = now;
            if (key != tipKey) { tipKey = key; tipSince = now; }
            visibleCard = Rect.zero;
            if (state.WorldLook || held.Id != 0)
            {
                cardOpacity = cardAmount = 0;
                shownTip = 0;
                return;
            }
            bool show = key != 0 && now - tipSince >= .17f;
            if (show)
            {
                shownTip = key;
                shownItem = item;
                shownDescription = description;
                shownAnchor = anchor;
            }
            cardAmount = Mathf.MoveTowards(cardAmount, show ? 1f : 0f, elapsed / (show ? .12f : .09f));
            cardOpacity = Mathf.SmoothStep(0f, 1f, cardAmount);
            if (cardOpacity <= 0 || shownTip == 0) return;
            item = shownItem;
            anchor = shownAnchor;
            description = shownDescription;
            float w = Mathf.Min(196, width - 12);
            const float icon = 6;
            label.fontSize = 11;
            label.fontStyle = FontStyle.Normal;
            bool wrap = label.wordWrap;
            label.wordWrap = true;
            float body = Mathf.Max(17, label.CalcHeight(new GUIContent(description), w - 2 * icon));
            string name = item.Id != 0 ? LootCatalog.Name(item.Type) : "Неопознанный предмет";
            label.fontSize = 13;
            label.fontStyle = FontStyle.Bold;
            float title = Mathf.Max(18, label.CalcHeight(new GUIContent(name), w - 2 * icon));
            float h = title + body + 28;
            // Keep every transfer target visible while the description is open.
            float left = (board.Open ? board.ChestPanel.x : board.BagPanel.x) - w - 7;
            float top = Mathf.Clamp(anchor.center.y - h * .5f, 5, height - h - 5);
            Rect r = new Rect(Mathf.Round(Mathf.Clamp(left, 5, width - w - 5)), Mathf.Round(top), w, h);
            visibleCard = r;
            Color tint = item.Id == 0 ? Amber : RarityColor(item.Type);
            Color previous = GUI.color;
            GUI.color = Fade(previous, cardOpacity);
            DrawPaper(r);
            Outline(r, Umber, 1);
            Block(r.x, r.y, r.width, 3, item.Id == 0 ? Oxide : Color.Lerp(PrintInk, tint, .5f));
            Text(r.x + icon, r.y + 4, w - 2 * icon, title, name, 13, PrintInk, true);
            string metadata = item.Id == 0 ? "Не опознано" : LootCatalog.RarityName(item.Type) + "  ·  " +
                LootCatalog.Width(item.Type, item.Rotated) + "×" + LootCatalog.Height(item.Type, item.Rotated);
            Text(r.x + icon, r.y + 4 + title, w - 2 * icon, 15, metadata, 10, Umber, true);
            Text(r.x + icon, r.y + title + 21, w - 2 * icon, body, description, 11, PrintInk);
            GUI.color = previous;
            label.wordWrap = wrap;
        }

        private static Color Fade(Color color, float opacity)
        {
            color.a *= opacity;
            return color;
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        internal bool ProbeTipVisible => tipKey != 0 && tipKey == shownTip && cardOpacity > .95f && visibleCard.width > 0;

        internal static void ProbePanelsAt(bool open, Vector2 screen, out Rect packPane, out Rect chestPane,
            out Rect pack, out Rect chest, out Rect[] slots)
        {
            var board = new Board(open, screen.x, screen.y);
            packPane = board.Frame;
            chestPane = board.ChestPanel;
            pack = board.Bag;
            chest = open ? board.Crate : Rect.zero;
            slots = new Rect[GearCatalog.Slots - 1];
            for (int i = 0; i < slots.Length; i++) slots[i] = GearRect(board, (GearLocation)(i + 1));
        }

        internal bool ProbeGearPointer(State state, GearLocation slot, EventType type, bool ctrl)
        {
            if (!IsGearSlot(slot)) return false;
            var board = GetBoard(state, out _);
            scriptedPointer = true;
            return HandlePointer(state, board, GearRect(board, slot).center, type, ctrl, false);
        }

        internal void ProbeGearHover(State state, GearLocation slot)
        {
            var board = GetBoard(state, out _);
            scriptedPointer = true;
            ObservePointer(state, board, GearRect(board, slot).center, true);
        }
#endif
    }
}
