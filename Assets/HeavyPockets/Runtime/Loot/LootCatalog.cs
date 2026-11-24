namespace HeavyPockets
{
    public struct LootItem
    {
        public uint Id;
        public byte Type, X, Y;
        public bool Rotated;
    }

    public struct LootFootprint
    {
        public byte X, Y, Width, Height;

        internal static LootFootprint Of(LootItem item) => new LootFootprint
        {
            X = item.X, Y = item.Y, Width = (byte)LootCatalog.Width(item.Type, item.Rotated),
            Height = (byte)LootCatalog.Height(item.Type, item.Rotated)
        };

        public bool Overlaps(int x, int y, int width, int height) =>
            x < X + Width && x + width > X && y < Y + Height && y + height > Y;
    }

    public enum LootRarity : byte { Common, Rare, Epic, Legendary }

    public static partial class LootCatalog
    {
        internal const float SearchItemSeconds = 0.75f;
        public const int BagWidth = 6, BagHeight = 9;
        public const int CrateWidth = 6, CrateHeight = 6;
        public const int BagCapacity = BagWidth * BagHeight;
        public const int CrateCells = CrateWidth * CrateHeight;
        public const int RandomTypeCount = 11;
        public const byte FoldingRule = 21, SealSpool = 22, PunchTicket = 23;
        public const int TypeCount = PunchTicket;
        private static readonly string[] Names =
        {
            "Неизвестный предмет", "Грузовой модуль", "Медная катушка",
            "Керамический предохранитель", "Латунный манометр", "Оптическая призма",
            "Шарикоподшипник", "Мультиметр", "Масляный фильтр", "Сигнальный фонарь",
            "Термодатчик", "Титановая муфта", "Карабин", "Пистолет", "Бронежилет", "Полевой рюкзак",
            "Подсумок I", "Подсумок II", "Подсумок III", "Подсумок IV", "Экспедиционный рюкзак",
            "Складной метр", "Пломбировочная проволока", "Книжка грузовых талонов"
        };
        // Nominal upright footprints shared by the authoritative simulation and HUD.
        private static readonly byte[] Widths  = { 0, 2, 2, 1, 2, 1, 1, 1, 2, 1, 1, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 1, 1 };
        private static readonly byte[] Heights = { 0, 2, 1, 1, 1, 1, 1, 2, 1, 2, 2, 1, 1, 1, 3, 2, 1, 1, 1, 1, 2, 1, 1, 1 };

        // Provisional rarity belongs to the catalog entry. A moved instance keeps its tier.
        private static readonly LootRarity[] Rarities =
        {
            LootRarity.Common, LootRarity.Legendary, LootRarity.Common, LootRarity.Common,
            LootRarity.Rare, LootRarity.Epic, LootRarity.Common, LootRarity.Epic, LootRarity.Common,
            LootRarity.Rare, LootRarity.Rare, LootRarity.Epic,
            // Carbine, pistol, armor and field pack.
            LootRarity.Epic, LootRarity.Rare, LootRarity.Epic, LootRarity.Rare,
            // Pouches I–IV and expedition pack.
            LootRarity.Rare, LootRarity.Rare, LootRarity.Epic, LootRarity.Legendary, LootRarity.Legendary,
            LootRarity.Common, LootRarity.Rare, LootRarity.Common
        };
        private static readonly string[] RarityNames = { "Обычный", "Редкий", "Эпический", "Легендарный" };
        public static LootRarity Rarity(byte type) => type <= TypeCount ? Rarities[type] : LootRarity.Common;
        public static string RarityName(byte type) => RarityNames[(int)Rarity(type)];

        public static string Name(byte type) => type <= TypeCount ? Names[type] : Names[0];
        public static int Width(byte type, bool rotated) => (rotated ? Heights : Widths)[type];
        public static int Height(byte type, bool rotated) => (rotated ? Widths : Heights)[type];

        public static bool FindSpace(LootItem[] occupants, LootItem item, bool bag,
            out int x, out int y, out bool rotated, LootFootprint[] outlines = null, int bagRows = BagHeight, uint vacated = 0)
        {
            rotated = item.Rotated;
            int width = bag ? LootCatalog.BagWidth : LootCatalog.CrateWidth;
            int height = bag ? bagRows : LootCatalog.CrateHeight;
            int turns = LootCatalog.Width(item.Type, false) == LootCatalog.Height(item.Type, false) ? 1 : 2;
            for (int turn = 0; turn < turns; turn++, rotated = !rotated)
                for (y = 0; y < height; y++)
                    for (x = 0; x < width; x++)
                        if (LootCatalog.CanPlace(occupants, item, bag, x, y, rotated, outlines, bagRows, vacated))
                            return true;
            x = y = -1;
            return false;
        }

        public static bool FitsBounds(byte type, bool bag, int x, int y, bool rotated, int bagRows = BagHeight)
        {
            return type > 0 && type <= TypeCount && x >= 0 && y >= 0 &&
                x <= (bag ? BagWidth : CrateWidth) - Width(type, rotated) &&
                y <= (bag ? bagRows : CrateHeight) - Height(type, rotated);
        }

        public static bool CanPlace(LootItem[] items, LootItem item, bool bag, int x, int y, bool rotated,
            LootFootprint[] unknown = null, int bagRows = BagHeight, uint vacated = 0)
        {
            if (!FitsBounds(item.Type, bag, x, y, rotated, bagRows)) return false;
            int width = Width(item.Type, rotated), height = Height(item.Type, rotated);
            foreach (var other in items)
            {
                if (other.Id != 0 && other.Id != item.Id && other.Id != vacated && LootFootprint.Of(other).Overlaps(x, y, width, height))
                    return false;
            }
            if (!bag && unknown != null)
                foreach (var footprint in unknown)
                    if (footprint.Overlaps(x, y, width, height)) return false;
            return true;
        }
    }
}
