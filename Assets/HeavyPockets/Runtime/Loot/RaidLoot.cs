#if UNITY_SERVER || UNITY_EDITOR || HEAVY_POCKETS_SERVER
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace HeavyPockets
{
    // Per player. The timer belongs to a concrete item, even if another player
    // changes the remaining crate layout between two server ticks.
    internal sealed class LootSearch
    {
        internal readonly HashSet<uint> Identified = new HashSet<uint>();
        private uint target;
        private float elapsed;

        internal float Remaining(RaidLoot loot)
        {
            uint next = loot.NextUnknown(Identified);
            if (next != target) { target = next; elapsed = 0f; }
            return target == 0 ? 0f : LootCatalog.SearchItemSeconds - elapsed;
        }

        internal bool Advance(RaidLoot loot, float delta)
        {
            if (Remaining(loot) <= 0f) return false;
            elapsed += delta;
            if (elapsed < LootCatalog.SearchItemSeconds) return false;
            Identified.Add(target);
            target = 0;
            elapsed = 0f;
            return true;
        }
    }

    internal sealed partial class RaidLoot
    {
        private struct Slot
        {
            public LootItem Item;
            public ulong? Owner; // null denotes the shared crate.
            public GearLocation Location;
        }

        private readonly List<Slot> slots = new List<Slot>();
        private readonly HashSet<uint> issued = new HashSet<uint>();
        private readonly HashSet<uint> crateLoot = new HashSet<uint>();
        internal bool CrateGenerated { get; private set; }

        internal void GenerateCrate()
        {
            if (CrateGenerated) return;
            using (var rng = RandomNumberGenerator.Create())
            {
                var scratch = new byte[4];
                var types = new byte[LootCatalog.RandomTypeCount];
                for (int i = 0; i < types.Length; i++) types[i] = (byte)(i + 1);
                // Reserve space in the test crate for both packs, four pouches and one piece of equipment.
                int count = 4 + RandomIndex(rng, scratch, 2);
                var cargo = new List<byte>(count + 7);
                for (int i = 0; i < count; i++)
                {
                    int selected = i + RandomIndex(rng, scratch, types.Length - i);
                    cargo.Add(types[selected]);
                    types[selected] = types[i];
                }
                cargo.Add(GearCatalog.FieldPack);
                cargo.Add(GearCatalog.ExpeditionPack);
                for (byte level = 0; level < 4; level++)
                    cargo.Add((byte)(GearCatalog.FirstPouch + level));
                cargo.Add((byte)(GearCatalog.Carbine + RandomIndex(rng, scratch, 3)));
                var placed = new LootItem[cargo.Count];
                int area = 0;
                for (int i = 0; i < cargo.Count; i++)
                {
                    placed[i] = new LootItem { Id = NextId(rng, scratch), Type = cargo[i] };
                    area += LootCatalog.Width(cargo[i], false) * LootCatalog.Height(cargo[i], false);
                }
                // Try a completely filled prefix first. Increase it only when the
                // footprints cannot tile that shape (for example, two large squares).
                int end = area;
                while (end <= LootCatalog.CrateCells && !PackCrate(placed, 0, 0, 0, end, end - area)) end++;
                if (end > LootCatalog.CrateCells) throw new InvalidOperationException("Crate loot exceeds its grid");
                Array.Sort(placed, (a, b) => Anchor(a).CompareTo(Anchor(b)));
                foreach (var item in placed)
                {
                    crateLoot.Add(item.Id);
                    slots.Add(new Slot { Item = item, Location = GearLocation.Crate });
                }
                CrateGenerated = true;
            }
        }

        // The 6x6 board fits in one bitmap, so each trial can use its own occupancy
        // without cloning a grid or modifying the authoritative item ledger.
        private static bool PackCrate(LootItem[] items, int placed, ulong occupied, int cell, int end, int gaps)
        {
            if (placed == items.Length) return true;
            while (cell < end && (occupied & (1UL << cell)) != 0) cell++;
            if (cell >= end) return false;
            int x = cell % LootCatalog.CrateWidth, y = cell / LootCatalog.CrateWidth;
            ulong shapesTried = 0;
            for (int i = placed; i < items.Length; i++)
            {
                var item = items[i];
                for (int turn = 0; turn <= 1; turn++)
                {
                    item.Rotated = turn != 0;
                    int w = LootCatalog.Width(item.Type, item.Rotated), h = LootCatalog.Height(item.Type, item.Rotated);
                    // Equal footprints are interchangeable regardless of type or instance ID.
                    ulong shape = 1UL << ((w - 1) * LootCatalog.CrateHeight + h - 1);
                    if ((shapesTried & shape) != 0) continue;
                    shapesTried |= shape;
                    if (x + w > LootCatalog.CrateWidth || cell + (h - 1) * LootCatalog.CrateWidth + w > end) continue;
                    ulong row = ((1UL << w) - 1) << cell;
                    ulong footprint = row;
                    for (int dy = 1; dy < h; dy++) footprint |= row << (dy * LootCatalog.CrateWidth);
                    if ((occupied & footprint) != 0) continue;
                    var previous = items[placed];
                    items[i] = previous;
                    item.X = (byte)x;
                    item.Y = (byte)y;
                    items[placed] = item;
                    if (PackCrate(items, placed + 1, occupied | footprint, cell + 1, end, gaps)) return true;
                    items[placed] = previous;
                    items[i] = item;
                }
            }
            return gaps > 0 && PackCrate(items, placed, occupied, cell + 1, end, gaps - 1);
        }

        private static int RandomIndex(RandomNumberGenerator rng, byte[] scratch, int bound)
        {
            uint limit = uint.MaxValue - uint.MaxValue % (uint)bound;
            uint sample;
            do
            {
                rng.GetBytes(scratch);
                sample = BitConverter.ToUInt32(scratch, 0);
            } while (sample >= limit);
            return (int)(sample % (uint)bound);
        }

        private static int Anchor(LootItem item) => item.Y * LootCatalog.CrateWidth + item.X;

        internal uint NextUnknown(HashSet<uint> identified)
        {
            int earliest = int.MaxValue;
            uint id = 0;
            foreach (var slot in slots)
            {
                if (slot.Owner.HasValue || identified.Contains(slot.Item.Id)) continue;
                int anchor = Anchor(slot.Item);
                if (anchor >= earliest) continue;
                earliest = anchor;
                id = slot.Item.Id;
            }
            return id;
        }

        internal void CrateView(HashSet<uint> identified, out LootItem[] known, out LootFootprint[] outlines)
        {
            var items = new List<LootItem>();
            var hidden = new List<LootFootprint>();
            foreach (var slot in slots)
            {
                if (slot.Owner.HasValue) continue;
                if (identified.Contains(slot.Item.Id)) items.Add(slot.Item);
                else hidden.Add(LootFootprint.Of(slot.Item));
            }
            hidden.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
            known = items.ToArray();
            outlines = hidden.ToArray();
        }

        private LootItem[] Contents(ulong? owner)
        {
            var contents = new List<LootItem>();
            foreach (var slot in slots)
                if (slot.Owner == owner && slot.Location == (owner.HasValue ? GearLocation.Bag : GearLocation.Crate)) contents.Add(slot.Item);
            return contents.ToArray();
        }

        internal LootItem[] Bag(ulong player) => Contents(player);

        internal RaidStatus Transfer(ulong player, HashSet<uint> identified, uint itemId, bool fromBag, bool toBag,
            bool automatic, int x, int y, bool rotated, int originalX, int originalY, bool originalRotated)
        {
            int index = slots.FindIndex(slot => slot.Item.Id == itemId);
            ulong? source = fromBag ? player : (ulong?)null;
            if (index < 0 || slots[index].Owner != source || slots[index].Location != (fromBag ? GearLocation.Bag : GearLocation.Crate) || (!fromBag && !identified.Contains(itemId)))
                return fromBag ? RaidStatus.Denied : RaidStatus.Empty;

            var item = slots[index].Item;
            if (!automatic && (originalX != item.X || originalY != item.Y || originalRotated != item.Rotated))
                return RaidStatus.Stale;
            ulong? destination = toBag ? player : (ulong?)null;
            int rows = toBag ? GearCatalog.BagRows(Gear(player)) : LootCatalog.BagHeight;
            LootItem[] occupants = Contents(destination);
            if (automatic)
            {
                if (!LootCatalog.FindSpace(occupants, item, toBag, out x, out y, out rotated, null, rows))
                    return toBag ? RaidStatus.BagFull : RaidStatus.NoSpace;
            }
            else if (!LootCatalog.CanPlace(occupants, item, toBag, x, y, rotated, null, rows))
                return RaidStatus.NoSpace;

            item.X = (byte)x;
            item.Y = (byte)y;
            item.Rotated = rotated;
            slots[index] = new Slot { Item = item, Owner = destination, Location = toBag ? GearLocation.Bag : GearLocation.Crate };
            if (!toBag) identified.Add(item.Id);
            return fromBag == toBag ? RaidStatus.Placed : toBag ? RaidStatus.Taken : RaidStatus.Stored;
        }


    }
}
#endif
