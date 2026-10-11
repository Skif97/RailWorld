using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Габарит колії в плані: місце, яке вона займає на землі. Секція займає прямокутник: уздовж це її довжина,
    /// упоперек довжина шпали з невеликим запасом. Колія це суцільна смуга таких прямокутників, а не окремі
    /// шпали: між шпалами однієї колії шпалі іншої лягти однаково нема куди, там рейки.
    /// Усі правила, де питання стоїть «чи лежать дві колії одна на одній» (тіло стрілки, вузол перетину,
    /// довге накладання, просвіт над і під колією), відповідають на нього цим класом.
    /// </summary>
    public static class Footprint
    {
        /// <summary>На скільки блоків габарит ширший за шпалу: по половині з кожного боку.</summary>
        public const double Margin = 0.2;

        /// <summary>Ширина габариту колії зі звичайною шпалою.</summary>
        public const double StandardWidth = TrackGauge.StandardSleeperLength + Margin;

        // Спільна площа, менша за це, перекриттям не вважається: два прямокутники лише торкаються
        private const double MinArea = 1e-4;

        public static double Width(Section section)
        {
            return section.SleeperLength + Margin;
        }

        /// <summary>Чи перекриваються в плані габарити двох секцій.</summary>
        public static bool Overlap(Section a, Section b)
        {
            return Overlap(a, b, out _, out _);
        }

        /// <summary>Те саме, і ще точка всередині спільної частини: там порівнюють висоти двох колій.</summary>
        public static bool Overlap(Section a, Section b, out double x, out double z)
        {
            return Overlap(a.FullStartPosition, a.FullEndPosition, Width(a), b.FullStartPosition, b.FullEndPosition, Width(b), out x, out z);
        }

        /// <summary>
        /// Чи перекриваються два прямокутники: кожен заданий відрізком осі від from до to і шириною впоперек.
        /// (x, z) це середина спільної частини.
        /// </summary>
        public static bool Overlap(Vec3d aFrom, Vec3d aTo, double aWidth, Vec3d bFrom, Vec3d bTo, double bWidth, out double x, out double z)
        {
            x = z = 0;

            // Спершу дешева відмова: прямокутники, чиї середини далі одна від одної, ніж сягають їхні кути,
            // перекритися не можуть. Таких пар переважна більшість, і різати їх нема чого
            double apartX = (aFrom.X + aTo.X - bFrom.X - bTo.X) / 2, apartZ = (aFrom.Z + aTo.Z - bFrom.Z - bTo.Z) / 2;
            double reach = (Math.Abs(aTo.X - aFrom.X) + Math.Abs(aTo.Z - aFrom.Z) + Math.Abs(bTo.X - bFrom.X) + Math.Abs(bTo.Z - bFrom.Z) + aWidth + bWidth) / 2;
            if (apartX * apartX + apartZ * apartZ > reach * reach) return false;

            double[] a = Corners(aFrom, aTo, aWidth), b = Corners(bFrom, bTo, bWidth);
            if (a == null || b == null) return false;

            // Обрізаємо перший прямокутник кожною з чотирьох сторін другого: що лишилося, те спільне
            var shape = new List<double>(a);
            for (int side = 0; side < 4 && shape.Count >= 6; side++)
            {
                double px = b[side * 2], pz = b[side * 2 + 1];
                double qx = b[(side + 1) % 4 * 2], qz = b[(side + 1) % 4 * 2 + 1];
                double ex = qx - px, ez = qz - pz;

                var kept = new List<double>();
                int count = shape.Count / 2;
                for (int i = 0; i < count; i++)
                {
                    double cx = shape[i * 2], cz = shape[i * 2 + 1];
                    double nx = shape[(i + 1) % count * 2], nz = shape[(i + 1) % count * 2 + 1];
                    // Усередині другого прямокутника це ліворуч від його сторони: кути йдуть проти годинникової
                    double inside = ex * (cz - pz) - ez * (cx - px);
                    double next = ex * (nz - pz) - ez * (nx - px);

                    if (inside >= 0)
                    {
                        kept.Add(cx);
                        kept.Add(cz);
                    }
                    if ((inside >= 0) != (next >= 0))
                    {
                        double t = inside / (inside - next);
                        kept.Add(cx + (nx - cx) * t);
                        kept.Add(cz + (nz - cz) * t);
                    }
                }
                shape = kept;
            }

            int corners = shape.Count / 2;
            if (corners < 3) return false;

            double area = 0;
            for (int i = 0; i < corners; i++)
            {
                int j = (i + 1) % corners;
                area += shape[i * 2] * shape[j * 2 + 1] - shape[j * 2] * shape[i * 2 + 1];
                x += shape[i * 2];
                z += shape[i * 2 + 1];
            }
            x /= corners;
            z /= corners;
            return Math.Abs(area) / 2 > MinArea;
        }

        /// <summary>
        /// Чи дві секції стикуються кінцями: у них є спільний стик, з тим самим допуском, з яким кінці
        /// з'єднуються при укладанні. Габарити таких секцій на кривій трохи
        /// заходять один на одного кутами, але це одна колія, а не дві колії одна на одній.
        /// </summary>
        public static bool Joined(Section a, Section b)
        {
            Vec3d[] ends = { a.FullStartPosition, a.FullEndPosition };
            foreach (Vec3d end in ends)
            {
                if (end.DistanceTo(b.FullStartPosition) <= SectionLinker.PositionTolerance || end.DistanceTo(b.FullEndPosition) <= SectionLinker.PositionTolerance) return true;
            }
            return false;
        }

        // Чотири кути прямокутника в плані, проти годинникової стрілки: x і z кожного
        private static double[] Corners(Vec3d from, Vec3d to, double width)
        {
            double dx = to.X - from.X, dz = to.Z - from.Z;
            double length = Math.Sqrt(dx * dx + dz * dz);
            if (length < 1e-9) return null;

            // Упоперек осі, на пів ширини
            double sx = -dz / length * width / 2, sz = dx / length * width / 2;
            return new[]
            {
                from.X - sx, from.Z - sz,
                to.X - sx, to.Z - sz,
                to.X + sx, to.Z + sz,
                from.X + sx, from.Z + sz
            };
        }
    }
}
