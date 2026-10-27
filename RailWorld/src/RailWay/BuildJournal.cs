using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Одна побудова колії: що вона додала у світ і що з нього прибрала. Цього досить, щоб її відкотити.
    /// </summary>
    public class BuildRecord
    {
        /// <summary>Секції, які побудова додала. Номер секції в чанку ніколи не повторюється, тож це точно вони.</summary>
        public List<SectionLink> Sections = new List<SectionLink>();

        /// <summary>
        /// Блоки, що стояли в клітинках колії до побудови: трава й сніг, які колія заміняє сама, і блоки,
        /// зламані режимом заміни. Блоки колії та порожні клітинки сюди не потрапляють.
        /// </summary>
        public Dictionary<BlockPos, int> Blocks = new Dictionary<BlockPos, int>();

        /// <summary>Запам'ятовує, що стоїть у клітинках секції. Викликати до того, як побудова їх змінить.</summary>
        public void Remember(IWorldAccessor world, Section section)
        {
            foreach (BlockPos pos in TrackBed.AllCells(section))
            {
                if (Blocks.ContainsKey(pos)) continue;

                Block block = world.BlockAccessor.GetBlock(pos);
                if (block.Id == 0 || block is BlockTrackBed) continue;
                Blocks[pos.Copy()] = block.Id;
            }
        }
    }

    /// <summary>
    /// Журнал побудов колії: останні побудови кожного гравця, щоб їх можна було відкотити по одній,
    /// починаючи з найновішої. Живе на сервері, в пам'яті: після перезапуску сервера відкочувати нічого.
    /// </summary>
    public class BuildJournal
    {
        /// <summary>Скільки останніх побудов пам'ятається для кожного гравця.</summary>
        public const int MaxRecords = 20;

        private Dictionary<string, List<BuildRecord>> records = new Dictionary<string, List<BuildRecord>>();

        public void Add(string playerUid, BuildRecord record)
        {
            if (record.Sections.Count == 0) return;

            if (!records.TryGetValue(playerUid, out List<BuildRecord> own))
            {
                own = new List<BuildRecord>();
                records[playerUid] = own;
            }
            own.Add(record);
            if (own.Count > MaxRecords) own.RemoveAt(0);
        }

        /// <summary>Скільки побудов гравця ще можна відкотити.</summary>
        public int Count(string playerUid)
        {
            return records.TryGetValue(playerUid, out List<BuildRecord> own) ? own.Count : 0;
        }

        /// <summary>
        /// Відкочує останню побудову гравця: прибирає її секції, які ще лежать, і повертає блоки, що стояли
        /// на їхньому місці. Секція, яку вже розібрали руками, пропускається. Колія, яку причепили до цих
        /// секцій пізніше, лишається з обірваним кінцем, як і після зламу секції.
        /// Відкочується лише вся побудова разом: якщо частина її лежить у незавантаженому чанку, нічого не
        /// змінюється, побудова лишається в журналі, а refusal каже, чому. Інакше частину вже не відкотити ніяк.
        /// Повертає false, якщо нічого не відкочено; refusal тоді null, якщо відкочувати просто нічого.
        /// </summary>
        public bool UndoLast(IWorldAccessor world, IPlayer byPlayer, out int sectionsRemoved, out int blocksRestored, out string refusal)
        {
            sectionsRemoved = blocksRestored = 0;
            refusal = null;
            if (!records.TryGetValue(byPlayer.PlayerUID, out List<BuildRecord> own) || own.Count == 0) return false;

            BuildRecord record = own[own.Count - 1];
            var session = new RailDataSession(world);
            if (!Reachable(session, record))
            {
                refusal = "частина побудови лежить там, де світ зараз не завантажений: підійдіть ближче";
                return false;
            }
            own.RemoveAt(own.Count - 1);

            // Спершу підсипка: поки секція ще записана, блоки колії перераховують свою форму вже без неї.
            // Інакше після секції лишився б гравій, який вона насипала
            var found = new List<Section>();
            var ballasted = new List<Section>();
            foreach (SectionLink link in record.Sections)
            {
                Section section = session.GetSection(link);
                if (section == null) continue;

                found.Add(section);
                if (!section.IsInstalled(SectionPart.Ballast)) continue;
                section.RemovePart(SectionPart.Ballast);
                // Змінилася лише деталь: перераховується лише те, чого секція стосується
                session.MarkChanged(section);
                ballasted.Add(section);
            }
            if (session.HasChanges) session.SaveAll();
            foreach (Section section in ballasted) TrackBed.Sync(world, section);

            // Тепер самі секції, так само, як при зламі секції цілком
            foreach (Section section in found)
            {
                DataInChunk data = session.Get(section.ChunkAddres);
                if (data == null) continue;

                SectionLinker.Unlink(session, section);
                data.Remove(section.IndexInChunk);
                session.MarkDirty(section.ChunkAddres);
                sectionsRemoved++;
            }
            if (session.HasChanges) session.SaveAll();
            foreach (Section section in found) TrackBed.Detach(world, section);

            // Блоки повертаються лише туди, де зараз порожньо: там, де вже щось стоїть, чуже не затираємо
            IBlockAccessor accessor = world.BlockAccessor;
            foreach (KeyValuePair<BlockPos, int> pair in record.Blocks)
            {
                if (accessor.GetBlock(pair.Key).Id != 0) continue;
                if (!world.Claims.TryAccess(byPlayer, pair.Key, EnumBlockAccessFlags.BuildOrBreak)) continue;

                accessor.SetBlock(pair.Value, pair.Key);
                blocksRestored++;
            }
            return true;
        }

        // Чи завантажені всі чанки, яких торкається побудова: її секцій і блоків, що треба повернути
        private static bool Reachable(RailDataSession session, BuildRecord record)
        {
            foreach (SectionLink link in record.Sections)
            {
                if (!session.IsChunkLoaded(link.Chunk)) return false;
            }
            foreach (BlockPos pos in record.Blocks.Keys)
            {
                if (!session.IsChunkLoaded(ModMath.FindChunk(pos.X, pos.Y, pos.Z))) return false;
            }
            return true;
        }
    }
}
