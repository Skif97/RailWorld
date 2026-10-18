using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Тримає записи про стрілки в чанках у згоді з колією. Працює тільки на сервері.
    /// Запис перераховується, коли міняється щось у зоні стрілки: з'явився чи зник зв'язок, поставили чи зняли
    /// рейку. Усі такі зміни проходять через RailDataSession.SaveAll, тому звідти його й викликають.
    /// </summary>
    public static class SwitchRegistry
    {
        // Чанки, де є недорахована стрілка: її гілка заходить у чанк, який тоді не був завантажений.
        // Між запусками не зберігається: чанк повертається сюди, коли його завантажують і переглядають
        private static HashSet<Vec3i> pending = new HashSet<Vec3i>();

        /// <summary>Забуває недораховані стрілки попереднього світу.</summary>
        public static void Reset()
        {
            pending.Clear();
        }

        /// <summary>
        /// Дораховує стрілки, які чекали на чанки колонки (chunkX, chunkZ) або сусідніх із нею.
        /// </summary>
        public static void CompletePendingNear(RailDataSession session, int chunkX, int chunkZ)
        {
            if (pending.Count == 0) return;

            foreach (Vec3i chunk in new List<Vec3i>(pending))
            {
                if (System.Math.Abs(chunk.X - chunkX) <= 1 && System.Math.Abs(chunk.Z - chunkZ) <= 1) Complete(session, chunk);
            }
        }

        /// <summary>
        /// Перераховує стрілки, яких стосуються зміни в цих чанках: ті, що в них записані, ті, що мають там
        /// з'явитися, і ті, на чиїх гілках лежать секції цих чанків.
        /// </summary>
        public static void Refresh(RailDataSession session, IEnumerable<Vec3i> changedChunks)
        {
            var seen = new HashSet<SectionEndKey>();
            var targets = new List<SwitchRef>();
            void Want(int chunkX, int chunkY, int chunkZ, int key)
            {
                if (seen.Add(SectionEndKey.OfSwitch(new Vec3i(chunkX, chunkY, chunkZ), key))) targets.Add(new SwitchRef { ChunkX = chunkX, ChunkY = chunkY, ChunkZ = chunkZ, Key = key });
            }

            foreach (Vec3i chunk in changedChunks)
            {
                DataInChunk data = session.Get(chunk);
                if (data == null) continue;

                foreach (int key in data.Switches.Keys) Want(chunk.X, chunk.Y, chunk.Z, key);
                foreach (Section section in data.RailWaySections.Values)
                {
                    for (int end = 0; end < 2; end++)
                    {
                        bool atStart = end == 0;
                        if (section.HasSwitch(atStart)) Want(chunk.X, chunk.Y, chunk.Z, RailSwitch.KeyOf(section.IndexInChunk, atStart));

                        SwitchRef member = section.GetSwitchRef(atStart);
                        if (member != null) Want(member.ChunkX, member.ChunkY, member.ChunkZ, member.Key);
                    }
                }
            }

            foreach (SwitchRef target in targets)
            {
                Update(session, new Vec3i(target.ChunkX, target.ChunkY, target.ChunkZ), target.Key / 2, target.Key % 2 == 0);
            }
        }

        /// <summary>
        /// Дописує стрілки чанка, яких у записах ще немає (чанк не був завантажений, коли колію клали), і
        /// перераховує пораховані неповністю (чанк гілки тоді не був завантажений), іншою версією розрахунку або
        /// з посиланням на секцію, якої вже немає. Повертає true, якщо щось змінилося.
        /// </summary>
        public static bool Complete(RailDataSession session, Vec3i chunk)
        {
            DataInChunk data = session.Get(chunk);
            if (data == null) return false;

            var missing = new List<int>();
            foreach (Section section in data.RailWaySections.Values)
            {
                for (int end = 0; end < 2; end++)
                {
                    bool atStart = end == 0;
                    if (!section.HasSwitch(atStart)) continue;

                    int key = RailSwitch.KeyOf(section.IndexInChunk, atStart);
                    // Запису немає, він не дорахований, пораховано іншою версією розрахунку або посилається на
                    // секцію, якої вже немає
                    if (!data.Switches.TryGetValue(key, out RailSwitch known) || !known.Resolved || known.Outdated || known.LostMember(session)) missing.Add(key);
                }
            }

            foreach (int key in missing) Update(session, chunk, key / 2, key % 2 == 0);
            // Чанк, у якому нічого перераховувати, міг лишитися у списку від стрілки, якої вже немає
            if (missing.Count == 0) pending.Remove(chunk);
            return missing.Count > 0;
        }

        /// <summary>
        /// Запис про стрілку на кінці секції, порахований, якщо його ще немає. null, якщо стрілки там немає.
        /// </summary>
        public static RailSwitch Get(RailDataSession session, Vec3i chunk, int trunkIndex, bool atStart)
        {
            DataInChunk data = session.Get(chunk);
            if (data == null) return null;

            int key = RailSwitch.KeyOf(trunkIndex, atStart);
            if (!data.Switches.TryGetValue(key, out RailSwitch known) || !known.Resolved || known.Outdated) Update(session, chunk, trunkIndex, atStart);

            data.Switches.TryGetValue(key, out known);
            return known;
        }

        // Рахує стрілку заново і записує її разом із посиланнями в секціях її гілок
        private static void Update(RailDataSession session, Vec3i chunk, int trunkIndex, bool atStart)
        {
            DataInChunk data = session.Get(chunk);
            // Чанк стрілки не завантажений: перерахуємо, коли з'явиться
            if (data == null) return;

            int key = RailSwitch.KeyOf(trunkIndex, atStart);
            data.Switches.TryGetValue(key, out RailSwitch old);
            data.RailWaySections.TryGetValue(trunkIndex, out Section trunk);
            RailSwitch built = trunk == null ? null : SwitchBuilder.Rebuild(old, trunk, atStart, session.GetSection);

            // Секції, що входили в стрілку раніше, а тепер ні, мають про неї забути
            if (old != null) Mark(session, old, chunk, key, built);

            if (built == null)
            {
                if (old != null)
                {
                    data.Switches.Remove(key);
                    session.MarkDirty(chunk);
                }
                if (trunk != null && trunk.ResetSwitch(atStart)) session.MarkDirty(chunk);
                Track(chunk, data);
                return;
            }

            // Той самий запис не переписуємо: інакше чанк щоразу зберігався б і розсилався клієнтам
            if (!built.SameAs(old))
            {
                data.Switches[key] = built;
                session.MarkDirty(chunk);
            }
            Track(chunk, data);

            for (int branch = 0; branch < built.Branches.Count; branch++)
            {
                List<SectionLink> members = built.Branches[branch].Members;
                for (int n = 0; n < members.Count; n++)
                {
                    Section member = session.GetSection(members[n]);
                    if (member == null) continue;

                    var reference = new SwitchRef { ChunkX = chunk.X, ChunkY = chunk.Y, ChunkZ = chunk.Z, Key = key, Branch = branch, Number = n + 1 };
                    if (member.SetSwitchRef(members[n].AtStart, reference)) session.MarkDirty(member.ChunkAddres);
                }
            }

            // Бік важеля залежить від того, куди йдуть гілки, а вони могли подовжитися
            SectionLinker.PlaceLever(session, trunk, atStart, built);
        }

        // Пам'ятає чанк, поки в ньому є недорахована стрілка
        private static void Track(Vec3i chunk, DataInChunk data)
        {
            foreach (RailSwitch record in data.Switches.Values)
            {
                if (record.Resolved) continue;
                pending.Add(chunk.Clone());
                return;
            }
            pending.Remove(chunk);
        }

        // Прибирає посилання на стрілку в секціях, яких у новому записі вже немає
        private static void Mark(RailDataSession session, RailSwitch old, Vec3i chunk, int key, RailSwitch built)
        {
            foreach (SwitchBranch branch in old.Branches)
            {
                foreach (SectionLink link in branch.Members)
                {
                    if (built != null && Contains(built, link)) continue;

                    Section member = session.GetSection(link);
                    if (member == null) continue;

                    SwitchRef reference = member.GetSwitchRef(link.AtStart);
                    if (reference != null && reference.PointsTo(chunk, key) && member.SetSwitchRef(link.AtStart, null)) session.MarkDirty(member.ChunkAddres);
                }
            }
        }

        private static bool Contains(RailSwitch record, SectionLink link)
        {
            foreach (SwitchBranch branch in record.Branches)
            {
                foreach (SectionLink member in branch.Members)
                {
                    if (member.ChunkX == link.ChunkX && member.ChunkY == link.ChunkY && member.ChunkZ == link.ChunkZ
                        && member.Index == link.Index && member.AtStart == link.AtStart) return true;
                }
            }
            return false;
        }
    }
}
