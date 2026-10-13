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
                TrackWalker walk = TrackWalker.Leaving(section, side == 0, resolve);

                for (int passed = 0; passed < OpenEndSections; passed++)
                {
                    if (walk.Next())
                    {
                        // Зайшли на гілку стрілки з боку її стику: це вже не край колії
                        if (walk.Section.HasSwitch(walk.Entered)) break;
                        continue;
                    }

                    // Попереду стрілка або непрочитана колія: теж не край
                    if (walk.Stop != WalkStop.End) break;

                    double away = walk.Section.GetEndPosition(!walk.Entered).SquareDistanceTo(near);
                    if (away < best)
                    {
                        best = away;
                        endSection = walk.Section;
                        endAtStart = !walk.Entered;
                    }
                    break;
                }
            }
            return endSection != null;
        }

        // Іде по колії, виходячи із секції через кінець exitAtStart. passed це скільки секцій уже пройдено від стику
        private static bool FindsSwitch(Section section, bool exitAtStart, int passed, Func<SectionLink, Section> resolve)
        {
            TrackWalker walk = TrackWalker.Leaving(section, exitAtStart, resolve);
            for (int guard = 0; guard < MaxWalk; guard++)
            {
                Section from = walk.Section;

                // Попереду колія розходиться: ми на спільній колії перед стрілкою. Тіло починається на самому
                // стику, тож у ньому лише той стик, з якого й почали
                if (!walk.Next()) return walk.Stop == WalkStop.Fork && passed == 0;

                // Зайшли в секцію через кінець, з якого виходять дві колії: ми на гілці, а це її стрілка.
                // Гілка в тілі стрілки, поки лежить на сусідній
                if (walk.Section.HasSwitch(walk.Entered))
                {
                    return passed <= OverlapSections(walk.Section, walk.Entered, from, resolve);
                }

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
            if (first == null || second == null) return previousAlong;
            TrackWalker a = TrackWalker.Entering(first, firstEntered, resolve), b = TrackWalker.Entering(second, secondEntered, resolve);

            for (int n = 1; n <= MaxPairWalk; n++)
            {
                // Середина секції з номером n лежить за (n - 0.5) секції від стрілки, а секція це пів блока
                double along = (n - 0.5) * 0.5;
                Vec3d p = a.Section.GetGlobalPos(), q = b.Section.GetGlobalPos();
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

                if (!a.Next() || !b.Next()) break;
            }

            // Гілки скінчилися, так і не розійшовшись: гостряк на всю пройдену довжину
            return previousAlong;
        }

        /// <summary>
        /// Скільки перших секцій двох гілок стоять у парі: габарити секцій з однаковим номером від стрілки
        /// перекриваються, і замість двох шпал малюється одна спільна. first і second це перші секції гілок.
        /// </summary>
        internal static int PairSections(Section first, bool firstEntered, Section second, bool secondEntered, Func<SectionLink, Section> resolve)
        {
            int pairs = 0;
            if (first == null || second == null) return pairs;
            TrackWalker a = TrackWalker.Entering(first, firstEntered, resolve), b = TrackWalker.Entering(second, secondEntered, resolve);

            // Гілки проходяться разом: секції з однаковим номером від стрілки
            for (int number = 1; number <= MaxPairWalk; number++)
            {
                if (number > 1 && (!a.Next() || !b.Next())) break;
                if (!Footprint.Overlap(a.Section, b.Section)) break;
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

            Section otherFirst = resolve(theirs);
            if (otherFirst == null) return 0;
            // Сусідньою гілкою йдемо так, як стоять її стрілки
            TrackWalker beside = TrackWalker.Riding(otherFirst, theirs.AtStart, resolve);
            TrackWalker walk = TrackWalker.Entering(branch, mine.AtStart, resolve);
            int count = 0;
            Section behind = null;

            for (int guard = 0; guard < MaxWalk; guard++)
            {
                // Сусідню гілку ведемо поруч: до секції, навпроти якої лежить дальній кінець чергової секції цієї
                Section current = walk.Section;
                Vec3d p = current.GetEndPosition(!walk.Entered);
                for (int hop = 0; hop < RouteCurve.MaxHopsPerPoint; hop++)
                {
                    if (RouteCurve.Along(beside.Section, beside.Entered, p, out double length, out _) <= length + 1e-6) break;

                    Section passed = beside.Section;
                    if (!beside.Next()) break;
                    behind = passed;
                }

                // Габарит секції може зачепити й сусідів тієї, що навпроти: гілки йдуть під кутом
                Section other = beside.Section, ahead = beside.Peek();
                bool lies = Footprint.Overlap(current, other)
                    || (behind != null && Footprint.Overlap(current, behind))
                    || (ahead != null && Footprint.Overlap(current, ahead));
                if (!lies) break;
                count++;

                // Далі по своїй гілці. Якщо вона сама розходиться, рахувати далі нема чого
                if (!walk.Next()) break;
            }

            return count;
        }
    }
}
