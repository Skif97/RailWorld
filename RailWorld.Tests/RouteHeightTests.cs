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
    }
}
