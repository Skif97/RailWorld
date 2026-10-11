using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Тіло стрілки: ділянка колії, яку стрілка займає своїми деталями. Починається на стику, з якого колія
    /// розходиться (там вістря гостряків), і тягнеться по обох гілках, поки вони лежать одна на одній:
    /// їхні габарити перекриваються (Footprint). Спільна колія перед стиком до тіла
    /// не належить: там звичайна колія.
    /// На тіло стрілки не може накластися ні тіло іншої стрілки, ні вузол перетину з іншою колією.
    /// Працює і на клієнті, і на сервері: секції сусідів дістає через resolve.
    /// </summary>
    public static class SwitchZone
    {
        /// <summary>
        /// На скільки секцій далі за тіло стрілки запис про неї стежить за кожною гілкою: зміна колії
        /// одразу за тілом може зсунути його край, і стрілку треба перерахувати.
        /// </summary>
        public const int ClearSections = 5;

        /// <summary>
        /// Не далі ніж за стільки секцій від відкритого краю колії точка маршруту стає на сам край.
        /// </summary>
        public const int OpenEndSections = 3;

        // Як далеко від стику шукати стрілку, в секціях. Гілки пологої стрілки лежать одна на одній довго
        private const int MaxWalk = 96;

        /// <summary>
        /// Чи лежить стик на цьому кінці секції в тілі якоїсь стрілки: це сам стик, з якого вона розходиться,
        /// або стик на її гілці, поки гілки лежать одна на одній.
        /// </summary>
        public static bool JointInBody(Section section, bool atStart, Func<SectionLink, Section> resolve)
        {
            if (section == null || resolve == null) return false;

            // Від стику колія йде в два боки: через саму секцію і геть від неї
            return FindsSwitch(section, !atStart, 1, resolve) || FindsSwitch(section, atStart, 0, resolve);
        }

        /// <summary>
        /// Чи секція лежить у тілі якоїсь стрілки: обидва її стики в ньому. Остання секція спільної колії
        /// перед стрілкою торкається тіла лише одним стиком і до нього не належить.
        /// </summary>
        public static bool InBody(Section section, Func<SectionLink, Section> resolve)
        {
            return JointInBody(section, true, resolve) && JointInBody(section, false, resolve);
        }

        /// <summary>
        /// Відкритий край колії, якщо він не далі ніж за OpenEndSections секцій від цієї секції, рахуючи її саму:
        /// кінець секції, з якого колія нікуди не йде. Відгалуження так близько до краю дало б стрілку з куцим
        /// хвостиком, тому маршрут, причеплений тут, стає на сам край. near це місце, куди цілився гравець:
        /// якщо країв поруч два, береться ближчий до нього. false, якщо краю поруч немає або до нього стрілка.
        /// </summary>
        public static bool FindOpenEnd(Section section, Vec3d near, Func<SectionLink, Section> resolve, out Section endSection, out bool endAtStart)
        {
            endSection = null;
            endAtStart = false;
            double best = double.MaxValue;

            for (int side = 0; side < 2; side++)
            {
                Section current = section;
                bool exitAtStart = side == 0;

                for (int passed = 0; passed < OpenEndSections; passed++)
                {
                    List<SectionLink> links = current.GetLinks(exitAtStart);
                    if (links.Count == 0)
                    {
                        double away = current.GetEndPosition(exitAtStart).SquareDistanceTo(near);
                        if (away < best)
                        {
                            best = away;
                            endSection = current;
                            endAtStart = exitAtStart;
                        }
                        break;
                    }

                    // Попереду стрілка, з будь-якого її боку: це вже не край колії
                    if (links.Count != 1) break;
                    Section next = resolve(links[0]);
                    if (next == null || next.HasSwitch(links[0].AtStart)) break;

                    current = next;
                    exitAtStart = !links[0].AtStart;
                }
            }
            return endSection != null;
        }

        // Іде по колії, виходячи із секції через кінець exitAtStart. passed це скільки секцій уже пройдено від стику
        private static bool FindsSwitch(Section section, bool exitAtStart, int passed, Func<SectionLink, Section> resolve)
        {
            for (int guard = 0; guard < MaxWalk; guard++)
            {
                List<SectionLink> links = section.GetLinks(exitAtStart);
                if (links.Count == 0) return false;

                // Попереду колія розходиться: ми на спільній колії перед стрілкою. Тіло починається на самому
                // стику, тож у ньому лише той стик, з якого й почали
                if (links.Count >= 2) return passed == 0;

                Section next = resolve(links[0]);
                if (next == null) return false;

                // Зайшли в секцію через кінець, з якого виходять дві колії: ми на гілці, а це її стрілка.
                // Гілка в тілі стрілки, поки лежить на сусідній
                if (next.HasSwitch(links[0].AtStart))
                {
                    return passed <= OverlapSections(next, links[0].AtStart, section, resolve);
                }

                section = next;
                exitAtStart = !links[0].AtStart;
                passed++;
            }
            return false;
        }

        // На скільки секцій від стрілки дві гілки порівнюються між собою
        private const int MaxPairWalk = 48;

        /// <summary>
        /// На скільки блоків рейка гостряка має відійти від рамної рейки, щоб гостряк скінчився: ширина головки
        /// рейки плюс жолоб для гребеня колеса. Так визначається довжина гостряка і на справжній залізниці:
        /// що пологіше розходяться колії, то далі це місце і то довший гостряк.
        /// </summary>
        public const double BladeHeelSeparation = 0.125;

        /// <summary>
        /// Де осі двох гілок розійшлися на apartNeeded блоків, у блоках від стрілки. Внутрішня рейка гілки
        /// відходить від рамної рейки сусідньої рівно на стільки ж, на скільки розійшлися осі.
        /// Гілки проходяться парами секцій від стрілки.
        /// </summary>
        internal static double CrossingDistance(Section first, bool firstEntered, Section second, bool secondEntered,
            Func<SectionLink, Section> resolve, double apartNeeded)
        {
            double previousApart = 0, previousAlong = 0;
            Section a = first, b = second;
            bool aEntered = firstEntered, bEntered = secondEntered;

            for (int n = 1; n <= MaxPairWalk && a != null && b != null; n++)
            {
                // Середина секції з номером n лежить за (n - 0.5) секції від стрілки, а секція це пів блока
                double along = (n - 0.5) * 0.5;
                Vec3d p = a.GetGlobalPos(), q = b.GetGlobalPos();
                double apart = Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Z - q.Z) * (p.Z - q.Z));
                double width = apartNeeded;

                if (apart >= width)
                {
                    double step = apart - previousApart;
                    double t = step > 1e-9 ? (width - previousApart) / step : 0;
                    return previousAlong + (along - previousAlong) * GameMath.Clamp(t, 0, 1);
                }

                previousApart = apart;
                previousAlong = along;

                List<SectionLink> aheadA = a.GetLinks(!aEntered), aheadB = b.GetLinks(!bEntered);
                if (aheadA.Count != 1 || aheadB.Count != 1) break;
                aEntered = aheadA[0].AtStart;
                bEntered = aheadB[0].AtStart;
                a = resolve(aheadA[0]);
                b = resolve(aheadB[0]);
            }

            // Гілки скінчилися, так і не розійшовшись: гостряк на всю пройдену довжину
            return previousAlong;
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

        /// <summary>
        /// Скільки перших секцій двох гілок стоять у парі: габарити секцій з однаковим номером від стрілки
        /// перекриваються, і замість двох шпал малюється одна спільна. first і second це перші секції гілок.
        /// </summary>
        internal static int PairSections(Section first, bool firstEntered, Section second, bool secondEntered, Func<SectionLink, Section> resolve)
        {
            int pairs = 0;
            for (int number = 1; number <= MaxPairWalk; number++)
            {
                Section a = Nth(first, firstEntered, number, resolve), b = Nth(second, secondEntered, number, resolve);
                if (a == null || b == null || !Footprint.Overlap(a, b)) break;
                pairs = number;
            }
            return pairs;
        }

        /// <summary>
        /// Скільки секцій гілки, рахуючи від стрілки, лежить на сусідній гілці: поки габарити двох гілок
        /// перекриваються, це ще стрілка.
        /// trunk і trunkAtStart це кінець, з якого колії розходяться, branch це перша секція потрібної гілки.
        /// </summary>
        internal static int OverlapSections(Section trunk, bool trunkAtStart, Section branch, Func<SectionLink, Section> resolve)
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
            Section behind = null;

            for (int guard = 0; guard < MaxWalk; guard++)
            {
                // Сусідню гілку ведемо поруч: до секції, навпроти якої лежить дальній кінець чергової секції цієї
                Vec3d p = current.GetEndPosition(!entered);
                for (int hop = 0; hop < 8; hop++)
                {
                    if (RouteCurve.Along(other, otherEntered, p, out double length, out _) <= length + 1e-6) break;

                    SectionLink link = other.GetActiveLink(!otherEntered);
                    Section next = link == null ? null : resolve(link);
                    if (next == null) break;
                    behind = other;
                    other = next;
                    otherEntered = link.AtStart;
                }

                // Габарит секції може зачепити й сусідів тієї, що навпроти: гілки йдуть під кутом
                SectionLink aheadLink = other.GetActiveLink(!otherEntered);
                Section ahead = aheadLink == null ? null : resolve(aheadLink);
                bool lies = Footprint.Overlap(current, other)
                    || (behind != null && Footprint.Overlap(current, behind))
                    || (ahead != null && Footprint.Overlap(current, ahead));
                if (!lies) break;
                count++;

                // Далі по своїй гілці. Якщо вона сама розходиться, рахувати далі нема чого
                List<SectionLink> onward = current.GetLinks(!entered);
                if (onward.Count != 1) break;
                Section following = resolve(onward[0]);
                if (following == null) break;
                current = following;
                entered = onward[0].AtStart;
            }

            return count;
        }
    }
}
