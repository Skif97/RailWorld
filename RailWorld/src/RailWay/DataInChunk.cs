using ProtoBuf;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    [ProtoContract]
    public class DataInChunk
    {
        public const string ModdataKey = "RailWayDataInChunk";

        // Номер для наступної секції. Тільки росте, тому номери не повторюються і на них можна посилатися
        [ProtoMember(1)]
        private int index;

        [ProtoMember(2)]
        public Dictionary<int, Section> RailWaySections;

        public DataInChunk()
        {
            index = 0;
            RailWaySections = new Dictionary<int, Section>();
        }

        // Секція не зберігає свій номер, він є ключем словника. Роздаємо номери після читання
        [ProtoAfterDeserialization]
        private void OnDeserialized()
        {
            if (RailWaySections == null) RailWaySections = new Dictionary<int, Section>();
            foreach (var kvp in RailWaySections)
                kvp.Value.IndexInChunk = kvp.Key;
        }

        /// <summary>
        /// Додає секцію і повертає її номер у чанку.
        /// </summary>
        public int Add(Section section)
        {
            int assigned = index++;
            section.IndexInChunk = assigned;
            RailWaySections[assigned] = section;
            return assigned;
        }

        // Розібрані дані чанків на сервері, щоб не розбирати їх із байтів при кожному зверненні.
        // Усі зміни йдуть через Save, тому кеш завжди збігається зі збереженим
        private static Dictionary<Vec3i, DataInChunk> serverCache = new Dictionary<Vec3i, DataInChunk>();

        public static void ClearServerCache()
        {
            serverCache.Clear();
        }

        /// <summary>
        /// Дані колії чанка або null, якщо чанк не завантажений чи колії в ньому немає.
        /// </summary>
        public static DataInChunk Get(IWorldAccessor world, Vec3i chunkCoord)
        {
            IWorldChunk chunk = world.BlockAccessor.GetChunk(chunkCoord.X, chunkCoord.Y, chunkCoord.Z);
            if (world.Side != EnumAppSide.Server) return chunk?.GetModdata<DataInChunk>(ModdataKey, null);

            if (chunk == null)
            {
                serverCache.Remove(chunkCoord);
                return null;
            }

            if (serverCache.TryGetValue(chunkCoord, out DataInChunk cached)) return cached;

            DataInChunk data = chunk.GetModdata<DataInChunk>(ModdataKey, null);
            if (data != null) serverCache[chunkCoord.Clone()] = data;
            return data;
        }

        /// <summary>
        /// Записує дані в чанк. Викликати тільки на сервері.
        /// </summary>
        public static void Save(IWorldAccessor world, Vec3i chunkCoord, DataInChunk data)
        {
            IWorldChunk chunk = world.BlockAccessor.GetChunk(chunkCoord.X, chunkCoord.Y, chunkCoord.Z);
            if (chunk == null) return;
            chunk.SetModdata(ModdataKey, data);
            chunk.MarkModified();
            serverCache[chunkCoord.Clone()] = data;
        }
    }
}
