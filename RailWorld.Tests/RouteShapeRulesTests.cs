using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Нарізка маршруту на точки і висоти біля стрілки маршруту: перехід від стрілки не жорстка умова для
    /// перетину далі по маршруту, надто коротка ділянка не пропускається мовчки, точок нарізки завжди непарно.
    /// </summary>
    public class RouteShapeRulesTests : TrackTestBase
    {
        private static RoutePoint Toward(double x, double z, double angle)
        {
            return new RoutePoint { Position = new Vec3d(x, 0, z), Tangent = new Vec3d(Math.Cos(angle), 0, Math.Sin(angle)) };
        }

        [Fact]
        public void CrossingFarFromRouteSwitchIsAllowed()
        {
            // Основна колія із заходу на схід. Від стику на x = 0 маршрут відходить під 30° прямо на дальню точку,
            // без проміжних: перехід від стрілки тягнеться на весь маршрут. За 25 блоків він перетинає колію з
            // півдня на північ, під 60° і далеко від стрілки
            List<Section> main = Lay(Point(-20, 0, 1, 0), Point(20, 0, 1, 0));
            double joint = JointNear(main, 0);
            Section track = main.Find(section => Math.Abs(section.FullStartPosition.X - joint) < 1e-6);
            Lay(Point(25, 5, 0, 1), Point(25, 30, 0, 1));

            double angle = Math.PI / 6;
            RoutePoint from = Toward(joint, 0, angle);
            from.Track = track;
            from.TrackJointAtStart = true;
            var route = new List<RoutePoint> { from, Toward(joint + 35 * Math.Cos(angle), 35 * Math.Sin(angle), angle) };

            RouteCurve.BuildPoints(route, Resolve, out string conflict, SectionsIn);
            Assert.Null(conflict);
        }

        [Fact]
        public void TooShortLegIsRefused()
        {
            var route = new List<RoutePoint> { Point(0, 0, 1, 0), Point(10, 0, 1, 0), Point(10.8, 0, 1, 0) };
            RouteCurve.BuildPoints(route, Resolve, out string conflict);
            Assert.NotNull(conflict);
        }

        [Fact]
        public void LegAfterSkippedOneKeepsItsStart()
        {
            // Середня ділянка надто коротка; наступна мусить починатися там, де стоїть її точка, а не з другої своєї
            var route = new List<RoutePoint> { Point(0, 0, 1, 0), Point(10, 0, 1, 0), Point(10.8, 0, 1, 0), Point(30, 0, 1, 0) };
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(route, Resolve, out _);
            Assert.Contains(points, point => Math.Abs(point.position.X - 10.8) < 1e-6);
        }

        // Точки нарізки гвинтової колії: радіус у плані, ухил, крок чверть блока вздовж колії
        private static List<PointOnBezierCurve> Helix(double radius, double slope, double length)
        {
            var points = new List<PointOnBezierCurve>();
            double cos = Math.Cos(slope), sin = Math.Sin(slope);
            for (double s = 0; s <= length; s += RouteCurve.PieceLength)
            {
                double phi = s * cos / radius;
                var tangent = new Vec3f((float)(-Math.Sin(phi) * cos), (float)sin, (float)(Math.Cos(phi) * cos));
                points.Add(new PointOnBezierCurve
                {
                    position = new Vec3d(radius * Math.Cos(phi), s * sin, radius * Math.Sin(phi)),
                    tangent = tangent,
                    normal = new Vec3f((float)Math.Cos(phi), 0, (float)Math.Sin(phi))
                });
            }
            return points;
        }

        [Fact]
        public void RadiusIsMeasuredAlongTheTrack()
        {
            // Гвинтовий підйом на 30°: натурний радіус це радіус у плані, поділений на cos² ухилу. Радіус у
            // плані на п'яту менший за межу, а натурний більший, тож такий підйом можна. Рівне коло того самого
            // радіуса в плані вже ні
            double limit = TrackRules.MinRadius - RouteRules.RadiusTolerance;
            double plan = limit * 0.8;
            Assert.Null(RouteRules.FindProblem(Helix(plan, Math.PI / 6, 6)));
            Assert.NotNull(RouteRules.FindProblem(Helix(plan, 0, 6)));
        }

        [Fact]
        public void CutAlwaysGivesOddCount()
        {
            // Прямі й дуги різної довжини, з кроком дрібнішим за шматок нарізки. Сама нарізка, без того, що з нею
            // робить маршрут
            for (int n = 0; n < 2000; n++)
            {
                double length = 1 + n * 0.0137;
                double turn = (n % 7) * 0.2;
                double endX = length * Math.Cos(turn / 2), endZ = length * Math.Sin(turn / 2);
                double handle = length / 3;
                var curve = new CubicBezierCurve3d(0, 0, 0, handle, 0, 0,
                    endX - Math.Cos(turn) * handle, 0, endZ - Math.Sin(turn) * handle, endX, 0, endZ,
                    1, RouteCurve.Weight, RouteCurve.Weight, 1);
                List<PointOnBezierCurve> points = curve.CutIntoEqualPieces(RouteCurve.PieceLength);

                Assert.True(points.Count % 2 == 1, "точок " + points.Count + " на довжині " + length.ToString("0.000"));
                // І останньою стоїть кінець кривої, без точки впритул перед ним
                Vec3d end = new Vec3d(endX, 0, endZ);
                Assert.True(points[points.Count - 1].position.DistanceTo(end) < 1e-6);
                Assert.True(points[points.Count - 2].position.DistanceTo(end) > RouteCurve.PieceLength / 2);
            }
        }
    }
}
