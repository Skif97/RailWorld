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
    }
}
