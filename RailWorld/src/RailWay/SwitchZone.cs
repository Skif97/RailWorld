using System;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>Чи стик або секція лежить у тілі стрілки.</summary>
    public enum SwitchBody
    {
        /// <summary>Не лежить: колію пройдено, стрілки там немає.</summary>
        Outside,
        /// <summary>Лежить у тілі стрілки.</summary>
        Inside,
        /// <summary>
        /// Невідомо: колія йде в чанк, який не вдалося прочитати, раніше, ніж знайшлася відповідь. Тіло пологої
        /// стрілки тягнеться на десятки блоків, тож воно може бути там. Правила тоді відмовляють, як запис стрілки
        /// в такому разі чекає на чанк (RailSwitch.Resolved).
        /// </summary>
        Unread
    }

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

        /// <summary>Чому правило відмовляє, коли про тіло стрілки сказати нічого не можна (SwitchBody.Unread).</summary>
        public const string UnreadRefusal = "колія поруч ще не завантажена: про стрілку там нічого не відомо";

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
        public static SwitchBody JointInBody(Section section, bool atStart, Func<SectionLink, Section> resolve)
        {
            if (section == null || resolve == null) return SwitchBody.Outside;

            // Від стику колія йде в два боки: через саму секцію і геть від неї
            SwitchBody through = FindsSwitch(section, !atStart, 1, resolve);
            if (through == SwitchBody.Inside) return through;
            SwitchBody away = FindsSwitch(section, atStart, 0, resolve);
            if (away == SwitchBody.Inside) return away;
            return through == SwitchBody.Unread || away == SwitchBody.Unread ? SwitchBody.Unread : SwitchBody.Outside;
        }

        /// <summary>
        /// Чи секція лежить у тілі якоїсь стрілки: обидва її стики в ньому. Остання секція спільної колії
        /// перед стрілкою торкається тіла лише одним стиком і до нього не належить.
        /// </summary>
        public static SwitchBody InBody(Section section, Func<SectionLink, Section> resolve)
        {
            SwitchBody start = JointInBody(section, true, resolve);
            if (start == SwitchBody.Outside) return start;
            SwitchBody end = JointInBody(section, false, resolve);
            if (end == SwitchBody.Outside) return end;
            return start == SwitchBody.Inside && end == SwitchBody.Inside ? SwitchBody.Inside : SwitchBody.Unread;
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
        private static SwitchBody FindsSwitch(Section section, bool exitAtStart, int passed, Func<SectionLink, Section> resolve)
        {
            TrackWalker walk = TrackWalker.Leaving(section, exitAtStart, resolve);
            for (int guard = 0; guard < MaxWalk; guard++)
            {
                Section from = walk.Section;

                if (!walk.Next())
                {
                    // Далі колія є, але її не прочитати: стрілка може бути там
                    if (walk.Stop == WalkStop.Unread) return SwitchBody.Unread;
                    // Попереду колія розходиться: ми на спільній колії перед стрілкою. Тіло починається на самому
                    // стику, тож у ньому лише той стик, з якого й почали
                    return walk.Stop == WalkStop.Fork && passed == 0 ? SwitchBody.Inside : SwitchBody.Outside;
                }

                // Зайшли в секцію через кінець, з якого виходять дві колії: ми на гілці, а це її стрілка.
                // Гілка в тілі стрілки, поки лежить на сусідній. Якщо гілки не дочитано, поки вони ще лежали одна
                // на одній, тіло може тягнутися й далі
                if (walk.Section.HasSwitch(walk.Entered))
                {
                    if (passed <= OverlapSections(walk.Section, walk.Entered, from, resolve, out bool cut)) return SwitchBody.Inside;
                    return cut ? SwitchBody.Unread : SwitchBody.Outside;
                }

                passed++;
            }
            return SwitchBody.Outside;
        }

        // На скільки секцій від стрілки дві гілки порівнюються між собою
        private const int MaxPairWalk = 48;

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
        /// cut: рахунок обірвано, бо котрусь із гілок далі не прочитати, а вони ще лежали одна на одній; тоді
        /// справжнє число може бути більшим.
        /// </summary>
        internal static int OverlapSections(Section trunk, bool trunkAtStart, Section branch, Func<SectionLink, Section> resolve, out bool cut)
        {
            cut = false;
            SectionLink mine = null, theirs = null;
            foreach (SectionLink link in trunk.GetLinks(trunkAtStart))
            {
                if (link.PointsTo(branch)) mine = link;
                else theirs = link;
            }
            if (mine == null || theirs == null) return 0;

            Section otherFirst = resolve(theirs);
            if (otherFirst == null)
            {
                cut = true;
                return 0;
            }
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
                // Сусідня гілка далі є, але її не прочитати: навпроти цієї секції може лежати саме вона
                bool besideUnread = false;
                for (int hop = 0; hop < RouteCurve.MaxHopsPerPoint; hop++)
                {
                    if (RouteCurve.Along(beside.Section, beside.Entered, p, out double length, out _) <= length + 1e-6) break;

                    Section passed = beside.Section;
                    if (!beside.Next())
                    {
                        besideUnread = beside.Stop == WalkStop.Unread;
                        break;
                    }
                    behind = passed;
                }

                // Габарит секції може зачепити й сусідів тієї, що навпроти: гілки йдуть під кутом
                Section other = beside.Section, ahead = beside.Peek();
                bool lies = Footprint.Overlap(current, other)
                    || (behind != null && Footprint.Overlap(current, behind))
                    || (ahead != null && Footprint.Overlap(current, ahead));
                if (!lies)
                {
                    cut = besideUnread;
                    break;
                }
                count++;

                // Далі по своїй гілці. Якщо вона сама розходиться, рахувати далі нема чого
                if (!walk.Next())
                {
                    cut = walk.Stop == WalkStop.Unread;
                    break;
                }
            }

            return count;
        }
    }
}
