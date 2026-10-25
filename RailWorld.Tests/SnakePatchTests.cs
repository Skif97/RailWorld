using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;
using Xunit.Abstractions;

namespace RailWorld.Tests
{
    /// <summary>
    /// Перемичка між двома кінцями колії на змійці: дві дуги однакового радіуса з перегином посередині. Колія, що там
    /// лежала, правил не порушувала, тож і перемичка між її кінцями з її напрямками має їх не порушувати.
    /// </summary>
    public class SnakePatchTests
    {
        private readonly ITestOutputHelper output;

        public SnakePatchTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        // Точка на стику колії: точне місце і напрямок колії
        private static RouteMarker Joint(double x, double z, double headingAngle)
        {
            return new RouteMarker
            {
                Block = new BlockPos((int)Math.Floor(x), 10, (int)Math.Floor(z), 0),
                Position = new Vec3d(x, 10.1, z),
                Heading = new Vec3d(Math.Cos(headingAngle), 0, Math.Sin(headingAngle)),
                Snapped = true,
                Exact = true,
                Aimed = true
            };
        }

        [Theory]
        [InlineData(3.6, 45)]
        [InlineData(3.6, 60)]
        [InlineData(3.6, 90)]
        [InlineData(6, 45)]
        public void PatchAcrossASnakeKeepsToTheRules(double radius, double turnDeg)
        {
            // Від (100, 100) на схід: дуга радіуса radius ліворуч на turnDeg, потім така сама праворуч
            double turn = turnDeg * Math.PI / 180;
            double x = 100 + 2 * radius * Math.Sin(turn), z = 100 + 2 * radius * (1 - Math.Cos(turn));
            List<RoutePoint> route = RouteBuilder.Build(new List<RouteMarker> { Joint(100, 100, 0), Joint(x, z, 0) });
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(route);
            Assert.True(points != null && points.Count >= 3, "маршрут не склався");

            RouteCheck check = RouteRules.Check(points);
            output.WriteLine("радіус змійки " + radius + ", поворот " + turnDeg + ": найтісніше місце перемички " + check.MinRadius.ToString("0.00"));
            Assert.Null(RouteRules.FindProblem(points));
            // Перемичка повторює змійку: дві дуги того самого радіуса, а не тісніший вигин біля одного кінця
            Assert.True(check.MinRadius > radius * 0.98, "перемичка тісніша за змійку: " + check.MinRadius.ToString("0.00"));
        }
    }
}
