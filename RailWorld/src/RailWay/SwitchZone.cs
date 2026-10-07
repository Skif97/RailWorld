using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Зона стрілки: ділянка колії навколо неї, де нову стрілку ставити не можна.
    /// Це частина спільної колії перед стрілкою, уся ділянка, де дві гілки ще лежать одна на одній
    /// (рейка однієї між рейками іншої), і ще трохи кожної гілки після того, як вони розійшлися.
    /// Працює і на клієнті, і на сервері: секції сусідів дістає через resolve.
    /// </summary>
    public static class SwitchZone
    {
        /// <summary>
        /// Скільки секцій спільної колії перед стрілкою і скільки секцій кожної гілки після розходження
        /// закриті для нових стрілок.
        /// </summary>
        public const int ClearSections = 5;

        // Як далеко від стику шукати стрілку, в секціях. Гілки пологої стрілки лежать одна на одній довго
        private const int MaxWalk = 96;

        /// <summary>
        /// Чи лежить стик на цьому кінці секції в зоні якоїсь стрілки.
        /// </summary>
        public static bool IsBlocked(Section section, bool atStart, Func<SectionLink, Section> resolve)
        {
            if (section == null || resolve == null) return false;

            // Від стику колія йде в два боки: через саму секцію і геть від неї
            return FindsSwitch(section, !atStart, 1, resolve) || FindsSwitch(section, atStart, 0, resolve);
        }

        // Іде по колії, виходячи із секції через кінець exitAtStart. passed це скільки секцій уже пройдено від стику
        private static bool FindsSwitch(Section section, bool exitAtStart, int passed, Func<SectionLink, Section> resolve)
        {
            for (int guard = 0; guard < MaxWalk; guard++)
            {
                List<SectionLink> links = section.GetLinks(exitAtStart);
                if (links.Count == 0) return false;

                // Попереду колія розходиться: ми на спільній колії перед стрілкою
                if (links.Count >= 2) return passed <= ClearSections;

                Section next = resolve(links[0]);
                if (next == null) return false;

                // Зайшли в секцію через кінець, з якого виходять дві колії: ми на гілці, а це її стрілка.
                // Гілка закрита, поки лежить на сусідній, і ще кілька секцій після
                if (next.HasSwitch(links[0].AtStart))
                {
                    return passed <= OverlapSections(next, links[0].AtStart, section, resolve) + ClearSections;
                }

                section = next;
                exitAtStart = !links[0].AtStart;
                passed++;
            }
            return false;
        }

        // Як далеко від секції шукати стрілку, до якої вона належить, у секціях
        private const int MaxPairWalk = 48;

        /// <summary>
        /// Чи лежить секція в стрілці, і якщо так, то яка секція сусідньої гілки стоїть із нею в парі.
        /// У стрілці шпали двох гілок перетинаються, тому замість двох шпал там малюється одна спільна,
        /// на обидві колії. Пара це секції з однаковим номером від стрілки: перша з першою, друга з другою.
        /// primary каже, котра з двох малює спільну шпалу: та, що на першій гілці стрілки; друга свою не малює.
        /// fromEnd каже, скільки пар іще лежить між цією і краєм стрілки: 0 для останньої пари, 1 для передостанньої;
        /// далі не рахується, для решти це 2. number це номер пари від стрілки, рахуючи з одиниці.
        /// </summary>
        public static bool FindPair(Section section, Func<SectionLink, Section> resolve, out Section partner, out bool primary, out int fromEnd, out int number)
        {
            partner = null;
            primary = false;
            fromEnd = 2;
            number = 0;
            if (section == null || resolve == null) return false;

            // Стрілка може бути з будь-якого боку секції
            return FindPairThrough(section, true, resolve, ref partner, ref primary, ref fromEnd, ref number)
                || FindPairThrough(section, false, resolve, ref partner, ref primary, ref fromEnd, ref number);
        }

        // Секція гілки з таким номером від стрілки; first це перша секція гілки, entered яким кінцем вона до стрілки
        private static Section Nth(Section first, bool entered, int number, Func<SectionLink, Section> resolve)
        {
            Section current = first;
            for (int k = 1; k < number && current != null; k++)
            {
                List<SectionLink> ahead = current.GetLinks(!entered);
                if (ahead.Count != 1) return null;
                entered = ahead[0].AtStart;
                current = resolve(ahead[0]);
            }
            return current;
        }

        // Чи перетинаються шпали двох секцій: їхні середини ближчі, ніж пів шпали однієї плюс пів шпали іншої
        private static bool SleepersOverlap(Section a, Section b)
        {
            if (a == null || b == null) return false;
            Vec3d p = a.GetGlobalPos(), q = b.GetGlobalPos();
            double apart = Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Z - q.Z) * (p.Z - q.Z));
            return apart < (a.SleeperLength + b.SleeperLength) / 2;
        }

        private static bool FindPairThrough(Section section, bool exitAtStart, Func<SectionLink, Section> resolve, ref Section partner, ref bool primary, ref int fromEnd, ref int pairNumber)
        {
            Section current = section;
            bool exit = exitAtStart;

            for (int number = 1; number <= MaxPairWalk; number++)
            {
                List<SectionLink> links = current.GetLinks(exit);
                // Кінець колії або стрілка попереду: у цей бік секція не на гілці
                if (links.Count != 1) return false;

                Section next = resolve(links[0]);
                if (next == null) return false;

                if (next.HasSwitch(links[0].AtStart))
                {
                    // Дійшли до стрілки з боку гілки. number це номер нашої секції на ній
                    List<SectionLink> branches = next.GetLinks(links[0].AtStart);
                    SectionLink mine = null, theirs = null;
                    foreach (SectionLink link in branches)
                    {
                        if (link.PointsTo(current)) mine = link;
                        else theirs = link;
                    }
                    if (mine == null || theirs == null) return false;

                    // Секція з тим самим номером на сусідній гілці. У парі вони, лише поки їхні шпали перетинаються
                    Section theirFirst = resolve(theirs);
                    Section other = Nth(theirFirst, theirs.AtStart, number, resolve);
                    if (!SleepersOverlap(section, other)) return false;

                    // Скільки пар далі, до краю стрілки
                    fromEnd = 0;
                    for (int k = 1; k <= 2; k++)
                    {
                        Section mineAhead = Nth(current, mine.AtStart, number + k, resolve);
                        Section theirsAhead = Nth(theirFirst, theirs.AtStart, number + k, resolve);
                        if (!SleepersOverlap(mineAhead, theirsAhead)) break;
                        fromEnd = k;
                    }

                    partner = other;
                    primary = branches[0] == mine;
                    pairNumber = number;
                    return true;
                }

                current = next;
                exit = !links[0].AtStart;
            }
            return false;
        }

        /// <summary>
        /// Скільки секцій гілки, рахуючи від стрілки, лежить на сусідній гілці: поки вісь однієї ближча до осі
        /// іншої, ніж довжина шпали, шпали двох гілок перетинаються, і це ще стрілка.
        /// trunk і trunkAtStart це кінець, з якого колії розходяться, branch це перша секція потрібної гілки.
        /// </summary>
        private static int OverlapSections(Section trunk, bool trunkAtStart, Section branch, Func<SectionLink, Section> resolve)
        {
            SectionLink mine = null, theirs = null;
            foreach (SectionLink link in trunk.GetLinks(trunkAtStart))
            {
                if (link.PointsTo(branch)) mine = link;
                else theirs = link;
            }
            if (mine == null || theirs == null) return 0;

            Section other = resolve(theirs);
            if (other == null) return 0;
            bool otherEntered = theirs.AtStart;

            Section current = branch;
            bool entered = mine.AtStart;
            int count = 0;

            for (int guard = 0; guard < MaxWalk; guard++)
            {
                // Дальній кінець чергової секції гілки: де він відносно сусідньої гілки
                Vec3d p = current.GetEndPosition(!entered);

                bool otherEnded = false;
                for (int hop = 0; hop < 8; hop++)
                {
                    if (RouteCurve.Along(other, otherEntered, p, out double length, out _) <= length + 1e-6) break;

                    SectionLink link = other.GetActiveLink(!otherEntered);
                    Section next = link == null ? null : resolve(link);
                    if (next == null)
                    {
                        otherEnded = true;
                        break;
                    }
                    other = next;
                    otherEntered = link.AtStart;
                }
                if (otherEnded) break;

                RouteCurve.Along(other, otherEntered, p, out _, out double sideways);
                if (sideways >= other.SleeperLength) break;
                count++;

                // Далі по своїй гілці. Якщо вона сама розходиться, рахувати далі нема чого
                List<SectionLink> ahead = current.GetLinks(!entered);
                if (ahead.Count != 1) break;
                Section following = resolve(ahead[0]);
                if (following == null) break;
                current = following;
                entered = ahead[0].AtStart;
            }

            return count;
        }
    }
}
