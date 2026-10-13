using System;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Поверхня полотна покладеної колії: на якій висоті воно лежить над точкою збоку від осі.
    /// У кожному місці вздовж осі полотно це поперечна лінія: точка осі і нормаль у ній. Секція дає три такі
    /// лінії, на початку, посередині і в кінці; між сусідніми лінія переходить плавно. Разом вони складають
    /// поверхню без сходинок, яка на сусідніх секціях сходиться не лише на осі, а й збоку від неї.
    /// Площина секції (RouteCurve.CrossPlane) цього не дає: на крутому схилі, де колія ще й повертає,
    /// площини сусідніх секцій розвернуті одна відносно одної і збоку від осі розходяться по висоті.
    /// Чиста геометрія.
    /// </summary>
    public static class TrackSurface
    {
        // На скільки блоків убік від точки міряти нахил поверхні
        private const double CantProbe = 0.05;

        // Поперечна лінія полотна: точка осі і нормаль у ній
        private struct Station
        {
            public Vec3d Axis;
            public Vec3d Normal;
        }

        /// <summary>
        /// Висота полотна секції над точкою p і його нахил для маршруту, що йде в напрямку (travelX, travelZ):
        /// наскільки полотно піднімається ліворуч від руху, радіани. before і after це сусідні секції колії
        /// з боку початку й кінця section; null, якщо колії там немає або сусіда не знайти: тоді полотно
        /// продовжується рівно, від крайньої поперечної лінії. false, якщо точка лежить там, де поверхні немає.
        /// </summary>
        public static bool At(Section section, Section before, Section after, Vec3d p, double travelX, double travelZ, out double height, out double cant)
        {
            cant = 0;
            if (!Height(section, before, after, p.X, p.Z, out height)) return false;

            double length = Math.Sqrt(travelX * travelX + travelZ * travelZ);
            if (length < 1e-9) return true;

            double leftX = -travelZ / length * CantProbe, leftZ = travelX / length * CantProbe;
            if (!Height(section, before, after, p.X + leftX, p.Z + leftZ, out double left)) return true;
            if (!Height(section, before, after, p.X - leftX, p.Z - leftZ, out double right)) return true;
            cant = Math.Atan((left - right) / (2 * CantProbe));
            return true;
        }

        private static bool Height(Section section, Section before, Section after, double x, double z, out double height)
        {
            height = 0;
            Station start = Of(section.FullStartPosition, section.StartNormal);
            Station middle = Of(section.GetGlobalPos(), section.CenterNormal);
            Station end = Of(section.FullEndPosition, section.EndNormal);

            // Перша половина секції; якщо точка за серединою, друга
            if (!Solve(start, middle, x, z, out double t)) return false;
            if (t > 1)
            {
                if (!Solve(middle, end, x, z, out t)) return false;
                if (t <= 1) return Lift(middle, end, t, x, z, false, out height);

                // За кінцем секції: полотно сусідньої або рівне продовження
                if (after != null && Nearest(after, end.Axis, out Station joint, out Station next) && Solve(joint, next, x, z, out double ahead))
                    return Lift(joint, next, Math.Max(ahead, 0), x, z, false, out height);
                return Lift(middle, end, t, x, z, true, out height);
            }
            if (t >= 0) return Lift(start, middle, t, x, z, false, out height);

            // Перед початком секції
            if (before != null && Nearest(before, start.Axis, out Station seam, out Station previous) && Solve(seam, previous, x, z, out double behind))
                return Lift(seam, previous, Math.Max(behind, 0), x, z, false, out height);
            return Lift(middle, start, 1 - t, x, z, true, out height);
        }

        private static Station Of(Vec3d axis, Vec3f normal)
        {
            return new Station { Axis = axis, Normal = new Vec3d(normal.X, normal.Y, normal.Z) };
        }

        // Половина сусідньої секції, що прилягає до стику joint: її поперечна лінія на стику і посередині
        private static bool Nearest(Section neighbour, Vec3d joint, out Station atJoint, out Station middle)
        {
            Vec3d start = neighbour.FullStartPosition, end = neighbour.FullEndPosition;
            bool byStart = start.SquareDistanceTo(joint) <= end.SquareDistanceTo(joint);
            atJoint = byStart ? Of(start, neighbour.StartNormal) : Of(end, neighbour.EndNormal);
            middle = Of(neighbour.GetGlobalPos(), neighbour.CenterNormal);
            // Сусід справді прилягає: колія могла обірватися, а посилання лишитися
            return atJoint.Axis.SquareDistanceTo(joint) < 1e-4;
        }

        // Нормаль другої лінії, повернута в той самий бік, що й перша: у сусідніх секцій вони можуть дивитися
        // в різні боки, а поперечній лінії це байдуже
        private static Vec3d Aligned(Vec3d first, Vec3d second)
        {
            return first.X * second.X + first.Z * second.Z < 0 ? new Vec3d(-second.X, -second.Y, -second.Z) : second;
        }

        // Де між лініями a (0) і b (1) лежить та, що проходить у плані через точку. Вісь і нормаль між a і b
        // міняються рівномірно, тож умова «точка на лінії» це квадратне рівняння. Може вийти менше 0 чи більше 1:
        // точка за краєм. false, якщо такої лінії немає
        private static bool Solve(Station a, Station b, double x, double z, out double t)
        {
            t = 0;
            Vec3d nb = Aligned(a.Normal, b.Normal);
            double dx = x - a.Axis.X, dz = z - a.Axis.Z;
            double ex = b.Axis.X - a.Axis.X, ez = b.Axis.Z - a.Axis.Z;
            double mx = nb.X - a.Normal.X, mz = nb.Z - a.Normal.Z;

            // (d - t e) x (n + t m) = 0
            double square = -(ex * mz - ez * mx);
            double linear = (dx * mz - dz * mx) - (ex * a.Normal.Z - ez * a.Normal.X);
            double free = dx * a.Normal.Z - dz * a.Normal.X;

            if (Math.Abs(square) < 1e-12)
            {
                if (Math.Abs(linear) < 1e-12) return false;
                t = -free / linear;
                return true;
            }

            double root = linear * linear - 4 * square * free;
            if (root < 0) return false;
            root = Math.Sqrt(root);
            double first = (-linear + root) / (2 * square), second = (-linear - root) / (2 * square);
            // Другий корінь лежить далеко, там, де поперечні лінії сходяться в одну точку
            t = Math.Abs(first - 0.5) <= Math.Abs(second - 0.5) ? first : second;
            return true;
        }

        // Висота над точкою на лінії t між a і b. flat: за краєм b нормаль далі не повертається, полотно
        // продовжується рівно
        private static bool Lift(Station a, Station b, double t, double x, double z, bool flat, out double height)
        {
            height = 0;
            Vec3d nb = Aligned(a.Normal, b.Normal);
            double turn = flat ? Math.Min(t, 1) : t;
            if (flat && t > 1)
            {
                // Рівне продовження: лінія з нормаллю краю, що проходить через точку
                double ex = b.Axis.X - a.Axis.X, ez = b.Axis.Z - a.Axis.Z;
                double across = ex * nb.Z - ez * nb.X;
                if (Math.Abs(across) < 1e-12) return false;
                t = ((x - a.Axis.X) * nb.Z - (z - a.Axis.Z) * nb.X) / across;
            }

            double cx = a.Axis.X + (b.Axis.X - a.Axis.X) * t, cy = a.Axis.Y + (b.Axis.Y - a.Axis.Y) * t, cz = a.Axis.Z + (b.Axis.Z - a.Axis.Z) * t;
            double nx = a.Normal.X + (nb.X - a.Normal.X) * turn, ny = a.Normal.Y + (nb.Y - a.Normal.Y) * turn, nz = a.Normal.Z + (nb.Z - a.Normal.Z) * turn;
            double flatLength = nx * nx + nz * nz;
            if (flatLength < 1e-12) return false;

            double aside = ((x - cx) * nx + (z - cz) * nz) / flatLength;
            height = cy + ny * aside;
            return true;
        }
    }
}
