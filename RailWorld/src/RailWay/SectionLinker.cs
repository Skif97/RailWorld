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
        // Чанки, у яких колія змінилася так, що перераховується все, що з ними пов'язане
        private HashSet<Vec3i> whole = new HashSet<Vec3i>();
        // Секції, у яких змінилися лише деталі: перераховується лише те, чого вони стосуються
        private List<Section> changed = new List<Section>();
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

        /// <summary>
        /// Чи посилання веде на секцію, якої вже немає: її чанк завантажений, а секції з таким номером у ньому
        /// немає. Про секцію в незавантаженому чанку сказати нічого не можна, і це не втрата.
        /// </summary>
        public bool Lost(SectionLink link)
        {
            return IsChunkLoaded(link.Chunk) && GetSection(link) == null;
        }

        /// <summary>
        /// Колія в чанку змінилася: він піде у збереження, а записи стрілок і перетинів, пов'язані з ним,
        /// перераховуються всі. Так після будь-якої зміни, про яку не відомо точніше.
        /// </summary>
        public void MarkDirty(Vec3i chunkCoord)
        {
            dirty.Add(chunkCoord.Clone());
            whole.Add(chunkCoord.Clone());
        }

        /// <summary>
        /// У секції змінилися лише деталі (рейка, шпала, підсипка), а не те, де вона лежить і з чим з'єднана. Її
        /// чанк піде у збереження, а перераховуються лише записи стрілок і перетинів, яких вона стосується.
        /// </summary>
        public void MarkChanged(Section section)
        {
            // Зберігаються лише чанки, які сесія читала сама
            Get(section.ChunkAddres);
            dirty.Add(section.ChunkAddres.Clone());
            changed.Add(section);
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
        /// Записує змінені чанки і розсилає їх клієнтам.
        /// </summary>
        public void SaveAll()
        {
            // Записи про стрілки й перетини мають відповідати колії вже в тому, що зараз піде у збереження й клієнтам.
            // Перерахунок сам позначає чанки, які зачепив
            var chunks = new List<Vec3i>(whole);
            // Секції, чий чанк і так перераховується цілком, окремо не потрібні
            List<Section> sections = changed.FindAll(section => !whole.Contains(section.ChunkAddres));
            SwitchRegistry.Refresh(this, chunks, sections);
            CrossingRegistry.Refresh(this, chunks, sections);

            foreach (Vec3i chunkCoord in dirty)
            {
                if (!loaded.TryGetValue(chunkCoord, out DataInChunk data)) continue;
                DataInChunk.Save(world, chunkCoord, data);
                RailWorld.SendChunkDataToClients(chunkCoord, data);
            }
            dirty.Clear();
            whole.Clear();
            changed.Clear();
        }
    }

    /// <summary>
    /// З'єднання між секціями колії і правила, що з них випливають. Зв'язує і розв'язує секції лише сервер;
    /// перевірки правил (FindSwitchProblem, FindLongOverlap і подібні) і питання про вже зв'язану колію
    /// (BranchToward) викликає й клієнт: для попереднього перегляду маршруту і для рендера.
    /// </summary>
    public static class SectionLinker
    {
        /// <summary>
        /// Наскільки близько мають лежати кінці двох секцій, щоб вважати їх одним стиком, а обидва кінці двох
        /// секцій, щоб вважати їх однією і тією самою. Маршрут, що чіпляється до колії, бере положення стику з
        /// самої колії, тож кінці збігаються точно; допуск лише на похибку обчислень. Більший був би діркою,
        /// в яку провалюється колесо, а перші секції відгалуження, що відходять від основної колії на частки
        /// сантиметра, викидало б як повтори.
        /// </summary>
        public const double PositionTolerance = 0.001;

        // Наскільки напрямки двох кінців мають бути протилежні (косинус кута між ними).
        // Напрямки колії кратні 22.5°, тож сусідній напрямок сюди вже не проходить
        public const double DirectionTolerance = -0.95;

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
            FastVec3d start = section.Start;
            FastVec3d end = section.End;
            double tolerance = PositionTolerance * PositionTolerance;

            foreach (Section other in sections)
            {
                FastVec3d otherStart = other.Start;
                FastVec3d otherEnd = other.End;

                bool same = otherStart.DistanceSq(start) <= tolerance && otherEnd.DistanceSq(end) <= tolerance;
                bool reversed = otherStart.DistanceSq(end) <= tolerance && otherEnd.DistanceSq(start) <= tolerance;
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
            foreach (SectionLink meeting in Meeting(section, atStart, new TrackIndex(chunk => session.Get(chunk)?.Grid)))
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
        /// назустріч. Саме з ними кінець буде з'єднано.
        /// </summary>
        public static List<SectionLink> Meeting(Section section, bool atStart, TrackIndex track)
        {
            var found = new List<SectionLink>();
            Vec3d pos = section.GetEndPosition(atStart);
            Vec3f dir = section.GetOutwardDirection(atStart);

            // Центр секції не далі ніж за блок від її кінця
            foreach (Section other in track.Around(pos, 1, 1))
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
            var overlay = new Dictionary<SectionKey, Section>();
            Section Find(SectionLink link) => overlay.TryGetValue(SectionKey.Of(link), out Section found) ? found : resolve(link);

            for (int i = 0; i < planned.Count; i++)
            {
                // Номера в чанку нова секція ще не має: даємо від'ємний, таких у справжніх не буває
                planned[i].IndexInChunk = -(i + 2);
                overlay[SectionKey.Of(planned[i])] = planned[i];
            }
            for (int i = 0; i + 1 < planned.Count; i++)
            {
                if (planned[i].FullEndPosition.DistanceTo(planned[i + 1].FullStartPosition) > PositionTolerance) continue;
                Connect(planned[i], false, planned[i + 1], true);
            }
            foreach (Join join in joins)
            {
                SectionKey key = SectionKey.Of(join.Other);
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
                if (built != null && ChooseLeverSide(join.Copy, join.Other.AtStart, built, Find, track) == 0) return "важелю стрілки немає де стати: з обох боків інші колії";

                // Стрілку не перетинає інша колія: це правило діє в обидва боки. Маршрут не може перетнути
                // стрілку, що вже лежить, і так само не може поставити стрілку там, де колію вже перетинає інша
                if (built != null && CrossedNearBody(join.Copy, join.Other.AtStart, built, Find, track)) return "стрілка надто близько до перетину з іншою колією";

                // І не лягає на колію, що до неї не належить: під тілом стрілки може просто лежати чужий шматок
                // колії, який нічого не перетинає. Тіло це обидві гілки, і вже покладена теж
                if (built != null)
                {
                    var own = new HashSet<SectionKey> { SectionKey.Of(join.Copy) };
                    foreach (SwitchBranch branch in built.Branches)
                    {
                        foreach (SectionLink member in branch.Members) own.Add(SectionKey.Of(member));
                    }
                    foreach (Section section in body)
                    {
                        // Сама секція тіла, якщо вона вже лежить, серед покладених теж є: це не чужа колія
                        SectionKey self = SectionKey.Of(section);
                        if (TrackSurvey.Examine(section, track).InPlane.Exists(other => !own.Contains(SectionKey.Of(other)) && SectionKey.Of(other) != self))
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
        /// Колія вище чи нижче на TrackRules.Clearance і більше не рахується: це міст або тунель.
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
        // Чи гілки стрілки розходяться так, як це може стрілка: один раз, кожна у свій бік, від самої стрілки
        // і дедалі швидше, а гостряк доходить до свого кореня. Причина відмови або null
        private static string BranchesProblem(Section trunk, bool atStart, RailSwitch built, System.Func<SectionLink, Section> resolve)
        {
            if (!SwitchSides.Branches(trunk, atStart, resolve, out List<Section> one, out List<Section> other)) return null;
            List<SectionLink> links = trunk.GetLinks(atStart);
            Section[] first = { one[0], other[0] };

            if (SwitchSides.CrossOver(one, other))
                return "гілки стрілки переходять одна через одну: стрілка розводить колії лише раз, кожну у свій бік";

            // Стрілка починається там, звідки гілки лише розходяться. Прокладання саме переносить її туди
            // (RouteBuilder.SettleSwitches); якщо не вийшло, будувати не можна. Стрілка стоїть лише на стику,
            // тож справжній початок може лежати в першій секції за ним
            if (SwitchSides.DivergesFrom(one, other) > 1)
                return "колії розходяться далі по ходу, ніж стоїть стрілка: поставте її там, де вони розходяться";

            for (int k = 0; k < 2; k++)
            {
                bool? mine = built.Branches[k].InnerIsFirst(1), theirs = built.Branches[1 - k].InnerIsFirst(1);
                if (mine == null || theirs == null) continue;

                BranchGeometry geometry = SwitchGeometry.Branch(first[k], links[k].AtStart, mine.Value, first[1 - k], links[1 - k].AtStart, theirs.Value, resolve);
                if (!SwitchGeometry.BladeFits(geometry, built.Branches[k].BladeSections, first[k].TrackWidth))
                    return "гостряк не вміщається: гілки розходяться надто полого або надто довго лежать одна на одній";
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
            var own = new HashSet<SectionKey>();

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
                    own.Add(SectionKey.Of(current));
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

                foreach (Section other in track.Around(middle, TrackSurvey.CrossReach, TrackRules.Clearance))
                {
                    // Гілки самої стрілки лежать одна на одній, але це не перетин із чужою колією
                    if (own.Contains(SectionKey.Of(other))) continue;
                    if (!TrackLevel.Same(step.Section, other)) continue;
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

                foreach (Section other in track.Around(middle, TrackSurvey.Reach, TrackRules.Clearance))
                {
                    if (own.Contains(SectionKey.Of(other))) continue;
                    if (!TrackLevel.Same(step.Section, other)) continue;
                    if (Footprint.Overlap(step.Section, other)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Чи нова ділянка проходить над чи під покладеною колією надто близько. Там, де габарити двох колій
        /// перекриваються, нова або лежить у площині покладеної (це перетин чи стрілка), або вища чи нижча
        /// щонайменше на TrackRules.Clearance (це міст чи тунель). Проміжного не буває: інакше шпала
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

        /// <summary>
        /// Ставить важіль стрілки з того боку, який вибрав ChooseLeverSide. Якщо вільного боку немає,
        /// важіль лишається де був: побудувати таку стрілку не дає FindSwitchProblem.
        /// </summary>
        internal static void PlaceLever(RailDataSession session, Section trunk, bool atStart, RailSwitch record)
        {
            // Гравець переставив важіль сам: його вибір лишається
            if (trunk.IsLeverManual(atStart)) return;

            int side = ChooseLeverSide(trunk, atStart, record, session.GetSection, new TrackIndex(chunk => session.Get(chunk)?.Grid));
            if (side == 0) return;

            if (trunk.GetLeverSide(atStart) == side && trunk.HasLeverSide(atStart)) return;
            trunk.SetLeverSide(atStart, side);
            session.MarkDirty(trunk.ChunkAddres);
        }

        /// <summary>
        /// Вибирає бік для важеля стрілки: 1 з боку нормалі спільної секції, -1 з протилежного.
        /// Найкраще йому із зовнішнього боку основної гілки (RailSwitch.Shape), тобто з протилежного від того,
        /// куди відходить друга: тоді він не стоїть між коліями. На розвилці так само від першої гілки.
        /// Але якщо там уже лежить інша колія і важіль опинився б під її шпалами, береться другий бік.
        /// Повертає 0, якщо зайняті обидва або гілок не прочитати.
        /// </summary>
        public static int ChooseLeverSide(Section trunk, bool atStart, RailSwitch record, System.Func<SectionLink, Section> resolve,
            TrackIndex track)
        {
            int other = OtherSide(trunk, atStart, record, record.Main, resolve);
            if (other == 0) return 0;

            HashSet<SectionKey> own = OwnSections(trunk, record);
            foreach (int side in new[] { -other, other })
            {
                if (LeverSideFree(trunk, atStart, side, own, track)) return side;
            }
            return 0;
        }

        /// <summary>
        /// Котра з двох гілок стрілки відходить від другої в бік side: 1 це бік нормалі спільної секції,
        /// -1 протилежний. Номер гілки це положення стрілки, в якому вона увімкнена. -1, якщо в записі
        /// стрілки боки гілок невідомі.
        /// </summary>
        public static int BranchToward(Section trunk, bool atStart, RailSwitch record, int side, System.Func<SectionLink, Section> resolve)
        {
            // Перша гілка лежить з протилежного боку від того, де від неї друга
            int other = OtherSide(trunk, atStart, record, 0, resolve);
            if (other == 0) return -1;
            return -other == side ? 0 : 1;
        }

        /// <summary>
        /// Чи може важіль стрілки стати з цього боку: чи не заходить у його місце інша колія.
        /// </summary>
        public static bool LeverSideFree(Section trunk, bool atStart, int side, RailSwitch record, TrackIndex track)
        {
            return LeverSideFree(trunk, atStart, side, OwnSections(trunk, record), track);
        }

        // З якого боку нормалі спільної секції від гілки from лежить друга гілка: 1 з боку нормалі, -1 з
        // протилежного, 0 якщо невідомо. Бік гілки записаний для її першої секції (SwitchBranch.InnerFirst),
        // а нормаль тієї секції може дивитися в інший бік, ніж нормаль спільної
        private static int OtherSide(Section trunk, bool atStart, RailSwitch record, int from, System.Func<SectionLink, Section> resolve)
        {
            if (record == null || record.Branches.Count != 2) return 0;
            SwitchBranch branch = record.Branches[from];
            bool? innerFirst = branch.InnerIsFirst(1);
            SectionLink link = branch.Member(1);
            Section first = link == null ? null : resolve(link);
            if (innerFirst == null || first == null) return 0;

            Vec3f trunkNormal = atStart ? trunk.StartNormal : trunk.EndNormal;
            Vec3f normal = link.AtStart ? first.StartNormal : first.EndNormal;
            bool same = trunkNormal.X * normal.X + trunkNormal.Z * normal.Z >= 0;
            return innerFirst.Value == same ? 1 : -1;
        }

        // Секції самої стрілки: вони лежать поруч із важелем, але це не чужа колія
        private static HashSet<SectionKey> OwnSections(Section trunk, RailSwitch record)
        {
            var own = new HashSet<SectionKey> { SectionKey.Of(trunk) };
            foreach (SwitchBranch branch in record.Branches)
            {
                foreach (SectionLink member in branch.Members) own.Add(SectionKey.Of(member));
            }
            return own;
        }

        private static bool LeverSideFree(Section trunk, bool atStart, int side, HashSet<SectionKey> own, TrackIndex track)
        {
            Vec3d lever = SectionPicker.LeverPosition(trunk, atStart, side);
            foreach (Section other in track.Around(lever, TrackSurvey.Reach, TrackRules.Clearance))
            {
                if (!own.Contains(SectionKey.Of(other)) && TrackSurvey.CoversLever(other, trunk, atStart, side)) return false;
            }
            return true;
        }

        /// <summary>
        /// Прибирає з секцій чанка те, що веде в нікуди: зв'язки з секціями, яких у завантаженому чанку вже немає,
        /// і посилання на записи стрілок і перетинів, яких немає в завантаженому чанку запису. Таке лишається,
        /// коли колію міняли, поки чанк цієї секції не був завантажений: прибрана гілка лишила б у спільній
        /// секції зв'язок і з ним стрілку-привид, а посилання на зниклий перетин не дало б з'явитися новому.
        /// Про незавантажений чанк нічого сказати не можна, і те, що веде туди, лишається.
        /// </summary>
        public static void PruneLost(RailDataSession session, Vec3i chunk)
        {
            DataInChunk data = session.Get(chunk);
            if (data == null) return;
            if (PruneLost(data, session.IsChunkLoaded, coord => session.Get(coord))) session.MarkDirty(chunk);
        }

        /// <summary>
        /// Те саме для даних чанка data. loaded каже, чи чанк завантажений, dataIn дає дані колії завантаженого
        /// чанка (null, якщо колії там немає). Повертає true, якщо щось прибрано.
        /// </summary>
        public static bool PruneLost(DataInChunk data, System.Func<Vec3i, bool> loaded, System.Func<Vec3i, DataInChunk> dataIn)
        {
            // Чи в завантаженому чанку немає того, що має там бути
            bool Gone(Vec3i chunk, System.Func<DataInChunk, bool> present)
            {
                if (!loaded(chunk)) return false;
                DataInChunk there = dataIn(chunk);
                return there == null || !present(there);
            }

            bool changed = false;
            foreach (Section section in data.RailWaySections.Values)
            {
                for (int end = 0; end < 2; end++)
                {
                    bool atStart = end == 0;
                    foreach (SectionLink link in new List<SectionLink>(section.GetLinks(atStart)))
                    {
                        if (Gone(link.Chunk, there => there.RailWaySections.ContainsKey(link.Index))) changed |= section.RemoveLink(atStart, link);
                    }

                    SwitchRef place = section.GetSwitchRef(atStart);
                    if (place != null && Gone(place.Chunk, there => there.Switches.ContainsKey(place.Key))) changed |= section.SetSwitchRef(atStart, null);
                }

                CrossingRef node = section.GetCrossingRef();
                if (node != null && Gone(node.Chunk, there => there.Crossings.ContainsKey(node.Key))) changed |= section.SetCrossingRef(null);
            }
            return changed;
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
        /// Куди веде кінець секції: активне посилання і секція за ним. null, якщо зв'язку немає або секції за
        /// ним не прочитати (чанк не завантажений). Лише читає: колію не змінює. Завислі зв'язки прибирає, а
        /// кінці, що лишилися без сусіда, доз'єднує завантаження чанка (RailWaySystem.CompleteSwitches).
        /// </summary>
        public static SectionStep Next(Section section, bool atStart, System.Func<SectionLink, Section> resolve)
        {
            SectionLink active = section.GetActiveLink(atStart);
            Section next = active == null ? null : resolve(active);
            return next == null ? null : new SectionStep { Section = next, EnterAtStart = active.AtStart };
        }

        /// <summary>
        /// Доз'єднує колію в чанку: кінцям секцій без жодного зв'язку шукає сусіда, як при прокладанні. Таке буває,
        /// коли колію клали, а сусідній чанк не був завантажений: зв'язок з'являється, щойно обидва в пам'яті.
        /// </summary>
        public static void LinkOpenEnds(RailDataSession session, Vec3i chunk)
        {
            DataInChunk data = session.Get(chunk);
            if (data == null) return;

            foreach (Section section in data.RailWaySections.Values)
            {
                if (section.GetLinks(true).Count == 0) LinkToNeighbours(session, section, true);
                if (section.GetLinks(false).Count == 0) LinkToNeighbours(session, section, false);
            }
        }
    }
}
