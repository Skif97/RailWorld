using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Перехідні криві за рахунок прямих: планувальник (RouteBuilder) лишає між кривими прямі, а другий прохід
    /// (RouteCurve.SmoothCurves) розкладає на них стрибок кривини, не міняючи радіуса дуг.
    /// </summary>
    public class TransitionTests
    {
        private static RouteMarker Block(int x, int z) => RouteMarker.OnBlock(new BlockPos(x, 0, z, 0), 0);

        // Радіус у плані в кожній точці нарізки через секцію, як його міряють правила; на прямій нескінченність
        private static List<double> Radii(List<PointOnBezierCurve> points)
        {
            var radii = new List<double>();
            for (int i = 2; i + 2 < points.Count; i += 2)
            {
                Vec3d a = points[i - 2].position, b = points[i].position, c = points[i + 2].position;
                double turn = Math.Abs(Math.Atan2((b.X - a.X) * (c.Z - b.Z) - (b.Z - a.Z) * (c.X - b.X), (b.X - a.X) * (c.X - b.X) + (b.Z - a.Z) * (c.Z - b.Z)));
                radii.Add(turn > 1e-6 ? RouteBuilder.HorizontalDistance(a, c) / 2 / turn : double.PositiveInfinity);
            }
            return radii;
        }

        // Найбільше, у скільки разів кривина між сусідніми точками стрибає в межах кривої тісніший за limit
        private static double SharpestJump(List<double> radii, double limit)
        {
            double sharpest = 1;
            for (int i = 1; i < radii.Count; i++)
            {
                double tighter = Math.Min(radii[i], radii[i - 1]), wider = Math.Max(radii[i], radii[i - 1]);
                if (tighter > limit) continue;
                sharpest = Math.Max(sharpest, double.IsInfinity(wider) ? double.PositiveInfinity : wider / tighter);
            }
            return sharpest;
        }

        [Fact]
        public void TemplateTurnEasesInAndKeepsItsRadius()
        {
            // Поворот радіуса 10 з меню: з прямої в дугу радіус спадає поступово, а сама дуга того радіуса, що задано
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(RouteBuilder.Build(RouteTemplate.Build(new BlockPos(100, 0, 100, 0), 0, RouteTemplate.Turn90, 10, 0, true)));
            List<double> radii = Radii(points);
            double tightest = double.MaxValue;
            foreach (double radius in radii) tightest = Math.Min(tightest, radius);
            Assert.InRange(tightest, 10 * 0.93, 12);
            // Без переходу радіус зі 99 падав до 20 і одразу до 10.5; тепер між сусідніми секціями не більше ніж уполовину
            Assert.True(SharpestJump(radii, 40) < 2, "кривина стрибає в " + SharpestJump(radii, 40).ToString("0.0") + " раза");
        }

        [Fact]
        public void CornersEaseInFromTheirStraights()
        {
            // Два кути: на стику прямої з кожною кривою кривина наростає поступово, і маршрут проходить правила
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(RouteBuilder.Build(new List<RouteMarker> { Block(0, 0), Block(20, 0), Block(30, 12), Block(30, 30) }));
            Assert.Null(RouteRules.FindProblem(points));
            List<double> radii = Radii(points);
            Assert.True(SharpestJump(radii, 40) < 2, "кривина стрибає в " + SharpestJump(radii, 40).ToString("0.0") + " раза");
        }

        [Fact]
        public void CornersTooCloseForTheirStraightAreRefused()
        {
            // Два кути на 90° за 9 блоків один від одного: дугам найменшого радіуса самим місця вистачило б (по 3.7), а з
            // прямою між ними ні. Пряма не поступається, тож маршрут відхилено, і гравець зсуває точку
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(RouteBuilder.Build(new List<RouteMarker> { Block(0, 0), Block(10, 0), Block(10, 9), Block(20, 9) }));
            Assert.StartsWith("поворот крутіший", RouteRules.FindProblem(points));
        }
    }
}
