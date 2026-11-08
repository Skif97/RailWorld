using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Перехідні криві за рахунок прямих: планувальник (RouteBuilder) лишає між кривими прямі, а другий прохід
    /// (RouteCurve.SmoothCurves) розкладає на них стрибок кривини, не міняючи радіуса дуг.
    /// </summary>
    public class TransitionTests : TrackTestBase
    {
        private static RouteMarker Block(int x, int z) => RouteMarker.OnBlock(new BlockPos(x, 0, z, 0), 0);

        // Радіус у плані в кожній точці нарізки через секцію, як його міряють правила; на прямій нескінченність
        private static List<double> Radii(List<PointOnBezierCurve> points)
        {
            var radii = new List<double>();
            for (int i = 2; i + 2 < points.Count; i += 2)
            {
                Vec3d a = points[i - 2].position, b = points[i].position, c = points[i + 2].position;
                double turn = Math.Abs(Math.Atan2((b.X - a.X) * (c.Z - b.Z) - (b.Z - a.Z) * (c.X - b.X), (b.X - a.X) * (c.X - b.X) + (b.Z - a.Z) * (c.Z - b.Z)));
                radii.Add(turn > 1e-6 ? RouteBuilder.HorizontalDistance(a, c) / 2 / turn : double.PositiveInfinity);
            }
            return radii;
        }

        // Кривина зі знаком у кожній точці нарізки через секцію (поворот на довжину), і найбільша зміна між сусідніми як
        // частка найбільшої кривини маршруту: різкий вхід у дугу дає зміну на всю кривину дуги одразу (1), плавний
        // перехід розкладає її на секції. Підходить і для S-подібної кривої, де кривина проходить через нуль
        private static double SteepestCurvatureStep(List<PointOnBezierCurve> points)
        {
            var curvature = new List<double>();
            for (int i = 2; i + 2 < points.Count; i += 2)
            {
                Vec3d a = points[i - 2].position, b = points[i].position, c = points[i + 2].position;
                double turn = Math.Atan2((b.X - a.X) * (c.Z - b.Z) - (b.Z - a.Z) * (c.X - b.X), (b.X - a.X) * (c.X - b.X) + (b.Z - a.Z) * (c.Z - b.Z));
                curvature.Add(turn / (RouteBuilder.HorizontalDistance(a, c) / 2));
            }
            double most = 0, steepest = 0;
            foreach (double k in curvature) most = Math.Max(most, Math.Abs(k));
            for (int i = 1; i < curvature.Count; i++) steepest = Math.Max(steepest, Math.Abs(curvature[i] - curvature[i - 1]));
            return most < 1e-9 ? 0 : steepest / most;
        }

        // Найбільший стрибок кривини між сусідніми секціями, що ще вважається плавним наростанням. На найкоротшій
        // перехідній кривій (RouteCurve.MinTransition) кривина росте рівно від нуля, і на її початку радіус між сусідніми
        // секціями падає вдвічі; стрибок з прямої просто в дугу це падіння з нескінченності
        private const double SmoothJump = 2.5;

        // Найбільше, у скільки разів кривина між сусідніми точками стрибає в межах кривої тісніший за limit
        private static double SharpestJump(List<double> radii, double limit)
        {
            double sharpest = 1;
            for (int i = 1; i < radii.Count; i++)
            {
                double tighter = Math.Min(radii[i], radii[i - 1]), wider = Math.Max(radii[i], radii[i - 1]);
                if (tighter > limit) continue;
                sharpest = Math.Max(sharpest, double.IsInfinity(wider) ? double.PositiveInfinity : wider / tighter);
            }
            return sharpest;
        }

        [Fact]
        public void TemplateTurnEasesInAndKeepsItsRadius()
        {
            // Поворот радіуса 10 з меню: з прямої в дугу радіус спадає поступово, а сама дуга того радіуса, що задано
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(RouteBuilder.Build(RouteTemplate.Build(new BlockPos(100, 0, 100, 0), 0, RouteTemplate.Turn90, 10, 0, true)));
            List<double> radii = Radii(points);
            double tightest = double.MaxValue;
            foreach (double radius in radii) tightest = Math.Min(tightest, radius);
            Assert.InRange(tightest, 10 * 0.93, 12);
            // Без переходу радіус з прямої падав до 20 і одразу до 10.5; тепер наростає рівно
            Assert.True(SharpestJump(radii, 40) < SmoothJump, "кривина стрибає в " + SharpestJump(radii, 40).ToString("0.0") + " раза");
        }

        [Fact]
        public void CornersEaseInFromTheirStraights()
        {
            // Два кути: на стику прямої з кожною кривою кривина наростає поступово, і маршрут проходить правила
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(RouteBuilder.Build(new List<RouteMarker> { Block(0, 0), Block(20, 0), Block(30, 12), Block(30, 30) }));
            Assert.Null(RouteRules.FindProblem(points));
            List<double> radii = Radii(points);
            Assert.True(SharpestJump(radii, 40) < SmoothJump, "кривина стрибає в " + SharpestJump(radii, 40).ToString("0.0") + " раза");
        }

        // Точка маршруту; exact: маршрут іде через неї точно (кінець маршруту), інакше це кінець дуги, як їх ставить
        // планувальник
        private static RoutePoint At(double x, double z, double tx, double tz, bool exact = false)
        {
            return new RoutePoint { Position = new Vec3d(x, 0, z), Tangent = new Vec3d(tx, 0, tz).Normalize(), Exact = exact };
        }

        [Fact]
        public void UTurnEasesInBetweenTheSameStraights()
        {
            // Розворот між двома паралельними прямими за 12 блоків одна від одної: зсувом уздовж прямої колію на другу
            // пряму не навести, тож дуга стискається під перехідні криві; кривина не стрибає, і правила маршрут пропускають
            var route = new List<RoutePoint> { At(0, 0, 1, 0, true), At(12, 0, 1, 0), At(12, 12, -1, 0), At(0, 12, -1, 0, true) };
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(route, null, out string conflict);
            Assert.Null(conflict);
            Assert.Null(RouteRules.FindProblem(points));
            List<double> radii = Radii(points);
            Assert.True(SharpestJump(radii, 40) < SmoothJump, "кривина стрибає в " + SharpestJump(radii, 40).ToString("0.0") + " раза");
            // Друга пряма там само, де була: кінець маршруту на ній
            Assert.Equal(12, points[points.Count - 1].position.Z, 6);
        }

        // Точка на блоці з навмисно заданим напрямком
        private static RouteMarker Aimed(int x, int z, double hx, double hz)
        {
            return new RouteMarker { Block = new BlockPos(x, 0, z, 0), Position = new Vec3d(x + 0.5, 0.125, z + 0.5), Heading = new Vec3d(hx, 0, hz).Normalize(), Aimed = true };
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void UTurnBetweenTwoEndsBuildsWithItsTransitions(bool throughCorner)
        {
            // Розворот між двома кінцями, що дивляться в протилежні боки за 14 блоків: від кожного кінця спершу пряма,
            // тоді розворот з перехідними кривими. Так само, якщо гравець поставив посередині звичайну точку
            var markers = new List<RouteMarker> { Aimed(0, 0, 0, -1), Aimed(14, 0, 0, 1) };
            if (throughCorner) markers.Insert(1, RouteMarker.OnBlock(new BlockPos(7, 0, -9, 0), 0));
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(RouteBuilder.Build(markers), null, out string conflict);
            Assert.Null(conflict);
            Assert.Null(RouteRules.FindProblem(points));
            double step = SteepestCurvatureStep(points);
            Assert.True(step < 0.5, "кривина між сусідніми секціями міняється на " + step.ToString("0.00") + " найбільшої");
        }

        [Theory]
        [InlineData(14)]
        [InlineData(16)]
        [InlineData(18)]
        public void UTurnBetweenLaidTracksThroughAnOrdinaryPointBuilds(int width)
        {
            // Дві покладені прямі колії на північ за width блоків одна від одної, маршрут від кінця однієї до кінця
            // другої через звичайну точку посередині, як у грі. Від кожного стику лише півтора блока прямої, і розворот
            // ліг рівно на них: остання точка перед кінцем буває за соті частки блока від нього
            List<Section> leftTrack = Lay(Point(0.5, 8, 0, -1), Point(0.5, 0, 0, -1));
            Section left = leftTrack[leftTrack.Count - 1];
            Section right = Lay(Point(width + 0.5, 0, 0, 1), Point(width + 0.5, 8, 0, 1))[0];
            for (int top = 6; top <= 12; top++)
            {
                var markers = new List<RouteMarker>
                {
                    RouteMarker.OnJoint(left, false, 1, true, Resolve),
                    Block(width / 2, -top),
                    RouteMarker.OnJoint(right, true, -1, false, Resolve)
                };
                List<PointOnBezierCurve> points = RouteCurve.BuildPoints(RouteBuilder.Build(markers), Resolve, out string conflict, SectionsIn);
                Assert.True(conflict == null, "вершина за " + top + " блоків: " + conflict);
                Assert.Null(RouteRules.FindProblem(points));
            }
        }

        [Fact]
        public void CurveWithoutStraightForItsTransitionIsRefused()
        {
            // За кривою лише блок прямої до кінця маршруту, а перехідна крива бере з прямої щонайменше половину своїх
            // трьох блоків: відмова
            var route = new List<RoutePoint> { At(0, 0, 1, 0, true), At(10, 0, 1, 0), At(20, 10, 0, 1), At(20, 11, 0, 1, true) };
            RouteCurve.BuildPoints(route, null, out string conflict);
            Assert.StartsWith("перехідній кривій не вистачає прямої", conflict);
        }

        [Fact]
        public void CtrlPointOnABendStaysExactAndTheBendsEaseIn()
        {
            // Точка з Ctrl убік від прямої між кінцями: через неї лягає прямий шматок, вигини до неї й від неї стають
            // кутами з перехідними кривими, а колія проходить точно через точку
            RouteMarker ctrl = Block(20, 8);
            ctrl.Exact = true;
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(RouteBuilder.Build(new List<RouteMarker> { Block(0, 0), ctrl, Block(40, 0) }), null, out string conflict);
            Assert.Null(conflict);
            Assert.Null(RouteRules.FindProblem(points));
            double closest = double.MaxValue;
            foreach (PointOnBezierCurve point in points) closest = Math.Min(closest, RouteBuilder.HorizontalDistance(point.position, ctrl.Position));
            Assert.True(closest < 1e-6, "колія проходить за " + closest.ToString("0.000") + " від точки з Ctrl");
            List<double> radii = Radii(points);
            Assert.True(SharpestJump(radii, 40) < SmoothJump, "кривина стрибає в " + SharpestJump(radii, 40).ToString("0.0") + " раза");
        }

        [Fact]
        public void CornersCloseTogetherNeedNoStraightBetweenThem()
        {
            // Два кути на 90° за 9 блоків один від одного, S-подібно: між двома кривими прямої не треба, другий прохід
            // згладжує їх разом, як одну; пряма потрібна лише там, де крива сходиться з кінцем маршруту
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(RouteBuilder.Build(new List<RouteMarker> { Block(0, 0), Block(10, 0), Block(10, 9), Block(20, 9) }), null, out string conflict);
            Assert.Null(conflict);
            Assert.Null(RouteRules.FindProblem(points));
            double step = SteepestCurvatureStep(points);
            Assert.True(step < 0.5, "кривина між сусідніми секціями міняється на " + step.ToString("0.00") + " найбільшої");
        }
    }
}
