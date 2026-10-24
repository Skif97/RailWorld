using System;
using System.Collections.Generic;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Покладена колія, якою її бачать правила: швидко знаходить секції біля точки. Правила прокладання для
    /// кожної нової секції питають «що лежить поруч»; без клітинок довелося б щоразу перебирати всі секції
    /// сусідніх чанків, а на довгому маршруті це сотні тисяч порівнянь. Клітинки тримає кожен чанк сам
    /// (ChunkTrackGrid), тож тут нічого не розкладається: запит бере готові клітинки тих чанків, яких сягає.
    /// </summary>
    public class TrackIndex
    {
        private readonly Func<Vec3i, ChunkTrackGrid> gridIn;

        /// <param name="gridIn">Секції чанка по клітинках; null, якщо колії в чанку немає.</param>
        public TrackIndex(Func<Vec3i, ChunkTrackGrid> gridIn)
        {
            this.gridIn = gridIn;
        }

        /// <summary>
        /// Секції, середина яких не далі ніж за reach блоків від точки в плані по кожній з двох осей.
        /// По висоті беруться всі з клітинок, яких сягає vertical: точну висоту відбирає той, хто питає, бо
        /// порівнює він не середини секцій, а висоти в потрібній йому точці, і на схилі середина може бути
        /// вище чи нижче за неї. Як далеко дивитися, знає лише той, хто питає: правило, якому треба далі, просить далі.
        /// </summary>
        public List<Section> Around(Vec3d p, double reach, double vertical)
        {
            var candidates = new List<Section>();
            Vec3i first = ModMath.FindChunk(p.X - reach, p.Y - vertical, p.Z - reach);
            Vec3i last = ModMath.FindChunk(p.X + reach, p.Y + vertical, p.Z + reach);
            int size = GlobalConstants.ChunkSize;

            for (int cy = first.Y; cy <= last.Y; cy++)
            {
                for (int cz = first.Z; cz <= last.Z; cz++)
                {
                    for (int cx = first.X; cx <= last.X; cx++)
                    {
                        ChunkTrackGrid grid = gridIn(new Vec3i(cx, cy, cz));
                        if (grid == null) continue;

                        // Запит у координатах цього чанка
                        double ox = cx * size, oy = cy * size, oz = cz * size;
                        grid.Collect(new Vec3d(p.X - reach - ox, p.Y - vertical - oy, p.Z - reach - oz),
                            new Vec3d(p.X + reach - ox, p.Y + vertical - oy, p.Z + reach - oz), candidates);
                    }
                }
            }

            var found = new List<Section>();
            foreach (Section section in candidates)
            {
                FastVec3d middle = section.Center;
                if (Math.Abs(middle.X - p.X) <= reach && Math.Abs(middle.Z - p.Z) <= reach) found.Add(section);
            }
            return found;
        }
    }
}
