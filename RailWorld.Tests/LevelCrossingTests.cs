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

        private string ConflictOf(double z)
        {
            // Маршрут на схід уздовж лінії з таким z
            var route = new List<RoutePoint> { At(0, 0, z), At(20, 0, z) };
            RouteCurve.BuildPoints(route, Resolve, out string conflict, SectionsIn);
            return conflict;
        }

        [Fact]
        public void RouteThroughAnExistingCrossingIsRefused()
        {
            // Дві колії вже перетинаються в точці (10, 0): одна з півночі на південь, друга навскіс
            Lay(Point(10, -10, 0, 1), Point(10, 10, 0, 1));
            Lay(Point(2, -8, 1, 1), Point(18, 8, 1, 1));

            // Третя крізь те саме місце
            Assert.NotNull(ConflictOf(0));
        }

        [Fact]
        public void CrossingNextToAnotherCrossingOnTheSameTrackIsRefused()
        {
            // Коротка коса колія перетинає довгу в точці (10, 0) і до маршруту не дотягується
            Lay(Point(10, -10, 0, 1), Point(10, 10, 0, 1));
            Lay(Point(8.5, -1.5, 1, 1), Point(11.5, 1.5, 1, 1));

            // Вузол це місце, де перекриваються габарити колій. Прямий перетин займає 0.975 блока в кожен бік,
            // косий під 45 градусів 2.35, разом 3.33: ближче вузли наклалися б
            Assert.NotNull(ConflictOf(2.5));
            // Трохи далі за цю суму вже можна
            Assert.Null(ConflictOf(3.5));
            // За шість блоків місця досить
            Assert.Null(ConflictOf(6));
        }

        // Маршрут навскіс під 45 градусів через точку (10, z) і маршрут уздовж осі Z через x = 10
        private static List<RoutePoint> Diagonal(double z)
        {
            double k = Math.Sqrt(0.5);
            return new List<RoutePoint>
            {
                new RoutePoint { Position = new Vec3d(10 - 8 * k, 0, z - 8 * k), Tangent = new Vec3d(k, 0, k) },
                new RoutePoint { Position = new Vec3d(10 + 8 * k, 0, z + 8 * k), Tangent = new Vec3d(k, 0, k) }
            };
        }

        private string ConflictOf(List<RoutePoint> route)
        {
            RouteCurve.BuildPoints(route, Resolve, out string conflict, SectionsIn);
            return conflict;
        }

        [Theory]
        // Вузол під 45 градусів тягнеться на 2.35 блока в кожен бік, два таких поруч це 4.71
        [InlineData(4.5, true)]
        [InlineData(5.0, false)]
        public void SpacingBetweenCrossingsIsTheSameInEitherBuildOrder(double apart, bool refused)
        {
            double k = Math.Sqrt(0.5);

            // Порядок перший: одна колія вже перетнута косою, поруч кладемо другу косу
            Lay(Point(10, -14, 0, 1), Point(10, 18, 0, 1));
            Lay(Point(10 - 8 * k, -8 * k, 1, 1), Point(10 + 8 * k, 8 * k, 1, 1));
            Assert.Equal(refused, ConflictOf(Diagonal(apart)) != null);
        }

        [Theory]
        // Вузол під 45 градусів тягнеться на 2.35 блока в кожен бік, два таких поруч це 4.71
        [InlineData(4.5, true)]
        [InlineData(5.0, false)]
        public void SpacingBetweenCrossingsIsTheSameInTheOtherBuildOrder(double apart, bool refused)
        {
            double k = Math.Sqrt(0.5);

            // Порядок другий: дві косі вже лежать на тій самій відстані одна від одної, кладемо колію через обидві
            Lay(Point(10 - 8 * k, -8 * k, 1, 1), Point(10 + 8 * k, 8 * k, 1, 1));
            Lay(Point(10 - 8 * k, apart - 8 * k, 1, 1), Point(10 + 8 * k, apart + 8 * k, 1, 1));
            var route = new List<RoutePoint>
            {
                new RoutePoint { Position = new Vec3d(10, 0, -14), Tangent = new Vec3d(0, 0, 1) },
                new RoutePoint { Position = new Vec3d(10, 0, 18), Tangent = new Vec3d(0, 0, 1) }
            };
            Assert.Equal(refused, ConflictOf(route) != null);
        }

        // Довга колія із заходу до стрілки в нулі координат: з неї виходять пряма на схід і дуга
        private Section LaySwitchAtOrigin()
        {
            List<Section> trunk = Lay(Point(-30, 0, 1, 0), Point(0, 0, 1, 0));
            Section last = trunk[trunk.Count - 1];
            Join(last, Straight()[0]);
            Join(last, Arc()[0]);
            return last;
        }

        // Маршрут навскіс під 20 градусів, що перетинає вісь X у точці x
        private static List<RoutePoint> Shallow(double x)
        {
            double dx = Math.Cos(Math.PI / 9), dz = -Math.Sin(Math.PI / 9);
            return new List<RoutePoint>
            {
                new RoutePoint { Position = new Vec3d(x - 8 * dx, 0, -8 * dz), Tangent = new Vec3d(dx, 0, dz) },
                new RoutePoint { Position = new Vec3d(x + 8 * dx, 0, 8 * dz), Tangent = new Vec3d(dx, 0, dz) }
            };
        }

        [Fact]
        public void CrossingWhoseNodeReachesASwitchZoneIsRefused()
        {
            LaySwitchAtOrigin();

            // Тіло стрілки починається на її стику, в нулі. Косий вузол під 20 градусів тягнеться на 5.53 блока
            // вздовж колії. Перетин за 4 блоки до стрілки накладається на тіло краєм вузла, хоча сама
            // перетнута секція до тіла не належить
            string problem = ConflictOf(Shallow(-4));
            Assert.NotNull(problem);
            Assert.Contains("перетин", problem);
            // За 6 блоків вузол до тіла не дістає. Запасу понад це немає: перед стиком звичайна колія
            Assert.Null(ConflictOf(Shallow(-6)));
        }

        [Fact]
        public void TrackOverASwitchLeverIsRefused()
        {
            Section trunk = LaySwitchAtOrigin();
            Vec3d lever = SectionPicker.LeverPosition(trunk, false);

            // Коротка ділянка вздовж колії стрілки, збоку від неї: її шпали накривають місце, де стоїть важіль
            double side = Math.Sign(lever.Z);
            List<Section> over = Plan(Point(lever.X - 3, lever.Z + side * 0.3, 1, 0), Point(lever.X + 3, lever.Z + side * 0.3, 1, 0));
            Assert.True(SectionLinker.FindLeverInTrack(over, SectionsIn) >= 0);

            // Та сама ділянка на три блоки далі вбік важеля не зачіпає
            List<Section> aside = Plan(Point(lever.X - 3, lever.Z + side * 3, 1, 0), Point(lever.X + 3, lever.Z + side * 3, 1, 0));
            Assert.Equal(-1, SectionLinker.FindLeverInTrack(aside, SectionsIn));
        }

        // Колія з півночі на південь через точку з таким x, на заданій висоті
        private void LayAcross(double x, double y)
        {
            Lay(new RoutePoint { Position = new Vec3d(x, y, -10), Tangent = new Vec3d(0, 0, 1) },
                new RoutePoint { Position = new Vec3d(x, y, 10), Tangent = new Vec3d(0, 0, 1) });
        }

        [Fact]
        public void TwoCrossingsOnDifferentHeightsNeedRoomBetweenThem()
        {
            // Дві колії на різній висоті, 0 і 0.4. Маршрут мусить лягти в площину кожної, а на перехід між ними
            // по висоті на 0.4 блока треба 2.9 блока. Між вузлами, що стоять за 3.5 блока, лишається 0.75
            LayAcross(10, 0);
            LayAcross(13.5, 0.4);

            string problem = ConflictOf(new List<RoutePoint> { At(0, 0, 0), At(40, 0, 0) });
            Assert.NotNull(problem);
            Assert.Contains("перехід по висоті", problem);
        }

        [Fact]
        public void TwoCrossingsOnDifferentHeightsFarApartAreFine()
        {
            LayAcross(10, 0);
            LayAcross(22, 0.4);

            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(new List<RoutePoint> { At(0, 0, 0), At(40, 0, 0) }, Resolve, out string conflict, SectionsIn);
            Assert.Null(conflict);
            // І сам маршрут вийшов без зламів: звичайний плавний перехід новим правилом не підсвічується
            Assert.Null(RouteRules.FindProblem(points));
        }

        [Fact]
        public void StepInHeightBetweenTwoLevelStretchesIsABreak()
        {
            // Рівний маршрут, у який уручну вставлено сходинку: за півтора блока вгору на 0.3 і далі рівно.
            // Дотичні лишилися горизонтальними, як після вирівнювання по чужій колії
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(new List<RoutePoint> { At(0, 0, 0), At(20, 0, 0) });
            Assert.Null(RouteRules.FindProblem(points));

            for (int i = 0; i < points.Count; i++)
            {
                PointOnBezierCurve point = points[i];
                double s = GameMath.Clamp((point.position.X - 8) / 1.5, 0, 1);
                point.position = new Vec3d(point.position.X, 0.3 * s * s * (3 - 2 * s), point.position.Z);
                points[i] = point;
            }

            string problem = RouteRules.FindProblem(points);
            Assert.NotNull(problem);
            Assert.Contains("по висоті", problem);
        }

        [Fact]
        public void SidestepBetweenTwoStraightStretchesIsATooSharpTurn()
        {
            // Прямий маршрут, у який уручну вставлено зигзаг: за півтора блока вбік на 0.3 і далі прямо.
            // Напрямок до і після той самий, тож на відрізку у два блоки поворот у сумі нульовий
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(new List<RoutePoint> { At(0, 0, 0), At(20, 0, 0) });
            for (int i = 0; i < points.Count; i++)
            {
                PointOnBezierCurve point = points[i];
                double s = GameMath.Clamp((point.position.X - 8) / 1.5, 0, 1);
                point.position = new Vec3d(point.position.X, 0, 0.3 * s * s * (3 - 2 * s));
                points[i] = point;
            }

            string problem = RouteRules.FindProblem(points);
            Assert.NotNull(problem);
            Assert.Contains("поворот", problem);
        }

        [Theory]
        [InlineData(1.0, 90)]
        // Півколо однією кривою місцями на 7% крутіше за свій радіус, тому маршрут скруглює кути
        // з запасом 1.05 (RouteBuilder.CornerRadiusFactor): із ним проходить і півколо
        [InlineData(1.05, 180)]
        [InlineData(1.0, 30)]
        [InlineData(2.0, 90)]
        public void TurnOnTheSmallestRadiusIsStillAllowed(double radiusFactor, double degrees)
        {
            // Дуга рівно на найменшому радіусі: суворіша перевірка не має відхиляти те, що правилом дозволено
            double radius = RouteRules.MinRadius * radiusFactor, angle = degrees * Math.PI / 180;
            var route = new List<RoutePoint>
            {
                new RoutePoint { Position = new Vec3d(-5, 0, 0), Tangent = new Vec3d(1, 0, 0) },
                new RoutePoint { Position = new Vec3d(0, 0, 0), Tangent = new Vec3d(1, 0, 0) },
                new RoutePoint
                {
                    Position = new Vec3d(radius * Math.Sin(angle), 0, radius * (1 - Math.Cos(angle))),
                    Tangent = new Vec3d(Math.Cos(angle), 0, Math.Sin(angle))
                }
            };

            Assert.Null(RouteRules.FindProblem(RouteCurve.BuildPoints(route)));
        }

        // Рівний маршрут на схід із заданим нахилом полотна в кожній точці: tilt це синус кута нахилу
        private static List<PointOnBezierCurve> Tilted(Func<double, double> tilt)
        {
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(new List<RoutePoint> { At(0, 0, 0), At(20, 0, 0) });
            for (int i = 0; i < points.Count; i++)
            {
                PointOnBezierCurve point = points[i];
                double sin = tilt(point.position.X);
                point.normal = new Vec3f(0, (float)sin, (float)Math.Sqrt(1 - sin * sin));
                points[i] = point;
            }
            return points;
        }

        [Fact]
        public void SuddenChangeOfCantBreaksTheRailsThoughTheAxisIsLevel()
        {
            // Вісь колії рівна, але посередині полотно різко перекошується. По осі зламу немає,
            // а рейки розходяться: одна стрибає вгору, друга вниз
            string problem = RouteRules.FindProblem(Tilted(x => x < 10 ? 0 : 0.3));
            Assert.NotNull(problem);
            Assert.Contains("по висоті", problem);

            // Той самий перекіс, набраний поступово на десяти блоках, рейок не ламає
            Assert.Null(RouteRules.FindProblem(Tilted(x => 0.3 * GameMath.Clamp((x - 5) / 10, 0, 1))));
        }

        [Fact]
        public void CrossingADoubleTrackIsAllowed()
        {
            // Дві паралельні колії за два блоки одна від одної, маршрут упоперек обох
            Lay(Point(10, -10, 0, 1), Point(10, 10, 0, 1));
            Lay(Point(12, -10, 0, 1), Point(12, 10, 0, 1));

            Assert.Null(ConflictOf(0));
        }

        [Fact]
        public void RouteStaysInThePlaneOfTheTrackWhileTheirSleepersOverlap()
        {
            // Колія з півночі на південь на рівні нуля. Маршрут перетинає її навскіс, під 30 градусів,
            // і одразу піднімається
            Lay(Point(20, -20, 0, 1), Point(20, 20, 0, 1));
            double dx = Math.Sin(Math.PI / 6), dz = Math.Cos(Math.PI / 6);
            var route = new List<RoutePoint>
            {
                new RoutePoint { Position = new Vec3d(20 - 15 * dx, 0, -15 * dz), Tangent = new Vec3d(dx, 0, dz) },
                new RoutePoint { Position = new Vec3d(20 + 25 * dx, 5, 25 * dz), Tangent = new Vec3d(dx, 0, dz) }
            };

            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(route, Resolve, out string conflict, SectionsIn);
            Assert.Null(conflict);

            // Дві смуги завширшки з габарит колії, 1.95 блока, під кутом 30 градусів перекриваються на 3.64 блока
            // вздовж маршруту в кожен бік від перетину осей. Доти маршрут мусить лежати рівно в площині колії.
            // Якби вузол рахувався по рейках, він кінчався б уже за 1.96 блока, і шпала маршруту
            // піднімалася б, ще лежачи під рейкою колії
            double overlap = Footprint.StandardWidth / 2 * (1 / Math.Sin(Math.PI / 6) + 1 / Math.Tan(Math.PI / 6));
            Assert.Equal(3.64, overlap, 2);

            int checkedPoints = 0;
            foreach (PointOnBezierCurve point in points)
            {
                double fromCrossing = Math.Sqrt((point.position.X - 20) * (point.position.X - 20) + point.position.Z * point.position.Z);
                if (fromCrossing > overlap - 0.05) continue;

                checkedPoints++;
                Assert.True(Math.Abs(point.position.Y) < 1e-6, "маршрут уже не в площині колії за " + fromCrossing.ToString("0.00") + " блока від перетину");
            }
            Assert.True(checkedPoints > 10);
        }

        // Точка на схилі, що піднімається на схід: 0.4 блока на блок, це близько 22 градусів
        private static RoutePoint Uphill(double x)
        {
            return new RoutePoint { Position = new Vec3d(x, x * 0.4, 0), Tangent = new Vec3d(1, 0.4, 0).Normalize() };
        }

        [Fact]
        public void SwitchOnASlopeKeepsItsSleepersInThePlaneOfTheTrack()
        {
            // Колія йде вгору схилом. Зі стику посередині відходить маршрут: повертає на 60 градусів
            // і лишається на висоті стику, тобто відносно колії йде вниз
            List<Section> below = Lay(Uphill(0), Uphill(20));
            List<Section> above = Lay(Uphill(20), Uphill(50));
            Join(below[below.Count - 1], above[0]);

            double angle = Math.PI / 3, radius = 12.5;
            RoutePoint from = Uphill(20);
            from.Track = above[0];
            from.TrackJointAtStart = true;
            from.Cant = 0;
            var route = new List<RoutePoint>
            {
                from,
                new RoutePoint
                {
                    Position = new Vec3d(20 + radius * Math.Sin(angle), 8, radius * (1 - Math.Cos(angle))),
                    Tangent = new Vec3d(Math.Cos(angle), 0, Math.Sin(angle))
                },
                new RoutePoint
                {
                    Position = new Vec3d(20 + radius * Math.Sin(angle) + 30 * Math.Cos(angle), 8, radius * (1 - Math.Cos(angle)) + 30 * Math.Sin(angle)),
                    Tangent = new Vec3d(Math.Cos(angle), 0, Math.Sin(angle))
                }
            };

            RouteCurve.BuildPoints(route, Resolve, out string conflict, SectionsIn);
            Assert.Null(conflict);
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
