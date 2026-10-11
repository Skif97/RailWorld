using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Покладена колія, розкладена по клітинках у плані, щоб швидко знаходити секції біля точки.
    /// Правила прокладання для кожної нової секції питають «що лежить поруч»; без цього довелося б
    /// щоразу перебирати всі секції сусідніх чанків, а на довгому маршруті це сотні тисяч порівнянь.
    /// Чанк читається один раз, коли до нього вперше дійшли. Об'єкт живе одну перевірку: колія за цей час
    /// не міняється, а для наступної перевірки створюється новий.
    /// </summary>
    public class TrackIndex
    {
        // Сторона клітинки в блоках
        private const int Cell = 4;
        private readonly Func<Vec3i, IEnumerable<Section>> sectionsIn;
        private readonly Dictionary<Vec3i, List<Section>> chunks = new Dictionary<Vec3i, List<Section>>();
        private readonly Dictionary<long, List<Entry>> cells = new Dictionary<long, List<Entry>>();
        // Клітинки, для яких уже прочитано всі чанки, куди може сягнути запит із неї: з висотою запиту
        private readonly HashSet<(int, int, int, int)> ready = new HashSet<(int, int, int, int)>();

        // Секція в клітинці разом із серединою в плані: щоб не рахувати її при кожному запиті
        private struct Entry
        {
            public Section Section;
            public double X, Z;
        }

        /// <param name="sectionsIn">Каже, які секції зберігаються в чанку; null, якщо чанка немає.</param>
        public TrackIndex(Func<Vec3i, IEnumerable<Section>> sectionsIn)
        {
            this.sectionsIn = sectionsIn;
        }

        /// <summary>Секції, що зберігаються в чанку. Порожній список, якщо чанка немає.</summary>
        public List<Section> InChunk(Vec3i chunk)
        {
            if (chunks.TryGetValue(chunk, out List<Section> known)) return known;

            var list = new List<Section>();
            IEnumerable<Section> stored = sectionsIn(chunk);
            if (stored != null) list.AddRange(stored);
            chunks[chunk] = list;

            foreach (Section section in list)
            {
                Vec3d middle = section.GetGlobalPos();
                long key = CellKey(CellOf(middle.X), CellOf(middle.Z));
                if (!cells.TryGetValue(key, out List<Entry> cell)) cells[key] = cell = new List<Entry>();
                cell.Add(new Entry { Section = section, X = middle.X, Z = middle.Z });
            }
            return list;
        }

        /// <summary>
        /// Секції, середина яких не далі ніж за reach блоків від точки в плані, по кожній з двох осей.
        /// По висоті беруться всі, що зберігаються в чанках на vertical блоків вище і нижче: потрібну висоту
        /// відбирає той, хто питає. Як далеко дивитися, знає лише він: правило, якому треба далі, просить далі.
        /// </summary>
        public List<Section> Around(Vec3d p, double reach, double vertical)
        {
            // Чанки читаються не для кожної точки, а раз на клітинку: одразу всі, куди сягне запит
            // із будь-якого її місця. Клітинка з запасом на досяжність менша за чанк, тож досить її кутів
            int cellX = CellOf(p.X), cellY = CellOf(p.Y), cellZ = CellOf(p.Z);
            int height = (int)Math.Ceiling(vertical), width = (int)Math.Ceiling(reach);
            if (ready.Add((cellX, cellY, cellZ, height * 1000 + width)))
            {
                for (int dx = 0; dx <= 1; dx++)
                    for (int dy = 0; dy <= 1; dy++)
                        for (int dz = 0; dz <= 1; dz++)
                            InChunk(ModMath.FindChunk(
                                (cellX + dx) * Cell + (dx * 2 - 1) * width,
                                (cellY + dy) * Cell + (dy * 2 - 1) * height,
                                (cellZ + dz) * Cell + (dz * 2 - 1) * width));
            }

            var found = new List<Section>();
            int fromX = CellOf(p.X - reach), toX = CellOf(p.X + reach), fromZ = CellOf(p.Z - reach), toZ = CellOf(p.Z + reach);
            for (int x = fromX; x <= toX; x++)
            {
                for (int z = fromZ; z <= toZ; z++)
                {
                    if (!cells.TryGetValue(CellKey(x, z), out List<Entry> cell)) continue;
                    foreach (Entry entry in cell)
                    {
                        if (Math.Abs(entry.X - p.X) <= reach && Math.Abs(entry.Z - p.Z) <= reach) found.Add(entry.Section);
                    }
                }
            }
            return found;
        }

        private static int CellOf(double coordinate)
        {
            return (int)Math.Floor(coordinate / Cell);
        }

        private static long CellKey(int x, int z)
        {
            return ((long)x << 32) ^ (uint)z;
        }
    }
}
