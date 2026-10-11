using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Перетин маршруту з уже покладеною колією в одному рівні: маршрут лягає в її площину,
    /// а до і після плавно веде до своїх точок.
    /// </summary>
    public class LevelCrossingTests : TrackTestBase
    {
        private static RoutePoint At(double x, double y, double z)
        {
            return new RoutePoint { Position = new Vec3d(x, y, z), Tangent = new Vec3d(1, 0, 0) };
        }

        [Fact]
        public void RouteClimbingAwayFromACrossingDoesNotDipIntoIt()
        {
            // Колія з півночі на південь на рівні нуля. Маршрут починається на тому самому рівні,
            // перетинає її за десять блоків і далі піднімається на чотири блоки
            Lay(Point(10, -10, 0, 1), Point(10, 10, 0, 1));
            var route = new List<RoutePoint> { At(0, 0, 0), At(40, 4, 0) };

            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(route, Resolve, out string conflict, SectionsIn);
            Assert.Null(conflict);

            // На перетині маршрут лежить у площині колії
            double atCrossing = double.NaN;
            foreach (PointOnBezierCurve point in points)
            {
                if (Math.Abs(point.position.X - 10) < 0.2) atCrossing = point.position.Y;
            }
            Assert.Equal(0, atCrossing, 2);

            // І ніде не йде вниз: від початку до перетину рівно, далі лише вгору
            for (int i = 1; i < points.Count; i++)
            {
                Assert.True(points[i].position.Y >= points[i - 1].position.Y - 1e-3,
                    "маршрут опускається біля x = " + points[i].position.X.ToString("0.0"));
            }
        }

        [Fact]
        public void RouteStillReachesItsOwnPointsAfterACrossing()
        {
            Lay(Point(10, -10, 0, 1), Point(10, 10, 0, 1));
            var route = new List<RoutePoint> { At(0, 0, 0), At(40, 4, 0) };

            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(route, Resolve, out _, SectionsIn);

            Assert.Equal(0, points[0].position.Y, 3);
            Assert.Equal(4, points[points.Count - 1].position.Y, 3);
        }
    }
}
