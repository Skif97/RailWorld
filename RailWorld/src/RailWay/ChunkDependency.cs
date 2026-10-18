using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Чи вигляд колії в одному чанку залежить від даних іншого. Звичайна колія малюється лише з власних секцій
    /// чанка; із чужих чанків беруться тільки стрілки й перетини: їхні записи і секції їхніх гілок та вузлів. Тому
    /// після зміни колії в чанку перемальовуються лише ті сусіди, яким вона справді потрібна.
    /// </summary>
    public static class ChunkDependency
    {
        /// <summary>
        /// Чи вигляд колії в чанку з даними data бере щось із чанка other: запис стрілки чи перетину, що лежить
        /// там і на який посилається секція data, або секцію гілки чи вузла такого запису чи запису, що лежить у
        /// самому data. dataIn дає дані колії чанка, які знає клієнт, або null.
        /// </summary>
        public static bool Reads(DataInChunk data, Vec3i other, Func<Vec3i, DataInChunk> dataIn)
        {
            bool In(SectionLink link) => link.ChunkX == other.X && link.ChunkY == other.Y && link.ChunkZ == other.Z;
            bool Switch(RailSwitch record) => record.Branches.Exists(branch => branch.Members.Exists(In));
            bool Crossing(RailCrossing record) => record.Tracks.Exists(track => track.Members.Exists(In));

            foreach (RailSwitch record in data.Switches.Values)
            {
                if (Switch(record)) return true;
            }
            foreach (RailCrossing record in data.Crossings.Values)
            {
                if (Crossing(record)) return true;
            }

            // Записи, на які посилаються секції, лежать в інших чанках. Секції однієї гілки посилаються на той
            // самий запис, тож кожен дивимося раз
            var seenSwitches = new HashSet<SectionEndKey>();
            var seenCrossings = new HashSet<SectionKey>();
            foreach (Section section in data.RailWaySections.Values)
            {
                for (int end = 0; end < 2; end++)
                {
                    SwitchRef place = section.GetSwitchRef(end == 0);
                    if (place == null || !seenSwitches.Add(SectionEndKey.OfSwitch(place.Chunk, place.Key))) continue;
                    if (place.Chunk.Equals(other)) return true;
                    if (dataIn(place.Chunk) is DataInChunk there && there.Switches.TryGetValue(place.Key, out RailSwitch record) && Switch(record)) return true;
                }

                CrossingRef node = section.GetCrossingRef();
                if (node == null || !seenCrossings.Add(new SectionKey(node.Chunk, node.Key))) continue;
                if (node.Chunk.Equals(other)) return true;
                if (dataIn(node.Chunk) is DataInChunk at && at.Crossings.TryGetValue(node.Key, out RailCrossing crossing) && Crossing(crossing)) return true;
            }
            return false;
        }
    }
}
