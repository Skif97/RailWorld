using System;
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
        private readonly HashSet<Vec3i> chunks = new HashSet<Vec3i>();

        /// <summary>Забуває недораховані записи попереднього світу.</summary>
        public void Clear()
        {
            chunks.Clear();
        }

        /// <summary>Пам'ятає чанк, поки в ньому є недорахований запис.</summary>
        public void Track(Vec3i chunk, bool unresolved)
        {
            if (unresolved) chunks.Add(chunk.Clone());
            else chunks.Remove(chunk);
        }

        /// <summary>Чанки з недорахованими записами в колонці (chunkX, chunkZ) або сусідніх із нею.</summary>
        public List<Vec3i> Near(int chunkX, int chunkZ)
        {
            var found = new List<Vec3i>();
            foreach (Vec3i chunk in chunks)
            {
                if (Math.Abs(chunk.X - chunkX) <= 1 && Math.Abs(chunk.Z - chunkZ) <= 1) found.Add(chunk);
            }
            return found;
        }
    }
}
