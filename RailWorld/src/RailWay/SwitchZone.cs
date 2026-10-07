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

        /// <summary>
        /// Скільки секцій гілки, рахуючи від стрілки, лежить на сусідній гілці: поки вісь однієї ближча до осі
        /// іншої, ніж ширина колії, хоч одна рейка лежить між рейками сусідньої.
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
                if (sideways >= other.TrackWidth) break;
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
