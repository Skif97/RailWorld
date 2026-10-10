using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Нарізка маршруту на точки секцій: RouteCurve.BuildPoints без покладеної колії поруч.
    /// </summary>
    public class RouteCurveTests
    {
        private static RoutePoint Point(double x, double z, double tangentX, double tangentZ)
        {
            return new RoutePoint { Position = new Vec3d(x, 0, z), Tangent = new Vec3d(tangentX, 0, tangentZ).Normalize() };
        }

        // Пряма між двома точками, що дивляться одна на одну, лишається прямою і ріжеться рівними відрізками
        [Fact]
        public void StraightRouteStaysStraight()
        {
            var route = new List<RoutePoint> { Point(0, 0, 1, 0), Point(10, 0, 1, 0) };

            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(route);

            // Секція це три точки поспіль з кроком два, тому точок завжди непарна кількість
            Assert.True(points.Count >= 3);
            Assert.Equal(1, points.Count % 2);

            Assert.Equal(0, points[0].position.X, 3);
            Assert.Equal(10, points[points.Count - 1].position.X, 3);

            for (int i = 0; i < points.Count; i++)
            {
                Assert.Equal(0, points[i].position.Z, 6);
                Assert.Equal(0, points[i].position.Y, 6);
                if (i == 0) continue;

                double step = points[i].position.X - points[i - 1].position.X;
                Assert.InRange(step, 0.2, 0.3);
            }
        }

        // Чверть кола між двома точками з дотичними по колу: крива тримається радіуса кола.
        // На цьому стоїть скруглення кутів маршруту: дуга між початком і кінцем скруглення має бути дугою
        [Fact]
        public void QuarterTurnFollowsTheCircle()
        {
            const double radius = 10;
            var route = new List<RoutePoint> { Point(0, 0, 1, 0), Point(radius, radius, 0, 1) };

            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(route);

            // Центр кола: звідки обидві точки видно під прямим кутом до їхніх дотичних
            double centerX = 0, centerZ = radius;
            foreach (PointOnBezierCurve point in points)
            {
                double fromCenter = Math.Sqrt(Math.Pow(point.position.X - centerX, 2) + Math.Pow(point.position.Z - centerZ, 2));
                Assert.InRange(fromCenter, radius * 0.97, radius * 1.03);
            }
        }

        // На прямій довжина керівного вектора це стала частка відстані між точками
        [Fact]
        public void HandleFactorOnStraightLine()
        {
            double factor = RouteCurve.HandleFactor(new Vec3d(1, 0, 0), new Vec3d(1, 0, 0));

            Assert.Equal((2 + 6 * RouteCurve.Weight) / (24 * RouteCurve.Weight), factor, 6);
        }

        // Маршрут із двох кривих поспіль не має зламу в точці, де вони сходяться
        [Fact]
        public void ConsecutiveCurvesJoinSmoothly()
        {
            var route = new List<RoutePoint> { Point(0, 0, 1, 0), Point(10, 2, 1, 0.4), Point(20, 8, 1, 0) };

            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(route);

            for (int i = 1; i < points.Count; i++)
            {
                Vec3f a = points[i - 1].tangent, b = points[i].tangent;
                double turn = Math.Abs(Math.Atan2(a.X * b.Z - a.Z * b.X, a.X * b.X + a.Z * b.Z));
                // Між сусідніми точками нарізки, за чверть блока, напрямок не може стрибнути
                Assert.True(turn < 0.2, $"злам {turn:0.000} рад у точці {i}");
            }
        }
    }
}
