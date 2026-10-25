using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;
using Xunit.Abstractions;

namespace RailWorld.Tests
{
    /// <summary>
    /// Гілка від стрілки на схилі: спершу лежить на полотні основної колії, потім переходить на висоту наступної точки
    /// маршруту. Перехід не має пролітати нижче за обидва свої кінці.
    /// </summary>
    public class BranchSagTests : TrackTestBase
    {
        private readonly ITestOutputHelper output;

        public BranchSagTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static RoutePoint At(double x, double y, double z, double tx, double ty, double tz)
        {
            return new RoutePoint { Position = new Vec3d(x, y, z), Tangent = new Vec3d(tx, ty, tz).Normalize() };
        }

        [Theory]
        [InlineData(0.1, 8)]
        [InlineData(0.2, 8)]
        [InlineData(0.1, 16)]
        public void BranchOffASlopeDoesNotSagBelowItsEnds(double slope, double sideways)
        {
            // Основна колія на схід, униз на slope за блок
            List<Section> main = Lay(At(-40, 40 * slope, 0, 1, -slope, 0), At(60, -60 * slope, 0, 1, -slope, 0));
            double a = JointNear(main, 0);
            Section from = main.Find(section => Math.Abs(section.FullStartPosition.X - a) < 1e-6);
            double jointY = from.FullStartPosition.Y;

            // Гілка від стику на схід і вбік, до точки на висоті самого стику
            RoutePoint start = At(a, jointY, 0, 1, -slope, 0);
            start.Track = from;
            start.TrackJointAtStart = true;
            RoutePoint end = At(a + 24, jointY, sideways, 1, 0, 0);
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(new List<RoutePoint> { start, end }, Resolve, out string conflict, SectionsIn);
            Assert.True(points != null, conflict);

            // Поки гілка лежить поверх основної колії, вона йде її полотном і спускається разом із нею: це неминуче.
            // Полотно основної колії це площина, висота на ній залежить лише від x. Перехід починається там, де гілка
            // востаннє на ній лежить; нижче за висоту в цьому місці він може опуститися лише на короткий розворот
            // ухилу, а не пролітати вниз на всій довжині
            int leave = -1;
            for (int i = 0; i < points.Count; i++)
            {
                double plane = jointY - slope * (points[i].position.X - a);
                if (Math.Abs(points[i].position.Y - plane) < 1e-3) leave = i;
            }
            Assert.True(leave > 0, "гілка не сходить з полотна основної колії");
            double lowest = double.MaxValue;
            for (int i = leave; i < points.Count; i++) lowest = Math.Min(lowest, points[i].position.Y);
            double below = points[leave].position.Y - lowest;
            output.WriteLine("ухил " + slope + ", убік " + sideways + ": перехід нижче за місце сходу на " + below.ToString("0.000"));
            Assert.Null(conflict);
            // Розворот ухилу від slope до нуля з радіусом переламу R опускає колію на slope² · R / 2; R удвічі більший за
            // найменший радіус (RouteCurve.MonotoneRamp)
            double turnaround = slope * slope * 2 * TrackRules.MinRadius / 2;
            Assert.True(below < turnaround + 0.01, "перехід пролітає на " + below.ToString("0.000") + " нижче за місце сходу з основної колії, а розворот ухилу лише " + turnaround.ToString("0.000"));
        }

        [Theory]
        [InlineData(0.4, 5, 14)]
        [InlineData(0.4, 6, 18)]
        [InlineData(0.8, 6, 24)]
        public void LinkBetweenTwoSwitchesClimbsOverTheWholeTransition(double rise, double apart, double length)
        {
            // Перемичка між двома паралельними коліями на різній висоті. Біля кожної стрілки маршрут ще трохи йде
            // площиною своєї колії, але це утримання поступається переходу: перехід не коротший, ніж потрібно на таку
            // різницю висот, тож найкрутіше місце не крутіше за рівномірний підйом на довжині переходу більш ніж удвічі (сам плавний
            // перехід із рівними кінцями посередині крутіший за рівномірний у півтора раза)
            List<Section> lower = Lay(At(-40, 0, 0, 1, 0, 0), At(60, 0, 0, 1, 0, 0));
            List<Section> upper = Lay(At(-40, rise, apart, 1, 0, 0), At(60, rise, apart, 1, 0, 0));
            double a = JointNear(lower, 0);
            Section from = lower.Find(section => Math.Abs(section.FullStartPosition.X - a) < 1e-6);
            Section to = upper[0];
            foreach (Section section in upper)
            {
                if (Math.Abs(section.FullEndPosition.X - (a + length)) < Math.Abs(to.FullEndPosition.X - (a + length))) to = section;
            }

            var markers = new List<RouteMarker>
            {
                new RouteMarker { Block = new BlockPos(0, 0, 0, 0), Position = from.FullStartPosition.Clone(), Heading = new Vec3d(1, 0, 0), Snapped = true, Exact = true, Aimed = true, Track = from, TrackJointAtStart = true },
                new RouteMarker { Block = new BlockPos(0, 0, 0, 0), Position = to.FullEndPosition.Clone(), Heading = new Vec3d(1, 0, 0), Snapped = true, Exact = true, Aimed = true, Track = to, TrackJointAtStart = false }
            };
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(RouteBuilder.Build(markers), Resolve, out string conflict, SectionsIn);
            Assert.Null(conflict);

            double steepest = 0;
            for (int i = 2; i < points.Count; i += 2)
            {
                Vec3d p0 = points[i - 2].position, p1 = points[i].position;
                steepest = Math.Max(steepest, Math.Abs(p1.Y - p0.Y) / RouteBuilder.HorizontalDistance(p0, p1));
            }
            double even = rise / Math.Max(rise * 6, 4);
            output.WriteLine("підйом " + rise + ", убік " + apart + ", довжина " + length + ": найкрутіше " + steepest.ToString("0.000") + ", рівномірно " + even.ToString("0.000"));
            Assert.True(steepest < even * 2, "перехід стиснутий у сходинку: найкрутіше місце " + steepest.ToString("0.000") + " при рівномірному " + even.ToString("0.000"));
        }
    }
}
