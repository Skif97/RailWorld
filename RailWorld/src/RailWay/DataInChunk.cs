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

        // Стрілки, чия спільна секція лежить у цьому чанку, за ключем RailSwitch.Key
        [ProtoMember(3)]
        public Dictionary<int, RailSwitch> Switches;

        /// <summary>
        /// Номер зміни: сервер збільшує його при кожному збереженні чанка. Дані чанка їдуть до клієнта двома
        /// дорогами, разом із чанком і окремим пакетом після кожної зміни, і приходять не завжди в тому
        /// порядку, в якому були відправлені. За цим номером клієнт бачить, котрі з них новіші.
        /// </summary>
        [ProtoMember(4)]
        public int Revision;

        public DataInChunk()
        {
            index = 0;
            RailWaySections = new Dictionary<int, Section>();
            Switches = new Dictionary<int, RailSwitch>();
        }

        // Секція не зберігає свій номер, він є ключем словника. Роздаємо номери після читання
        [ProtoAfterDeserialization]
        private void OnDeserialized()
        {
            if (RailWaySections == null) RailWaySections = new Dictionary<int, Section>();
            // У збереженнях, зроблених до появи списку стрілок, його немає
            if (Switches == null) Switches = new Dictionary<int, RailSwitch>();
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

        // Дані колії на клієнті: єдина розібрана копія, з неї і малюється колія, і читає все інше.
        // Кладе їх сюди RailWaySystem, коли вони приходять із сервера
        private static Dictionary<Vec3i, DataInChunk> clientCache = new Dictionary<Vec3i, DataInChunk>();

        public static void ClearClientCache()
        {
            clientCache.Clear();
        }

        /// <summary>Дані колії чанка, якими їх знає клієнт, або null.</summary>
        public static DataInChunk GetClient(Vec3i chunkCoord)
        {
            clientCache.TryGetValue(chunkCoord, out DataInChunk data);
            return data;
        }

        public static void SetClient(Vec3i chunkCoord, DataInChunk data)
        {
            clientCache[chunkCoord.Clone()] = data;
        }

        /// <summary>Забуває дані колії чанка на клієнті. Повертає true, якщо вони були.</summary>
        public static bool RemoveClient(Vec3i chunkCoord)
        {
            return clientCache.Remove(chunkCoord);
        }

        /// <summary>
        /// Дані колії чанка або null, якщо чанк не завантажений чи колії в ньому немає.
        /// </summary>
        public static DataInChunk Get(IWorldAccessor world, Vec3i chunkCoord)
        {
            IWorldChunk chunk = world.BlockAccessor.GetChunk(chunkCoord.X, chunkCoord.Y, chunkCoord.Z);
            if (world.Side != EnumAppSide.Server)
            {
                if (chunk == null) return null;

                DataInChunk known = GetClient(chunkCoord);
                if (known != null) return known;

                // Чанк щойно прийшов, і RailWaySystem його ще не розібрав: беремо те, що він привіз
                known = chunk.GetModdata<DataInChunk>(ModdataKey, null);
                if (known != null) SetClient(chunkCoord, known);
                return known;
            }

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
            data.Revision++;
            chunk.SetModdata(ModdataKey, data);
            chunk.MarkModified();
            serverCache[chunkCoord.Clone()] = data;
        }
    }
}
