namespace HeavyPockets
{
    public enum GearLocation : byte
    {
        Bag, Primary, Secondary, Armor, Backpack, Pouch, PouchCell, PouchCell2, PouchCell3, PouchCell4, Crate
    }

    public static class GearCatalog
    {
        public const int Slots = (int)GearLocation.Crate;
        public const byte Carbine = 12, Pistol = 13, Armor = 14, FieldPack = 15, FirstPouch = 16, ExpeditionPack = 20;
        private static readonly string[] Names = { "Инвентарь", "Основное оружие", "Запасное оружие", "Броня",
            "Рюкзак", "Подсумок", "Подсумок · 1", "Подсумок · 2", "Подсумок · 3", "Подсумок · 4", "Ящик" };

        public static string Name(GearLocation slot) => (byte)slot < Names.Length ? Names[(int)slot] : "";
        public static bool IsSlot(GearLocation slot) => slot >= GearLocation.Primary && slot < GearLocation.Crate;
        public static bool IsPouchCell(GearLocation slot) => slot >= GearLocation.PouchCell && slot <= GearLocation.PouchCell4;
        public const int PouchWidth = 2;

        // A pouch item lives at its top-left cell. The other covered cells reference
        // that same instance through its footprint, including when a quick key is used.
        public static GearLocation PouchOwner(GearLocation cell, LootItem[] gear)
        {
            if (!IsPouchCell(cell) || gear == null || gear.Length != Slots) return cell;
            int bit = 1 << (cell - GearLocation.PouchCell);
            for (var slot = GearLocation.PouchCell; slot <= GearLocation.PouchCell4; slot++)
                if (gear[(int)slot].Id != 0 && (PouchMask(slot, gear[(int)slot]) & bit) != 0) return slot;
            return cell;
        }

        private static int PouchMask(GearLocation slot, LootItem item)
        {
            int index = slot - GearLocation.PouchCell;
            int x = index % PouchWidth, y = index / PouchWidth;
            int width = LootCatalog.Width(item.Type, item.Rotated), height = LootCatalog.Height(item.Type, item.Rotated);
            if (x + width > PouchWidth || y + height > 2) return 0;
            int row = ((1 << width) - 1) << index;
            return height == 2 ? row | (row << PouchWidth) : row;
        }

        public static bool FitsPouch(GearLocation slot, LootItem item, LootItem[] gear, uint vacated = 0)
        {
            if (!IsPouchCell(slot) || !Accepts(slot, item.Type)) return false;
            int mask = PouchMask(slot, item);
            if (mask == 0 || (mask >> PouchLevel(gear)) != 0) return false;
            for (var other = GearLocation.PouchCell; other <= GearLocation.PouchCell4; other++)
            {
                var existing = gear[(int)other];
                if (existing.Id != 0 && existing.Id != item.Id && existing.Id != vacated &&
                    (PouchMask(other, existing) & mask) != 0) return false;
            }
            return true;
        }
        public static bool Selectable(GearLocation slot) => slot == GearLocation.Primary || slot == GearLocation.Secondary ||
            slot == GearLocation.PouchCell || slot == GearLocation.PouchCell2;

        public static int PouchLevel(LootItem[] gear)
        {
            if (gear == null || gear.Length != Slots) return 0;
            byte type = gear[(int)GearLocation.Pouch].Type;
            return type >= FirstPouch && type < FirstPouch + 4 ? type - FirstPouch + 1 : 0;
        }

        public static int BagRows(LootItem[] gear) => RowsForPack(
            gear != null && gear.Length == Slots ? gear[(int)GearLocation.Backpack].Type : (byte)0);

        private static int RowsForPack(byte type) => type == ExpeditionPack ? LootCatalog.BagHeight : type == FieldPack ? 6 : 4;

        public static bool Available(GearLocation slot, LootItem[] gear)
        {
            if (gear == null || gear.Length != Slots) return false;
            if (slot >= GearLocation.PouchCell && slot <= GearLocation.PouchCell4)
                return (int)slot - (int)GearLocation.PouchCell < PouchLevel(gear);
            return (byte)slot <= (byte)GearLocation.Crate;
        }

        public static bool Accepts(GearLocation slot, byte type)
        {
            if (type == 0 || type > LootCatalog.TypeCount) return false;
            switch (slot)
            {
                case GearLocation.Bag:
                case GearLocation.Crate: return true;
                case GearLocation.Primary:
                case GearLocation.Secondary: return type == Carbine || type == Pistol;
                case GearLocation.Armor: return type == Armor;
                case GearLocation.Backpack: return type == FieldPack || type == ExpeditionPack;
                case GearLocation.Pouch: return type >= FirstPouch && type < FirstPouch + 4;
                default: return IsPouchCell(slot) && (type <= LootCatalog.RandomTypeCount || type >= LootCatalog.FoldingRule);
            }
        }

        // Calculate both sides before updating either. Shared with the HUD so a valid
        // preview uses the same backpack capacity and return position as the server.
        public static RaidStatus PlanMove(LootItem item, GearLocation from, GearLocation to, int x, int y, bool rotated,
            LootItem[] bag, LootItem[] crate, LootItem[] gear, LootFootprint[] outlines,
            out LootItem target, out LootItem displaced)
        {
            target = item;
            displaced = default;
            bool fromSlot = IsSlot(from), toSlot = IsSlot(to);
            if (item.Id == 0 || (from == to && !(IsPouchCell(to) && item.Rotated != rotated)) ||
                (!fromSlot && !toSlot) || !Available(from, gear) || !Available(to, gear) ||
                from == GearLocation.Pouch || to == GearLocation.Pouch || !Accepts(to, item.Type)) return RaidStatus.Denied;

            int rows = BagRows(gear);
            if (toSlot)
            {
                if (x != -1 || y != -1) return RaidStatus.Denied;
                if (from != to) displaced = gear[(int)to];
                if (displaced.Id != 0 && !Accepts(from, displaced.Type)) return RaidStatus.Denied;
                target.X = target.Y = 0;
                target.Rotated = IsPouchCell(to) && rotated;
                if (IsPouchCell(to) && !FitsPouch(to, target, gear, displaced.Id)) return RaidStatus.NoSpace;
                if (IsPouchCell(from) && displaced.Id != 0 &&
                    (!FitsPouch(from, displaced, gear, item.Id) ||
                    (IsPouchCell(to) && (PouchMask(from, displaced) & PouchMask(to, target)) != 0)))
                    return RaidStatus.NoSpace;
            }
            if (from == GearLocation.Backpack || to == GearLocation.Backpack)
            {
                rows = RowsForPack(to == GearLocation.Backpack ? target.Type : displaced.Type);
                foreach (var other in bag)
                    if (other.Id != item.Id && !LootCatalog.FitsBounds(other.Type, true, other.X, other.Y, other.Rotated, rows))
                        return RaidStatus.NoSpace;
            }
            if (!toSlot)
            {
                bool toBag = to == GearLocation.Bag;
                return PlaceOnBoard(ref target, toBag ? bag : crate, toBag, x, y, rotated, outlines, rows);
            }
            if (displaced.Id == 0 || fromSlot) return RaidStatus.Placed;

            bool sourceBag = from == GearLocation.Bag;
            var board = sourceBag ? bag : crate;
            var status = PlaceOnBoard(ref displaced, board, sourceBag, item.X, item.Y, item.Rotated, outlines, rows, item.Id);
            return status == RaidStatus.Placed ? status :
                PlaceOnBoard(ref displaced, board, sourceBag, -1, -1, item.Rotated, outlines, rows, item.Id);
        }

        private static RaidStatus PlaceOnBoard(ref LootItem item, LootItem[] board, bool bag, int x, int y,
            bool rotated, LootFootprint[] outlines, int rows, uint vacated = 0)
        {
            if (x == -1 && y == -1)
            {
                item.Rotated = rotated;
                if (!LootCatalog.FindSpace(board, item, bag, out x, out y, out rotated, outlines, rows, vacated))
                    return bag ? RaidStatus.BagFull : RaidStatus.NoSpace;
            }
            else if (!LootCatalog.CanPlace(board, item, bag, x, y, rotated, outlines, rows, vacated))
                return RaidStatus.NoSpace;
            item.X = (byte)x;
            item.Y = (byte)y;
            item.Rotated = rotated;
            return RaidStatus.Placed;
        }
    }
}
