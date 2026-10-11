using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>Що показала перевірка нарізаного маршруту.</summary>
    public class RouteCheck
    {
        /// <summary>Для кожної точки нарізки: чи є в ній порушення.</summary>
        public bool[] Problems;
        public double MinRadius;
        public double MinVerticalRadius;
        public bool CrossesItself;
        public bool Broken;

        /// <summary>Усі порушення словами, найважче перше; порожній список, якщо маршрут годиться.</summary>
        public List<string> Reasons()
        {
            var reasons = new List<string>();
            if (Broken) reasons.Add("рейки розриваються по висоті");
            if (MinRadius < RouteRules.MinRadius - RouteRules.RadiusTolerance) reasons.Add("поворот крутіший за радіус " + RouteRules.MinRadius.ToString("0.0"));
            if (MinVerticalRadius < RouteRules.MinRadius - RouteRules.RadiusTolerance) reasons.Add("перелом по висоті крутіший за радіус " + RouteRules.MinRadius.ToString("0.0"));
            if (CrossesItself) reasons.Add("маршрут перетинає сам себе");
            return reasons;
        }
    }

    /// <summary>
    /// Межі, яких має триматися сам маршрут, незалежно від колій навколо: радіус повороту, перелом по висоті,
    /// розрив рейок, перетин із собою. Клієнт перевіряє цим те, що показує, сервер те, що будує.
    /// Правила щодо вже покладеної колії лежать у RouteCurve і SectionLinker.
    /// </summary>
    public static class RouteRules
    {
        // Найменший радіус повороту в плані і найменший радіус перелому по висоті, в блоках.
        // У ширинах колії, як і розміри стрілки: при колії 0.78 це 3.51 блока
        public const double MinRadius = TrackGauge.StandardWidth * 4.5;
        // Крива між точками не ідеальне коло, тому радіус трохи менший за межу ще не порушення
        public const double RadiusTolerance = TrackGauge.StandardWidth * 0.3;
        // Скільки блоків по висоті має бути між двома витками маршруту, що проходять один над одним
        public const double Clearance = 3;
        // Точки маршруту, ближчі одна до одної за це вздовж колії, на перетин не перевіряються: це сусіди
        private const double NeighbourRun = 6;
        // На скількох точках нарізки в кожен бік міряється радіус (точка це чверть блока)
        private const int RadiusWindow = 4;
        // Між сусідніми точками нарізки ухил, крутіший за це, означає розрив рейок, а не підйом, градуси
        public const double BreakSlopeDeg = 60;

        /// <summary>
        /// Коротка причина, чому нарізаний маршрут не можна будувати, або null, якщо можна.
        /// </summary>
        public static string FindProblem(List<PointOnBezierCurve> points)
        {
            List<string> reasons = Check(points).Reasons();
            return reasons.Count == 0 ? null : reasons[0];
        }

        /// <summary>
        /// Перевіряє нарізаний маршрут: поворот у плані або перелом по висоті крутіший за найменший радіус,
        /// сходинка між сусідніми точками, інший виток маршруту ближче за просвіт.
        /// </summary>
        public static RouteCheck Check(List<PointOnBezierCurve> points)
        {
            int count = points.Count;
            bool[] problems = new bool[count];
            double minRadius = double.MaxValue;
            double minVerticalRadius = double.MaxValue;
            bool crossesItself = false;
            bool broken = false;
            var check = new RouteCheck { Problems = problems, MinRadius = minRadius, MinVerticalRadius = minVerticalRadius };
            if (count < 3) return check;

            // Сходинка: сусідні точки нарізки стоять на різній висоті майже одна над одною
            for (int i = 0; i + 1 < count; i++)
            {
                if (SlopeDeg(points[i].position, points[i + 1].position) <= BreakSlopeDeg) continue;
                problems[i] = true;
                problems[i + 1] = true;
                broken = true;
            }

            // Відстань уздовж колії від початку маршруту
            double[] run = new double[count];
            for (int i = 1; i < count; i++)
            {
                Vec3d from = points[i - 1].position, to = points[i].position;
                double dx = to.X - from.X, dy = to.Y - from.Y, dz = to.Z - from.Z;
                run[i] = run[i - 1] + Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }

            // Радіус це довжина відрізка колії, поділена на кут, на який на ньому повернула дотична:
            // окремо в плані і окремо по висоті
            for (int i = 0; i < count; i++)
            {
                int a = Math.Max(0, i - RadiusWindow);
                int b = Math.Min(count - 1, i + RadiusWindow);
                double length = run[b] - run[a];
                if (length < 1e-6) continue;

                Vec3f ta = points[a].tangent, tb = points[b].tangent;
                double turn = Math.Abs(Math.Atan2(ta.X * tb.Z - ta.Z * tb.X, ta.X * tb.X + ta.Z * tb.Z));
                double pitch = Math.Abs(Pitch(tb) - Pitch(ta));

                double radius = turn > 1e-6 ? length / turn : double.MaxValue;
                double verticalRadius = pitch > 1e-6 ? length / pitch : double.MaxValue;

                if (radius < minRadius) minRadius = radius;
                if (verticalRadius < minVerticalRadius) minVerticalRadius = verticalRadius;
                if (radius < MinRadius - RadiusTolerance || verticalRadius < MinRadius - RadiusTolerance) problems[i] = true;
            }

            // Злам між сусідніми секціями. Попередня перевірка порівнює напрямок на кінцях відрізка
            // у два блоки і не бачить вигину, що вміщається всередину нього: колія пірнає і вирівнюється
            // або звертає вбік і одразу назад, а напрямок на обох кінцях той самий. Колія ж малюється
            // секціями по дві точки нарізки, і злам це кут між двома сусідніми: його і міряємо.
            // У плані досить осі: рейки там повторюють її на сталій відстані
            for (int i = 2; i + 2 < count; i++)
            {
                Vec3d before = points[i - 2].position, here = points[i].position, after = points[i + 2].position;
                double ax = here.X - before.X, az = here.Z - before.Z;
                double bx = after.X - here.X, bz = after.Z - here.Z;
                double turn = Math.Abs(Math.Atan2(ax * bz - az * bx, ax * bx + az * bz));
                if (turn < 1e-6) continue;

                // Від середини однієї секції до середини наступної
                double radius = (run[i + 2] - run[i - 2]) / 2 / turn;
                if (radius < minRadius) minRadius = radius;
                if (radius < MinRadius - RadiusTolerance) problems[i] = true;
            }

            // Те саме по висоті.
            // Міряється по осі колії і по кожній рейці окремо. Там, де різко міняється нахил полотна, вісь
            // лишається на місці, а рейки розходяться: одна йде вгору, друга вниз, і ламаються обидві
            double halfGauge = TrackGauge.StandardWidth / 2;
            for (int i = 2; i + 2 < count; i++)
            {
                for (int side = -1; side <= 1; side++)
                {
                    double offset = side * halfGauge;
                    if (side != 0 && (points[i - 2].normal == null || points[i].normal == null || points[i + 2].normal == null)) continue;

                    double bend = Math.Abs(ChordPitch(points[i], points[i + 2], offset) - ChordPitch(points[i - 2], points[i], offset));
                    if (bend < 1e-6) continue;

                    // Від середини однієї секції до середини наступної
                    double verticalRadius = (run[i + 2] - run[i - 2]) / 2 / bend;
                    if (verticalRadius < minVerticalRadius) minVerticalRadius = verticalRadius;
                    if (verticalRadius < MinRadius - RadiusTolerance) problems[i] = true;
                }
            }

            // Перетин із собою. Точки розкладені по клітинках, щоб не порівнювати кожну з кожною. Клітинка
            // не менша за відстань, на якій шукаємо: тоді все, що може бути поруч, лежить у сусідніх клітинках
            double reach = Footprint.StandardWidth;
            int cellSize = (int)Math.Ceiling(reach);

            var cells = new Dictionary<long, List<int>>();
            for (int i = 0; i < count; i++)
            {
                long cell = CellKey((int)Math.Floor(points[i].position.X / cellSize), (int)Math.Floor(points[i].position.Z / cellSize));
                if (!cells.TryGetValue(cell, out List<int> list)) cells[cell] = list = new List<int>();
                list.Add(i);
            }

            for (int i = 0; i < count; i++)
            {
                Vec3d p = points[i].position;
                int cx = (int)Math.Floor(p.X / cellSize), cz = (int)Math.Floor(p.Z / cellSize);

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!cells.TryGetValue(CellKey(cx + dx, cz + dz), out List<int> list)) continue;

                        foreach (int j in list)
                        {
                            // Кожну пару дивимося один раз, і лише далекі одна від одної вздовж колії
                            if (j <= i || run[j] - run[i] < NeighbourRun) continue;

                            Vec3d q = points[j].position;
                            if (Math.Abs(q.Y - p.Y) >= Clearance) continue;
                            if (RouteBuilder.HorizontalDistance(p, q) >= reach) continue;

                            problems[i] = true;
                            problems[j] = true;
                            crossesItself = true;
                        }
                    }
                }
            }

            check.MinRadius = minRadius;
            check.MinVerticalRadius = minVerticalRadius;
            check.CrossesItself = crossesItself;
            check.Broken = broken;
            return check;
        }

        // Нахил відрізка між двома точками на рейці, радіани. Точка на рейці лежить за offset блоків убік
        // від осі колії вздовж нормалі полотна; нуль це сама вісь
        private static double ChordPitch(PointOnBezierCurve from, PointOnBezierCurve to, double offset)
        {
            double fromX = from.position.X, fromY = from.position.Y, fromZ = from.position.Z;
            double toX = to.position.X, toY = to.position.Y, toZ = to.position.Z;
            if (offset != 0 && from.normal != null)
            {
                fromX += from.normal.X * offset;
                fromY += from.normal.Y * offset;
                fromZ += from.normal.Z * offset;
            }
            if (offset != 0 && to.normal != null)
            {
                toX += to.normal.X * offset;
                toY += to.normal.Y * offset;
                toZ += to.normal.Z * offset;
            }

            double dx = toX - fromX, dz = toZ - fromZ;
            return Math.Atan2(toY - fromY, Math.Sqrt(dx * dx + dz * dz));
        }

        private static double Pitch(Vec3f tangent)
        {
            return Math.Atan2(tangent.Y, Math.Sqrt(tangent.X * tangent.X + tangent.Z * tangent.Z));
        }

        private static long CellKey(int x, int z)
        {
            return ((long)x << 32) ^ (uint)z;
        }

        /// <summary>Ухил відрізка між двома точками, градуси.</summary>
        public static double SlopeDeg(Vec3d a, Vec3d b)
        {
            double run = RouteBuilder.HorizontalDistance(a, b);
            return Math.Atan2(Math.Abs(b.Y - a.Y), run) * GameMath.RAD2DEG;
        }
    }
}
