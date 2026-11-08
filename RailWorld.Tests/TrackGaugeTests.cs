using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>Колії різної ширини: розміри, правила в ширинах колії, і що вони не з'єднуються.</summary>
    public class TrackGaugeTests : TrackTestBase
    {
        // Секції цієї ширини по маршруту з точок
        private static List<Section> Sections(TrackGauge gauge, params RoutePoint[] route)
        {
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(new List<RoutePoint>(route));
            var sections = new List<Section>();
            for (int i = 0; i + 2 < points.Count; i += 2) sections.Add(gauge.NewSection(points[i], points[i + 1], points[i + 2]));
            return sections;
        }

        [Fact]
        public void BroadTrackIs1520WithItsOwnSleeper()
        {
            Section section = Sections(TrackGauge.Broad, Point(0, 0, 1, 0), Point(4, 0, 1, 0))[0];
            Assert.Equal(1.52f, section.TrackWidth, 3);
            Assert.Equal(2.75f, section.SleeperLength, 3);
            Assert.Same(TrackGauge.Broad, TrackGauge.Of(section));
            Assert.Same(TrackGauge.Narrow, TrackGauge.ByCode("unknown"));
        }

        [Theory]
        [InlineData(0.12, false)]
        [InlineData(0.09, true)]
        public void BroadTrackIsNoSteeperThanItsMaxSlope(double slope, bool allowed)
        {
            // Пряма з одним ухилом: зміни ухилу немає, лишається сам ухил. На широкій колії межа 10%
            var route = new List<RoutePoint>
            {
                new RoutePoint { Position = new Vec3d(0, 0, 0), Tangent = new Vec3d(1, slope, 0).Normalize() },
                new RoutePoint { Position = new Vec3d(40, 40 * slope, 0), Tangent = new Vec3d(1, slope, 0).Normalize() }
            };
            string problem = RouteRules.FindProblem(RouteCurve.BuildPoints(route), TrackGauge.Broad);
            if (allowed) Assert.Null(problem);
            else Assert.Contains("ухил крутіший", problem);
            // Вузька колія такий ухил дозволяє
            Assert.Null(RouteRules.FindProblem(RouteCurve.BuildPoints(route), TrackGauge.Narrow));
        }

        [Fact]
        public void BroadTrackHasGentlerRules()
        {
            Assert.True(TrackGauge.Broad.MinRadius > TrackGauge.Narrow.MinRadius);
            Assert.True(TrackGauge.Broad.MaxBladeSections > TrackGauge.Narrow.MaxBladeSections);

            // Дуга радіуса 5: вузькій колії годиться (межа 3.51), широкій ні (межа 45)
            double radius = 5, angle = Math.PI / 3;
            var route = new List<RoutePoint> { Point(0, 0, 1, 0), Point(radius * Math.Sin(angle), radius * (1 - Math.Cos(angle)), Math.Cos(angle), Math.Sin(angle)) };
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(route);
            Assert.Null(RouteRules.FindProblem(points, TrackGauge.Narrow));
            Assert.NotNull(RouteRules.FindProblem(points, TrackGauge.Broad));
        }

        [Fact]
        public void TracksOfDifferentGaugeDoNotMeet()
        {
            Lay(Point(0, 0, 1, 0), Point(10, 0, 1, 0));

            // Продовження з кінця вузької колії: вузьке з нею сходиться, широке ні
            Section narrow = Sections(TrackGauge.Narrow, Point(10, 0, 1, 0), Point(14, 0, 1, 0))[0];
            Section broad = Sections(TrackGauge.Broad, Point(10, 0, 1, 0), Point(14, 0, 1, 0))[0];
            Assert.NotEmpty(SectionLinker.Meeting(narrow, true, SectionsIn));
            Assert.Empty(SectionLinker.Meeting(broad, true, SectionsIn));
        }
    }
}
