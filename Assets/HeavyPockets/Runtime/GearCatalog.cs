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
                default: return slot >= GearLocation.PouchCell && slot <= GearLocation.PouchCell4 &&
                    type <= LootCatalog.RandomTypeCount && LootCatalog.Width(type, false) * LootCatalog.Height(type, false) <= 2;
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
            if (item.Id == 0 || from == to || (!fromSlot && !toSlot) || !Available(from, gear) || !Available(to, gear) ||
                from == GearLocation.Pouch || to == GearLocation.Pouch || !Accepts(to, item.Type)) return RaidStatus.Denied;

            int rows = BagRows(gear);
            if (toSlot)
            {
                if (x != -1 || y != -1) return RaidStatus.Denied;
                displaced = gear[(int)to];
                if (displaced.Id != 0 && !Accepts(from, displaced.Type)) return RaidStatus.Denied;
                target.X = target.Y = 0;
                target.Rotated = false;
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
