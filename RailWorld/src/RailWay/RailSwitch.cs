using ProtoBuf;
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
        public Vintagestory.API.MathTools.Vec3i Chunk => new Vintagestory.API.MathTools.Vec3i(ChunkX, ChunkY, ChunkZ);

        public bool SameAs(SwitchRef other)
        {
            return other != null && ChunkX == other.ChunkX && ChunkY == other.ChunkY && ChunkZ == other.ChunkZ
                && Key == other.Key && Branch == other.Branch && Number == other.Number;
        }

        public bool PointsTo(Vintagestory.API.MathTools.Vec3i chunk, int key)
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

        /// <summary>До якої секції включно гілка це ще стрілка: тут рейки мають стояти, щоб стрілка працювала.</summary>
        public int ZoneSections => Math.Max(Math.Max(PairSections, OverlapSections), FrogTo);

        /// <summary>Секція гілки з таким номером від стрілки або null, якщо гілка коротша.</summary>
        public SectionLink Member(int number)
        {
            return number >= 1 && number <= Members.Count ? Members[number - 1] : null;
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
                || Members.Count != other.Members.Count) return false;
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
        }
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

        /// <summary>Дві гілки в тому самому порядку, що й зв'язки спільної секції: перша це основна.</summary>
        [ProtoMember(3)]
        public List<SwitchBranch> Branches = new List<SwitchBranch>();

        /// <summary>
        /// Стрілку зібрано: на обох гілках у її зоні на кожній секції стоять обидві рейки і шпала. Лише тоді
        /// її можна переводити і малювати стрілкою, з гостряками, хрестовиною і власними брусами. Доки бракує
        /// хоч однієї деталі, це дві колії, покладені одна на одну: кожна секція малює свої рейки і свою шпалу.
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

        /// <summary>Ключ стрілки в чанку: у секції два кінці, і на кожному може бути своя.</summary>
        public int Key => KeyOf(TrunkIndex, AtStart);

        public static int KeyOf(int trunkIndex, bool atStart)
        {
            return trunkIndex * 2 + (atStart ? 0 : 1);
        }

        public bool SameAs(RailSwitch other)
        {
            if (other == null || TrunkIndex != other.TrunkIndex || AtStart != other.AtStart || Complete != other.Complete
                || Resolved != other.Resolved || Branches.Count != other.Branches.Count) return false;
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
            if (built == null || built.Resolved || previous == null || !previous.Covers(built)) return built;

            built.Branches = previous.Branches;
            built.Complete = previous.Complete && partsInPlace;
            return built;
        }

        // partsInPlace: у прочитаній частині зони стрілки на кожній секції стоять обидві рейки і шпала
        private static RailSwitch Build(Section trunk, bool atStart, Func<SectionLink, Section> resolve, out bool partsInPlace)
        {
            partsInPlace = true;
            List<SectionLink> links = trunk.GetLinks(atStart);
            if (links.Count != 2) return null;

            var result = new RailSwitch { TrunkIndex = trunk.IndexInChunk, AtStart = atStart, Resolved = true };

            Section[] first = { resolve(links[0]), resolve(links[1]) };
            if (first[0] == null || first[1] == null)
            {
                // Перша ж секція якоїсь гілки недоступна: про стрілку відомо лише, що вона є
                result.Resolved = false;
                result.Branches.Add(new SwitchBranch());
                result.Branches.Add(new SwitchBranch());
                return result;
            }

            for (int k = 0; k < 2; k++)
            {
                Section mine = first[k], other = first[1 - k];
                bool mineAtStart = links[k].AtStart, otherAtStart = links[1 - k].AtStart;

                BranchGeometry geometry = SwitchGeometry.Branch(mine, mineAtStart, other, otherAtStart, resolve);
                double heelAt = SwitchZone.CrossingDistance(mine, mineAtStart, other, otherAtStart, resolve, SwitchZone.BladeHeelSeparation);

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

                Section current = mine;
                bool entered = mineAtStart;
                for (int number = 1; number <= length; number++)
                {
                    branch.Members.Add(new SectionLink(current, entered));
                    if (number <= zone && (!current.RailsInstalled || !current.SleeperInstalled)) partsInPlace = false;

                    List<SectionLink> ahead = current.GetLinks(!entered);
                    // Гілка скінчилася або сама розходиться: далі це вже не ця стрілка
                    if (ahead.Count != 1) break;

                    Section next = resolve(ahead[0]);
                    if (next == null)
                    {
                        // Колія далі є, але прочитати її не вдалося
                        if (number < length) result.Resolved = false;
                        break;
                    }
                    entered = ahead[0].AtStart;
                    current = next;
                }

                result.Branches.Add(branch);
            }

            result.Complete = partsInPlace && result.Resolved;
            return result;
        }
    }
}
