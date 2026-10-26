using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Висота маршруту між точками, які поставив гравець: вона не виходить за висоти самих точок. Раніше ухил у точці
    /// брався з її сусідів, і перед підйомом колія спершу пірнала нижче за початок (третій аудит: 0.475 блока).
    /// </summary>
    public class RouteHeightTests
    {
        // Колія може відхилитися від висоти точок хіба що на допуск висот
        private const double Tolerance = 0.02;

        private static RouteMarker Block(int x, int y, int z, double yaw = 0)
        {
            return RouteMarker.OnBlock(new BlockPos(x, y, z, 0), yaw);
        }

        private static List<PointOnBezierCurve> Cut(params RouteMarker[] markers)
        {
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(RouteBuilder.Build(new List<RouteMarker>(markers)));
            Assert.True(points != null && points.Count >= 3, "маршрут не склався");
            return points;
        }

        [Theory]
        [InlineData(0)]   // прямо
        [InlineData(10)]  // поворот на 90° у другій точці
        [InlineData(5)]   // поворот близько 45°
        public void LevelStretchBeforeAClimbDoesNotDip(int turnZ)
        {
            // Приклад з аудиту: A і B на одній висоті за 20 блоків, C ще далі й на 5 вище
            List<PointOnBezierCurve> points = Cut(Block(0, 10, 0), Block(20, 10, 0), Block(30, 15, turnZ));
            double start = points[0].position.Y;

            for (int i = 1; i < points.Count; i++)
            {
                double y = points[i].position.Y;
                Assert.True(y > start - Tolerance, "колія пірнає на " + (start - y).ToString("0.000") + " нижче початку біля точки " + i);
                Assert.True(y > points[i - 1].position.Y - Tolerance, "колія спускається біля точки " + i + ", хоча маршрут лише піднімається");
            }
        }

        [Fact]
        public void HillTopIsNotHigherThanItsPoint()
        {
            // Підйом і спуск: вершина на висоті середньої точки, а не вище
            List<PointOnBezierCurve> points = Cut(Block(0, 10, 0), Block(20, 15, 0), Block(40, 10, 0));
            double top = points[0].position.Y + 5;
            foreach (PointOnBezierCurve point in points)
            {
                Assert.True(point.position.Y < top + Tolerance, "колія виходить на " + (point.position.Y - top).ToString("0.000") + " вище вершини");
            }
        }

        [Fact]
        public void SteadyClimbKeepsItsSlopeThroughTheMiddlePoint()
        {
            // Рівний підйом через три точки не ламається в середній: ухил там той самий, що на обох ділянках
            List<PointOnBezierCurve> points = Cut(Block(0, 10, 0), Block(20, 14, 0), Block(40, 18, 0));
            int middle = points.Count / 2;
            Vec3d a = points[middle - 2].position, b = points[middle + 2].position;
            Assert.Equal(0.2, (b.Y - a.Y) / RouteBuilder.HorizontalDistance(a, b), 2);
        }

        [Theory]
        [InlineData(20, 0.1)]
        [InlineData(30, 0.15)]
        public void ClimbInATurnHasTheSameSlopeAlongTheWholeTrack(int side, double slope)
        {
            // Рівний підйом через кут на 90°: кінці на стиках колії з ухилом slope, кут на блоці посередині по висоті,
            // кінець на висоті slope на довжину самої колії. Висота розкладається по довжині колії, а не ламаної, тож
            // ухил скрізь той самий; по ламаній дуга, коротша за свої дві частини ламаної, мала б ухил крутіший
            RouteMarker Joint(double x, double y, double z, double hx, double hz) => new RouteMarker
            {
                Block = new BlockPos((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z), 0),
                Position = new Vec3d(x, y, z),
                Heading = new Vec3d(hx, slope, hz).Normalize(),
                Snapped = true, Exact = true, Aimed = true
            };
            List<PointOnBezierCurve> Route(double length)
            {
                RouteMarker corner = RouteMarker.OnBlock(new BlockPos(side, 0, 0, 0), 0);
                corner.Position = new Vec3d(side + 0.5, 10 + slope * length / 2, 0.5);
                return Cut(Joint(0.5, 10, 0.5, 1, 0), corner, Joint(side + 0.5, 10 + slope * length, side + 0.5, 0, 1));
            }
            // Довжину колії видно лише з самого маршруту: скільки кут забирає від прямих, вирішує RouteBuilder
            List<PointOnBezierCurve> first = Route(2 * side);
            double planLength = 0;
            for (int i = 1; i < first.Count; i++) planLength += RouteBuilder.HorizontalDistance(first[i - 1].position, first[i].position);
            List<PointOnBezierCurve> points = Route(planLength);

            double steepest = 0, gentlest = double.MaxValue;
            for (int i = 4; i + 4 < points.Count; i += 2)
            {
                Vec3d a = points[i - 2].position, b = points[i + 2].position;
                double s = (b.Y - a.Y) / RouteBuilder.HorizontalDistance(a, b);
                steepest = Math.Max(steepest, s);
                gentlest = Math.Min(gentlest, s);
            }
            Assert.True(steepest < gentlest * 1.05, "ухил від " + gentlest.ToString("0.000") + " до " + steepest.ToString("0.000") + " при заданому " + slope);
        }
    }
}
