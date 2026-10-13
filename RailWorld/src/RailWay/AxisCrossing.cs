using System;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Чи перетинаються в плані осі колій. Правило одне на все: і для сервера, що заводить запис перетину,
    /// і для правил прокладання, і для маршруту, що вирівнюється по перетнутій колії.
    /// Посередині секції питань немає. На її кінці, тобто на стику із сусідньою, перетин рахується, якщо
    /// колія за цим стиком продовжується: колія, що впирається кінцем у бік іншої, її не перетинає. Від того,
    /// в який бік покладено секції, це не залежить. Перетин рівно на стику тому знаходиться на обох секціях
    /// біля нього; кому треба порахувати перетини, зливає збіги в одному місці.
    /// Висоту тут не порівнюють: чи колії в одному рівні, вирішує той, хто питає.
    /// </summary>
    internal static class AxisCrossing
    {
        /// <summary>
        /// Як близько до кінця відрізка перетин вважається таким, що лежить на самому кінці, у частках
        /// довжини відрізка. Секція це пів блока, тож це пів міліметра.
        /// </summary>
        public const double Edge = 1e-3;

        /// <summary>
        /// Чи шматок осі від from до to перетинає вісь покладеної секції. Кінці самого шматка рахуються:
        /// що лежить за ними, знає лише той, хто питає. t це місце перетину на шматку, від 0 до 1.
        /// </summary>
        public static bool Of(Vec3d from, Vec3d to, Section laid, out double t)
        {
            return Of(from, to, laid, out t, out _);
        }

        /// <summary>
        /// Чи нова, ще не покладена секція перетне покладену. Нова секція, що стає кінцем на кінець
        /// покладеної і продовжує її напрямок, з нею з'єднається: це та сама колія, а не перетин.
        /// </summary>
        public static bool OfPlanned(Section planned, Section laid)
        {
            Vec3d from = planned.FullStartPosition, to = planned.FullEndPosition;
            if (!Of(from, to, laid, out double t, out double u)) return false;
            if (!AtEnd(t) || !AtEnd(u)) return true;

            Vec3d start = laid.FullStartPosition, end = laid.FullEndPosition;
            double rx = to.X - from.X, rz = to.Z - from.Z, sx = end.X - start.X, sz = end.Z - start.Z;
            double cos = Math.Abs(rx * sx + rz * sz) / Math.Sqrt((rx * rx + rz * rz) * (sx * sx + sz * sz));
            return cos < -SectionLinker.DirectionTolerance;
        }

        /// <summary>
        /// Чи перетинаються осі двох покладених секцій. Секції однієї колії чи однієї стрілки сходяться
        /// кінцями, але це не перетин.
        /// </summary>
        public static bool OfLaid(Section a, Section b)
        {
            if (a == b || SameTrack(a, b)) return false;
            return Of(a.FullStartPosition, a.FullEndPosition, b, out double t, out _) && On(a, t);
        }

        private static bool Of(Vec3d from, Vec3d to, Section laid, out double t, out double u)
        {
            Vec3d start = laid.FullStartPosition, end = laid.FullEndPosition;
            if (!PlanLines.Meet(from.X, from.Z, to.X - from.X, to.Z - from.Z, start.X, start.Z, end.X - start.X, end.Z - start.Z, out t, out u)) return false;
            if (t < -Edge || t > 1 + Edge || !On(laid, u)) return false;

            t = GameMath.Clamp(t, 0, 1);
            return true;
        }

        private static bool AtEnd(double at)
        {
            return at <= Edge || at >= 1 - Edge;
        }

        // Чи місце лежить на покладеній секції: at це частка її довжини
        private static bool On(Section section, double at)
        {
            if (at < -Edge || at > 1 + Edge) return false;
            if (at <= Edge) return section.GetLinks(true).Count > 0;
            if (at >= 1 - Edge) return section.GetLinks(false).Count > 0;
            return true;
        }

        // Чи дві секції належать одній колії або одній стрілці: сусідні, або мають спільну сусідню
        private static bool SameTrack(Section a, Section b)
        {
            for (int end = 0; end < 2; end++)
            {
                foreach (SectionLink mine in a.GetLinks(end == 0))
                {
                    if (mine.PointsTo(b)) return true;
                    for (int otherEnd = 0; otherEnd < 2; otherEnd++)
                    {
                        foreach (SectionLink theirs in b.GetLinks(otherEnd == 0))
                        {
                            if (mine.ChunkX == theirs.ChunkX && mine.ChunkY == theirs.ChunkY && mine.ChunkZ == theirs.ChunkZ && mine.Index == theirs.Index) return true;
                        }
                    }
                }
            }
            return false;
        }
    }
}
