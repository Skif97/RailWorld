using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// RailLine: де лінія однієї рейки проходить крізь площину перерізу другої. Лінії в тестах прямі,
    /// на рівній землі: тоді відповідь відома точно.
    /// </summary>
    public class RailLineTests
    {
        // Лінія рейки з точки (0, 0) під кутом angle до осі X, перерізи через step блоків, перший за start
        private static List<RailFrame> Line(double angle, double start, double step, int count)
        {
            var along = new Vec3d(Math.Cos(angle), 0, Math.Sin(angle));
            var side = new Vec3d(-Math.Sin(angle), 0, Math.Cos(angle));
            var frames = new List<RailFrame>();
            for (int i = 0; i < count; i++)
            {
                double s = start + i * step;
                frames.Add(new RailFrame { Center = new Vec3d(along.X * s, 0, along.Z * s), Side = side.Clone(), Up = new Vec3d(0, 1, 0), Along = along.Clone() });
            }
            return frames;
        }

        // Переріз упоперек осі X у точці x
        private static RailFrame Station(double x)
        {
            return new RailFrame { Center = new Vec3d(x, 0, 0), Side = new Vec3d(0, 0, 1), Up = new Vec3d(0, 1, 0), Along = new Vec3d(1, 0, 0) };
        }

        [Fact]
        public void CrossFindsThePointBetweenTwoFrames()
        {
            double angle = 0.2;
            RailFrame hit = RailLine.Cross(Line(angle, 0, 0.25, 20), 0, Station(1.3), 0);

            Assert.NotNull(hit);
            Assert.Equal(1.3, hit.Center.X, 6);
            Assert.Equal(1.3 * Math.Tan(angle), hit.Center.Z, 6);
        }

        [Fact]
        public void CrossDoesNotDependOnHowTheLineIsCut()
        {
            double angle = 0.2;
            RailFrame station = Station(1.3);

            // Та сама рейка, порізана зі зсувом на частину секції і з іншим кроком
            RailFrame even = RailLine.Cross(Line(angle, 0, 0.25, 20), 0, station, 5);
            RailFrame shifted = RailLine.Cross(Line(angle, -0.11, 0.25, 20), 0, station, 5);
            RailFrame coarse = RailLine.Cross(Line(angle, -0.07, 0.4, 12), 0, station, 5);

            Assert.Equal(even.Center.Z, shifted.Center.Z, 6);
            Assert.Equal(even.Center.Z, coarse.Center.Z, 6);
        }

        [Fact]
        public void CrossWithOffsetFollowsTheOtherRailOfTheTrack()
        {
            double angle = 0.2, width = 0.78;
            RailFrame hit = RailLine.Cross(Line(angle, 0, 0.25, 20), -width, Station(1.3), 0);

            // Друга рейка тієї ж колії: пряма, паралельна першій, за ширину колії від неї
            double expected = 1.3 * Math.Tan(angle) - width / Math.Cos(angle);
            Assert.Equal(1.3, hit.Center.X, 6);
            Assert.Equal(expected, hit.Center.Z, 6);
        }

        [Fact]
        public void CrossExtendsTheLineALittleBeyondItsEnd()
        {
            List<RailFrame> line = Line(0.2, 0.3, 0.25, 4);

            // Трохи раніше за перший переріз лінії: продовжуємо прямою
            Assert.Equal(0.1 * Math.Tan(0.2), RailLine.Cross(line, 0, Station(0.1), 0).Center.Z, 6);
            // Далеко за останнім: лінія туди не доходить
            Assert.Null(RailLine.Cross(line, 0, Station(5), 3));
        }

        // Рейка дугою з точки (0, 0): спершу вздовж осі X, далі завертає в бік +Z на три чверті кола
        // і повертається назад повз свій початок. Так іде крута гілка, що завертає більш як на 90 градусів
        private static List<RailFrame> SharpCurve(double radius, int count)
        {
            var frames = new List<RailFrame>();
            for (int i = 0; i < count; i++)
            {
                double turn = Math.PI * 1.5 * i / (count - 1);
                frames.Add(new RailFrame
                {
                    Center = new Vec3d(radius * Math.Sin(turn), 0, radius * (1 - Math.Cos(turn))),
                    Side = new Vec3d(-Math.Sin(turn), 0, Math.Cos(turn)),
                    Up = new Vec3d(0, 1, 0),
                    Along = new Vec3d(Math.Cos(turn), 0, Math.Sin(turn))
                });
            }
            return frames;
        }

        [Fact]
        public void CrossTakesTheNearestOfTwoCrossingsOfASharpCurve()
        {
            // Дуга перетинає площину x = 1 двічі: на початку і на зворотному шляху, за поворотом
            List<RailFrame> curve = SharpCurve(3, 40);

            Assert.True(RailLine.Cross(curve, 0, Station(1), 0).Center.Z < 1);
            Assert.True(RailLine.Cross(curve, 0, Station(1), 39).Center.Z > 5);
        }

        [Fact]
        public void CrossAtTheVeryStartOfASharpCurveStaysAtTheStart()
        {
            List<RailFrame> curve = SharpCurve(3, 40);

            // Перший переріз лежить на площині, але через похибку обчислень опиняється на волосину попереду.
            // Рейка перетинає ту саму площину ще раз на зворотному шляху: брати треба початок
            RailFrame station = Station(0);
            station.Center.X = -1e-15;
            RailFrame hit = RailLine.Cross(curve, 0, station, 0);
            Assert.Equal(0, hit.Center.Z, 6);

            // Площина трохи раніше за початок рейки: продовжуємо початок, а не беремо далекий перетин
            hit = RailLine.Cross(curve, 0, Station(-0.1), 0);
            Assert.Equal(-0.1, hit.Center.X, 6);
            Assert.Equal(0, hit.Center.Z, 6);
        }

        [Fact]
        public void StockRailLiesOnTheBladeLineAtTheSwitchAndMovesAway()
        {
            // Гостряк іде вздовж осі X, сусідня колія відходить від нього під кутом у бік +Z. Рамна рейка це
            // зовнішня рейка сусідньої колії: на самій стрілці вона лежить на лінії гостряка, далі відходить
            double angle = 0.1, width = 0.78;
            List<RailFrame> otherInner = Line(angle, 0, 0.25, 30);
            foreach (RailFrame frame in otherInner)
            {
                // З лінії зовнішньої рейки робимо внутрішню: вона за ширину колії в бік нашої колії,
                // і її Side дивиться туди ж
                frame.Side.Mul(-1);
                frame.Center.Add(frame.Side.X * width, 0, frame.Side.Z * width);
            }

            Assert.True(SwitchGeometry.StockOffset(Station(0), otherInner, width, 0, out double atSwitch));
            Assert.Equal(0, atSwitch, 6);

            Assert.True(SwitchGeometry.StockOffset(Station(2), otherInner, width, 8, out double further));
            Assert.Equal(2 * Math.Tan(angle), further, 6);
        }
    }
}
