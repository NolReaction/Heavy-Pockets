using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    internal sealed partial class RaidHud
    {
        private struct QuickItem
        {
            public uint Id;
            public bool Bag;
        }

        private struct Crossing
        {
            public QuickItem Item;
            public float Distance;
        }

        private const float TransferInterval = 0.18f; // The server replenishes six actions per second.
        private readonly Queue<QuickItem> transfers = new Queue<QuickItem>();
        private readonly HashSet<uint> swept = new HashSet<uint>();
        private readonly List<Crossing> crossings = new List<Crossing>();
        private QuickItem activeTransfer;
        private uint activeOperation, hoverId;
        private bool sourceSnapshotDue, sweepHeld, pointerInitialized, pointerActive, wasLooking;
        private float nextTransferTime;
        private Vector2 sweepPoint;

        private static LootItem UnderPointer(State state, Board board, Vector2 point, out bool bag)
        {
            return board.Hit(point, out bag, out int x, out int y) ?
                At(bag ? state.BagItems : state.CrateItems, x, y) : default;
        }

        private LootItem HoveredItem(State state, Board board, out bool bag)
        {
            LootItem item = UnderPointer(state, board, pointer, out bag);
            return pointerActive && item.Id == hoverId ? item : default;
        }

        private void ObservePointer(State state, Board board, Vector2 point, bool deliberate)
        {
            bool moved = pointerInitialized && point != pointer;
            pointerInitialized = true;
            pointer = point;
            bool releasedLook = wasLooking && !state.WorldLook;
            wasLooking = state.WorldLook;
            if (state.WorldLook)
            {
                pointerActive = false;
                hoverId = 0;
                return;
            }
            if (!board.InPanel(point))
            {
                pointerActive = false;
                hoverId = 0;
                return;
            }
            if (!deliberate && !moved && !releasedLook) return;
            LootItem under = UnderPointer(state, board, point, out _);
            if (under.Id == 0 && GearHit(board, point, out var slot)) under = GearItem(state, GearCatalog.PouchOwner(slot, state.Gear));
            if (releasedLook && !deliberate && keyboardFocus && under.Id == 0) return;
            keyboardFocus = false;
            pointerActive = true;
            hoverId = under.Id;
        }

        internal void PhysicalTransferInput(State state, bool pressed, bool down)
        {
            var board = GetBoard(state, out float scale);
            if (scale <= 0f) return;
            Vector3 mouse = Input.mousePosition;
            var point = new Vector2(mouse.x / scale, (Screen.height - mouse.y) / scale);
            ObservePointer(state, board, point, pressed);
            TransferInput(state, board, point, pressed, down);
        }

        private void TransferInput(State state, Board board, Vector2 point, bool pressed, bool down)
        {
            if (!state.Crate || state.WorldLook || held.Id != 0 || (!pressed && !down))
            {
                sweepHeld = false;
                swept.Clear();
                return;
            }
            if (pressed)
            {
                sweepHeld = down;
                swept.Clear();
                sweepPoint = point;
                var item = UnderPointer(state, board, point, out bool bag);
                if (item.Id != 0) Sweep(bag, item.Id);
            }
            else if (sweepHeld && point != sweepPoint)
            {
                // Fast mouse movement may cross several footprints between two rendered frames.
                crossings.Clear();
                CollectCrossings(state.BagItems, board.Bag, true, point);
                CollectCrossings(state.CrateItems, board.Crate, false, point);
                crossings.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                foreach (var hit in crossings) Sweep(hit.Item.Bag, hit.Item.Id);
                sweepPoint = point;
            }
        }

        private void CollectCrossings(LootItem[] items, Rect grid, bool bag, Vector2 point)
        {
            Vector2 delta = point - sweepPoint;
            foreach (var item in items)
            {
                if (swept.Contains(item.Id)) continue;
                var rect = new Rect(grid.x + item.X * Cell, grid.y + item.Y * Cell,
                    LootCatalog.Width(item.Type, item.Rotated) * Cell, LootCatalog.Height(item.Type, item.Rotated) * Cell);
                float enter = 0f, exit = 1f;
                if (CrossAxis(sweepPoint.x, delta.x, rect.xMin, rect.xMax, ref enter, ref exit) &&
                    CrossAxis(sweepPoint.y, delta.y, rect.yMin, rect.yMax, ref enter, ref exit) &&
                    (enter < exit || rect.Contains(point)))
                    crossings.Add(new Crossing { Item = new QuickItem { Bag = bag, Id = item.Id }, Distance = enter });
            }
        }

        private static bool CrossAxis(float start, float delta, float min, float max, ref float enter, ref float exit)
        {
            if (delta == 0f) return start >= min && start < max;
            float a = (min - start) / delta, b = (max - start) / delta;
            enter = Mathf.Max(enter, Mathf.Min(a, b));
            exit = Mathf.Min(exit, Mathf.Max(a, b));
            return enter <= exit;
        }

        private void Sweep(bool bag, uint id)
        {
            if (swept.Add(id)) QueueTransfer(bag, id);
        }

        private void QueueTransfer(bool bag, uint id)
        {
            if (activeOperation != 0 && activeTransfer.Id == id && (activeTransfer.Bag == bag || !sourceSnapshotDue)) return;
            foreach (var queued in transfers)
                if (queued.Id == id) return;
            transfers.Enqueue(new QuickItem { Id = id, Bag = bag });
        }

        internal void PumpTransfers(State state, float now)
        {
            if (!state.Crate || activeOperation != 0 || now < nextTransferTime) return;
            while (transfers.Count > 0)
            {
                var next = transfers.Dequeue();
                if (Find(next.Bag ? state.BagItems : state.CrateItems, next.Id) < 0) continue;
                activeOperation = quickTransfer(next.Bag, next.Id);
                if (activeOperation == 0) continue;
                activeTransfer = next;
                sourceSnapshotDue = false;
                nextTransferTime = now + TransferInterval;
                return;
            }
        }

        internal void TransferReply(uint operation, RaidStatus status)
        {
            if (operation == 0 || activeOperation != operation) return;
            sourceSnapshotDue = status == RaidStatus.Stored || status == RaidStatus.Taken;
            if (!sourceSnapshotDue) activeOperation = 0;
        }

        internal void TransferSnapshot(bool bag)
        {
            if (sourceSnapshotDue && activeTransfer.Bag == bag)
            {
                sourceSnapshotDue = false;
                activeOperation = 0;
            }
        }

        internal void CancelQuickTransfers(bool discardActive = false)
        {
            transfers.Clear();
            swept.Clear();
            sweepHeld = false;
            if (discardActive)
            {
                sourceSnapshotDue = false;
                activeOperation = 0;
            }
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        internal uint ProbeSelectedItem(State state)
        {
            EnsureSelection(state.CrateItems, state.BagItems);
            return selectedId;
        }

        internal uint ProbeHoveredItem(State state) => HoveredItem(state, GetBoard(state, out _), out _).Id;

        internal void ProbeHover(State state, bool bag, float column, float row)
        {
            var board = GetBoard(state, out _);
            scriptedPointer = true;
            Vector2 point = (bag ? board.Bag : board.Crate).position + new Vector2(column, row) * Cell;
            ObservePointer(state, board, point, true);
            TransferInput(state, board, point, false, sweepHeld);
        }

        internal void ProbeTransferKey(State state, bool down)
        {
            var board = GetBoard(state, out _);
            bool pressed = down && !sweepHeld;
            ObservePointer(state, board, pointer, pressed);
            TransferInput(state, board, pointer, pressed, down);
        }
#endif
    }
}
