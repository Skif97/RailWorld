using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Петля, що виходить зі стику на колії і повертається на ту саму колію далі: перетину із собою в ній немає.
    /// </summary>
    public class LoopBackTests : TrackTestBase
    {
        private static RoutePoint At(double x, double z, double tx, double tz)
        {
            return new RoutePoint { Position = new Vec3d(x, 0, z), Tangent = new Vec3d(tx, 0, tz).Normalize() };
        }

        [Theory]
        [InlineData(40)]
        [InlineData(24)]
        public void PassingLoopBackToTheSameTrackIsNotACrossing(double apart)
        {
            List<Section> main = Lay(Point(-40, 0, 1, 0), Point(60, 0, 1, 0));
            double a = JointNear(main, 0), b = JointNear(main, apart);
            Section from = main.Find(section => Math.Abs(section.FullStartPosition.X - a) < 1e-6);
            Section to = main.Find(section => Math.Abs(section.FullEndPosition.X - b) < 1e-6);

            RoutePoint start = At(a, 0, 1, 0);
            start.Track = from;
            start.TrackJointAtStart = true;
            RoutePoint end = At(b, 0, 1, 0);
            end.Track = to;
            end.TrackJointAtStart = false;
            // Від стику вбік на північ, уздовж колії і назад на неї далі по ходу
            var route = new List<RoutePoint> { start, At(a + apart / 3, 6, 1, 0), At(b - apart / 3, 6, 1, 0), end };

            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(route, Resolve, out string conflict, SectionsIn);
            RouteCheck check = RouteRules.Check(points);
            Assert.False(check.CrossesItself, string.Join("; ", check.Reasons()) + " / " + conflict);
        }
    }
}
