using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Готові форми з меню: точки, які шаблон ставить за гравця, і маршрут, що з них виходить.
    /// </summary>
    public class RouteTemplateTests
    {
        private static readonly BlockPos Origin = new BlockPos(100, 10, 100, 0);

        private static List<PointOnBezierCurve> Cut(string mode, double yaw, int lengthOrRadius, int climb, bool left)
        {
            List<RouteMarker> markers = RouteTemplate.Build(Origin, yaw, mode, lengthOrRadius, climb, left);
            return RouteCurve.BuildPoints(RouteBuilder.Build(markers));
        }

        private static double PlanLength(List<PointOnBezierCurve> points)
        {
            double length = 0;
            for (int i = 1; i < points.Count; i++) length += RouteBuilder.HorizontalDistance(points[i - 1].position, points[i].position);
            return length;
        }

        [Fact]
        public void StraightTakesAsManyBlocksAsAsked()
        {
            // Погляд на схід: пряма на п'ять блоків іде від заднього краю першого блока до переднього краю п'ятого
            List<PointOnBezierCurve> points = Cut(RouteTemplate.Straight, 0, 5, 0, true);
            Assert.Equal(100, points[0].position.X, 6);
            Assert.Equal(105, points[points.Count - 1].position.X, 6);
            Assert.Equal(100.5, points[points.Count - 1].position.Z, 6);
            Assert.Null(RouteRules.FindProblem(points));

            // Один блок це найкоротша пряма
            Assert.Equal(1, PlanLength(Cut(RouteTemplate.SingleBlock, 0, 30, 3, true)), 6);
        }

        [Fact]
        public void StraightClimbsByTheAskedHeight()
        {
            List<PointOnBezierCurve> points = Cut(RouteTemplate.Straight, 0, 30, 4, true);
            Assert.Equal(4, points[points.Count - 1].position.Y - points[0].position.Y, 6);
            Assert.Null(RouteRules.FindProblem(points));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void QuarterTurnEndsAcrossItsStartOnTheAskedSide(bool left)
        {
            // Погляд на схід. Ліворуч від сходу це північ, тобто менший Z
            List<PointOnBezierCurve> points = Cut(RouteTemplate.Turn90, 0, 10, 0, left);
            Vec3f tangent = points[points.Count - 1].tangent;
            Assert.Equal(0, tangent.X, 3);
            Assert.Equal(left ? -1 : 1, tangent.Z, 3);
            Assert.Equal(left, points[points.Count - 1].position.Z < 100.5);
        }

        [Fact]
        public void TurnIsNotTighterThanTheAskedRadius()
        {
            // Чверть кола радіуса R має довжину R * pi / 2. Якби дуга вийшла тіснішою, вона була б коротшою,
            // а прямі на кінцях довшими; сам маршрут від цього лише довшає, тому міряємо найтісніше місце
            foreach (int radius in new[] { 4, 10, 30 })
            {
                List<PointOnBezierCurve> points = Cut(RouteTemplate.Turn90, 0, radius, 0, true);
                double tightest = double.MaxValue;
                for (int i = 2; i + 2 < points.Count; i++)
                {
                    Vec3d a = points[i - 2].position, b = points[i].position, c = points[i + 2].position;
                    double turn = Math.Abs(Math.Atan2((b.X - a.X) * (c.Z - b.Z) - (b.Z - a.Z) * (c.X - b.X), (b.X - a.X) * (c.X - b.X) + (b.Z - a.Z) * (c.Z - b.Z)));
                    if (turn > 1e-6) tightest = Math.Min(tightest, RouteBuilder.HorizontalDistance(a, c) / 2 / turn);
                }
                // Крива Безьє повторює коло не точно: до 7% крутіша місцями
                Assert.True(tightest > radius * 0.93, "радіус " + radius + ": найтісніше місце " + tightest.ToString("0.00"));
                Assert.True(tightest < radius + 2, "радіус " + radius + ": дуга надто простора, " + tightest.ToString("0.00"));
            }
        }

        [Theory]
        [InlineData(RouteTemplate.Turn90, 10, 6)]
        [InlineData(RouteTemplate.Turn90, 10, 3)]
        [InlineData(RouteTemplate.Turn90, 30, -8)]
        [InlineData(RouteTemplate.Turn45, 12, 4)]
        public void ClimbingTurnRisesInOneSmoothWave(string mode, int radius, int climb)
        {
            // Поворот з підйомом починається і закінчується рівно, а ухил плавно росте до середини і так само
            // спадає: без зламу на першому блоці і без горбка за ним
            List<PointOnBezierCurve> points = Cut(mode, 0, radius, climb, true);
            Assert.Null(RouteRules.FindProblem(points));
            Assert.Equal(climb, points[points.Count - 1].position.Y - points[0].position.Y, 6);

            var slopes = new List<double>();
            for (int i = 0; i + 2 < points.Count; i += 2)
            {
                Vec3d a = points[i].position, b = points[i + 2].position;
                slopes.Add((b.Y - a.Y) / RouteBuilder.HorizontalDistance(a, b) * Math.Sign(climb));
            }

            double average = Math.Abs(climb) / PlanLength(points);
            // Крайні секції майже рівні: стик із рівною колією без зламу
            Assert.True(slopes[0] < average * 0.25, "перша секція вже має ухил " + slopes[0].ToString("0.000") + " при середньому " + average.ToString("0.000"));
            Assert.True(slopes[slopes.Count - 1] < average * 0.25, "остання секція ще має ухил " + slopes[slopes.Count - 1].ToString("0.000"));

            // Одна хвиля: до найкрутішого місця ухил лише росте, після нього лише спадає
            int peak = slopes.IndexOf(Math.Max(slopes[0], System.Linq.Enumerable.Max(slopes)));
            for (int i = 1; i < slopes.Count; i++)
            {
                double change = slopes[i] - slopes[i - 1];
                Assert.True(i <= peak ? change > -0.004 : change < 0.004,
                    "ухил іде не однією хвилею біля секції " + i + ": " + slopes[i - 1].ToString("0.000") + " і " + slopes[i].ToString("0.000"));
            }
        }

        [Fact]
        public void EveryTemplateInEveryDirectionCanBeBuilt()
        {
            foreach (string mode in new[] { RouteTemplate.SingleBlock, RouteTemplate.Straight, RouteTemplate.Turn90, RouteTemplate.Turn45 })
            {
                for (int direction = 0; direction < 8; direction++)
                {
                    foreach (bool left in new[] { true, false })
                    {
                        foreach (int size in new[] { 4, 9, 30 })
                        {
                            List<PointOnBezierCurve> points = Cut(mode, direction * Math.PI / 4 + 0.2, size, 0, left);
                            string about = mode + ", напрямок " + direction + ", " + (left ? "ліворуч" : "праворуч") + ", розмір " + size;
                            Assert.True(points.Count >= 3, about + ": маршрут не склався");
                            Assert.True(RouteRules.FindProblem(points) == null, about + ": " + RouteRules.FindProblem(points));
                        }
                    }
                }
            }
        }
    }
}
