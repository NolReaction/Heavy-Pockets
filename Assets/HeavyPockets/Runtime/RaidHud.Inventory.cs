using System;
using UnityEngine;

namespace HeavyPockets
{
    internal sealed partial class RaidHud
    {
        internal const float Cell = 32f;
        private const float Side = 9f;
        private static readonly Color Surface = new Color32(30, 47, 49, 255);
        private static readonly Color EmptyCell = new Color32(32, 46, 48, 255);
        private static readonly Color Unsearched = new Color32(48, 58, 59, 255);
        private static readonly Color GridLine = new Color32(85, 100, 98, 255);
        private static readonly Color FieldLabel = new Color32(205, 211, 191, 255);
        private static readonly Color Bad = new Color32(255, 139, 113, 255);

        private readonly Func<bool, uint, uint> quickTransfer;
        private readonly Action<LootItem, bool, bool, int, int, bool> place;
        private readonly Action close;

        private uint selectedId;
        private bool sourceBag, heldBag, rotated, dragging, scriptedPointer, keyboardFocus;
        private LootItem held;
        private Vector2 pressPoint, pointer, grabToCenter;
        internal Vector2 GhostCenter => pointer + grabToCenter;

        internal bool IsHoldingItem => held.Id != 0;

        private float openingTime;
        private bool showingInventory;
        private float Opening => showingInventory ? Mathf.SmoothStep(0f, 1f,
            (Time.unscaledTime - openingTime) / .20f) : 1f;

        private struct Board
        {
            internal Rect Frame, BagPanel, EquipmentPanel, ChestPanel, Bag, Crate, Close;
            internal bool Open;
            internal Board(bool open, float width, float height, float reveal = 1f)
            {
                Open = open;
                const float gap = 5f, equipmentWidth = 155f, crateGridTop = 37f;
                float paneHeight = 78 + LootCatalog.BagHeight * Cell + 25 + 2 * Cell + 3 + Side;
                float bagWidth = LootCatalog.BagWidth * Cell, chestWidth = LootCatalog.CrateWidth * Cell;
                float dockWidth = equipmentWidth + gap + bagWidth + 2 * Side + (open ? gap + chestWidth + 2 * Side : 0f);
                float top = Mathf.Round(Mathf.Clamp((height - paneHeight) * .32f, 5f, 34f));
                float right = Mathf.Round(width - 10f + (dockWidth + 10f) * (1f - reveal));
                EquipmentPanel = new Rect(right - equipmentWidth, top, equipmentWidth, paneHeight);
                BagPanel = new Rect(EquipmentPanel.x - gap - bagWidth - Side * 2f, top, bagWidth + Side * 2f, paneHeight);
                Frame = Rect.MinMaxRect(BagPanel.x, top, right, top + paneHeight);
                ChestPanel = open ? new Rect(BagPanel.x - gap - chestWidth - Side * 2f, top,
                    chestWidth + Side * 2f, crateGridTop + LootCatalog.CrateHeight * Cell + Side) : Rect.zero;
                Bag = new Rect(BagPanel.x + Side, top + 78, bagWidth, LootCatalog.BagHeight * Cell);
                Crate = open ? new Rect(ChestPanel.x + Side, top + crateGridTop, chestWidth, LootCatalog.CrateHeight * Cell) : Rect.zero;
                Close = new Rect(right - 25, top + 5, 21, 22);
            }

            internal bool InPanel(Vector2 point) => BagPanel.Contains(point) || EquipmentPanel.Contains(point) ||
                (Open && ChestPanel.Contains(point));

            internal bool Hit(Vector2 p, out bool bag, out int x, out int y)
            {
                bag = Bag.Contains(p);
                Rect grid = bag ? Bag : Crate;
                x = Mathf.FloorToInt((p.x - grid.x) / Cell);
                y = Mathf.FloorToInt((p.y - grid.y) / Cell);
                return bag || (Open && Crate.Contains(p));
            }
        }

        internal RaidHud(Func<bool, uint, uint> quickTransfer, Action<LootItem, bool, bool, int, int, bool> place, Action close)
        {
            this.quickTransfer = quickTransfer;
            this.place = place;
            this.close = close;
        }

        internal void ResetSelection()
        {
            CancelDrag();
            CancelQuickTransfers(true);
            ResetGearInteraction();
            selectedId = 0;
            hoverId = 0;
            pointerInitialized = pointerActive = wasLooking = false;
            sourceBag = false;
            scriptedPointer = false;
            keyboardFocus = false;
        }

        internal bool CancelDrag()
        {
            bool hadItem = held.Id != 0;
            held = default;
            heldGear = GearLocation.Bag;
            dragging = false;
            return hadItem;
        }

        internal bool RotateHeld()
        {
            if (held.Id == 0) return false;
            rotated = !rotated;
            dragging = true;
            return true;
        }

        private Vector2 HeldSize => new Vector2(LootCatalog.Width(held.Type, rotated) * Cell,
            LootCatalog.Height(held.Type, rotated) * Cell);

        private bool Target(Board board, out bool bag, out int x, out int y)
        {
            bool onBoard = board.Hit(GhostCenter, out bag, out _, out _);
            Vector2 origin = (bag ? board.Bag : board.Crate).position;
            Vector2 tile = (GhostCenter - HeldSize * 0.5f - origin) / Cell;
            x = Mathf.FloorToInt(tile.x + 0.5f);
            y = Mathf.FloorToInt(tile.y + 0.5f);
            return onBoard;
        }

        private static int UsedCells(LootItem[] items)
        {
            int sum = 0;
            foreach (var item in items)
                sum += LootCatalog.Width(item.Type, false) * LootCatalog.Height(item.Type, false);
            return sum;
        }

        private static int Find(LootItem[] items, uint id)
        {
            for (int i = 0; i < items.Length; i++)
                if (items[i].Id == id) return i;
            return -1;
        }

        private static LootItem At(LootItem[] items, int x, int y)
        {
            foreach (var item in items)
                if (x >= item.X && y >= item.Y && x < item.X + LootCatalog.Width(item.Type, item.Rotated)
                    && y < item.Y + LootCatalog.Height(item.Type, item.Rotated)) return item;
            return default;
        }

        private void EnsureSelection(LootItem[] crate, LootItem[] bag)
        {
            if (held.Id != 0 && !IsGearSlot(heldGear))
            {
                var source = heldBag ? bag : crate;
                int index = Find(source, held.Id);
                if (index < 0 || source[index].X != held.X || source[index].Y != held.Y || source[index].Rotated != held.Rotated)
                    CancelDrag();
            }
            if (!IsGearSlot(selectedGear) && selectedId != 0 && Find(sourceBag ? bag : crate, selectedId) < 0) selectedId = 0;
        }

        internal void SelectColumn(LootItem[] crate, LootItem[] bag, bool fromBag)
        {
            if (held.Id != 0) return;
            selectedGear = GearLocation.Bag;
            keyboardFocus = true;
            SelectFirst(crate, bag, fromBag);
        }

        private static int Order(LootItem item, bool bag) =>
            item.Y * (bag ? LootCatalog.BagWidth : LootCatalog.CrateWidth) + item.X;

        private void SelectFirst(LootItem[] crate, LootItem[] bag, bool fromBag)
        {
            sourceBag = fromBag;
            var items = fromBag ? bag : crate;
            selectedId = 0;
            int first = int.MaxValue;
            foreach (var item in items)
            {
                int order = Order(item, fromBag);
                if (order >= first) continue;
                first = order;
                selectedId = item.Id;
            }
        }

        internal void StepSelection(LootItem[] crate, LootItem[] bag, int step)
        {
            if (held.Id != 0) return;
            if (IsGearSlot(selectedGear)) { selectedId = 0; selectedGear = GearLocation.Bag; }
            EnsureSelection(crate, bag);
            if (!keyboardFocus && selectedId == 0) sourceBag = crate.Length == 0;
            var items = sourceBag ? bag : crate;
            keyboardFocus = true;
            if (items.Length == 0) return;
            int index = Find(items, selectedId);
            if (index < 0)
            {
                SelectFirst(crate, bag, sourceBag);
                if (step > 0) return;
                index = Find(items, selectedId);
            }
            int current = Order(items[index], sourceBag);
            int cells = sourceBag ? LootCatalog.BagWidth * LootCatalog.BagHeight : LootCatalog.CrateWidth * LootCatalog.CrateHeight;
            int nearest = int.MaxValue;
            foreach (var item in items)
            {
                int distance = ((Order(item, sourceBag) - current) * step + cells) % cells;
                if (distance == 0 || distance >= nearest) continue;
                nearest = distance;
                selectedId = item.Id;
            }
        }

        internal void TransferSelection(State state)
        {
            if (!keyboardFocus || held.Id != 0 || !state.Crate || IsGearSlot(selectedGear)) return;
            EnsureSelection(state.CrateItems, state.BagItems);
            if (selectedId != 0) QueueTransfer(sourceBag, selectedId);
        }

        internal bool CanDrop(State state, bool bag, int x, int y)
        {
            if (held.Id == 0 || (!state.Crate && !bag)) return false;
            if (IsGearSlot(heldGear))
                return CanMoveGear(state, bag ? GearLocation.Bag : GearLocation.Crate, x, y);
            return (state.Crate || heldBag) && LootCatalog.CanPlace(bag ? state.BagItems : state.CrateItems,
                held, bag, x, y, rotated, bag ? null : state.Unknown, GearCatalog.BagRows(state.Gear));
        }

        private bool HandlePointer(State state, Board board, Vector2 p, EventType type, bool ctrl, bool doubleClick)
        {
            ObservePointer(state, board, p, type == EventType.MouseDown || type == EventType.MouseMove);
            if (state.WorldLook || (type == EventType.MouseDown && !board.InPanel(p))) return false;
            EnsureSelection(state.CrateItems, state.BagItems);
            ValidateGearSelection(state);
            if (HandleGearPointer(state, board, p, type, ctrl || doubleClick)) return true;
            bool onGrid = board.Hit(p, out bool bag, out int x, out int y);
            if (type == EventType.MouseDown)
            {
                if (board.Close.Contains(p)) { CancelDrag(); close(); return true; }
                CancelDrag();
                selectedGear = GearLocation.Bag;
                selectedId = 0;
                if (!onGrid) return false;
                LootItem clicked = At(bag ? state.BagItems : state.CrateItems, x, y);
                if (clicked.Id == 0) return false;
                selectedId = clicked.Id;
                sourceBag = bag;
                if ((ctrl || doubleClick) && state.Crate)
                    QueueTransfer(bag, clicked.Id);
                else
                {
                    held = clicked;
                    heldBag = bag;
                    rotated = clicked.Rotated;
                    Rect grid = bag ? board.Bag : board.Crate;
                    grabToCenter = grid.position + new Vector2(clicked.X, clicked.Y) * Cell + HeldSize * 0.5f - p;
                    pressPoint = p;
                }
                return true;
            }
            if (held.Id == 0) return false;
            if (type == EventType.MouseDrag)
            {
                dragging |= (p - pressPoint).sqrMagnitude >= 9f;
                return true;
            }
            if (type == EventType.MouseUp)
            {
                if (dragging && Target(board, out bag, out int left, out int top) && CanDrop(state, bag, left, top))
                {
                    if (IsGearSlot(heldGear) || bag != heldBag || left != held.X || top != held.Y || rotated != held.Rotated)
                    {
                        if (IsGearSlot(heldGear))
                            MoveGear?.Invoke(held.Id, heldGear, bag ? GearLocation.Bag : GearLocation.Crate, left, top, rotated);
                        else place(held, heldBag, bag, left, top, rotated);
                        sourceBag = bag;
                    }
                }
                CancelDrag();
                return true;
            }
            return false;
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        internal bool ProbePointer(State state, bool bag, float column, float row, EventType type, bool ctrl)
        {
            var board = GetBoard(state, out _);
            Rect grid = bag ? board.Bag : board.Crate;
            scriptedPointer = true;
            var point = grid.position + new Vector2(column, row) * Cell;
            return HandlePointer(state, board, point, type, ctrl, false);
        }

        internal bool ProbeTargetCell(State state, bool bag, int x, int y, EventType type)
        {
            if (held.Id == 0) return false;
            Vector2 p = new Vector2(x, y) + (HeldSize * 0.5f - grabToCenter) / Cell;
            return ProbePointer(state, bag, p.x, p.y, type, false);
        }
#endif
        private Board GetBoard(State state, out float scale) => ScreenBoard(state.Crate, Opening, out scale);

        private static Board ScreenBoard(bool crate, float reveal, out float scale)
        {
            scale = Mathf.Min(Screen.width / 840f, Screen.height / 500f, 2.2f);
            return scale > 0 ? new Board(crate, Screen.width / scale, Screen.height / scale, reveal) :
                new Board(crate, 840, 500, reveal);
        }

        internal bool PointerOverInventory(State state)
        {
            Board board = GetBoard(state, out float scale);
            Vector3 mouse = Input.mousePosition;
            return scale > 0 && board.InPanel(new Vector2(mouse.x / scale, (Screen.height - mouse.y) / scale));
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        internal static void ProbeInventoryLayout(bool crate, out Rect panel, out Rect bag, out Rect chest, out float scale)
        {
            var board = ScreenBoard(crate, 1f, out scale);
            panel = board.Frame;
            bag = board.Bag;
            chest = crate ? board.Crate : Rect.zero;
        }

        internal static Rect ProbeInventoryArt(Rect grid, LootItem item) =>
            LootArt.RenderedBounds(ItemArtRect(TileRect(grid, item.X, item.Y, item, item.Rotated)),
                item.Type, item.Rotated, out _);
#endif

        private void DrawInventory(State state, float screenWidth, float screenHeight, float scale)
        {
            float opening = Opening;
            var b = new Board(state.Crate, screenWidth, screenHeight, opening);

            EnsureSelection(state.CrateItems, state.BagItems);
            var ev = Event.current;
            Vector3 screenPoint = Input.mousePosition;
            Vector2 actual = new Vector2(screenPoint.x / scale, (Screen.height - screenPoint.y) / scale);
            if (ev.type != EventType.Repaint)
            {
                if (!Application.isFocused) return;
                if (ev.type == EventType.MouseDown && ev.button == 1 && CancelDrag()) { ev.Use(); return; }
                if (ev.button == 0 && (ev.type == EventType.MouseDown || ev.type == EventType.MouseDrag || ev.type == EventType.MouseUp))
                {
                    scriptedPointer = false;
                    if (HandlePointer(state, b, actual, ev.type, ev.control, ev.clickCount >= 2)) ev.Use();
                }
                return;
            }
            if (!scriptedPointer)
                ObservePointer(state, b, actual, false);
            GUI.color = Fade(GUI.color, Mathf.Lerp(.12f, 1f, opening));
            if (state.Crate) DrawInventoryPanel(b.ChestPanel, "Ящик", Oxide);
            DrawInventoryPanel(b.BagPanel, "Инвентарь", DeepTeal);
            DrawInventoryPanel(b.EquipmentPanel, "Снаряжение", Umber);
            ValidateGearSelection(state);
            DrawGear(state, b);
            DrawContainer(state, b, true);
            if (state.Crate) DrawContainer(state, b, false);
            DrawClose(b.Close);
            DrawItemCard(state, b, screenWidth, screenHeight);
            if (dragging && held.Id != 0) DrawGhost(b, state);
        }

        private static readonly Color ManifestPaper = new Color32(230, 213, 176, 255);
        private static readonly Color LockedCell = new Color32(24, 35, 37, 255);
        private static readonly Color PrintInk = new Color32(29, 48, 52, 255);
        private static readonly Color Umber = new Color32(103, 78, 55, 255);
        private static readonly Color Oxide = new Color32(158, 73, 47, 255);
        private static readonly Color DeepTeal = new Color32(52, 89, 84, 255);
        private static Texture2D hatchTexture;
        private static Texture2D inkWashes;
        private static readonly Texture2D[] itemGradients = new Texture2D[4];
        private GUIStyle headline;

        // The paper occupies the first swatch of the original print texture.
        private static void DrawPaper(Rect rect)
        {
            if (inkWashes == null) inkWashes = Resources.Load<Texture2D>("InventoryInkWashes");
            if (inkWashes == null) { Fill(rect, ManifestPaper); return; }
            var uv = new Rect(2f / 768f, 258f / 512f, 252f / 768f, 252f / 512f);
            GUI.DrawTextureWithTexCoords(rect, inkWashes, uv, true);
        }

        private static void ItemWash(Rect rect, byte type)
        {
            int tier = (int)LootCatalog.Rarity(type);
            Color ink = RarityColor(type);
            if (itemGradients[tier] == null)
            {
                var gradient = new Texture2D(2, 2, TextureFormat.RGB24, false) {
                    wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
                gradient.SetPixels(new[] { Color.Lerp(EmptyCell, ink, .12f), EmptyCell,
                    Color.Lerp(EmptyCell, ink, .22f), Color.Lerp(EmptyCell, ink, .06f) });
                gradient.Apply(false, true);
                itemGradients[tier] = gradient;
            }
            GUI.DrawTextureWithTexCoords(rect, itemGradients[tier], new Rect(.25f, .25f, .5f, .5f), true);
            Block(rect.x + 1, rect.yMax - 2, Mathf.Min(20f, rect.width - 2), 1f, ink);
        }

        private void DrawInventoryPanel(Rect r, string title, Color ink)
        {
            Block(r.x + 2, r.y + 3, r.width, r.height, new Color32(13, 23, 23, 125));
            DrawPaper(r);
            Block(r.x, r.y, r.width, 2, ink);
            Block(r.x + Side, r.y + 30, r.width - 2 * Side, 1, Fade(ink, .54f));
            if (headline == null)
            {
                var font = Font.CreateDynamicFontFromOSFont(new[] { "Georgia", "Times New Roman" }, 24);
                headline = new GUIStyle(label) { font = font, fontStyle = FontStyle.Bold, fontSize = 17,
                    wordWrap = false, alignment = TextAnchor.MiddleLeft };
            }
            headline.normal.textColor = PrintInk;
            GUI.Label(new Rect(r.x + Side, r.y + 2, r.width - Side * 2, 27), title, headline);
        }

        private void DrawClose(Rect r)
        {
            bool hover = pointerActive && r.Contains(pointer);
            if (hover) Fill(r, Fade(Umber, .25f));
            var c = r.center;
            Stroke(c.x - 3.5f, c.y - 3.5f, c.x + 3.5f, c.y + 3.5f, 1.5f, PrintInk);
            Stroke(c.x + 3.5f, c.y - 3.5f, c.x - 3.5f, c.y + 3.5f, 1.5f, PrintInk);
        }

        private static void Hatch(Rect r)
        {
            if (hatchTexture == null)
            {
                const int size = 8;
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        pixels[y * size + x] = (x - y + size) % size < 2 ?
                            new Color32(66, 78, 76, 80) : new Color32(0, 0, 0, 0);
                hatchTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
                    filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave };
                hatchTexture.SetPixels32(pixels);
                hatchTexture.Apply(false, true);
            }
            GUI.DrawTextureWithTexCoords(r, hatchTexture, new Rect(0, 0, r.width / 8f, r.height / 8f), true);
        }

        private static void DrawLock(Vector2 center, Color color)
        {
            float x = Mathf.Round(center.x), y = Mathf.Round(center.y);
            Outline(new Rect(x - 3, y - 5, 6, 6), color, 1);
            Block(x - 4, y - 1, 8, 6, color);
            Block(x, y + 1, 1, 2, LockedCell);
        }

        private static void Fill(Rect r, Color color) => Block(r.x, r.y, r.width, r.height, color);

        private static Color RarityColor(byte type)
        {
            switch (LootCatalog.Rarity(type))
            {
                case LootRarity.Rare: return new Color32(139, 213, 225, 255);
                case LootRarity.Epic: return new Color32(226, 173, 220, 255);
                case LootRarity.Legendary: return new Color32(255, 207, 123, 255);
                default: return new Color32(201, 210, 183, 255);
            }
        }

        private bool HoveredFootprint(State state, Board board, out LootFootprint shape)
        {
            shape = default;
            if (!pointerActive || !state.Crate || !board.Hit(pointer, out bool bag, out int x, out int y) || bag) return false;
            foreach (var f in state.Unknown)
                if (x >= f.X && x < f.X + f.Width && y >= f.Y && y < f.Y + f.Height)
                {
                    shape = f;
                    return true;
                }
            return false;
        }

        private LootItem FocusedItem(State state, Board board, out bool bag)
        {
            if (!keyboardFocus) return HoveredItem(state, board, out bag);
            bag = sourceBag;
            var items = bag ? state.BagItems : state.CrateItems;
            int index = Find(items, selectedId);
            return index < 0 ? default : items[index];
        }

        private void DrawContainer(State state, Board board, bool bag)
        {
            Rect grid = bag ? board.Bag : board.Crate;
            Fill(grid, new Color32(57, 70, 70, 255));
            int cols = bag ? LootCatalog.BagWidth : LootCatalog.CrateWidth;
            int rows = bag ? LootCatalog.BagHeight : LootCatalog.CrateHeight;
            int usableRows = bag ? GearCatalog.BagRows(state.Gear) : rows;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                {
                    var cell = new Rect(grid.x + x * Cell + 1, grid.y + y * Cell + 1, Cell - 2, Cell - 2);
                    bool locked = y >= usableRows;
                    if (locked || !bag) Fill(cell, LockedCell);
                    else Fill(cell, EmptyCell);
                    if (locked || !bag) Hatch(new Rect(cell.x + 1, cell.y + 1, cell.width - 2, cell.height - 2));
                    if (locked) DrawLock(cell.center, GridLine);
                }
            if (!bag)
            {
                bool hover = !dragging && !keyboardFocus && HoveredFootprint(state, board, out _);
                float search = Mathf.Clamp01(1f - (state.SearchFinishAt - Time.unscaledTime) / LootCatalog.SearchItemSeconds);
                for (int i = 0; i < state.Unknown.Length; i++)
                    DrawFootprint(grid, state.Unknown[i], i == 0, search, hover);
            }

            var items = bag ? state.BagItems : state.CrateItems;
            LootItem focus = FocusedItem(state, board, out bool focusBag);
            foreach (var item in items)
            {
                bool picked = held.Id == item.Id && heldBag == bag && dragging;
                bool selected = !dragging && focus.Id == item.Id && focusBag == bag;
                var rect = TileRect(grid, item.X, item.Y, item, item.Rotated);
                Color rarity = RarityColor(item.Type);
                if (picked)
                {
                    Outline(rect, GridLine, 1);
                    Corners(rect, Color.Lerp(GridLine, bag ? Mint : Amber, .35f), 5, 1);
                    continue;
                }
                ItemWash(rect, item.Type);
                if (selected) Outline(rect, rarity, 1);
                LootArt.Draw(ItemArtRect(rect), item.Type, item.Rotated);
                if (selected) Corners(rect, sweepHeld ? Mint : Paper, 4, 1);
            }
        }

        private static Rect ItemArtRect(Rect tile) =>
            new Rect(tile.x + 2, tile.y + 1, tile.width - 4, tile.height - 4);

        private void DrawFootprint(Rect grid, LootFootprint shape, bool active, float progress, bool allowHover)
        {
            var r = new Rect(grid.x + shape.X * Cell + 1, grid.y + shape.Y * Cell + 1,
                shape.Width * Cell - 3, shape.Height * Cell - 3);
            Fill(r, Unsearched);
            Outline(r, Color.Lerp(GridLine, FieldLabel, .32f), 1);
            Color seam = active ? Color.Lerp(FieldLabel, Amber, .6f) : Color.Lerp(GridLine, FieldLabel, .35f);
            Corners(r, active ? Color.Lerp(GridLine, Amber, .6f) : GridLine, 4, 1);

            // An unmarked package opens along its longest side. Only the server
            // supplies the contents; a late snapshot leaves the seal open and still.
            bool horizontal = r.width >= r.height;
            float extent = (horizontal ? r.width : r.height) * .5f - 3f;
            float gap = active ? Mathf.Lerp(1f, extent - 1f, Mathf.SmoothStep(0f, 1f, progress)) : 1f;
            for (float d = gap; d < extent; d += 5f)
            {
                float dash = Mathf.Min(3f, extent - d);
                if (horizontal)
                {
                    Block(r.center.x + d, r.center.y, dash, 1, seam);
                    Block(r.center.x - d - dash, r.center.y, dash, 1, seam);
                }
                else
                {
                    Block(r.center.x, r.center.y + d, 1, dash, seam);
                    Block(r.center.x, r.center.y - d - dash, 1, dash, seam);
                }
            }
            if (active)
            {
                // A finite trace follows the actual perimeter once per search.
                float length = progress * 2f * (r.width + r.height);
                float top = Mathf.Min(length, r.width);
                if (top > 0) Block(r.x, r.y, top, 1, Amber);
                float right = Mathf.Clamp(length - r.width, 0, r.height);
                if (right > 0) Block(r.xMax - 1, r.y, 1, right, Amber);
                float bottom = Mathf.Clamp(length - r.width - r.height, 0, r.width);
                if (bottom > 0) Block(r.xMax - bottom, r.yMax - 1, bottom, 1, Amber);
                float left = Mathf.Clamp(length - 2 * r.width - r.height, 0, r.height);
                if (left > 0) Block(r.x, r.yMax - left, 1, left, Amber);
            }
            if (allowHover && r.Contains(pointer)) Corners(r, Paper, 4, 1);
        }

        private static Rect TileRect(Rect grid, int x, int y, LootItem item, bool rotation) =>
            new Rect(grid.x + x * Cell + 1, grid.y + y * Cell + 1,
                LootCatalog.Width(item.Type, rotation) * Cell - 2, LootCatalog.Height(item.Type, rotation) * Cell - 2);

        private static void Outline(Rect r, Color col, float weight)
        {
            Block(r.x, r.y, r.width, weight, col);
            Block(r.x, r.yMax - weight, r.width, weight, col);
            Block(r.x, r.y, weight, r.height, col);
            Block(r.xMax - weight, r.y, weight, r.height, col);
        }

        private static void Corners(Rect r, Color color, float length, float weight)
        {
            Block(r.x, r.y, length, weight, color);
            Block(r.x, r.y, weight, length, color);
            Block(r.xMax - length, r.y, length, weight, color);
            Block(r.xMax - weight, r.y, weight, length, color);
            Block(r.x, r.yMax - weight, length, weight, color);
            Block(r.x, r.yMax - length, weight, length, color);
            Block(r.xMax - length, r.yMax - weight, length, weight, color);
            Block(r.xMax - weight, r.yMax - length, weight, length, color);
        }

        private void DrawGhost(Board board, State state)
        {
            if (Target(board, out bool bag, out int x, out int y))
            {
                Rect grid = bag ? board.Bag : board.Crate;
                Rect placement = TileRect(grid, x, y, held, rotated);
                Rect r = Rect.MinMaxRect(Mathf.Max(grid.x, placement.x), Mathf.Max(grid.y, placement.y),
                    Mathf.Min(grid.xMax, placement.xMax), Mathf.Min(grid.yMax, placement.yMax));
                bool valid = CanDrop(state, bag, x, y);
                Outline(r, valid ? Mint : Bad, 1);
                Corners(r, valid ? Mint : Bad, 5, 2);

                if (!valid)
                {
                    Block(r.x + 4, r.y + 4, 14, 14, Surface);
                    Stroke(r.x + 7, r.y + 7, r.x + 15, r.y + 15, 2, Bad);
                    Stroke(r.x + 15, r.y + 7, r.x + 7, r.y + 15, 2, Bad);
                }
            }
            Vector2 size = HeldSize;
            Rect tile = new Rect(GhostCenter - size * .5f + Vector2.one, size - Vector2.one * 3);
            LootArt.Draw(ItemArtRect(tile), held.Type, rotated);
        }
    }
}
