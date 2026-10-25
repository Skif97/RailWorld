using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;

namespace RailWorld
{
    /// <summary>
    /// Речі, прив'язані до секцій колії (деталі стрілок, важелі, стани стрілок), за ключем і згруповані по
    /// чанках, у яких ці секції лежать. Чанк береться з самого ключа (sectionOf), тож знайти річ за ключем це
    /// два пошуки в словнику, а все про один чанк лежить разом: перебудова чи вивантаження чанка не перебирає
    /// решти світу.
    /// </summary>
    internal class ChunkKeyed<TKey, TValue>
    {
        private readonly Func<TKey, SectionKey> sectionOf;
        private readonly Dictionary<(int, int, int), Dictionary<TKey, TValue>> chunks = new Dictionary<(int, int, int), Dictionary<TKey, TValue>>();

        public ChunkKeyed(Func<TKey, SectionKey> sectionOf)
        {
            this.sectionOf = sectionOf;
        }

        /// <summary>Речі всіх чанків: чанк за чанком. Для обходу всього, наприклад малювання.</summary>
        public Dictionary<(int, int, int), Dictionary<TKey, TValue>>.ValueCollection All => chunks.Values;

        private (int, int, int) ChunkOf(TKey key)
        {
            SectionKey section = sectionOf(key);
            return (section.ChunkX, section.ChunkY, section.ChunkZ);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            value = default;
            return chunks.TryGetValue(ChunkOf(key), out Dictionary<TKey, TValue> inChunk) && inChunk.TryGetValue(key, out value);
        }

        public void Set(TKey key, TValue value)
        {
            (int, int, int) chunk = ChunkOf(key);
            if (!chunks.TryGetValue(chunk, out Dictionary<TKey, TValue> inChunk)) chunks[chunk] = inChunk = new Dictionary<TKey, TValue>();
            inChunk[key] = value;
        }

        public bool Remove(TKey key)
        {
            (int, int, int) chunk = ChunkOf(key);
            if (!chunks.TryGetValue(chunk, out Dictionary<TKey, TValue> inChunk) || !inChunk.Remove(key)) return false;
            if (inChunk.Count == 0) chunks.Remove(chunk);
            return true;
        }

        /// <summary>Речі чанка; null, якщо там нічого немає. Поки обходиш, міняти їх не можна.</summary>
        public Dictionary<TKey, TValue> In(Vec3i chunk)
        {
            chunks.TryGetValue((chunk.X, chunk.Y, chunk.Z), out Dictionary<TKey, TValue> inChunk);
            return inChunk;
        }

        /// <summary>Прибирає все про чанк і віддає, що там було, щоб це звільнити; null, якщо нічого не було.</summary>
        public Dictionary<TKey, TValue> TakeChunk(Vec3i chunk)
        {
            (int, int, int) at = (chunk.X, chunk.Y, chunk.Z);
            if (!chunks.TryGetValue(at, out Dictionary<TKey, TValue> inChunk)) return null;
            chunks.Remove(at);
            return inChunk;
        }

        public void Clear()
        {
            chunks.Clear();
        }
    }
}
