using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Пам'ять розмітки між перерахунками: з нею маршрут і всі відмови мають виходити ті самі,
    /// а рахуватися наново має лише те, що змінилося.
    /// </summary>
    public class RouteCacheTests : TrackTestBase
    {
        // Маршрут зигзагом на схід через count точок, остання зсунута на (lastX, lastZ)
        private static List<RoutePoint> Zigzag(int count, int lastX, int lastZ, int y = 0)
        {
            var markers = new List<RouteMarker>();
            for (int i = 0; i < count; i++)
            {
                markers.Add(RouteMarker.OnBlock(new BlockPos(100 + i * 30, y, 200 + (i % 2) * 8, 0), 0));
            }
            markers.Add(RouteMarker.OnBlock(new BlockPos(100 + count * 30 + lastX, y, 200 + lastZ, 0), 0));
            return RouteBuilder.Build(markers);
        }

        private void LayWorld()
        {
            // Колія впоперек маршруту на його рівні: перетин в одному рівні
            Lay(new RoutePoint { Position = new Vec3d(160, 0.125, 180), Tangent = new Vec3d(0, 0, 1) },
                new RoutePoint { Position = new Vec3d(160, 0.125, 230), Tangent = new Vec3d(0, 0, 1) });
            // Колія впоперек на блок вище: надто близько над маршрутом
            Lay(new RoutePoint { Position = new Vec3d(250, 1.5, 180), Tangent = new Vec3d(0, 0, 1) },
                new RoutePoint { Position = new Vec3d(250, 1.5, 230), Tangent = new Vec3d(0, 0, 1) });
            // Колія високо над маршрутом: міст, нікого не стосується
            Lay(new RoutePoint { Position = new Vec3d(310, 8, 180), Tangent = new Vec3d(0, 0, 1) },
                new RoutePoint { Position = new Vec3d(310, 8, 230), Tangent = new Vec3d(0, 0, 1) });
            // Колія вздовж маршруту поруч із ним
            Lay(new RoutePoint { Position = new Vec3d(330, 0.125, 204.5), Tangent = new Vec3d(1, 0, 0) },
                new RoutePoint { Position = new Vec3d(380, 0.125, 204.5), Tangent = new Vec3d(1, 0, 0) });
        }

        private static void AssertSame(List<PointOnBezierCurve> expected, List<PointOnBezierCurve> actual)
        {
            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.True(expected[i].position.X == actual[i].position.X && expected[i].position.Y == actual[i].position.Y && expected[i].position.Z == actual[i].position.Z,
                    "точка " + i + " стоїть інакше");
                Assert.True(expected[i].normal.X == actual[i].normal.X && expected[i].normal.Y == actual[i].normal.Y && expected[i].normal.Z == actual[i].normal.Z,
                    "у точці " + i + " інший нахил полотна");
                Assert.True(expected[i].tangent.X == actual[i].tangent.X && expected[i].tangent.Y == actual[i].tangent.Y && expected[i].tangent.Z == actual[i].tangent.Z,
                    "у точці " + i + " інший напрямок");
            }
        }

        [Fact]
        public void RouteIsTheSameWithAndWithoutTheCache()
        {
            LayWorld();
            var cache = new RouteCache();

            // Остання точка їде, як під прицілом гравця; пам'ять лишається від попередніх положень
            foreach (int[] move in new[] { new[] { 0, 0 }, new[] { 1, 0 }, new[] { 1, 1 }, new[] { 4, -6 }, new[] { 0, 0 }, new[] { -9, 12 } })
            {
                List<RoutePoint> route = Zigzag(10, move[0], move[1]);

                var plainAt = new List<int>();
                List<PointOnBezierCurve> plain = RouteCurve.BuildPoints(route, Resolve, out string plainConflict, SectionsIn, plainAt);
                var cachedAt = new List<int>();
                List<PointOnBezierCurve> cached = RouteCurve.BuildPoints(route, Resolve, out string cachedConflict, SectionsIn, cachedAt, cache);

                AssertSame(plain, cached);
                Assert.Equal(plainConflict, cachedConflict);
                Assert.Equal(plainAt, cachedAt);
            }
        }

        [Fact]
        public void WorldInTheTestReallyTriggersTheRules()
        {
            // Інакше попередній тест порівнював би два порожні результати
            LayWorld();
            var at = new List<int>();
            RouteCurve.BuildPoints(Zigzag(10, 0, 0), Resolve, out string conflict, SectionsIn, at);
            Assert.NotNull(conflict);
            Assert.NotEmpty(at);
        }

        [Fact]
        public void MovingTheLastPointRecomputesOnlyTheTail()
        {
            LayWorld();
            var cache = new RouteCache();

            RouteCurve.BuildPoints(Zigzag(10, 0, 0), Resolve, out _, SectionsIn, new List<int>(), cache);
            int first = cache.Misses;
            Assert.True(first > 500, "маршрут надто короткий для цього тесту: " + first);

            RouteCurve.BuildPoints(Zigzag(10, 1, 0), Resolve, out _, SectionsIn, new List<int>(), cache);
            int moved = cache.Misses - first;
            // Маршрут із десяти ділянок; рух останньої точки зачіпає дві-три останні
            Assert.True(moved < first / 3, "після руху останньої точки наново пораховано " + moved + " з " + first);

            // Той самий маршрут удруге: рахувати нема чого
            RouteCurve.BuildPoints(Zigzag(10, 1, 0), Resolve, out _, SectionsIn, new List<int>(), cache);
            Assert.Equal(first + moved, cache.Misses);
        }

        [Fact]
        public void LongSessionNeverRecomputesTheWholeRoute()
        {
            LayWorld();
            var cache = new RouteCache();

            RouteCurve.BuildPoints(Zigzag(10, 0, 0), Resolve, out _, SectionsIn, new List<int>(), cache);
            int whole = cache.Misses;

            // Гравець довго водить останню точку. Пам'ять за цей час кілька разів переповнюється і чиститься,
            // але маршрут, що на екрані, в ній лишається: жоден рух не коштує як перший розрахунок
            int worst = 0;
            for (int i = 1; i <= 400; i++)
            {
                int before = cache.Misses;
                RouteCurve.BuildPoints(Zigzag(10, i % 23 - 11, i % 17 - 8), Resolve, out _, SectionsIn, new List<int>(), cache);
                worst = Math.Max(worst, cache.Misses - before);
            }
            Assert.True(worst < whole / 2, "один із рухів перерахував " + worst + " при " + whole + " на весь маршрут");
        }

        [Fact]
        public void ClearedCacheForgetsTheTrackItSaw()
        {
            var cache = new RouteCache();
            List<RoutePoint> route = Zigzag(4, 0, 0);

            // Спершу колії немає: маршрут чистий
            RouteCurve.BuildPoints(route, Resolve, out string before, SectionsIn, new List<int>(), cache);
            Assert.Null(before);

            // Поклали колію, яка перетинає першу ділянку маршруту майже вздовж, гостріше за дозволений кут.
            // Хто тримає пам'ять, має її очистити, дізнавшись про зміну
            Vec3d along = new Vec3d(30, 0, 2).Normalize();
            Lay(new RoutePoint { Position = new Vec3d(100, 0.125, 203), Tangent = along },
                new RoutePoint { Position = new Vec3d(130, 0.125, 205), Tangent = along });
            cache.Clear();

            RouteCurve.BuildPoints(route, Resolve, out string after, SectionsIn, new List<int>(), cache);
            Assert.NotNull(after);
        }
    }
}
