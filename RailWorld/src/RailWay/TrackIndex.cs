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
        // Те, що запит збирає з клітинок, і чанк, про який питає сітку: свої в кожного TrackIndex, щоб не
        // створювати їх на кожен запит. Сам Around нікого не кличе, крім gridIn, тож вкладених запитів у ньому
        // немає; результат кожного запиту свій список
        private readonly List<Section> candidates = new List<Section>();
        private readonly Vec3i asked = new Vec3i();

        /// <param name="gridIn">Секції чанка по клітинках; null, якщо колії в чанку немає. Чанк, про який питають,
        /// один і той самий об'єкт на всі запити: зберігати його не можна, лише шукати за ним.</param>
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
            candidates.Clear();
            int size = GlobalConstants.ChunkSize;
            // Чанки так само, як ModMath.FindChunk
            int firstX = (int)Math.Truncate((p.X - reach) / size), lastX = (int)Math.Truncate((p.X + reach) / size);
            int firstY = (int)Math.Truncate((p.Y - vertical) / size), lastY = (int)Math.Truncate((p.Y + vertical) / size);
            int firstZ = (int)Math.Truncate((p.Z - reach) / size), lastZ = (int)Math.Truncate((p.Z + reach) / size);

            for (int cy = firstY; cy <= lastY; cy++)
            {
                for (int cz = firstZ; cz <= lastZ; cz++)
                {
                    for (int cx = firstX; cx <= lastX; cx++)
                    {
                        asked.Set(cx, cy, cz);
                        ChunkTrackGrid grid = gridIn(asked);
                        if (grid == null) continue;

                        // Запит у координатах цього чанка
                        double ox = cx * size, oy = cy * size, oz = cz * size;
                        grid.Collect(p.X - reach - ox, p.Y - vertical - oy, p.Z - reach - oz,
                            p.X + reach - ox, p.Y + vertical - oy, p.Z + reach - oz, candidates);
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
