using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Чанки, де є недорахований запис стрілки чи перетину: його колія заходить у чанк, який тоді не був
    /// завантажений. Коли завантажується колонка, записи в ній і в сусідніх дораховуються. Між запусками не
    /// зберігається: чанк повертається сюди, коли його завантажують і переглядають. У кожного реєстру свій.
    /// </summary>
    public class PendingChunks
    {
        // По колонках: (x, z) колонки → висоти її чанків. Сусідів колонки так знаходять дев'ять пошуків, а не
        // перебір усіх недорахованих чанків світу
        private readonly Dictionary<(int, int), HashSet<int>> columns = new Dictionary<(int, int), HashSet<int>>();

        /// <summary>Забуває недораховані записи попереднього світу.</summary>
        public void Clear()
        {
            columns.Clear();
        }

        /// <summary>Пам'ятає чанк, поки в ньому є недорахований запис.</summary>
        public void Track(Vec3i chunk, bool unresolved)
        {
            (int, int) column = (chunk.X, chunk.Z);
            if (unresolved)
            {
                if (!columns.TryGetValue(column, out HashSet<int> heights)) columns[column] = heights = new HashSet<int>();
                heights.Add(chunk.Y);
            }
            else if (columns.TryGetValue(column, out HashSet<int> heights) && heights.Remove(chunk.Y) && heights.Count == 0)
            {
                columns.Remove(column);
            }
        }

        /// <summary>Чанки з недорахованими записами в колонці (chunkX, chunkZ) або сусідніх із нею.</summary>
        public List<Vec3i> Near(int chunkX, int chunkZ)
        {
            var found = new List<Vec3i>();
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!columns.TryGetValue((chunkX + dx, chunkZ + dz), out HashSet<int> heights)) continue;
                    foreach (int y in heights) found.Add(new Vec3i(chunkX + dx, y, chunkZ + dz));
                }
            return found;
        }
    }
}
