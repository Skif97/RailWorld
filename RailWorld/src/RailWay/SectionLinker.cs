using System;
using System.Collections.Generic;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Посилання з кінця однієї секції на сусідню: де вона лежить і яким кінцем приєднана.
    /// </summary>
    [ProtoContract]
    public class SectionLink
    {
        [ProtoMember(1)] public int ChunkX;
        [ProtoMember(2)] public int ChunkY;
        [ProtoMember(3)] public int ChunkZ;
        [ProtoMember(4)] public int Index;

        /// <summary>true, якщо сусід приєднаний своїм початком, false якщо кінцем.</summary>
        [ProtoMember(5)] public bool AtStart;

        public SectionLink() { }

        public SectionLink(Section target, bool atStart)
        {
            ChunkX = target.ChunkAddres.X;
            ChunkY = target.ChunkAddres.Y;
            ChunkZ = target.ChunkAddres.Z;
            Index = target.IndexInChunk;
            AtStart = atStart;
        }

        public Vec3i Chunk => new Vec3i(ChunkX, ChunkY, ChunkZ);

        public bool PointsTo(Section section)
        {
            Vec3i chunk = section.ChunkAddres;
            return Index == section.IndexInChunk && ChunkX == chunk.X && ChunkY == chunk.Y && ChunkZ == chunk.Z;
        }
    }

    /// <summary>
    /// Куди веде кінець секції: сусідня секція і кінець, через який у неї заїжджають.
    /// </summary>
    public class SectionStep
    {
        public Section Section;
        public bool EnterAtStart;
    }

    /// <summary>
    /// Дані колії кількох чанків на час однієї операції на сервері: кожен чанк читається один раз,
    /// а змінені записуються і розсилаються клієнтам наприкінці.
    /// </summary>
    public class RailDataSession
    {
        private IWorldAccessor world;
        private Dictionary<Vec3i, DataInChunk> loaded = new Dictionary<Vec3i, DataInChunk>();
        private HashSet<Vec3i> dirty = new HashSet<Vec3i>();

        public RailDataSession(IWorldAccessor world)
        {
            this.world = world;
        }

        public bool HasChanges => dirty.Count > 0;

        /// <summary>
        /// Чи завантажений сам чанк. Якщо ні, про колію в ньому зараз нічого сказати не можна.
        /// </summary>
        public bool IsChunkLoaded(Vec3i chunkCoord)
        {
            return world.BlockAccessor.GetChunk(chunkCoord.X, chunkCoord.Y, chunkCoord.Z) != null;
        }

        /// <summary>
        /// Дані колії чанка або null, якщо чанк не завантажений чи колії в ньому немає і create == false.
        /// </summary>
        public DataInChunk Get(Vec3i chunkCoord, bool create = false)
        {
            if (loaded.TryGetValue(chunkCoord, out DataInChunk data)) return data;
            if (!IsChunkLoaded(chunkCoord)) return null;

            data = DataInChunk.Get(world, chunkCoord);
            if (data == null)
            {
                if (!create) return null;
                data = new DataInChunk();
            }

            loaded[chunkCoord.Clone()] = data;
            return data;
        }

        public Section GetSection(SectionLink link)
        {
            DataInChunk data = Get(link.Chunk);
            if (data == null) return null;
            data.RailWaySections.TryGetValue(link.Index, out Section section);
            return section;
        }

        public void MarkDirty(Vec3i chunkCoord)
        {
            dirty.Add(chunkCoord.Clone());
        }

        /// <summary>
        /// Записує один чанк, дані якого щойно змінили, і розсилає його клієнтам; заразом перераховує
        /// стрілки, яких ця зміна стосується. Для змін, що не проходять через власну сесію.
        /// </summary>
        public static void SaveChunk(IWorldAccessor world, Vec3i chunkCoord)
        {
            var session = new RailDataSession(world);
            if (session.Get(chunkCoord) == null) return;
            session.MarkDirty(chunkCoord);
            session.SaveAll();
        }

        /// <summary>
        /// Записує змінені чанки і розсилає їх клієнтам.
        /// </summary>
        public void SaveAll()
        {
            // Записи про стрілки мають відповідати колії вже в тому, що зараз піде у збереження й клієнтам.
            // Перерахунок сам позначає чанки, які зачепив
            SwitchRegistry.Refresh(this, new List<Vec3i>(dirty));

            foreach (Vec3i chunkCoord in dirty)
            {
                if (!loaded.TryGetValue(chunkCoord, out DataInChunk data)) continue;
                DataInChunk.Save(world, chunkCoord, data);
                RailWorld.SendChunkDataToClients(chunkCoord, data);
            }
            dirty.Clear();
        }
    }

    /// <summary>
    /// З'єднання між секціями колії. Працює тільки на сервері.
    /// </summary>
    public static class SectionLinker
    {
        // Кінці ділянок лягають на край блока з похибкою нарізки, тому збіг шукаємо з допуском
        public const double PositionTolerance = 0.1;

        // Наскільки напрямки двох кінців мають бути протилежні (косинус кута між ними).
        // Напрямки колії кратні 22.5°, тож сусідній напрямок сюди вже не проходить
        public const double DirectionTolerance = -0.95;

        /// <summary>
        /// Наскільки близько мають лежати обидва кінці двох секцій, щоб вважати їх однією і тією самою.
        /// Навмисно набагато тісніше за PositionTolerance: перші секції відгалуження відходять від основної
        /// колії на частки сантиметра, і з грубішою міркою їх викидало як повтори, а стрілка зсувалася далі.
        /// Справжній повтор (той самий маршрут прокладено вдруге) збігається точно.
        /// </summary>
        public const double DuplicateTolerance = 0.001;

        /// <summary>
        /// Скільки сусідів може бути в одного кінця секції. Один це звичайна колія, два це стрілка:
        /// основна колія і відгалуження. Потрійних стрілок не буває.
        /// </summary>
        public const int MaxLinksPerEnd = 2;

        /// <summary>
        /// Чи лежить у чанку секція з тими самими кінцями, в будь-якому з двох напрямків.
        /// </summary>
        public static bool HasSameSection(DataInChunk data, Section section)
        {
            return HasSameSection(data.RailWaySections.Values, section);
        }

        public static bool HasSameSection(IEnumerable<Section> sections, Section section)
        {
            if (sections == null) return false;
            Vec3d start = section.FullStartPosition;
            Vec3d end = section.FullEndPosition;

            foreach (Section other in sections)
            {
                Vec3d otherStart = other.FullStartPosition;
                Vec3d otherEnd = other.FullEndPosition;

                bool same = otherStart.DistanceTo(start) <= DuplicateTolerance && otherEnd.DistanceTo(end) <= DuplicateTolerance;
                bool reversed = otherStart.DistanceTo(end) <= DuplicateTolerance && otherEnd.DistanceTo(start) <= DuplicateTolerance;
                if (same || reversed) return true;
            }
            return false;
        }

        /// <summary>
        /// З'єднує щойно прокладену ділянку: сусідні секції між собою, а два крайні кінці з тим, що вже лежить поруч.
        /// </summary>
        public static void LinkStretch(RailDataSession session, List<Section> stretch)
        {
            for (int i = 0; i + 1 < stretch.Count; i++)
            {
                // Перевіряємо збіг, бо секція між ними могла не записатися в незавантажений чанк
                if (stretch[i].FullEndPosition.DistanceTo(stretch[i + 1].FullStartPosition) > PositionTolerance) continue;
                Link(session, stretch[i], false, stretch[i + 1], true);
            }

            // Кінці, що лишилися без сусіда: краї ділянки і місця, де нова секція не будувалася,
            // бо там уже лежала така сама
            foreach (Section section in stretch)
            {
                if (section.GetLinks(true).Count == 0) LinkToNeighbours(session, section, true);
                if (section.GetLinks(false).Count == 0) LinkToNeighbours(session, section, false);
            }
        }

        /// <summary>
        /// Шукає секції, кінець яких збігається з цим кінцем і дивиться назустріч, і з'єднує з ними.
        /// </summary>
        public static void LinkToNeighbours(RailDataSession session, Section section, bool atStart)
        {
            foreach (SectionLink meeting in Meeting(section, atStart, chunk => session.Get(chunk)?.RailWaySections.Values))
            {
                Section other = session.GetSection(meeting);
                if (other == null) continue;

                // До стрілки третю гілку не приєднуємо. Зв'язок, який уже є, це не зачіпає
                bool known = other.GetLinks(meeting.AtStart).Exists(link => link.PointsTo(section));
                if (!known && (other.GetLinks(meeting.AtStart).Count >= MaxLinksPerEnd
                    || section.GetLinks(atStart).Count >= MaxLinksPerEnd)) continue;

                Link(session, section, atStart, other, meeting.AtStart);
            }
        }

        /// <summary>
        /// Кінці вже покладених секцій, які сходяться з цим кінцем секції: лежать у тій самій точці й дивляться
        /// назустріч. Саме з ними кінець буде з'єднано. sectionsIn каже, які секції зберігаються в чанку.
        /// </summary>
        public static List<SectionLink> Meeting(Section section, bool atStart, System.Func<Vec3i, IEnumerable<Section>> sectionsIn)
        {
            var found = new List<SectionLink>();
            Vec3d pos = section.GetEndPosition(atStart);
            Vec3f dir = section.GetOutwardDirection(atStart);

            // Секція зберігається в чанку свого центру, а центр не далі ніж за блок від кінця
            var chunks = new HashSet<Vec3i>();
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                        chunks.Add(ModMath.FindChunk(pos.X + dx, pos.Y + dy, pos.Z + dz));

            foreach (Vec3i chunkCoord in chunks)
            {
                IEnumerable<Section> sections = sectionsIn(chunkCoord);
                if (sections == null) continue;

                foreach (Section other in sections)
                {
                    if (other == section) continue;

                    for (int end = 0; end < 2; end++)
                    {
                        bool otherAtStart = end == 0;
                        if (other.GetEndPosition(otherAtStart).DistanceTo(pos) > PositionTolerance) continue;

                        Vec3f otherDir = other.GetOutwardDirection(otherAtStart);
                        if (dir.X * otherDir.X + dir.Y * otherDir.Y + dir.Z * otherDir.Z > DirectionTolerance) continue;

                        found.Add(new SectionLink(other, otherAtStart));
                    }
                }
            }
            return found;
        }

        // Місце, де кінець нової секції з'єднається з кінцем уже покладеної
        private class Join
        {
            // Номер нової секції в ділянці і котрий її кінець
            public int Index;
            public bool AtStart;
            // Покладена секція і її кінець
            public SectionLink Other;
            // З цього кінця покладена колія вже йде далі: зі з'єднанням тут буде стрілка
            public bool MakesSwitch;
            // Копія покладеної секції, на якій з'єднання приміряється
            public Section Copy;
        }

        /// <summary>
        /// Чи ділянка нових секцій, покладена поруч із колією, що вже лежить, дала б стрілку в зоні іншої
        /// стрілки. Стрілка виникає не лише там, де маршрут навмисно причепили до колії: кінець нової ділянки
        /// з'єднується з усім, що лежить у тій самій точці, і якщо колія звідти вже йде далі, це стрілка.
        /// Інша стрілка це й та, що вже лежить, і та, яку дасть другий кінець цієї ж ділянки: ділянка, що
        /// відходить від колії і тут же в неї повертається, ставить дві стрілки одна в одну.
        /// Тому правило перевіряється на колії, якою вона стане після побудови: нова ділянка приміряється
        /// поверх покладеної, а покладені секції, до яких вона чіпляється, беруться копіями, справжні дані
        /// не змінюються. planned це нові секції по порядку, щойно створені й ще нікуди не записані; метод
        /// з'єднує їх між собою.
        /// Заразом перевіряється сама нова стрілка: якщо для неї не знаходиться хрестовина, бо гілки
        /// розходяться надто полого або гілка закоротка, щоб рейки встигли перетнутися, будувати її не можна.
        /// Повертає причину відмови або null; at це номер секції в planned, біля якої стрілка.
        /// </summary>
        public static string FindSwitchProblem(IList<Section> planned, System.Func<Vec3i, IEnumerable<Section>> sectionsIn, System.Func<SectionLink, Section> resolve, out int at)
        {
            at = -1;
            var joins = new List<Join>();
            for (int i = 0; i < planned.Count; i++)
            {
                for (int end = 0; end < 2; end++)
                {
                    bool atStart = end == 0;

                    // Цим кінцем секція з'єднується з сусідньою секцією тієї ж ділянки, а не з покладеною колією
                    int neighbour = atStart ? i - 1 : i + 1;
                    if (neighbour >= 0 && neighbour < planned.Count
                        && planned[neighbour].GetEndPosition(!atStart).DistanceTo(planned[i].GetEndPosition(atStart)) <= PositionTolerance) continue;

                    foreach (SectionLink meeting in Meeting(planned[i], atStart, sectionsIn))
                    {
                        Section other = resolve(meeting);
                        if (other == null) continue;

                        // До стрілки третю гілку не приєднуємо: такого з'єднання не буде
                        int links = other.GetLinks(meeting.AtStart).Count;
                        if (links >= MaxLinksPerEnd) continue;
                        // З вільного кінця колія просто продовжується; стрілка там, де вона вже йде далі
                        joins.Add(new Join { Index = i, AtStart = atStart, Other = meeting, MakesSwitch = links == 1 });
                    }
                }
            }
            if (!joins.Exists(join => join.MakesSwitch)) return null;

            // Колія після побудови: нові секції і копії покладених, до яких вони чіпляються, поверх справжніх
            var overlay = new Dictionary<string, Section>();
            string Key(Vec3i chunk, int index) => chunk.X + "_" + chunk.Y + "_" + chunk.Z + "_" + index;
            Section Find(SectionLink link) => overlay.TryGetValue(Key(link.Chunk, link.Index), out Section found) ? found : resolve(link);

            for (int i = 0; i < planned.Count; i++)
            {
                // Номера в чанку нова секція ще не має: даємо від'ємний, таких у справжніх не буває
                planned[i].IndexInChunk = -(i + 2);
                overlay[Key(planned[i].ChunkAddres, planned[i].IndexInChunk)] = planned[i];
            }
            for (int i = 0; i + 1 < planned.Count; i++)
            {
                if (planned[i].FullEndPosition.DistanceTo(planned[i + 1].FullStartPosition) > PositionTolerance) continue;
                Connect(planned[i], false, planned[i + 1], true);
            }
            foreach (Join join in joins)
            {
                string key = Key(join.Other.Chunk, join.Other.Index);
                if (!overlay.TryGetValue(key, out join.Copy))
                {
                    join.Copy = Serializer.DeepClone(resolve(join.Other));
                    join.Copy.IndexInChunk = join.Other.Index;
                    overlay[key] = join.Copy;
                }
                Connect(planned[join.Index], join.AtStart, join.Copy, join.Other.AtStart);
            }

            foreach (Join join in joins)
            {
                if (!join.MakesSwitch) continue;
                Section mine = planned[join.Index];

                // Саме це з'єднання знімаємо: перевіряється місце під стрілку, а не вже готова стрілка.
                // Дивимося з обох боків: і вздовж покладеної колії, і вздовж нової ділянки
                mine.RemoveLink(join.AtStart, join.Copy);
                join.Copy.RemoveLink(join.Other.AtStart, mine);
                bool blocked = SwitchZone.IsBlocked(join.Copy, join.Other.AtStart, Find) || SwitchZone.IsBlocked(mine, join.AtStart, Find);
                Connect(mine, join.AtStart, join.Copy, join.Other.AtStart);

                at = join.Index;
                if (blocked) return "надто близько до іншої стрілки";

                // Та сама стрілка, якою її порахує сервер після побудови. Якщо частина гілки в незавантаженому
                // чанку, сказати про хрестовину нічого не можна, і тоді не відмовляємо
                RailSwitch built = SwitchBuilder.Build(join.Copy, join.Other.AtStart, Find);
                if (built != null && built.Resolved && built.Branches[0].FrogFrom == 0) return "стрілка без хрестовини: гілки розходяться надто полого або гілка закоротка";
            }
            at = -1;
            return null;
        }

        /// <summary>
        /// Скільки секцій поспіль нова колія може лежати на вже покладеній: 40 секцій це 20 блоків.
        /// На стільки ж секцій від стрілки шукається хрестовина.
        /// </summary>
        public const int MaxOverlapSections = 40;

        /// <summary>
        /// Чи нова ділянка лежить на вже покладеній колії довше, ніж дозволено. Лежить на ній та секція,
        /// чия шпала перекривається зі шпалою покладеної: так само рахується зона стрілки. Коротке накладання
        /// буває на стрілці й на перетині; довге означає, що дві колії йдуть одна поверх одної і не розходяться.
        /// Колія вище чи нижче на RouteCurve.CrossingClearance і більше не рахується: це міст або тунель.
        /// Рахується колія, яка вийде, а не лише шматок, який кладуть зараз: якщо накладання своїм краєм
        /// стикується з покладеною колією, що теж лежить на іншій, воно продовжується вздовж неї. Інакше
        /// довге накладання можна було б зібрати з кількох коротких.
        /// planned це нові секції по порядку. from і to це перша й остання нова секція найдовшого накладання.
        /// </summary>
        public static bool FindLongOverlap(IList<Section> planned, System.Func<Vec3i, IEnumerable<Section>> sectionsIn,
            System.Func<SectionLink, Section> resolve, out int from, out int to)
        {
            from = to = -1;
            int longest = MaxOverlapSections;
            int runFrom = -1;

            for (int i = 0; i <= planned.Count; i++)
            {
                bool lies = i < planned.Count && LiesOnTrack(planned[i], sectionsIn);
                // Накладання триває, поки секції лежать на колії і йдуть одна за одною без розриву
                if (lies && runFrom >= 0 && planned[i - 1].FullEndPosition.DistanceTo(planned[i].FullStartPosition) <= PositionTolerance) continue;

                if (runFrom >= 0)
                {
                    int length = i - runFrom
                        + LaidOverlap(planned[runFrom], true, sectionsIn, resolve)
                        + LaidOverlap(planned[i - 1], false, sectionsIn, resolve);
                    if (length > longest)
                    {
                        longest = length;
                        from = runFrom;
                        to = i - 1;
                    }
                }
                runFrom = lies ? i : -1;
            }
            return from >= 0;
        }

        // Скільки секцій покладеної колії, з якою стикується цей кінець нової секції, поспіль лежать на іншій
        // колії, якщо йти від стику. Далі за дозволену межу не рахує: більше знати не треба
        private static int LaidOverlap(Section section, bool atStart, System.Func<Vec3i, IEnumerable<Section>> sectionsIn,
            System.Func<SectionLink, Section> resolve)
        {
            int best = 0;
            foreach (SectionLink meeting in Meeting(section, atStart, sectionsIn))
            {
                Section current = resolve(meeting);
                bool entered = meeting.AtStart;
                int count = 0;

                while (current != null && count <= MaxOverlapSections && LiesOnTrack(current, sectionsIn))
                {
                    count++;
                    List<SectionLink> ahead = current.GetLinks(!entered);
                    if (ahead.Count != 1) break;
                    entered = ahead[0].AtStart;
                    current = resolve(ahead[0]);
                }
                best = Math.Max(best, count);
            }
            return best;
        }

        // Чи шпала нової секції перекривається зі шпалою якоїсь покладеної секції
        private static bool LiesOnTrack(Section section, System.Func<Vec3i, IEnumerable<Section>> sectionsIn)
        {
            Vec3d p = section.GetGlobalPos();

            // Покладена секція зберігається в чанку свого центру, а центр не далі ніж за два блоки звідси
            var chunks = new HashSet<Vec3i>();
            for (int dx = -1; dx <= 1; dx += 2)
                for (int dy = -1; dy <= 1; dy += 2)
                    for (int dz = -1; dz <= 1; dz += 2)
                        chunks.Add(ModMath.FindChunk(p.X + dx * 2, p.Y + dy * 2, p.Z + dz * 2));

            foreach (Vec3i chunkCoord in chunks)
            {
                IEnumerable<Section> sections = sectionsIn(chunkCoord);
                if (sections == null) continue;

                foreach (Section other in sections)
                {
                    if (other == section) continue;

                    Vec3d q = other.GetGlobalPos();
                    if (Math.Abs(q.X - p.X) > 2 || Math.Abs(q.Z - p.Z) > 2 || Math.Abs(q.Y - p.Y) >= RouteCurve.CrossingClearance) continue;

                    double along = RouteCurve.Along(other, true, p, out double length, out double sideways);
                    if (along < 0 || along > length) continue;
                    if (sideways < (section.SleeperLength + other.SleeperLength) / 2) return true;
                }
            }
            return false;
        }

        private static void Connect(Section a, bool aAtStart, Section b, bool bAtStart)
        {
            a.AddLink(aAtStart, new SectionLink(b, bAtStart));
            b.AddLink(bAtStart, new SectionLink(a, aAtStart));
        }

        private static void Link(RailDataSession session, Section a, bool aAtStart, Section b, bool bAtStart)
        {
            if (a.AddLink(aAtStart, new SectionLink(b, bAtStart))) session.MarkDirty(a.ChunkAddres);
            if (b.AddLink(bAtStart, new SectionLink(a, aAtStart))) session.MarkDirty(b.ChunkAddres);

            // Якщо з якогось кінця тепер виходять дві колії, там з'явилася стрілка. Її запис і бік важеля
            // порахує SwitchRegistry, коли сесія зберігатиме зміни
        }

        // Як далеко вздовж гілок дивитися, щоб зрозуміти, котра з них пряміша, у секціях
        private const int LeverLookAhead = 8;

        /// <summary>
        /// Вибирає бік для важеля стрілки: із зовнішнього боку прямішої з двох колій, тобто з протилежного
        /// від того, куди відхиляється друга. Тоді важіль не стоїть між коліями.
        /// </summary>
        internal static void PlaceLever(RailDataSession session, Section trunk, bool atStart)
        {
            List<SectionLink> links = trunk.GetLinks(atStart);
            if (links.Count < 2) return;

            Vec3d joint = trunk.GetEndPosition(atStart);
            Vec3f normal = atStart ? trunk.StartNormal : trunk.EndNormal;

            // На скільки кожна гілка відходить убік від стику, якщо міряти вздовж нормалі спільної колії
            double[] sideways = new double[2];
            for (int i = 0; i < 2; i++)
            {
                Section current = session.GetSection(links[i]);
                bool entered = links[i].AtStart;
                if (current == null) return;

                for (int step = 1; step < LeverLookAhead; step++)
                {
                    List<SectionLink> ahead = current.GetLinks(!entered);
                    if (ahead.Count != 1) break;
                    Section next = session.GetSection(ahead[0]);
                    if (next == null) break;
                    entered = ahead[0].AtStart;
                    current = next;
                }

                Vec3d far = current.GetEndPosition(!entered);
                sideways[i] = (far.X - joint.X) * normal.X + (far.Z - joint.Z) * normal.Z;
            }

            // Пряміша та, що відійшла менше. Друга відхиляється від неї в цей бік, а важіль стає з протилежного
            int straighter = Math.Abs(sideways[0]) <= Math.Abs(sideways[1]) ? 0 : 1;
            double diverging = sideways[1 - straighter] - sideways[straighter];
            int side = diverging > 0 ? -1 : 1;


            if (trunk.GetLeverSide(atStart) == side && trunk.HasLeverSide(atStart)) return;
            trunk.SetLeverSide(atStart, side);
            session.MarkDirty(trunk.ChunkAddres);
        }

        /// <summary>
        /// Прибирає посилання на секцію в усіх її сусідів. Викликати перед видаленням секції.
        /// </summary>
        public static void Unlink(RailDataSession session, Section section)
        {
            for (int end = 0; end < 2; end++)
            {
                foreach (SectionLink link in section.GetLinks(end == 0))
                {
                    Section other = session.GetSection(link);
                    if (other != null && other.RemoveLink(link.AtStart, section)) session.MarkDirty(other.ChunkAddres);
                }
            }
        }

        /// <summary>
        /// Куди веде кінець секції. Якщо зв'язку ще немає (колія прокладена до появи зв'язків
        /// або сусідній чанк тоді не був завантажений), шукає сусіда зараз і запам'ятовує.
        /// Повертає null, якщо далі колії немає або сусідній чанк не завантажений.
        /// Після виклику треба зберегти сесію, якщо в ній з'явилися зміни.
        /// </summary>
        public static SectionStep GetNext(RailDataSession session, Section section, bool atStart)
        {
            // Посилання на секції, яких уже немає, прибираємо. Якщо чанк сусіда не завантажений, не чіпаємо
            List<SectionLink> links = new List<SectionLink>(section.GetLinks(atStart));
            foreach (SectionLink link in links)
            {
                if (session.GetSection(link) != null || !session.IsChunkLoaded(link.Chunk)) continue;
                section.RemoveLink(atStart, link);
                session.MarkDirty(section.ChunkAddres);
            }

            if (section.GetLinks(atStart).Count == 0) LinkToNeighbours(session, section, atStart);

            SectionLink active = section.GetActiveLink(atStart);
            if (active == null) return null;

            Section next = session.GetSection(active);
            if (next == null) return null;

            return new SectionStep { Section = next, EnterAtStart = active.AtStart };
        }
    }
}
