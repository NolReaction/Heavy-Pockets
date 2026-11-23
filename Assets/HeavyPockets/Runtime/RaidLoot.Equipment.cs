#if UNITY_SERVER || UNITY_EDITOR || HEAVY_POCKETS_SERVER
using System.Collections.Generic;
using System.Security.Cryptography;

namespace HeavyPockets
{
    internal sealed partial class RaidLoot
    {
        private uint NextId(RandomNumberGenerator rng, byte[] scratch)
        {
            uint id;
            do
            {
                rng.GetBytes(scratch);
                id = System.BitConverter.ToUInt32(scratch, 0);
            } while (id == 0 || !issued.Add(id));
            return id;
        }

        internal void IssueKit(ulong player, byte pouchLevel)
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] scratch = new byte[4];
                void Issue(GearLocation where, byte type) => slots.Add(new Slot
                {
                    Owner = player, Location = where,
                    Item = new LootItem { Id = NextId(rng, scratch), Type = type }
                });
                Issue(GearLocation.Primary, GearCatalog.Carbine);
                Issue(GearLocation.Secondary, GearCatalog.Pistol);
                Issue(GearLocation.Armor, GearCatalog.Armor);
                Issue(GearLocation.Backpack, GearCatalog.FieldPack);
                Issue(GearLocation.Pouch, (byte)(GearCatalog.FirstPouch + pouchLevel - 1));
            }
        }

        internal LootItem[] Gear(ulong player)
        {
            var result = new LootItem[GearCatalog.Slots];
            foreach (var slot in slots)
                if (slot.Owner == player && GearCatalog.IsSlot(slot.Location)) result[(int)slot.Location] = slot.Item;
            return result;
        }

        internal LootItem[] ExtractedLoot(ulong player)
        {
            var result = new List<LootItem>();
            foreach (var slot in slots)
                if (slot.Owner == player && crateLoot.Contains(slot.Item.Id))
                    result.Add(slot.Item);
            return result.ToArray();
        }

        internal bool HasGear(ulong player, GearLocation where) =>
            slots.Exists(s => s.Owner == player && s.Location == where);

        internal RaidStatus MoveGear(ulong player, HashSet<uint> identified, uint id,
            GearLocation from, GearLocation to, int x, int y, bool rotated)
        {
            ulong? owner = from == GearLocation.Crate ? (ulong?)null : player;
            int index = slots.FindIndex(s => s.Item.Id == id && s.Location == from && s.Owner == owner);
            if (index < 0 || (from == GearLocation.Crate && !identified.Contains(id)))
                return RaidStatus.Denied;
            var status = GearCatalog.PlanMove(slots[index].Item, from, to, x, y, rotated, Bag(player), Contents(null),
                Gear(player), null, out var item, out var displaced);
            if (status != RaidStatus.Placed) return status;

            ulong? newOwner = to == GearLocation.Crate ? (ulong?)null : player;
            if (displaced.Id != 0)
            {
                int displacedIndex = slots.FindIndex(s => s.Item.Id == displaced.Id);
                slots[displacedIndex] = new Slot { Item = displaced, Owner = owner, Location = from };
                if (from == GearLocation.Crate) identified.Add(displaced.Id);
            }
            slots[index] = new Slot { Item = item, Owner = newOwner, Location = to };
            if (to == GearLocation.Crate) identified.Add(id);
            return RaidStatus.Placed;
        }
    }
}
#endif
