using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// RouteBuilder: маршрут із точок, які поставив гравець. Цей самий код складає маршрут і на клієнті
    /// для показу, і на сервері для побудови.
    /// </summary>
    public class RouteBuilderTests
    {
        // Точка на блоці, гравець дивиться на схід (кут 0) або куди сказано
        private static RouteMarker Block(int x, int z, double yaw = 0)
        {
            return RouteMarker.OnBlock(new BlockPos(x, 10, z, 0), yaw);
        }

        [Fact]
        public void MarkerOnABlockStandsOverItsMiddleAndLooksAlongTheGrid()
        {
            // Погляд трохи вбік від сходу округлюється до сходу
            RouteMarker marker = Block(5, 7, 0.1);

            Assert.Equal(5.5, marker.Position.X, 6);
            Assert.Equal(7.5, marker.Position.Z, 6);
            Assert.Equal(1, marker.Heading.X, 6);
            Assert.Equal(0, marker.Heading.Z, 6);
        }

        [Fact]
        public void OneMarkerIsNotARoute()
        {
            Assert.Null(RouteBuilder.Build(new List<RouteMarker> { Block(0, 0) }));
        }

        [Fact]
        public void TwoMarkersGiveAStraightRunFromBlockEdgeToBlockEdge()
        {
            List<RoutePoint> route = RouteBuilder.Build(new List<RouteMarker> { Block(0, 0), Block(10, 0) });

            Assert.Equal(2, route.Count);
            // Кінці винесені на краї своїх блоків, щоб колія займала блок цілком
            Assert.Equal(0, route[0].Position.X, 6);
            Assert.Equal(11, route[1].Position.X, 6);
            Assert.Equal(1, route[0].Tangent.X, 6);
            Assert.Equal(1, route[1].Tangent.X, 6);
        }

        [Fact]
        public void CornerMarkerIsRoundedAndExactMarkerIsPassedThrough()
        {
            // Схід, потім поворот на південь: проміжна точка це кут
            var corner = new List<RouteMarker> { Block(0, 0), Block(20, 0), Block(20, 20, -Math.PI / 2) };
            List<RoutePoint> rounded = RouteBuilder.Build(corner);

            // Замість кута стоять початок і кінець дуги: маршрут через сам кут не проходить
            Vec3d at = corner[1].Position;
            Assert.True(rounded.Count > 3);
            foreach (RoutePoint point in rounded) Assert.True(point.Position.DistanceTo(at) > 0.5);

            // Точна точка лишається в маршруті як є
            corner[1].Exact = true;
            List<RoutePoint> exact = RouteBuilder.Build(corner);
            Assert.Contains(exact, point => point.Position.DistanceTo(at) < 1e-6);
        }

        // Точка на блоці з напрямком, заданим навмисно (Ctrl), вектором у плані
        private static RouteMarker Facing(int x, int z, double headingX, double headingZ)
        {
            RouteMarker marker = Block(x, z);
            marker.Heading = new Vec3d(headingX, 0, headingZ).Normalize();
            marker.Aimed = true;
            return marker;
        }

        [Theory]
        // Друга точка за два блоки на схід, обидві навмисно дивляться на захід: маршрут мусить зробити повну петлю
        [InlineData(2, 0, -1, 0)]
        // Навскіс: петля чи розворот більше ніж на 180° на одному-двох блоках
        [InlineData(1, 1, -1, 0)]
        [InlineData(2, 1, -1, -1)]
        public void LoopBetweenTwoCloseMarkersIsTooTight(int dx, int dz, double headingX, double headingZ)
        {
            var markers = new List<RouteMarker> { Facing(0, 0, headingX, headingZ), Facing(dx, dz, headingX, headingZ) };
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(RouteBuilder.Build(markers));

            RouteCheck check = RouteRules.Check(points);
            Assert.True(check.MinRadius < TrackRules.MinRadius - RouteRules.RadiusTolerance);
            Assert.Contains(check.Reasons(), reason => reason.StartsWith("поворот крутіший"));
        }

        [Fact]
        public void FreeEndDoesNotDependOnWhereThePlayerLooked()
        {
            // Та сама кінцева точка, поставлена з різним поглядом без Ctrl: маршрут той самий
            List<RoutePoint> east = RouteBuilder.Build(new List<RouteMarker> { Facing(0, 0, 1, 0), Block(12, 5, 0) });
            List<RoutePoint> north = RouteBuilder.Build(new List<RouteMarker> { Facing(0, 0, 1, 0), Block(12, 5, Math.PI / 2) });

            Assert.Equal(east.Count, north.Count);
            for (int i = 0; i < east.Count; i++)
            {
                Assert.Equal(0, east[i].Position.DistanceTo(north[i].Position), 9);
                Assert.Equal(0, east[i].Tangent.DistanceTo(north[i].Tangent), 9);
            }
        }

        [Fact]
        public void TwoPointRouteWithAFreeEndIsOneArc()
        {
            // Від заданого напрямку на схід до вільної точки: маршрут одна дуга, і на кінці напрямок дзеркальний
            // відносно хорди. Тоді дуга кола торкається обох напрямків
            List<RoutePoint> route = RouteBuilder.Build(new List<RouteMarker> { Facing(0, 0, 1, 0), Block(12, 5) });
            Vec3d a = route[0].Position, b = route[route.Count - 1].Position;
            Vec3d chord = new Vec3d(b.X - a.X, 0, b.Z - a.Z).Normalize();
            Vec3d start = route[0].Tangent, end = route[route.Count - 1].Tangent;

            Assert.Equal(start.X * chord.X + start.Z * chord.Z, end.X * chord.X + end.Z * chord.Z, 6);
            Assert.Equal(-(start.X * chord.Z - start.Z * chord.X), end.X * chord.Z - end.Z * chord.X, 6);
        }

        [Fact]
        public void AimedEndKeepsTheDirectionThePlayerChose()
        {
            RouteMarker end = Block(12, 5, Math.PI / 2);
            end.Aimed = true;
            List<RoutePoint> route = RouteBuilder.Build(new List<RouteMarker> { Facing(0, 0, 1, 0), end });

            Assert.Equal(0, route[route.Count - 1].Tangent.DistanceTo(end.Heading), 9);
        }
    }
}
