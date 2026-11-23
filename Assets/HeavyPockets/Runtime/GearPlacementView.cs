using System;
using System.Collections.Generic;

namespace HeavyPockets
{
    // Commands share one reliable channel with the receipt and snapshots. Keep each
    // requested placement visible until its own failure or its final equipment snapshot.
    internal sealed class GearPlacementView
    {
        private sealed class Move
        {
            internal uint Operation;
            internal GearLocation From, To;
            internal LootItem Target, Displaced;
            internal bool Accepted;
        }

        private readonly List<Move> moves = new List<Move>();
        internal int Count => moves.Count;
        internal bool IsPending(uint id) => id != 0 && moves.Exists(move => move.Target.Id == id || move.Displaced.Id == id);
        internal void Reset() => moves.Clear();
        internal void Add(uint op, GearLocation from, GearLocation to, LootItem target, LootItem displaced)
        {
            if (op != 0) moves.Add(new Move { Operation = op, From = from, To = to, Target = target, Displaced = displaced });
        }
        internal void Reply(uint op, RaidStatus status)
        {
            var move = moves.Find(m => m.Operation == op);
            if (move == null) return;
            if (status == RaidStatus.Placed) move.Accepted = true;
            else moves.Remove(move);
        }
        internal void ConfirmGear() => moves.RemoveAll(move => move.Accepted);

        internal void Project(ref LootItem[] bag, ref LootItem[] crate, ref LootItem[] gear, LootFootprint[] outlines, bool viewing)
        {
            if (moves.Count == 0) return;
            gear = (LootItem[])gear.Clone();
            foreach (var move in moves)
            {
                bool slotFrom = GearCatalog.IsSlot(move.From), slotTo = GearCatalog.IsSlot(move.To);
                var item = move.Target;
                var displaced = move.Displaced;
                if (!move.Accepted && (move.From != GearLocation.Crate || viewing) && (move.To != GearLocation.Crate || viewing))
                {
                    var source = slotFrom ? gear[(int)move.From] :
                        Array.Find(move.From == GearLocation.Bag ? bag : crate, entry => entry.Id == move.Target.Id);
                    if (source.Id != item.Id || GearCatalog.PlanMove(source, move.From, move.To,
                        slotTo ? -1 : item.X, slotTo ? -1 : item.Y, item.Rotated, bag, crate, gear, outlines,
                        out item, out displaced) != RaidStatus.Placed) continue;
                }
                if (slotFrom) gear[(int)move.From] = displaced;
                if (slotTo) gear[(int)move.To] = item;
                if (move.From == GearLocation.Bag || move.To == GearLocation.Bag)
                    bag = UpdateBoard(bag, item.Id, displaced.Id, move.To == GearLocation.Bag ? item : displaced);
                if (viewing && (move.From == GearLocation.Crate || move.To == GearLocation.Crate))
                    crate = UpdateBoard(crate, item.Id, displaced.Id, move.To == GearLocation.Crate ? item : displaced);
            }
        }

        private static LootItem[] UpdateBoard(LootItem[] board, uint moving, uint replacing, LootItem received)
        {
            var items = new List<LootItem>(board.Length + 1);
            foreach (var item in board)
                if (item.Id != moving && item.Id != replacing) items.Add(item);
            if (received.Id != 0) items.Add(received);
            return items.ToArray();
        }
    }
}
