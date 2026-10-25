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
            Within(p.X - reach, p.Y - vertical, p.Z - reach, p.X + reach, p.Y + vertical, p.Z + reach);

            var found = new List<Section>();
            foreach (Section section in candidates)
            {
                FastVec3d middle = section.Center;
                if (Math.Abs(middle.X - p.X) <= reach && Math.Abs(middle.Z - p.Z) <= reach) found.Add(section);
            }
            return found;
        }

        /// <summary>
        /// Секції з клітинок, яких торкається коробка в глобальних координатах: клітинки цілком, точно відбирає той,
        /// хто питає. Список спільний для всіх запитів цього TrackIndex і дійсний до наступного.
        /// </summary>
        public List<Section> Within(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
        {
            candidates.Clear();
            int size = GlobalConstants.ChunkSize;
            // Чанки так само, як ModMath.FindChunk
            int firstX = (int)Math.Truncate(minX / size), lastX = (int)Math.Truncate(maxX / size);
            int firstY = (int)Math.Truncate(minY / size), lastY = (int)Math.Truncate(maxY / size);
            int firstZ = (int)Math.Truncate(minZ / size), lastZ = (int)Math.Truncate(maxZ / size);

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
                        grid.Collect(minX - ox, minY - oy, minZ - oz, maxX - ox, maxY - oy, maxZ - oz, candidates);
                    }
                }
            }
            return candidates;
        }
    }
}
