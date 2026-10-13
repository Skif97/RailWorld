using System;
using System.Collections.Generic;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Чому обхід колії далі не пішов.
    /// </summary>
    internal enum WalkStop
    {
        /// <summary>Обхід ще йде.</summary>
        None,
        /// <summary>Колія скінчилася: з цього кінця секції сусідів немає.</summary>
        End,
        /// <summary>Колія розходиться стрілкою: далі це вже дві різні колії.</summary>
        Fork,
        /// <summary>Сусід є, але прочитати його не вдалося: його чанк не завантажений.</summary>
        Unread
    }

    /// <summary>
    /// Іде по покладеній колії секція за секцією, зі зв'язку на зв'язок. Стоїть на секції і пам'ятає, яким
    /// кінцем у неї зайшов; наступний крок виходить через протилежний.
    /// Звичайний обхід іде однією колією і на стрілці зупиняється: за нею колій уже дві. Обхід «як їде
    /// вагонетка» на стрілці звертає туди, куди вона зараз переведена.
    /// Скільки йти, вирішує той, хто йде: у кожного правила своя межа, і на замкненій колії обхід сам
    /// не зупиниться.
    /// </summary>
    internal sealed class TrackWalker
    {
        private readonly Func<SectionLink, Section> resolve;
        private readonly bool followSwitches;

        /// <summary>Секція, на якій стоїть обхід.</summary>
        public Section Section { get; private set; }

        /// <summary>Яким кінцем зайшли в секцію: true початком. Далі обхід виходить через протилежний.</summary>
        public bool Entered { get; private set; }

        /// <summary>Чому останній крок не вдався; None, поки обхід іде.</summary>
        public WalkStop Stop { get; private set; }

        /// <summary>Сусіди, на яких обхід зупинився (Fork і Unread); порожньо, якщо колія скінчилася.</summary>
        public List<SectionLink> Ahead => Section.GetLinks(!Entered);

        private TrackWalker(Section section, bool entered, Func<SectionLink, Section> resolve, bool followSwitches)
        {
            Section = section;
            Entered = entered;
            this.resolve = resolve;
            this.followSwitches = followSwitches;
        }

        /// <summary>Обхід, що стоїть на секції, в яку зайшли кінцем entered.</summary>
        public static TrackWalker Entering(Section section, bool entered, Func<SectionLink, Section> resolve)
        {
            return new TrackWalker(section, entered, resolve, false);
        }

        /// <summary>Обхід, що стоїть на секції і вийде з неї через кінець exitAtStart.</summary>
        public static TrackWalker Leaving(Section section, bool exitAtStart, Func<SectionLink, Section> resolve)
        {
            return new TrackWalker(section, !exitAtStart, resolve, false);
        }

        /// <summary>Те саме, що Entering, але на стрілці обхід іде туди, куди вона зараз переведена.</summary>
        public static TrackWalker Riding(Section section, bool entered, Func<SectionLink, Section> resolve)
        {
            return new TrackWalker(section, entered, resolve, true);
        }

        /// <summary>
        /// Секція, на яку став би наступний крок, або null. Обхід лишається на місці.
        /// </summary>
        public Section Peek()
        {
            return Look(out _, out _);
        }

        /// <summary>
        /// Переходить на наступну секцію колії. false, якщо далі йти нікуди: обхід лишається на останній
        /// секції, а Stop каже чому.
        /// </summary>
        public bool Next()
        {
            Section next = Look(out SectionLink link, out WalkStop stop);
            Stop = stop;
            if (next == null) return false;

            Section = next;
            Entered = link.AtStart;
            return true;
        }

        private Section Look(out SectionLink link, out WalkStop stop)
        {
            link = null;
            List<SectionLink> links = Ahead;
            if (links.Count == 0)
            {
                stop = WalkStop.End;
                return null;
            }
            if (links.Count > 1 && !followSwitches)
            {
                stop = WalkStop.Fork;
                return null;
            }

            link = followSwitches ? Section.GetActiveLink(!Entered) : links[0];
            Section next = resolve?.Invoke(link);
            stop = next == null ? WalkStop.Unread : WalkStop.None;
            return next;
        }
    }
}
