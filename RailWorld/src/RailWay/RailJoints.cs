using System;
using System.Collections.Generic;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Стики рейок. Рейка не безкінечна: колія складена з рейок певної довжини, і там, де дві стикуються,
    /// їх стягує накладка на болтах. Де саме стики, вирішується один раз, коли ділянку кладуть, і зберігається
    /// в секціях.
    /// Колію перед ділянкою переглядають один раз на всю побудову, а не для кожної секції: від першої секції
    /// назад, не далі ніж на довжину рейки. Знайшовся стик, рахунок секцій продовжується від нього; не знайшовся,
    /// стик стає на першу секцію ділянки. Виняток один: якщо позаду колії менше, ніж на рейку, або немає зовсім,
    /// це початок колії, і рахунок іде від нього: перший стик буде лише через довжину рейки. Далі в ділянці стик припадає на кожну RailSections-ту
    /// секцію. Тому виходить однаково, чи колію кладуть одним довгим маршрутом, чи шматками по кілька блоків.
    /// Чиста логіка: сусідні секції дістає через resolve.
    /// </summary>
    public static class RailJoints
    {
        /// <summary>Довжина рейки в секціях: стик припадає на кожну таку за рахунком.</summary>
        public const int RailSections = 20;

        /// <summary>
        /// Розставляє стики на щойно покладеній ділянці: секції йдуть по порядку вздовж колії і вже з'єднані
        /// між собою та з колією, до якої ділянку прикладено.
        /// </summary>
        public static void Mark(IList<Section> stretch, Func<SectionLink, Section> resolve)
        {
            if (stretch.Count == 0) return;

            // Скільки секцій без стику лежить перед ділянкою, не далі ніж на довжину рейки. Якщо колія позаду
            // довша за рейку і стику на ній немає, рахунок починається так, ніби рейка щойно скінчилася: перша
            // ж секція ділянки стає зі стиком. Якщо колія позаду коротша або її немає зовсім, це початок колії:
            // стикувати першу рейку нема з чим, і рахунок іде від самого початку
            int since = 0;
            Section first = stretch[0];
            for (int end = 0; end < 2; end++)
            {
                bool atStart = end == 0;
                // Назад це не в бік решти ділянки. У ділянки з однієї секції таким може бути кожен кінець
                if (stretch.Count > 1 && LinksTo(first, atStart, stretch[1])) continue;
                if (first.GetLinks(atStart).Count == 0) continue;

                int passed = Look(first, atStart, resolve, out bool joint);
                since = joint || passed < RailSections ? passed : RailSections - 1;
                break;
            }

            // Скільки секцій від останньої секції ділянки до найближчого стику попереду: ділянкою могли
            // закрити проміжок між двома коліями, і тоді свій стик поруч із чужим не потрібен
            int ahead = -1;
            Section last = stretch[stretch.Count - 1];
            if (stretch.Count > 1)
            {
                for (int end = 0; end < 2 && ahead < 0; end++)
                {
                    bool atStart = end == 0;
                    if (LinksTo(last, atStart, stretch[stretch.Count - 2])) continue;

                    int passed = Look(last, atStart, resolve, out bool joint);
                    if (joint) ahead = passed;
                }
            }

            for (int i = 0; i < stretch.Count; i++)
            {
                since++;
                if (since < RailSections) continue;
                since = 0;

                // Секцій між цією і стиком попереду
                int between = ahead < 0 ? RailSections : stretch.Count - 1 - i + ahead;
                if (between >= RailSections - 1) stretch[i].RailJoint = true;
            }
        }

        private static bool LinksTo(Section section, bool atStart, Section other)
        {
            foreach (SectionLink link in section.GetLinks(atStart))
            {
                if (link.PointsTo(other)) return true;
            }
            return false;
        }

        // Іде по колії від секції через її кінець exitAtStart, не далі ніж на довжину рейки. Повертає, скільки
        // секцій без стику пройдено; joint каже, чи зупинилися на секції зі стиком. Зупиняється також там, де
        // колія кінчається або розходиться стрілкою
        private static int Look(Section section, bool exitAtStart, Func<SectionLink, Section> resolve, out bool joint)
        {
            joint = false;
            TrackWalker walk = TrackWalker.Leaving(section, exitAtStart, resolve);
            int passed = 0;
            while (passed < RailSections && walk.Next())
            {
                if (walk.Section.RailJoint)
                {
                    joint = true;
                    break;
                }
                passed++;
            }
            return passed;
        }
    }
}
