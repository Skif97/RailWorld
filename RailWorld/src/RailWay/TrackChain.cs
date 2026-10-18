using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Колія, якою їде вагонетка: секції підряд за активними посиланнями, зі стрілками в їхньому теперішньому
    /// положенні, в обидва боки від однієї секції. Так само від секції до секції її веде сервер
    /// (SectionLinker.GetNext). Уздовж ланцюжка одна координата, відстань від початку середньої секції в бік її
    /// кінця. За нею місце вагонетки кладеться на колію, і від одного місця до іншого можна пройти колією, а не
    /// навпростець.
    /// Ланцюжок один, без розгалужень: проти стрілки він іде туди, куди вона переведена. Якщо вагонетка щойно
    /// проїхала стрілку з боку гілки, переведеної на іншу, гілки позаду в ланцюжку не буде.
    /// </summary>
    public class TrackChain
    {
        private struct Piece
        {
            public Section Section;
            // Ланцюжок іде по секції від її кінця до початку
            public bool Reversed;
            // Координата ланцюжка там, де він заходить у секцію
            public double From;
            public double Length;
        }

        // По порядку вздовж ланцюжка
        private readonly List<Piece> pieces = new List<Piece>();
        // Ті самі секції: на кільці ланцюжок не має обійти його і взяти секцію вдруге
        private readonly HashSet<Section> taken = new HashSet<Section>();

        /// <summary>Скільки секцій у ланцюжку.</summary>
        public int Count => pieces.Count;

        /// <summary>
        /// Будує ланцюжок навколо секції middle, на reach блоків у кожен бік від неї, або менше, якщо колія
        /// раніше закінчується чи її сусідніх чанків немає.
        /// </summary>
        public void Build(Section middle, double reach, Func<SectionLink, Section> resolve)
        {
            pieces.Clear();
            taken.Clear();
            if (middle == null) return;

            var center = new Piece { Section = middle, Reversed = false, From = 0, Length = RailPose.Length(middle) };
            pieces.Add(center);
            taken.Add(middle);

            Piece last = center;
            while (last.From + last.Length < reach && Next(last, true, resolve, out Piece next))
            {
                next.From = last.From + last.Length;
                pieces.Add(next);
                last = next;
            }

            Piece first = center;
            while (-first.From < reach && Next(first, false, resolve, out Piece previous))
            {
                previous.From = first.From - previous.Length;
                pieces.Insert(0, previous);
                first = previous;
            }
        }

        // Сусід секції piece за активним посиланням: далі вздовж ланцюжка (forward) або перед нею
        private bool Next(Piece piece, bool forward, Func<SectionLink, Section> resolve, out Piece next)
        {
            next = default;

            // Ланцюжок виходить із секції через її кінець, якщо іде вперед від її початку до кінця або назад
            // від кінця до початку, інакше через початок
            bool leaveAtStart = piece.Reversed == forward;
            SectionLink link = piece.Section.GetActiveLink(leaveAtStart);
            Section section = link == null ? null : resolve(link);
            if (section == null || !taken.Add(section)) return false;

            // Ідучи вперед, у сусіда заходимо через його початок: тоді по ньому ланцюжок іде від початку до кінця.
            // Ідучи назад, навпаки
            next = new Piece { Section = section, Reversed = forward != link.AtStart, Length = RailPose.Length(section) };
            return true;
        }

        /// <summary>
        /// Координата ланцюжка, де стоїть вагонетка з місцем position (див. RailPose.Distance). false, якщо всі
        /// секції ланцюжка далі за maxDistance від нього.
        /// </summary>
        public bool Locate(Vec3d position, double halfHeight, double maxDistance, out double at)
        {
            at = 0;
            double best = maxDistance;
            bool found = false;
            foreach (Piece piece in pieces)
            {
                double distance = RailPose.Distance(piece.Section, position, halfHeight, out double s);
                if (distance > best) continue;

                best = distance;
                found = true;
                at = piece.From + (piece.Reversed ? piece.Length - s : s);
            }
            return found;
        }

        /// <summary>
        /// Секція і відстань s від її початку за координатою ланцюжка; за краями ланцюжка це його край.
        /// reversed каже, що ланцюжок іде по цій секції від кінця до початку. false, якщо ланцюжок порожній.
        /// </summary>
        public bool At(double at, out Section section, out double s, out bool reversed)
        {
            section = null;
            s = 0;
            reversed = false;
            if (pieces.Count == 0) return false;

            Piece piece = pieces[pieces.Count - 1];
            for (int i = 0; i < pieces.Count; i++)
            {
                if (at > pieces[i].From + pieces[i].Length && i < pieces.Count - 1) continue;
                piece = pieces[i];
                break;
            }

            double into = GameMath.Clamp(at - piece.From, 0, piece.Length);
            section = piece.Section;
            s = piece.Reversed ? piece.Length - into : into;
            reversed = piece.Reversed;
            return true;
        }
    }
}
