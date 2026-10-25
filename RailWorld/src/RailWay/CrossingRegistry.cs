using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Тримає записи про перетини колій у чанках у згоді з колією. Працює тільки на сервері.
    /// Запис перераховується, коли міняється щось у вузлі: поклали чи зняли секцію, рейку або шпалу. Усі такі
    /// зміни проходять через RailDataSession.SaveAll, тому звідти його й викликають.
    /// Зв'язків між двома коліями перетину немає, тож нові перетини шукаються по місцю: чи не перетинає
    /// секція зміненого чанка вісь іншої секції поруч.
    /// </summary>
    public static class CrossingRegistry
    {
        // Чанки з недорахованими перетинами, що чекають на завантаження сусідніх
        private static readonly PendingChunks pending = new PendingChunks();

        /// <summary>Забуває недораховані перетини попереднього світу.</summary>
        public static void Reset()
        {
            pending.Clear();
        }

        /// <summary>
        /// Дораховує перетини, які чекали на чанки колонки (chunkX, chunkZ) або сусідніх із нею.
        /// </summary>
        public static void CompletePendingNear(RailDataSession session, int chunkX, int chunkZ)
        {
            foreach (Vec3i chunk in pending.Near(chunkX, chunkZ)) Complete(session, chunk);
        }

        /// <summary>
        /// Перераховує перетини, яких стосуються зміни в цих чанках: ті, що в них записані, ті, у вузлах яких
        /// лежать секції цих чанків, і ті, що мають там з'явитися.
        /// </summary>
        public static void Refresh(RailDataSession session, IEnumerable<Vec3i> changedChunks, IEnumerable<Section> changedSections)
        {
            var chunks = new List<Vec3i>(changedChunks);
            var sections = new List<Section>(changedSections);
            var seen = new HashSet<SectionKey>();
            var targets = new List<CrossingRef>();

            foreach (Vec3i chunk in chunks)
            {
                DataInChunk data = session.Get(chunk);
                if (data == null) continue;

                foreach (int key in data.Crossings.Keys) Want(seen, targets, chunk, key);
                foreach (Section section in data.RailWaySections.Values)
                {
                    CrossingRef member = section.GetCrossingRef();
                    if (member != null) Want(seen, targets, member.Chunk, member.Key);
                }
            }
            // Секції, у яких змінилися лише деталі: перетин, у вузлі якого вона лежить. Нові перетини від деталей
            // не з'являються, тож їх тут не шукаємо
            var sectionKeys = new List<SectionKey>(sections.Count);
            foreach (Section section in sections)
            {
                CrossingRef member = section.GetCrossingRef();
                if (member != null) Want(seen, targets, member.Chunk, member.Key);
                sectionKeys.Add(SectionKey.Of(section));
            }
            // Посилання на запис мають лише секції вузла, а запис тримає ще й запас за ним: ці записи шукаються
            // одним проходом на всі зміни разом
            foreach (SectionKey record in TouchedBy(chunks, sectionKeys, coord => session.Get(coord))) Want(seen, targets, new Vec3i(record.ChunkX, record.ChunkY, record.ChunkZ), record.Index);

            foreach (CrossingRef target in targets) Update(session, target.Chunk, target.Key, null);

            // Нові перетини шукаються після перерахунку наявних: секція, що випала з вузла старого перетину,
            // може вже лежати в новому. Колію поруч усі чанки шукають через один TrackIndex
            var track = new TrackIndex(coord => session.Get(coord)?.Grid);
            foreach (Vec3i chunk in chunks) Discover(session, chunk, track);
        }

        /// <summary>
        /// Дописує перетини чанка, яких у записах ще немає (чанк запису не був завантажений, коли колію клали),
        /// і перераховує пораховані
        /// неповністю (чанк однієї з колій тоді не був завантажений), іншою версією розрахунку або з посиланням
        /// на секцію, якої вже немає.
        /// </summary>
        public static void Complete(RailDataSession session, Vec3i chunk)
        {
            DataInChunk data = session.Get(chunk);
            if (data == null) return;

            var unresolved = new List<int>();
            foreach (RailCrossing record in data.Crossings.Values)
            {
                if (!record.Resolved || record.Outdated || record.LostMember(session)) unresolved.Add(record.Key);
            }
            foreach (int key in unresolved) Update(session, chunk, key, null);
            // Чанк, у якому нічого перераховувати, міг лишитися у списку від перетину, якого вже немає
            if (unresolved.Count == 0) pending.Track(chunk, false);

            Discover(session, chunk, new TrackIndex(coord => session.Get(coord)?.Grid));
        }

        // Перетин впізнається перетнутою секцією основної колії: його ключ це її номер у чанку
        /// <summary>
        /// Записи перетинів, серед секцій яких є секція одного з чанків chunks або одна із секцій sections: чанк
        /// запису і його ключ. Посилання на запис мають лише секції вузла, а запис пам'ятає ще й запас за ним
        /// (CrossingBuilder.MarginSections), тож зміна колії в запасі, що лежить в іншому чанку, знаходить свій
        /// запис лише так. Записи шукаються в сусідніх чанках: усі секції запису лежать не далі за вузол і запас
        /// від перетину, тобто ближче за чанк. Кожен запис поруч зі змінами переглядається один раз, хоч би
        /// скільки змін його оточувало. dataIn дає дані колії завантаженого чанка або null.
        /// </summary>
        public static List<SectionKey> TouchedBy(IEnumerable<Vec3i> chunks, IEnumerable<SectionKey> sections, System.Func<Vec3i, DataInChunk> dataIn)
        {
            var changedChunks = new HashSet<(int, int, int)>();
            var changedSections = new HashSet<SectionKey>(sections);
            var around = new HashSet<(int, int, int)>();
            foreach (Vec3i chunk in chunks)
            {
                changedChunks.Add((chunk.X, chunk.Y, chunk.Z));
                AddAround(around, chunk.X, chunk.Y, chunk.Z);
            }
            foreach (SectionKey section in changedSections) AddAround(around, section.ChunkX, section.ChunkY, section.ChunkZ);

            var found = new List<SectionKey>();
            foreach ((int x, int y, int z) in around)
            {
                var coord = new Vec3i(x, y, z);
                DataInChunk data = dataIn(coord);
                if (data == null) continue;

                foreach (RailCrossing record in data.Crossings.Values)
                {
                    if (Touches(record, changedChunks, changedSections)) found.Add(new SectionKey(coord, record.Key));
                }
            }
            return found;
        }

        private static void AddAround(HashSet<(int, int, int)> around, int x, int y, int z)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++) around.Add((x + dx, y + dy, z + dz));
        }

        // Чи лежить серед секцій запису секція змінених чанків або сама змінена секція
        private static bool Touches(RailCrossing record, HashSet<(int, int, int)> changedChunks, HashSet<SectionKey> changedSections)
        {
            foreach (CrossingTrack track in record.Tracks)
            {
                foreach (SectionLink link in track.Members)
                {
                    if (changedChunks.Contains((link.ChunkX, link.ChunkY, link.ChunkZ)) || changedSections.Contains(SectionKey.Of(link))) return true;
                }
            }
            return false;
        }

        private static void Want(HashSet<SectionKey> seen, List<CrossingRef> targets, Vec3i chunk, int key)
        {
            if (seen.Add(new SectionKey(chunk, key))) targets.Add(new CrossingRef { ChunkX = chunk.X, ChunkY = chunk.Y, ChunkZ = chunk.Z, Key = key });
        }

        // Шукає перетини секцій чанка, про які ще немає запису, і записує їх. track це колія поруч. Секції чанка
        // обходяться без копії: запис перетину міняє посилання в секціях, а не самі секції чанка
        private static void Discover(RailDataSession session, Vec3i chunk, TrackIndex track)
        {
            DataInChunk data = session.Get(chunk);
            if (data == null) return;

            foreach (Section section in data.RailWaySections.Values)
            {
                if (section.GetCrossingRef() != null) continue;

                foreach (Section other in track.Around(section.GetGlobalPos(), TrackSurvey.CrossReach, TrackRules.Clearance))
                {
                    // Секція, що вже лежить у вузлі іншого перетину, другого не починає: два вузли один
                    // на одному прокладання не дозволяє
                    if (other.GetCrossingRef() != null || !CrossingBuilder.Cross(section, other)) continue;

                    // Основна колія та, що лежала раніше. Якщо жодну не поклали щойно (перетин знайшовся, коли
                    // завантажився чанк), вибір лише має бути тим самим, з якого боку на перетин не глянь
                    bool sectionIsMain = session.WasLaid(section) == session.WasLaid(other) ? Before(section, other) : session.WasLaid(other);
                    Section main = sectionIsMain ? section : other, crossing = sectionIsMain ? other : section;
                    Update(session, main.ChunkAddres, main.IndexInChunk, crossing);
                    break;
                }
            }
        }

        // Порядок секцій, що не залежить від того, котру з двох назвати першою
        private static bool Before(Section a, Section b)
        {
            Vec3i x = a.ChunkAddres, y = b.ChunkAddres;
            if (x.X != y.X) return x.X < y.X;
            if (x.Y != y.Y) return x.Y < y.Y;
            if (x.Z != y.Z) return x.Z < y.Z;
            return a.IndexInChunk < b.IndexInChunk;
        }

        // Рахує перетин заново і записує його разом із посиланнями в секціях вузла. crossing це перетнута
        // секція другої колії, якщо запису ще немає; інакше її каже сам запис
        private static void Update(RailDataSession session, Vec3i chunk, int key, Section crossing)
        {
            DataInChunk data = session.Get(chunk);
            // Чанк перетину не завантажений: перерахуємо, коли з'явиться
            if (data == null) return;

            data.Crossings.TryGetValue(key, out RailCrossing old);
            data.RailWaySections.TryGetValue(key, out Section main);

            // Чекати на чанк другої колії є сенс, лише поки основна секція на місці: без неї перетину вже немає, хоч
            // би що лежало там. Тоді запис прибирається одразу, а посилання на нього в секціях незавантажених чанків
            // прибере PruneLost, коли вони завантажаться
            if (crossing == null && main != null && old != null && old.Tracks.Count == 2 && old.Tracks[1].Center >= 1 && old.Tracks[1].Center <= old.Tracks[1].Members.Count)
            {
                SectionLink link = old.Tracks[1].Members[old.Tracks[1].Center - 1];
                if (!session.IsChunkLoaded(link.Chunk))
                {
                    // Про другу колію зараз нічого сказати не можна: запис лишається, але чекає на її чанк
                    if (old.Resolved)
                    {
                        old.Resolved = false;
                        session.MarkDirty(chunk);
                    }
                    Track(chunk, data);
                    return;
                }
                crossing = session.GetSection(link);
            }

            RailCrossing built = main != null && crossing != null && CrossingBuilder.Cross(main, crossing)
                ? CrossingBuilder.Rebuild(old, main, crossing, session.GetSection) : null;

            // Секції, що лежали у вузлі раніше, а тепер ні, мають про нього забути
            if (old != null) Unmark(session, old, chunk, key, built);

            if (built == null)
            {
                if (old != null)
                {
                    data.Crossings.Remove(key);
                    session.MarkDirty(chunk);
                }
                Track(chunk, data);
                return;
            }

            // Той самий запис не переписуємо: інакше чанк щоразу зберігався б і розсилався клієнтам
            if (!built.SameAs(old))
            {
                data.Crossings[key] = built;
                session.MarkDirty(chunk);
            }
            Track(chunk, data);

            for (int k = 0; k < built.Tracks.Count; k++)
            {
                CrossingTrack track = built.Tracks[k];
                for (int number = track.NodeFrom; number <= track.NodeTo; number++)
                {
                    Section member = session.GetSection(track.Members[number - 1]);
                    if (member == null) continue;

                    var reference = new CrossingRef { ChunkX = chunk.X, ChunkY = chunk.Y, ChunkZ = chunk.Z, Key = key, Track = k, Number = number };
                    if (member.SetCrossingRef(reference)) session.MarkDirty(member.ChunkAddres);
                }
            }
        }

        // Пам'ятає чанк, поки в ньому є недорахований перетин
        private static void Track(Vec3i chunk, DataInChunk data)
        {
            bool unresolved = false;
            foreach (RailCrossing record in data.Crossings.Values)
            {
                if (!record.Resolved) { unresolved = true; break; }
            }
            pending.Track(chunk, unresolved);
        }

        // Прибирає посилання на перетин у секціях, яких у вузлі нового запису вже немає
        private static void Unmark(RailDataSession session, RailCrossing old, Vec3i chunk, int key, RailCrossing built)
        {
            for (int k = 0; k < old.Tracks.Count; k++)
            {
                CrossingTrack track = old.Tracks[k];
                for (int number = track.NodeFrom; number <= track.NodeTo; number++)
                {
                    if (number < 1 || number > track.Members.Count) continue;
                    SectionLink link = track.Members[number - 1];
                    if (built != null && InNode(built, link)) continue;

                    Section member = session.GetSection(link);
                    if (member == null) continue;

                    CrossingRef reference = member.GetCrossingRef();
                    if (reference != null && reference.PointsTo(chunk, key) && member.SetCrossingRef(null)) session.MarkDirty(member.ChunkAddres);
                }
            }
        }

        private static bool InNode(RailCrossing record, SectionLink link)
        {
            foreach (CrossingTrack track in record.Tracks)
            {
                for (int number = track.NodeFrom; number <= track.NodeTo; number++)
                {
                    SectionLink member = track.Members[number - 1];
                    if (member.ChunkX == link.ChunkX && member.ChunkY == link.ChunkY && member.ChunkZ == link.ChunkZ && member.Index == link.Index) return true;
                }
            }
            return false;
        }
    }
}
