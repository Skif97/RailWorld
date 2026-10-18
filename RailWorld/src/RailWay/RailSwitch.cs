using ProtoBuf;
using Vintagestory.API.MathTools;
using System;
using System.Collections.Generic;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Посилання секції на стрілку, на гілці якої вона лежить: де запис про стрілку, котра це гілка
    /// і котра за рахунком секція на ній, від стрілки з одиниці.
    /// </summary>
    [ProtoContract]
    public class SwitchRef
    {
        [ProtoMember(1)] public int ChunkX;
        [ProtoMember(2)] public int ChunkY;
        [ProtoMember(3)] public int ChunkZ;
        /// <summary>Ключ стрілки в її чанку, RailSwitch.Key.</summary>
        [ProtoMember(4)] public int Key;
        [ProtoMember(5)] public int Branch;
        [ProtoMember(6)] public int Number;

        /// <summary>Чанк, у якому лежить запис про стрілку.</summary>
        public Vec3i Chunk => new Vec3i(ChunkX, ChunkY, ChunkZ);

        public bool SameAs(SwitchRef other)
        {
            return other != null && ChunkX == other.ChunkX && ChunkY == other.ChunkY && ChunkZ == other.ChunkZ
                && Key == other.Key && Branch == other.Branch && Number == other.Number;
        }

        public bool PointsTo(Vec3i chunk, int key)
        {
            return ChunkX == chunk.X && ChunkY == chunk.Y && ChunkZ == chunk.Z && Key == key;
        }
    }

    /// <summary>
    /// Одна з двох гілок стрілки: її секції по порядку від стрілки і місця деталей стрілки на ній.
    /// Номери секцій рахуються від стрілки з одиниці.
    /// </summary>
    [ProtoContract]
    public class SwitchBranch
    {
        /// <summary>
        /// Секції гілки від стрілки до кінця її зони. AtStart у кожного посилання каже, котрим кінцем
        /// секція ближча до стрілки. Перша з них виходить просто зі стику.
        /// </summary>
        [ProtoMember(1)]
        public List<SectionLink> Members = new List<SectionLink>();

        /// <summary>Скільки перших секцій мають спільну шпалу з секцією другої гілки під тим самим номером.</summary>
        [ProtoMember(2)]
        public int PairSections;

        /// <summary>Скільки перших секцій лежать на другій гілці: її вісь ближча, ніж довжина шпали.</summary>
        [ProtoMember(3)]
        public int OverlapSections;

        /// <summary>Скільки перших секцій займає гостряк.</summary>
        [ProtoMember(4)]
        public int BladeSections;

        /// <summary>Перша і остання секції, зайняті хрестовиною. Нулі, якщо хрестовини немає.</summary>
        [ProtoMember(5)]
        public int FrogFrom;
        [ProtoMember(6)]
        public int FrogTo;

        /// <summary>
        /// Для кожної секції з Members: чи друга гілка лежить з боку її першої рейки (з боку нормалі). Там
        /// у гілки внутрішня рейка. Див. SwitchSides.
        /// </summary>
        [ProtoMember(7)]
        public List<bool> InnerFirst = new List<bool>();

        /// <summary>До якої секції включно гілка це ще стрілка: тут рейки мають стояти, щоб стрілка працювала.</summary>
        public int ZoneSections => Math.Max(Math.Max(PairSections, OverlapSections), FrogTo);

        /// <summary>Секція гілки з таким номером від стрілки або null, якщо гілка коротша.</summary>
        public SectionLink Member(int number)
        {
            return number >= 1 && number <= Members.Count ? Members[number - 1] : null;
        }

        /// <summary>
        /// Чи внутрішня рейка секції з таким номером це її перша рейка (з боку нормалі); null, якщо такої
        /// секції в записі немає.
        /// </summary>
        public bool? InnerIsFirst(int number)
        {
            return number >= 1 && number <= InnerFirst.Count ? InnerFirst[number - 1] : (bool?)null;
        }

        /// <summary>Чи шпала секції з таким номером спільна із секцією другої гілки під тим самим номером.</summary>
        public bool SharesSleeper(int number)
        {
            return number >= 1 && number <= PairSections;
        }

        /// <summary>
        /// Чи на секції з таким номером внутрішню рейку гілки заміняє деталь стрілки: гостряк або хрестовина.
        /// Звичайна рейка там не малюється.
        /// </summary>
        public bool ReplacesInnerRail(int number)
        {
            if (number < 1) return false;
            if (number <= BladeSections) return true;
            return FrogFrom > 0 && number >= FrogFrom && number <= FrogTo;
        }

        public bool SameAs(SwitchBranch other)
        {
            if (other == null || PairSections != other.PairSections || OverlapSections != other.OverlapSections
                || BladeSections != other.BladeSections || FrogFrom != other.FrogFrom || FrogTo != other.FrogTo
                || Members.Count != other.Members.Count || InnerFirst.Count != other.InnerFirst.Count) return false;
            for (int n = 0; n < InnerFirst.Count; n++)
            {
                if (InnerFirst[n] != other.InnerFirst[n]) return false;
            }
            return StartsWith(other);
        }

        /// <summary>Чи гілка починається тими самими секціями, що й other, у тому самому порядку.</summary>
        public bool StartsWith(SwitchBranch other)
        {
            if (other.Members.Count > Members.Count) return false;
            for (int n = 0; n < other.Members.Count; n++)
            {
                SectionLink mine = Members[n], theirs = other.Members[n];
                if (mine.ChunkX != theirs.ChunkX || mine.ChunkY != theirs.ChunkY || mine.ChunkZ != theirs.ChunkZ
                    || mine.Index != theirs.Index || mine.AtStart != theirs.AtStart) return false;
            }
            return true;
        }

        [ProtoAfterDeserialization]
        private void OnDeserialized()
        {
            if (Members == null) Members = new List<SectionLink>();
            if (InnerFirst == null) InnerFirst = new List<bool>();
        }
    }

    /// <summary>
    /// Яка стрілка за формою: котра гілка основна, або це розвилка, де основної немає.
    /// </summary>
    public enum SwitchShape
    {
        /// <summary>Запис зроблено до того, як записи стрілок знали форму: його треба перерахувати.</summary>
        Unknown = 0,
        /// <summary>Основна перша гілка: друга з неї відходить.</summary>
        MainFirst = 1,
        /// <summary>Основна друга гілка.</summary>
        MainSecond = 2,
        /// <summary>Гілки розходяться в різні боки приблизно однаково: основної немає.</summary>
        Fork = 3
    }

    /// <summary>
    /// Стрілка: кінець секції, з якого колія розходиться на дві гілки. Зберігається в чанку своєї спільної
    /// секції разом із тим, що про неї можна порахувати наперед, щоб ніхто не шукав цього щоразу ходінням
    /// по зв'язках. Рахує її SwitchBuilder, коли стрілка з'являється або коли колія в її зоні змінюється.
    /// Положення стрілки і час останнього переведення лежать на самій спільній секції: їх на кожному стику
    /// читає вагонетка.
    /// </summary>
    [ProtoContract]
    public class RailSwitch
    {
        /// <summary>Спільна секція і її кінець, з якого виходять гілки.</summary>
        [ProtoMember(1)]
        public int TrunkIndex;
        [ProtoMember(2)]
        public bool AtStart;

        /// <summary>
        /// Дві гілки в тому самому порядку, що й зв'язки спільної секції; номер гілки це положення стрілки,
        /// в якому вона увімкнена. Котра з них основна, каже Shape.
        /// </summary>
        [ProtoMember(3)]
        public List<SwitchBranch> Branches = new List<SwitchBranch>();

        /// <summary>
        /// Котра гілка основна, або це розвилка. Основна та, що менше відвертає від напрямку колії на стику
        /// до кінця тіла стрілки (SwitchBuilder.ShapeOf). Від цього залежить і розкладка брусів, і бік важеля.
        /// </summary>
        [ProtoMember(6)]
        public SwitchShape Shape;

        /// <summary>
        /// Стрілку зібрано: обидві гілки доходять до хрестовини, і в зоні стрілки на кожній секції стоять обидві
        /// рейки і шпала. Лише тоді її можна переводити і малювати стрілкою, з гостряками, хрестовиною і власними
        /// брусами. Доки бракує хоч однієї деталі чи хрестовини, це дві колії, покладені одна на одну: кожна
        /// секція малює свої рейки і свою шпалу.
        /// </summary>
        [ProtoMember(4)]
        public bool Complete;

        /// <summary>
        /// Усі секції зони вдалося прочитати. Якщо ні (чанк гілки не був завантажений), запис треба
        /// перерахувати, коли чанк з'явиться. Доти в ньому або те, що вдалося прочитати, і стрілка вважається
        /// незібраною, або дані попереднього повного розрахунку: див. SwitchBuilder.Rebuild.
        /// </summary>
        [ProtoMember(5)]
        public bool Resolved;

        /// <summary>
        /// Якою версією розрахунку пораховано запис.
        /// </summary>
        [ProtoMember(7)]
        public int Formula;

        /// <summary>
        /// Версія розрахунку стрілки: збільшується, коли міняється те, як SwitchBuilder рахує запис (гостряк,
        /// хрестовина, боки гілок, форма). Запис з іншою версією застарілий і перераховується, коли
        /// завантажується його чанк. Забути збільшити не дасть тест-еталон RecordFormulaTests.
        /// </summary>
        public const int CurrentFormula = 2;

        /// <summary>Ключ стрілки в чанку: у секції два кінці, і на кожному може бути своя.</summary>
        public int Key => KeyOf(TrunkIndex, AtStart);

        public static int KeyOf(int trunkIndex, bool atStart)
        {
            return trunkIndex * 2 + (atStart ? 0 : 1);
        }

        public bool SameAs(RailSwitch other)
        {
            if (other == null || TrunkIndex != other.TrunkIndex || AtStart != other.AtStart || Complete != other.Complete
                || Resolved != other.Resolved || Formula != other.Formula || Shape != other.Shape || Branches.Count != other.Branches.Count) return false;
            for (int k = 0; k < Branches.Count; k++)
            {
                if (!Branches[k].SameAs(other.Branches[k])) return false;
            }
            return true;
        }

        /// <summary>
        /// Чи запис описує ту саму колію, що й partial, і знає про неї не менше: кожна гілка partial це початок
        /// відповідної гілки цього запису.
        /// </summary>
        public bool Covers(RailSwitch partial)
        {
            if (partial == null || TrunkIndex != partial.TrunkIndex || AtStart != partial.AtStart
                || Branches.Count != partial.Branches.Count) return false;
            for (int k = 0; k < Branches.Count; k++)
            {
                if (!Branches[k].StartsWith(partial.Branches[k])) return false;
            }
            return true;
        }

        /// <summary>
        /// Номер основної гілки; на розвилці основною вважається перша, коли треба вибрати одну (бік важеля).
        /// </summary>
        public int Main => Shape == SwitchShape.MainSecond ? 1 : 0;

        /// <summary>
        /// Запис пораховано іншою версією розрахунку: його треба перерахувати.
        /// </summary>
        public bool Outdated => Formula != CurrentFormula;

        /// <summary>
        /// Чи запис посилається на секцію, якої вже немає: її чанк завантажений, а секції з таким номером у ньому
        /// немає. Так буває, коли секцію прибрали, поки чанк зі стрілкою не був завантажений.
        /// </summary>
        public bool LostMember(RailDataSession session)
        {
            return Branches.Exists(branch => branch.Members.Exists(session.Lost));
        }

        /// <summary>
        /// Гілка, на яку вказує посилання секції, або null, якщо такої в записі немає: стрілку пораховано
        /// неповністю або посилання застаріле.
        /// </summary>
        public SwitchBranch BranchOf(SwitchRef place)
        {
            return Branches.Count == 2 && place.Branch >= 0 && place.Branch < 2 ? Branches[place.Branch] : null;
        }

        /// <summary>
        /// Секція другої гілки під тим самим номером, що й секція з посиланням place: її сусідка по стрілці.
        /// null, якщо друга гілка коротша.
        /// </summary>
        public SectionLink Opposite(SwitchRef place)
        {
            return BranchOf(place) == null ? null : Branches[1 - place.Branch].Member(place.Number);
        }

        [ProtoAfterDeserialization]
        private void OnDeserialized()
        {
            if (Branches == null) Branches = new List<SwitchBranch>();
        }
    }

    /// <summary>
    /// Рахує стрілку з колії. Чиста геометрія і зв'язки секцій: сусідні секції дістає через resolve,
    /// тому працює однаково на сервері, на клієнті й у тестах.
    /// </summary>
    public static class SwitchBuilder
    {
        /// <summary>
        /// Стрілка на кінці atStart секції trunk, або null, якщо з цього кінця не виходить рівно дві колії.
        /// </summary>
        public static RailSwitch Build(Section trunk, bool atStart, Func<SectionLink, Section> resolve)
        {
            return Build(trunk, atStart, resolve, out _);
        }

        /// <summary>
        /// Рахує стрілку заново, маючи її попередній запис. Якщо всю стрілку прочитати не вдалося (чанк гілки
        /// не завантажений), а попередній запис описує ту саму колію і знає про неї більше, його дані
        /// лишаються: колія в незавантаженому чанку змінитися не могла. Запис при цьому позначений як
        /// недорахований, а стрілка стає незібраною, лише якщо деталі бракує в прочитаній частині.
        /// </summary>
        public static RailSwitch Rebuild(RailSwitch previous, Section trunk, bool atStart, Func<SectionLink, Section> resolve)
        {
            RailSwitch built = Build(trunk, atStart, resolve, out bool partsInPlace);
            if (built == null || built.Resolved || previous == null || previous.Outdated || !previous.Covers(built)) return built;

            built.Branches = previous.Branches;
            // Форма рахується з гілок (ShapeOf), тож береться разом з ними: з прочитаної частини вона вийшла б
            // іншою, а то й невідомою, якщо не прочитано навіть першої секції гілки
            built.Shape = previous.Shape;
            built.Complete = previous.Complete && partsInPlace;
            return built;
        }

        // partsInPlace: у прочитаній частині зони стрілки на кожній секції стоять обидві рейки і шпала
        private static RailSwitch Build(Section trunk, bool atStart, Func<SectionLink, Section> resolve, out bool partsInPlace)
        {
            partsInPlace = true;
            List<SectionLink> links = trunk.GetLinks(atStart);
            if (links.Count != 2) return null;

            var result = new RailSwitch { TrunkIndex = trunk.IndexInChunk, AtStart = atStart, Resolved = true, Formula = RailSwitch.CurrentFormula };

            Section[] first = { resolve(links[0]), resolve(links[1]) };
            if (first[0] == null || first[1] == null)
            {
                // Перша ж секція якоїсь гілки недоступна: про стрілку відомо лише, що вона є
                result.Resolved = false;
                result.Branches.Add(new SwitchBranch());
                result.Branches.Add(new SwitchBranch());
                return result;
            }

            // З якого боку від кожної секції гілки лежить друга: від цього залежить уся геометрія стрілки
            var sides = new List<bool>[2];
            SwitchSides.Of(trunk, atStart, resolve, out sides[0], out sides[1]);

            for (int k = 0; k < 2; k++)
            {
                Section mine = first[k], other = first[1 - k];
                bool mineAtStart = links[k].AtStart, otherAtStart = links[1 - k].AtStart;

                BranchGeometry geometry = SwitchGeometry.Branch(mine, mineAtStart, sides[k][0], other, otherAtStart, sides[1 - k][0], resolve);
                double heelAt = SwitchGeometry.HeelAt(geometry, mine.TrackWidth);
                // Без хрестовини це не стрілка, а дві колії одна на одній: так буває, коли гілку розібрали до неї.
                // Покласти таку стрілку не можна (SectionLinker.FindSwitchProblem), і зібраною вона не вважається.
                // Якщо ж колію далі не прочитати, хрестовина може бути там: запис чекає на чанк
                if (geometry.Throat < 0)
                {
                    if (geometry.Cut) result.Resolved = false;
                    else partsInPlace = false;
                }

                var branch = new SwitchBranch
                {
                    PairSections = SwitchZone.PairSections(mine, mineAtStart, other, otherAtStart, resolve),
                    OverlapSections = SwitchZone.OverlapSections(trunk, atStart, mine, resolve),
                    BladeSections = SwitchGeometry.BladeSections(heelAt, geometry),
                    FrogFrom = geometry.Throat >= 0 ? geometry.HideFrom : 0,
                    FrogTo = geometry.Throat >= 0 ? geometry.HideTo : 0
                };

                // Секції гілки: зона стрілки і ще кілька секцій за нею, закритих для нових стрілок
                int zone = branch.ZoneSections;
                int length = zone + SwitchZone.ClearSections;

                TrackWalker walk = TrackWalker.Entering(mine, mineAtStart, resolve);
                for (int number = 1; number <= length; number++)
                {
                    Section current = walk.Section;
                    branch.Members.Add(new SectionLink(current, walk.Entered));
                    if (number <= zone && (!current.RailsInstalled || !current.SleeperInstalled)) partsInPlace = false;

                    // Гілка скінчилася або сама розходиться: далі це вже не ця стрілка
                    if (walk.Next()) continue;

                    // Колія далі є, але прочитати її не вдалося
                    if (walk.Stop == WalkStop.Unread && number < length) result.Resolved = false;
                    break;
                }
                branch.InnerFirst = sides[k].GetRange(0, Math.Min(branch.Members.Count, sides[k].Count));

                result.Branches.Add(branch);
            }

            result.Shape = ShapeOf(trunk, atStart, result.Branches, resolve);
            result.Complete = partsInPlace && result.Resolved;
            return result;
        }

        /// <summary>
        /// У скільки разів одна гілка може відвертати крутіше за другу, щоб це ще була розвилка без основної колії.
        /// </summary>
        public const double ForkTurnRatio = 1.5;

        /// <summary>
        /// Форма стрілки: наскільки кожна гілка відвернула від напрямку колії на стику до кінця тіла стрілки
        /// (ZoneSections, а якщо гілку прочитано не всю, до останньої прочитаної секції). Основна та, що менше.
        /// Розвилка, коли вони відвертають у різні боки і одна не більш ніж у ForkTurnRatio разів крутіше.
        /// Міряється по тілу стрілки, а не по шпалах чи довільній кількості секцій: тоді форма не залежить
        /// від того, де закінчуються шпали за стрілкою.
        /// </summary>
        internal static SwitchShape ShapeOf(Section trunk, bool atStart, List<SwitchBranch> branches, Func<SectionLink, Section> resolve)
        {
            if (branches.Count != 2) return SwitchShape.Unknown;

            Vec3f outward = trunk.GetOutwardDirection(atStart);
            double[] turn = new double[2];
            for (int k = 0; k < 2; k++)
            {
                SwitchBranch branch = branches[k];
                Section far = null;
                bool entered = true;
                for (int n = Math.Min(Math.Max(branch.ZoneSections, 1), branch.Members.Count); n >= 1 && far == null; n--)
                {
                    SectionLink member = branch.Member(n);
                    far = resolve(member);
                    entered = member.AtStart;
                }
                if (far == null) return SwitchShape.Unknown;

                Vec3d from = far.GetEndPosition(entered), to = far.GetEndPosition(!entered);
                double dx = to.X - from.X, dz = to.Z - from.Z;
                turn[k] = Math.Atan2(outward.X * dz - outward.Z * dx, outward.X * dx + outward.Z * dz);
            }

            double gentle = Math.Min(Math.Abs(turn[0]), Math.Abs(turn[1])), steep = Math.Max(Math.Abs(turn[0]), Math.Abs(turn[1]));
            if (turn[0] * turn[1] < 0 && steep <= gentle * ForkTurnRatio) return SwitchShape.Fork;
            return Math.Abs(turn[0]) <= Math.Abs(turn[1]) ? SwitchShape.MainFirst : SwitchShape.MainSecond;
        }
    }
}
