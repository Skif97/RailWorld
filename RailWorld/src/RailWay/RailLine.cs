using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Лінія рейки: її перерізи по порядку вздовж колії. Дві колії, що лежать поруч або перетинаються,
    /// порізані на шматки кожна по-своєму, тому переріз однієї з номером N не стоїть навпроти перерізу
    /// другої з тим самим номером. Зіставляти їх треба геометрично: де лінія однієї рейки проходить
    /// крізь площину перерізу другої. Потрібно стрілкам і перетинам колій.
    /// </summary>
    public static class RailLine
    {
        // Наскільки далеко за свій край лінію можна продовжити прямою, блоків: одна секція колії
        private const double MaxExtend = 0.5;
        // Ближче за це до площини переріз вважається таким, що лежить на ній, блоків
        private const double OnPlane = 1e-9;

        /// <summary>Переріз між двома сусідніми: part від 0 (перший) до 1 (другий).</summary>
        public static RailFrame Between(RailFrame a, RailFrame b, double part)
        {
            Vec3d Mixed(Vec3d p, Vec3d q) => new Vec3d(p.X + (q.X - p.X) * part, p.Y + (q.Y - p.Y) * part, p.Z + (q.Z - p.Z) * part);
            return new RailFrame { Center = Mixed(a.Center, b.Center), Side = Mixed(a.Side, b.Side).Normalize(), Up = Mixed(a.Up, b.Up).Normalize(), Along = Mixed(a.Along, b.Along).Normalize() };
        }

        /// <summary>
        /// Де лінія рейки проходить крізь площину перерізу station: площину через його середину впоперек
        /// його напрямку. Повертає переріз лінії в цьому місці або null, якщо лінія туди не доходить.
        /// offset зсуває лінію вбік на стільки блоків уздовж Side кожного її перерізу: так з однієї рейки
        /// колії виходить друга. near це номер перерізу лінії, біля якого шукати: якщо лінія перетинає
        /// площину не раз, береться найближчий до нього перетин.
        /// </summary>
        public static RailFrame Cross(IList<RailFrame> line, double offset, RailFrame station, int near)
        {
            if (line == null || line.Count == 0 || station == null) return null;

            int count = line.Count;
            near = GameMath.Clamp(near, 0, count - 1);

            // Наскільки переріз лінії попереду площини, блоків; за площиною від'ємне
            double Ahead(int i)
            {
                RailFrame frame = line[i];
                return (frame.Center.X + frame.Side.X * offset - station.Center.X) * station.Along.X
                    + (frame.Center.Y + frame.Side.Y * offset - station.Center.Y) * station.Along.Y
                    + (frame.Center.Z + frame.Side.Z * offset - station.Center.Z) * station.Along.Z;
            }

            // Крута колія може перетнути ту саму площину ще раз, далеко за поворотом. Потрібен перетин,
            // найближчий до near уздовж лінії. Тому відрізки лінії переглядаються по черзі в обидва боки
            // від near, і пошук зупиняється на першому знайденому: далі можуть бути лише дальші.
            // away це відстань до перетину в перерізах лінії
            RailFrame best = null;
            double bestAway = double.MaxValue;
            double forward = Ahead(near), backward = forward;

            for (int reach = 0; reach < count && best == null; reach++)
            {
                int from = near + reach;
                if (from < count - 1)
                {
                    double next = Ahead(from + 1);
                    if (Crosses(forward, next, out double part))
                    {
                        bestAway = reach + part;
                        best = Shifted(Between(line[from], line[from + 1], part), offset);
                    }
                    forward = next;
                }

                int back = near - reach - 1;
                if (back >= 0)
                {
                    double previous = Ahead(back);
                    if (Crosses(previous, backward, out double part) && reach + 1 - part < bestAway)
                    {
                        bestAway = reach + 1 - part;
                        best = Shifted(Between(line[back], line[back + 1], part), offset);
                    }
                    backward = previous;
                }

                if (from >= count - 1 && back < 0) break;
            }

            // Площина лежить за краєм лінії: продовжуємо лінію прямою від цього краю, але не далеко.
            // Це береться, і коли перетину всередині немає, і коли він є, але далі від near, ніж край
            for (int turn = 0; turn < 2; turn++)
            {
                int end = turn == 0 ? 0 : count - 1;
                if (Math.Abs(end - near) >= bestAway) continue;

                RailFrame last = line[end];
                double facing = last.Along.X * station.Along.X + last.Along.Y * station.Along.Y + last.Along.Z * station.Along.Z;
                if (Math.Abs(facing) < 1e-6) continue;

                // Лише назовні: всередині лінії перетин уже знайдено б
                double step = -Ahead(end) / facing;
                if (end == 0 ? step > 0 : step < 0) continue;
                if (Math.Abs(step) > MaxExtend) continue;

                double spacing = count > 1 ? line[end].Center.DistanceTo(line[end == 0 ? 1 : count - 2].Center) : 1;
                double away = Math.Abs(end - near) + (spacing > 1e-9 ? Math.Abs(step) / spacing : 0);
                if (away >= bestAway) continue;

                bestAway = away;
                best = Shifted(last, offset);
                best.Center.Add(last.Along.X * step, last.Along.Y * step, last.Along.Z * step);
            }

            return best;
        }

        // Чи площина лежить між двома сусідніми перерізами, які попереду неї на a і b блоків, і де саме:
        // part від 0 (перший) до 1 (другий). Переріз, що лежить на самій площині з точністю до похибки
        // обчислень, це теж перетин
        private static bool Crosses(double a, double b, out double part)
        {
            part = 0;
            if ((a > OnPlane && b > OnPlane) || (a < -OnPlane && b < -OnPlane)) return false;

            part = Math.Abs(a - b) < 1e-12 ? 0 : GameMath.Clamp(a / (a - b), 0, 1);
            return true;
        }

        /// <summary>
        /// Те саме, що Cross, але повертає лише місце перетину в самому перерізі station: відстань від його
        /// середини вздовж його боку, блоків. false, якщо лінія до перерізу не доходить.
        /// </summary>
        public static bool Offset(IList<RailFrame> line, double offset, RailFrame station, int near, out double across)
        {
            across = 0;
            RailFrame hit = Cross(line, offset, station, near);
            if (hit == null) return false;

            across = (hit.Center.X - station.Center.X) * station.Side.X + (hit.Center.Y - station.Center.Y) * station.Side.Y
                + (hit.Center.Z - station.Center.Z) * station.Side.Z;
            return true;
        }

        // Той самий переріз, зсунутий убік уздовж свого Side
        private static RailFrame Shifted(RailFrame frame, double offset)
        {
            return new RailFrame
            {
                Center = new Vec3d(frame.Center.X + frame.Side.X * offset, frame.Center.Y + frame.Side.Y * offset, frame.Center.Z + frame.Side.Z * offset),
                Side = frame.Side,
                Up = frame.Up,
                Along = frame.Along
            };
        }
    }
}
