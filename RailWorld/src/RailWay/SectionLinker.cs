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
        // Секції, покладені за цю операцію
        private HashSet<Section> laid = new HashSet<Section>();

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

        /// <summary>Запам'ятовує, що секцію покладено щойно, за цю операцію.</summary>
        public void MarkLaid(Section section)
        {
            laid.Add(section);
        }

        /// <summary>Чи секцію покладено за цю операцію. Перетин питає це, щоб знати, котра з двох колій лежала раніше.</summary>
        public bool WasLaid(Section section)
        {
            return laid.Contains(section);
        }

        /// <summary>
        /// Записує один чанк, дані якого щойно змінили, і розсилає його клієнтам; заразом перераховує
        /// стрілки й перетини, яких ця зміна стосується. Для змін, що не проходять через власну сесію.
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
            // Записи про стрілки й перетини мають відповідати колії вже в тому, що зараз піде у збереження й клієнтам.
            // Перерахунок сам позначає чанки, які зачепив
            SwitchRegistry.Refresh(this, new List<Vec3i>(dirty));
            CrossingRegistry.Refresh(this, new List<Vec3i>(dirty));

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
    /// З'єднання між секціями колії і правила, що з них випливають. Зв'язує і розв'язує секції лише сервер;
    /// перевірки правил (FindSwitchProblem, FindLongOverlap і подібні) і питання про вже зв'язану колію
    /// (BranchToward) викликає й клієнт: для попереднього перегляду маршруту і для рендера.
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

        /// <summary>Те саме по вже розкладеній колії.</summary>
        public static List<SectionLink> Meeting(Section section, bool atStart, TrackIndex track)
        {
            return Meeting(section, atStart, track.InChunk);
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
        public static string FindSwitchProblem(IList<Section> planned, TrackIndex track, System.Func<SectionLink, Section> resolve, out int at)
        {
            return FindSwitchProblem(planned, Examine(planned, track), track, resolve, out at);
        }

        /// <summary>Те саме, коли нові секції вже оглянуто: facts це факти про кожну з planned.</summary>
        public static string FindSwitchProblem(IList<Section> planned, IList<SectionFacts> facts, TrackIndex track, System.Func<SectionLink, Section> resolve, out int at)
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

                    foreach (SectionLink meeting in Meeting(planned[i], atStart, track))
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

                at = join.Index;

                // Та сама стрілка, якою її порахує сервер після побудови, і секції її тіла
                RailSwitch built = SwitchBuilder.Build(join.Copy, join.Other.AtStart, Find);
                var body = new List<Section>();
                if (built != null)
                {
                    foreach (SwitchBranch branch in built.Branches)
                    {
                        for (int n = 1; n <= branch.ZoneSections; n++)
                        {
                            SectionLink member = branch.Member(n);
                            Section section = member == null ? null : Find(member);
                            if (section != null) body.Add(section);
                        }
                    }
                }

                // Тіло нової стрілки не накладається на тіло іншої: ні тієї, що вже лежить, ні тієї, яку дасть
                // другий кінець цього ж маршруту. Саме це з'єднання на час перевірки знімаємо: інакше кожна
                // секція нового тіла знайшла б у ньому саму себе
                mine.RemoveLink(join.AtStart, join.Copy);
                join.Copy.RemoveLink(join.Other.AtStart, mine);
                bool blocked = SwitchZone.JointInBody(join.Copy, join.Other.AtStart, Find) || body.Exists(section => SwitchZone.InBody(section, Find));
                Connect(mine, join.AtStart, join.Copy, join.Other.AtStart);
                if (blocked) return "надто близько до іншої стрілки";

                // Якщо частина гілки в незавантаженому чанку, сказати про хрестовину нічого не можна,
                // і тоді не відмовляємо
                if (built != null && built.Resolved && built.Branches[0].FrogFrom == 0) return "стрілка без хрестовини: гілки розходяться надто полого або гілка закоротка";

                if (built != null && built.Resolved)
                {
                    string shape = BranchesProblem(join.Copy, join.Other.AtStart, built, Find);
                    if (shape != null) return shape;
                }

                // Важелю має бути де стати: якщо з обох боків від стрілки вже лежать інші колії, будувати її не можна
                if (ChooseLeverSide(join.Copy, join.Other.AtStart, Find, track) == 0) return "важелю стрілки немає де стати: з обох боків інші колії";

                // Стрілку не перетинає інша колія: це правило діє в обидва боки. Маршрут не може перетнути
                // стрілку, що вже лежить, і так само не може поставити стрілку там, де колію вже перетинає інша
                if (built != null && CrossedNearBody(join.Copy, join.Other.AtStart, built, Find, track)) return "стрілка надто близько до перетину з іншою колією";

                // І не лягає на колію, що до неї не належить: під тілом стрілки може просто лежати чужий шматок
                // колії, який нічого не перетинає. Тіло це обидві гілки, і вже покладена теж
                if (built != null)
                {
                    var own = new HashSet<string> { SectionKey(join.Copy) };
                    foreach (SwitchBranch branch in built.Branches)
                    {
                        foreach (SectionLink member in branch.Members) own.Add(Key(member.Chunk, member.Index));
                    }
                    foreach (Section section in body)
                    {
                        // Сама секція тіла, якщо вона вже лежить, серед покладених теж є: це не чужа колія
                        string self = SectionKey(section);
                        if (TrackSurvey.Examine(section, track).InPlane.Exists(other => !own.Contains(SectionKey(other)) && SectionKey(other) != self))
                            return "стрілка лягає на іншу колію";
                    }
                }
            }

            // Нова колія лежить на покладеній у тій самій площині лише там, де це стрілка або перетин.
            // Просто лягти поруч, шпалами на шпали, не можна: це вже не колія, а дві колії одна в одній.
            // Вузол перетину це ділянка накладання, в межах якої осі двох колій перетинаються
            bool[] inNode = new bool[planned.Count];
            for (int from = 0; from < planned.Count;)
            {
                if (facts[from].InPlane.Count == 0)
                {
                    from++;
                    continue;
                }

                int to = from;
                bool crossed = facts[from].Crossed;
                while (to + 1 < planned.Count && facts[to + 1].InPlane.Count > 0
                    && planned[to].FullEndPosition.DistanceTo(planned[to + 1].FullStartPosition) <= PositionTolerance)
                {
                    to++;
                    crossed |= facts[to].Crossed;
                }

                for (int i = from; crossed && i <= to; i++) inNode[i] = true;
                from = to + 1;
            }

            // Чи секція лежить у стрілці колії, якою вона стане після побудови
            bool InSwitch(Section section)
            {
                return SwitchZone.JointInBody(section, true, Find) || SwitchZone.JointInBody(section, false, Find);
            }

            for (int i = 0; i < planned.Count; i++)
            {
                if (facts[i].InPlane.Count == 0) continue;
                Section other = facts[i].InPlane[0];

                // Спершу перетин: це вже відомо, а щоб дізнатися про стрілку, треба пройти вздовж колії
                if (inNode[i]) continue;

                Section laid = Find(new SectionLink(other, true));
                if (InSwitch(planned[i]) && InSwitch(laid)) continue;

                at = i;
                return "колія лягає на іншу без стрілки чи перетину";
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
        /// чий габарит перекривається з габаритом покладеної: так само рахується тіло стрілки. Коротке накладання
        /// буває на стрілці й на перетині; довге означає, що дві колії йдуть одна поверх одної і не розходяться.
        /// Колія вище чи нижче на RouteCurve.CrossingClearance і більше не рахується: це міст або тунель.
        /// Рахується колія, яка вийде, а не лише шматок, який кладуть зараз: якщо накладання своїм краєм
        /// стикується з покладеною колією, що теж лежить на іншій, воно продовжується вздовж неї. Інакше
        /// довге накладання можна було б зібрати з кількох коротких.
        /// planned це нові секції по порядку. from і to це перша й остання нова секція найдовшого накладання.
        /// </summary>
        public static bool FindLongOverlap(IList<Section> planned, TrackIndex track,
            System.Func<SectionLink, Section> resolve, out int from, out int to)
        {
            return FindLongOverlap(planned, Examine(planned, track), track, resolve, out from, out to);
        }

        /// <summary>Те саме, коли нові секції вже оглянуто.</summary>
        public static bool FindLongOverlap(IList<Section> planned, IList<SectionFacts> facts, TrackIndex track,
            System.Func<SectionLink, Section> resolve, out int from, out int to)
        {
            from = to = -1;
            int longest = MaxOverlapSections;
            int runFrom = -1;

            for (int i = 0; i <= planned.Count; i++)
            {
                bool lies = i < planned.Count && facts[i].LiesOnTrack;
                // Накладання триває, поки секції лежать на колії і йдуть одна за одною без розриву
                if (lies && runFrom >= 0 && planned[i - 1].FullEndPosition.DistanceTo(planned[i].FullStartPosition) <= PositionTolerance) continue;

                if (runFrom >= 0)
                {
                    int length = i - runFrom
                        + LaidOverlap(planned[runFrom], true, track, resolve)
                        + LaidOverlap(planned[i - 1], false, track, resolve);
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
        private static int LaidOverlap(Section section, bool atStart, TrackIndex track,
            System.Func<SectionLink, Section> resolve)
        {
            int best = 0;
            foreach (SectionLink meeting in Meeting(section, atStart, track))
            {
                Section first = resolve(meeting);
                if (first == null) continue;

                TrackWalker walk = TrackWalker.Entering(first, meeting.AtStart, resolve);
                int count = 0;
                while (count <= MaxOverlapSections && TrackSurvey.Examine(walk.Section, track).LiesOnTrack)
                {
                    count++;
                    if (!walk.Next()) break;
                }
                best = Math.Max(best, count);
            }
            return best;
        }

        /// <summary>
        /// Чи нова ділянка проходить по важелю стрілки, що вже стоїть. Важіль стоїть збоку від колії, поза
        /// її шпалами, тож зона стрілки його не захищає: чужа колія може пройти поруч і накрити його.
        /// Повертає номер секції в planned, під якою опинився б важіль, або -1.
        /// </summary>
        public static int FindLeverInTrack(IList<Section> planned, TrackIndex track)
        {
            return FindLeverInTrack(Examine(planned, track));
        }

        /// <summary>Те саме, коли нові секції вже оглянуто.</summary>
        public static int FindLeverInTrack(IList<SectionFacts> facts)
        {
            for (int i = 0; i < facts.Count; i++)
            {
                if (facts[i].CoversLever) return i;
            }
            return -1;
        }

        /// <summary>Оглядає покладену колію навколо кожної з нових секцій.</summary>
        public static List<SectionFacts> Examine(IList<Section> planned, TrackIndex track)
        {
            var facts = new List<SectionFacts>(planned.Count);
            foreach (Section section in planned) facts.Add(TrackSurvey.Examine(section, track));
            return facts;
        }

        // Крок уздовж колії від тіла стрілки назовні: секція, котрим кінцем у неї зайшли, чи вона сама в тілі,
        // і якщо ні, скільки блоків від краю тіла до її початку
        // Чи гілки стрілки розходяться так, як це може стрілка: один раз, кожна у свій бік, гостряк доходить
        // до свого кореня, а рейки в хрестовині перетинаються один раз. Причина відмови або null
        private static string BranchesProblem(Section trunk, bool atStart, RailSwitch built, System.Func<SectionLink, Section> resolve)
        {
            List<SectionLink> links = trunk.GetLinks(atStart);
            Section[] first = { resolve(links[0]), resolve(links[1]) };
            if (first[0] == null || first[1] == null) return null;

            if (SwitchSides.CrossOver(SwitchSides.Walk(first[0], links[0].AtStart, resolve), SwitchSides.Walk(first[1], links[1].AtStart, resolve)))
                return "гілки стрілки переходять одна через одну: стрілка розводить колії лише раз, кожну у свій бік";

            for (int k = 0; k < 2; k++)
            {
                bool? mine = built.Branches[k].InnerIsFirst(1), theirs = built.Branches[1 - k].InnerIsFirst(1);
                if (mine == null || theirs == null) continue;

                BranchGeometry geometry = SwitchGeometry.Branch(first[k], links[k].AtStart, mine.Value, first[1 - k], links[1 - k].AtStart, theirs.Value, resolve);
                if (!SwitchGeometry.BladeFits(geometry, built.Branches[k].BladeSections, first[k].TrackWidth))
                    return "гостряк не вміщається: гілки розходяться надто полого або надто довго лежать одна на одній";
                if (!SwitchGeometry.FrogCrossesOnce(geometry))
                    return "рейки гілок у хрестовині сходяться кілька разів: гілки виляють одна відносно одної";
            }

            return null;
        }

        // Скільки секцій щонайбільше проходить пошук перетинів біля стрілки в один бік: тіло найпологішої
        // стрілки і найдовший вузол перетину за ним
        private const int MaxBodyWalk = 128;

        private class BodyStep
        {
            public Section Section;
            public bool Entered;
            public bool InBody;
            public double At;
        }

        // Чи вузол перетину з чужою колією накладається на тіло стрілки. Вузол тягнеться вздовж колії в обидва
        // боки від місця, де перетнулися осі, тож шукати перетини треба не лише в самому тілі, а й далі від
        // нього: на спільній колії перед стиком і на гілках за тілом. Дістає вузол до тіла чи ні, видно
        // з габаритів: чи лежить колія, що перетинає, на котрійсь секції тіла
        private static bool CrossedNearBody(Section trunk, bool atStart, RailSwitch built, System.Func<SectionLink, Section> resolve,
            TrackIndex track)
        {
            // Далі за найдовший можливий вузол шукати нема чого
            double reach = RouteCurve.MaxNodeReach;
            var steps = new List<BodyStep>();
            var own = new HashSet<string>();
            string Key(Section section) => section.ChunkAddres.X + "_" + section.ChunkAddres.Y + "_" + section.ChunkAddres.Z + "_" + section.IndexInChunk;

            // Іде по колії від first, у яку зайшли кінцем entered; перші bodySections секцій це тіло стрілки
            void Walk(Section first, bool entered, int bodySections)
            {
                if (first == null) return;

                TrackWalker walk = TrackWalker.Entering(first, entered, resolve);
                double at = 0;
                for (int n = 0; n < MaxBodyWalk && at <= reach; n++)
                {
                    Section current = walk.Section;
                    bool inBody = n < bodySections;
                    steps.Add(new BodyStep { Section = current, Entered = walk.Entered, InBody = inBody, At = at });
                    own.Add(Key(current));
                    if (!inBody) at += current.FullStartPosition.DistanceTo(current.FullEndPosition);

                    if (!walk.Next()) break;
                }
            }

            Walk(trunk, atStart, 0);
            foreach (SwitchBranch branch in built.Branches)
            {
                SectionLink first = branch.Member(1);
                Section section = first == null ? null : resolve(first);
                if (section != null) Walk(section, first.AtStart, branch.ZoneSections);
            }

            bool crossed = false;
            foreach (BodyStep step in steps)
            {
                Vec3d from = step.Section.GetEndPosition(step.Entered), to = step.Section.GetEndPosition(!step.Entered);
                Vec3d middle = step.Section.GetGlobalPos();

                foreach (Section other in track.Around(middle, TrackSurvey.CrossReach, RouteCurve.CrossingClearance))
                {
                    // Гілки самої стрілки лежать одна на одній, але це не перетин із чужою колією
                    if (own.Contains(Key(other))) continue;
                    if (Math.Abs(other.GetGlobalPos().Y - middle.Y) >= RouteCurve.CrossingClearance) continue;
                    if (!AxisCrossing.OfLaid(step.Section, other)) continue;

                    if (step.InBody) return true;
                    crossed = true;
                }
            }
            if (!crossed) return false;

            // Перетин поруч є. Його вузол дістає до тіла стрілки, якщо чужа колія лежить габаритом на секції тіла
            foreach (BodyStep step in steps)
            {
                if (!step.InBody) continue;
                Vec3d middle = step.Section.GetGlobalPos();

                foreach (Section other in track.Around(middle, TrackSurvey.Reach, RouteCurve.CrossingClearance))
                {
                    if (own.Contains(Key(other))) continue;
                    if (Math.Abs(other.GetGlobalPos().Y - middle.Y) >= RouteCurve.CrossingClearance) continue;
                    if (Footprint.Overlap(step.Section, other)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Чи нова ділянка проходить над чи під покладеною колією надто близько. Там, де габарити двох колій
        /// перекриваються, нова або лежить у площині покладеної (це перетин чи стрілка), або вища чи нижча
        /// щонайменше на RouteCurve.CrossingClearance (це міст чи тунель). Проміжного не буває: інакше шпала
        /// однієї колії стояла б у рейках другої. Правило не залежить від того, чи перетинаються осі колій:
        /// колія може пройти над самим краєм чи боком іншої.
        /// Повертає номери всіх секцій у planned, де це сталося б: показ підсвічує кожну.
        /// </summary>
        public static List<int> FindTooCloseAbove(IList<Section> planned, TrackIndex track)
        {
            return FindTooCloseAbove(Examine(planned, track));
        }

        /// <summary>Те саме, коли нові секції вже оглянуто.</summary>
        public static List<int> FindTooCloseAbove(IList<SectionFacts> facts)
        {
            var found = new List<int>();
            for (int i = 0; i < facts.Count; i++)
            {
                if (facts[i].TooClose) found.Add(i);
            }
            return found;
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
        /// Ставить важіль стрілки з того боку, який вибрав ChooseLeverSide. Якщо вільного боку немає,
        /// важіль лишається де був: побудувати таку стрілку не дає FindSwitchProblem.
        /// </summary>
        internal static void PlaceLever(RailDataSession session, Section trunk, bool atStart)
        {
            // Гравець переставив важіль сам: його вибір лишається
            if (trunk.IsLeverManual(atStart)) return;

            int side = ChooseLeverSide(trunk, atStart, session.GetSection, new TrackIndex(chunk => session.Get(chunk)?.RailWaySections.Values));
            if (side == 0) return;

            if (trunk.GetLeverSide(atStart) == side && trunk.HasLeverSide(atStart)) return;
            trunk.SetLeverSide(atStart, side);
            session.MarkDirty(trunk.ChunkAddres);
        }

        /// <summary>
        /// Вибирає бік для важеля стрілки: 1 з боку нормалі спільної секції, -1 з протилежного.
        /// Найкраще йому із зовнішнього боку прямішої з двох колій, тобто з протилежного від того, куди
        /// відхиляється друга: тоді він не стоїть між коліями. Але якщо там уже лежить інша колія і важіль
        /// опинився б під її шпалами, береться другий бік. Повертає 0, якщо зайняті обидва.
        /// </summary>
        public static int ChooseLeverSide(Section trunk, bool atStart, System.Func<SectionLink, Section> resolve,
            TrackIndex track)
        {
            // Секції самої стрілки: вони лежать поруч із важелем, але це не чужа колія
            var own = new HashSet<string>();
            own.Add(SectionKey(trunk));

            double[] sideways = BranchSideways(trunk, atStart, resolve, own);
            if (sideways == null) return 0;

            // Пряміша та, що відійшла менше. Друга відхиляється від неї в цей бік, а важіль стає з протилежного
            int straighter = Math.Abs(sideways[0]) <= Math.Abs(sideways[1]) ? 0 : 1;
            double diverging = sideways[1 - straighter] - sideways[straighter];
            int preferred = diverging > 0 ? -1 : 1;

            foreach (int side in new[] { preferred, -preferred })
            {
                if (LeverSideFree(trunk, atStart, side, own, track)) return side;
            }
            return 0;
        }

        /// <summary>
        /// На скільки кожна з двох гілок стрілки відходить убік від стику, якщо міряти вздовж нормалі спільної
        /// колії: додатне в бік нормалі. Номер гілки це номер зв'язку, він же положення стрілки. Біля самого
        /// стику гілки ще лежать разом, тож дивимося на кілька секцій далі. own, якщо заданий, збирає пройдені
        /// секції. null, якщо гілок не дві або котрусь не знайти.
        /// </summary>
        public static double[] BranchSideways(Section trunk, bool atStart, System.Func<SectionLink, Section> resolve, HashSet<string> own = null)
        {
            List<SectionLink> links = trunk.GetLinks(atStart);
            if (links.Count < 2) return null;

            Vec3d joint = trunk.GetEndPosition(atStart);
            Vec3f normal = atStart ? trunk.StartNormal : trunk.EndNormal;

            double[] sideways = new double[2];
            for (int i = 0; i < 2; i++)
            {
                Section first = resolve(links[i]);
                if (first == null) return null;
                own?.Add(SectionKey(first));

                TrackWalker walk = TrackWalker.Entering(first, links[i].AtStart, resolve);
                for (int step = 1; step < LeverLookAhead && walk.Next(); step++) own?.Add(SectionKey(walk.Section));

                Vec3d far = walk.Section.GetEndPosition(!walk.Entered);
                sideways[i] = (far.X - joint.X) * normal.X + (far.Z - joint.Z) * normal.Z;
            }
            return sideways;
        }

        /// <summary>
        /// Котра з двох гілок стрілки відходить від другої в бік side: 1 це бік нормалі спільної секції,
        /// -1 протилежний. Номер гілки це положення стрілки, в якому вона увімкнена. -1, якщо гілок не дві
        /// або котрусь не знайти.
        /// </summary>
        public static int BranchToward(Section trunk, bool atStart, int side, System.Func<SectionLink, Section> resolve)
        {
            double[] sideways = BranchSideways(trunk, atStart, resolve);
            if (sideways == null) return -1;
            return (sideways[1] > sideways[0]) == (side > 0) ? 1 : 0;
        }

        /// <summary>
        /// Чи може важіль стрілки стати з цього боку: чи не заходить у його місце інша колія.
        /// </summary>
        public static bool LeverSideFree(Section trunk, bool atStart, int side, System.Func<SectionLink, Section> resolve,
            TrackIndex track)
        {
            // Секції самої стрілки лежать поруч із важелем, але це не чужа колія
            var own = new HashSet<string>();
            own.Add(SectionKey(trunk));
            foreach (SectionLink link in trunk.GetLinks(atStart))
            {
                Section first = resolve(link);
                if (first == null) continue;

                TrackWalker walk = TrackWalker.Entering(first, link.AtStart, resolve);
                for (int step = 0; step < LeverLookAhead; step++)
                {
                    own.Add(SectionKey(walk.Section));
                    if (!walk.Next()) break;
                }
            }
            return LeverSideFree(trunk, atStart, side, own, track);
        }

        private static bool LeverSideFree(Section trunk, bool atStart, int side, HashSet<string> own, TrackIndex track)
        {
            Vec3d lever = SectionPicker.LeverPosition(trunk, atStart, side);
            foreach (Section other in track.Around(lever, TrackSurvey.Reach, RouteCurve.CrossingClearance))
            {
                if (!own.Contains(SectionKey(other)) && TrackSurvey.CoversLever(other, trunk, atStart, side)) return false;
            }
            return true;
        }

        private static string SectionKey(Section section)
        {
            return section.ChunkAddres.X + "_" + section.ChunkAddres.Y + "_" + section.ChunkAddres.Z + "_" + section.IndexInChunk;
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
