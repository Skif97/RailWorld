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
            List<Section> a = Branch(first, firstEntered, resolve), b = Branch(second, secondEntered, resolve);
            firstSides = InnerFirst(a, b);
            secondSides = InnerFirst(b, a);
        }

        private static List<Section> Branch(Section first, bool entered, Func<SectionLink, Section> resolve)
        {
            var sections = new List<Section>();
            TrackWalker walk = TrackWalker.Entering(first, entered, resolve);
            do sections.Add(walk.Section);
            while (sections.Count < MaxSections && walk.Next());
            return sections;
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
