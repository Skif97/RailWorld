using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// З якого боку від кожної секції гілки стрілки лежить друга гілка: там у гілки внутрішня рейка, яку
    /// заміняють гостряк і хрестовина, а із зовнішнього боку стоїть контррейка.
    /// Біля стику гілки лежать одна на одній, і там бік не видно: дві майже однакові осі можуть ще й кілька
    /// разів перейти одна через одну. Тому бік береться там, де гілка вже вийшла за другу колію обома рейками,
    /// і звідти переноситься вздовж гілки секція за секцією, до стику і далі від нього. Нормалі сусідніх
    /// секцій можуть дивитися в різні боки, тож переноситься сам бік у світі, а не номер рейки.
    /// </summary>
    internal static class SwitchSides
    {
        /// <summary>
        /// Скільки секцій кожної гілки щонайбільше проходиться. Більше, ніж може бути секцій у записі стрілки:
        /// тіло найпологішої стрілки і закриті для нових стрілок секції за ним.
        /// </summary>
        public const int MaxSections = 128;

        /// <summary>
        /// Для кожної секції гілки mine по порядку від стрілки: чи друга гілка лежить з боку її першої рейки
        /// (з боку нормалі). partner це секції другої гілки по порядку від стрілки. Порожньо, якщо котрась
        /// гілка порожня.
        /// </summary>
        public static List<bool> InnerFirst(IList<Section> mine, IList<Section> partner)
        {
            var result = new List<bool>(mine.Count);
            int pairs = Math.Min(mine.Count, partner.Count);
            if (pairs == 0) return result;

            // Де бік видно найкраще: перша пара, де гілка вийшла за другу колію обома рейками. Якщо гілки так
            // далеко не розійшлися, то пара, де вони розійшлися найдалі
            int at = 0;
            double offset = 0;
            for (int n = 0; n < pairs; n++)
            {
                double here = Offset(mine[n], partner[n]);
                if (Math.Abs(here) > Math.Abs(offset))
                {
                    at = n;
                    offset = here;
                }
                if (Math.Abs(here) >= mine[n].TrackWidth + RailProfile.HeadHalfWidth * 2) break;
            }

            var sides = new Vec3d[mine.Count];
            sides[at] = Normal(mine[at]).Mul(offset >= 0 ? 1 : -1);
            for (int n = at - 1; n >= 0; n--) sides[n] = Carried(mine[n], sides[n + 1]);
            for (int n = at + 1; n < mine.Count; n++) sides[n] = Carried(mine[n], sides[n - 1]);

            for (int n = 0; n < mine.Count; n++)
            {
                Vec3d normal = Normal(mine[n]);
                result.Add(normal.X * sides[n].X + normal.Z * sides[n].Z >= 0);
            }
            return result;
        }

        /// <summary>
        /// Те саме для обох гілок стрілки, що починаються секціями first і second; entered кажуть, котрим кінцем
        /// кожна з них до стрілки.
        /// </summary>
        public static void Of(Section first, bool firstEntered, Section second, bool secondEntered, Func<SectionLink, Section> resolve,
            out List<bool> firstSides, out List<bool> secondSides)
        {
            List<Section> a = Walk(first, firstEntered, resolve), b = Walk(second, secondEntered, resolve);
            firstSides = InnerFirst(a, b);
            secondSides = InnerFirst(b, a);
        }

        /// <summary>Секції гілки по порядку від стрілки, не більше MaxSections; first це перша з них.</summary>
        public static List<Section> Walk(Section first, bool entered, Func<SectionLink, Section> resolve)
        {
            var sections = new List<Section>();
            TrackWalker walk = TrackWalker.Entering(first, entered, resolve);
            do sections.Add(walk.Section);
            while (sections.Count < MaxSections && walk.Next());
            return sections;
        }

        /// <summary>
        /// Чи гілки стрілки розходяться не так, як стрілка: один раз, кожна у свій бік. Бік гілки mine це той,
        /// з якого від неї лежить друга гілка там, де вони розійшлися (InnerFirst). Ні, якщо до того, як
        /// розійтися, друга гілка заходить на протилежний бік далі, ніж на пів головки рейки: гілки переходять
        /// одна через одну, і гостряк ліг би криво. Міліметрові переходи на ділянці, де гілки лежать одна
        /// на одній, сюди не рахуються. І ні, якщо, розійшовшись обома рейками, гілки знову лягають одна на
        /// одну раніше, ніж відійдуть на габарит: так буває, коли гілку прокладено вздовж колії, що виляє.
        /// Що далі, ніж габарит, то вже не стрілка: там гілка може перетнути колію звичайним перетином.
        /// </summary>
        public static bool CrossOver(IList<Section> mine, IList<Section> partner)
        {
            List<bool> innerFirst = InnerFirst(mine, partner);
            bool parted = false;
            for (int n = 0; n < mine.Count; n++)
            {
                Vec3d side = Normal(mine[n]).Mul(innerFirst[n] ? 1 : -1);
                double apart = Toward(mine[n], partner, side);
                if (!parted)
                {
                    if (apart < -RailProfile.HeadHalfWidth) return true;
                    parted = apart >= mine[n].TrackWidth + RailProfile.HeadHalfWidth * 2;
                    continue;
                }
                if (apart >= Footprint.Width(mine[n])) return false;
                if (apart < mine[n].TrackWidth) return true;
            }
            return false;
        }

        // На скільки крок, на який гілки відходять одна від одної за секцію, може бути меншим за наступний через
        // похибку вимірювання, блоків: як кут у 1° на пів блока
        private static readonly double TurnSlack = 0.5 * Math.Tan(Math.PI / 180);

        // Менша зміна відстані між гілками це похибка обчислень з дробовими числами, а не зміна, блоків
        private const double Precision = 1e-6;

        /// <summary>
        /// З початку якої секції гілки mine (номер з нуля) друга гілка відходить від неї так, як у стрілці: лише
        /// відходить, і дедалі швидше, аж поки гілка не вийде за другу колію обома рейками. Тут стрілка
        /// справді починається: звідси зазор між гостряком і рамною рейкою росте до кореня, відведений
        /// гостряк утворює з рамною рейкою гострий кут, що сходиться до хрестовини, а кут між гілками до
        /// хрестовини лише більшає. Перед цим місцем гілки лежать одна на одній або йдуть поруч паралельно:
        /// відстань між ними то росте, то меншає або стоїть. 0, якщо гілки розходяться від самої стрілки,
        /// як і має бути.
        /// </summary>
        public static int DivergesFrom(IList<Section> mine, IList<Section> partner)
        {
            if (mine.Count == 0 || partner.Count == 0) return 0;

            List<bool> innerFirst = InnerFirst(mine, partner);
            var apart = new double[mine.Count];
            int parted = mine.Count - 1;
            for (int n = 0; n < mine.Count; n++)
            {
                apart[n] = Toward(mine[n], partner, Normal(mine[n]).Mul(innerFirst[n] ? 1 : -1));
                if (apart[n] >= mine[n].TrackWidth + RailProfile.HeadHalfWidth * 2)
                {
                    parted = n;
                    break;
                }
            }

            // Від місця, де розійшлися, до стрілки: поки відстань меншає, і крок, на який вона меншає, теж.
            // Зупинились на секції still: вона ще не відходить, наступна вже відходить. Відстань міряється
            // посередині секцій, тож справжній початок лежить між їхніми серединами, і стик між ними саме
            // посередині цього проміжку
            for (int still = parted; still > 0; still--)
            {
                double step = apart[still] - apart[still - 1];
                if (step <= Precision) return still + 1;
                if (still < parted && step > apart[still + 1] - apart[still] + TurnSlack) return still + 1;
            }
            return 0;
        }

        // Де від середини секції лежить найближча точка осі колії track, у плані: відстань до неї в бік side.
        // Від'ємна, якщо колія з протилежного боку
        private static double Toward(Section section, IList<Section> track, Vec3d side)
        {
            Vec3d p = Middle(section);
            double best = double.MaxValue, bx = 0, bz = 0;
            foreach (Section other in track)
            {
                Vec3d a = other.FullStartPosition, b = other.FullEndPosition;
                double dx = b.X - a.X, dz = b.Z - a.Z, length = dx * dx + dz * dz;
                double t = length < 1e-12 ? 0 : GameMath.Clamp(((p.X - a.X) * dx + (p.Z - a.Z) * dz) / length, 0, 1);
                double x = a.X + dx * t - p.X, z = a.Z + dz * t - p.Z;
                if (x * x + z * z >= best) continue;
                best = x * x + z * z;
                bx = x;
                bz = z;
            }
            double distance = Math.Sqrt(best);
            return bx * side.X + bz * side.Z >= 0 ? distance : -distance;
        }

        // На скільки середина секції partner зсунута від середини mine убік, у бік її нормалі
        private static double Offset(Section mine, Section partner)
        {
            Vec3d a = Middle(mine), b = Middle(partner), normal = Normal(mine);
            return (b.X - a.X) * normal.X + (b.Z - a.Z) * normal.Z;
        }

        // Той самий бік у світі, перенесений на сусідню секцію: її нормаль, повернута туди, куди дивився бік
        private static Vec3d Carried(Section section, Vec3d side)
        {
            Vec3d normal = Normal(section);
            return normal.X * side.X + normal.Z * side.Z >= 0 ? normal : normal.Mul(-1);
        }

        // Нормаль посередині секції в плані, одиничної довжини
        private static Vec3d Normal(Section section)
        {
            Vec3f normal = section.CenterNormal;
            var flat = new Vec3d(normal.X, 0, normal.Z);
            return flat.Length() < 1e-9 ? new Vec3d(1, 0, 0) : flat.Normalize();
        }

        private static Vec3d Middle(Section section)
        {
            Vec3d start = section.FullStartPosition, end = section.FullEndPosition;
            return new Vec3d((start.X + end.X) / 2, 0, (start.Z + end.Z) / 2);
        }
    }
}
