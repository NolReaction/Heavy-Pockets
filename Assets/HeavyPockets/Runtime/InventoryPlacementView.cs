using System;
using System.Collections.Generic;

namespace HeavyPockets
{
    // Keep received snapshots intact. The UI overlays outstanding drops in command order.
    internal sealed class InventoryPlacementView
    {
        private sealed class Placement
        {
            public uint Operation;
            public LootItem Source, Target;
            public bool FromBag, ToBag, Accepted, AwaitBag, AwaitCrate;
        }

        private readonly List<Placement> pending = new List<Placement>();
        private LootItem[] serverBag = Array.Empty<LootItem>(), serverCrate = Array.Empty<LootItem>();
        internal LootItem[] ConfirmedBag => serverBag;
        internal LootItem[] ConfirmedCrate => serverCrate;
        public LootItem[] Bag { get; private set; } = Array.Empty<LootItem>();
        public LootItem[] Crate { get; private set; } = Array.Empty<LootItem>();
        public LootFootprint[] Unknown { get; private set; } = Array.Empty<LootFootprint>();

        public void Place(uint operation, LootItem item, bool fromBag, bool toBag, int x, int y, bool rotated)
        {
            if (operation == 0) return;
            var target = item;
            target.X = (byte)x;
            target.Y = (byte)y;
            target.Rotated = rotated;
            pending.Add(new Placement
            {
                Operation = operation, Source = item, Target = target, FromBag = fromBag, ToBag = toBag,
                AwaitBag = fromBag || toBag, AwaitCrate = !fromBag || !toBag
            });
            Rebuild();
        }

        public void Acknowledge(uint operation, RaidStatus status)
        {
            if (operation == 0) return;
            var move = pending.Find(p => p.Operation == operation);
            if (move == null) return;
            move.Accepted = status == RaidStatus.Placed || status == RaidStatus.Taken || status == RaidStatus.Stored;
            if (!move.Accepted) pending.Remove(move);
            Rebuild();
        }

        public void Snapshot(bool bag, LootItem[] items, LootFootprint[] unknown = null)
        {
            if (bag) serverBag = items;
            else
            {
                serverCrate = items;
                Unknown = unknown ?? Array.Empty<LootFootprint>();
            }
            // The server sends the operation result first, then one snapshot per affected board
            // on the same ordered channel. Prior snapshots cannot complete an outstanding drop.
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var move = pending[i];
                if (!move.Accepted) continue;
                if (bag) move.AwaitBag = false;
                else move.AwaitCrate = false;
                if (!move.AwaitBag && !move.AwaitCrate) pending.RemoveAt(i);
            }
            Rebuild();
        }

        public void Close(bool sessionEnded)
        {
            serverCrate = Array.Empty<LootItem>();
            Unknown = Array.Empty<LootFootprint>();
            if (sessionEnded) pending.Clear();
            else pending.RemoveAll(p => !p.FromBag || !p.ToBag);
            Rebuild();
        }

        private void Rebuild()
        {
            Bag = serverBag;
            Crate = serverCrate;
            if (pending.Count == 0) return;
            var bag = new List<LootItem>(serverBag);
            var crate = new List<LootItem>(serverCrate);
            foreach (var move in pending)
            {
                var source = move.FromBag ? bag : crate;
                var target = move.ToBag ? bag : crate;
                var item = move.Target;
                // A competing snapshot can remove the item or occupy the destination before a reply.
                if (!move.Accepted && !source.Exists(i => i.Id == item.Id && i.X == move.Source.X &&
                    i.Y == move.Source.Y && i.Rotated == move.Source.Rotated)) continue;
                if (!LootCatalog.CanPlace(target.ToArray(), item, move.ToBag, item.X, item.Y, item.Rotated, Unknown)) continue;
                bag.RemoveAll(i => i.Id == item.Id);
                crate.RemoveAll(i => i.Id == item.Id);
                target.Add(item);
            }
            Bag = bag.ToArray();
            Crate = crate.ToArray();
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        internal bool IsPending(uint id) => pending.Exists(p => p.Target.Id == id);
#endif
    }
}
