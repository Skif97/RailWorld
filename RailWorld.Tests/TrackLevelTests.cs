using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Чи дві колії на одному рівні. Усі правила питають це однаково: висоти обох колій в одній точці плану.
    /// </summary>
    public class TrackLevelTests : TrackTestBase
    {
        // Колія на крутому схилі вздовж x, ухил 1:2; на x = 10 вона на висоті 5
        private const double Rise = 0.5;

        private List<Section> LaySlope()
        {
            Vec3d along = new Vec3d(1, Rise, 0).Normalize();
            return Lay(new RoutePoint { Position = new Vec3d(0, 0, 0), Tangent = along },
                new RoutePoint { Position = new Vec3d(20, 20 * Rise, 0), Tangent = along });
        }

        // Рівна колія впоперек над точкою x = 10 на висоті height. Зсунута вздовж себе на чверть секції,
        // щоб вісь колії на схилі перетинала її посеред секції, а не на стику: секції тут не зв'язані
        private static List<Section> PlanAcross(double height)
        {
            Vec3d along = new Vec3d(0, 0, 1);
            return Plan(new RoutePoint { Position = new Vec3d(10, height, -6.125), Tangent = along },
                new RoutePoint { Position = new Vec3d(10, height, 5.875), Tangent = along });
        }

        [Fact]
        public void BridgeOverTrackOnASlopeIsNotOnItsLevel()
        {
            List<Section> slope = LaySlope();
            // Міст на чотири блоки над колією: просвіт є по всій ширині габариту
            List<Section> bridge = PlanAcross(10 * Rise + 4);

            // Середини деяких секцій, які правила порівнюють, стоять ближче за просвіт: на схилі
            // різниця висот середин це не різниця рівнів
            bool closeMiddles = false;
            foreach (Section upper in bridge)
            {
                foreach (Section lower in SectionsIn.Around(upper.GetGlobalPos(), TrackSurvey.Reach, RouteCurve.CrossingClearance + 4))
                {
                    if (upper.GetGlobalPos().Y - lower.GetGlobalPos().Y < RouteCurve.CrossingClearance) closeMiddles = true;
                    Assert.False(TrackLevel.Same(upper, lower));
                }

                SectionFacts facts = TrackSurvey.Examine(upper, SectionsIn);
                Assert.False(facts.LiesOnTrack);
                Assert.False(facts.Crossed);
                Assert.False(facts.TooClose);
            }
            Assert.True(closeMiddles);
        }

        [Fact]
        public void TrackCrossingInTheSamePlaneIsOnItsLevel()
        {
            List<Section> slope = LaySlope();
            // Друга колія впоперек, що проходить через колію на схилі там, де та на висоті 5
            List<Section> across = PlanAcross(10 * Rise);

            bool crossed = false;
            foreach (Section section in across)
            {
                foreach (Section other in slope)
                {
                    if (!AxisCrossing.OfLaid(section, other)) continue;
                    crossed = true;
                    Assert.True(TrackLevel.Same(section, other));
                }
            }
            Assert.True(crossed);
        }

        [Fact]
        public void LevelsSplitHalfwayToTheClearance()
        {
            Assert.True(TrackLevel.Same(5, 5 + RouteCurve.CrossingClearance / 2 - 0.01));
            Assert.False(TrackLevel.Same(5, 5 + RouteCurve.CrossingClearance / 2));
            Assert.False(TrackLevel.Same(5, 5 - RouteCurve.CrossingClearance));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(30)]
        [InlineData(45)]
        [InlineData(70)]
        public void TrackUnderABridgeThreeBlocksUpIsAllowedAtAnyAngle(double angleDeg)
        {
            // Міст на висоті трьох блоків, колія під ним на землі під кутом: на рівній землі зазор цілий,
            // і три блоки це найменший міст
            Lay(new RoutePoint { Position = new Vec3d(0, 3, 0), Tangent = new Vec3d(1, 0, 0) },
                new RoutePoint { Position = new Vec3d(30, 3, 0), Tangent = new Vec3d(1, 0, 0) });
            double a = angleDeg * Math.PI / 180;
            var along = new Vec3d(Math.Cos(a), 0, Math.Sin(a));
            List<Section> under = Plan(new RoutePoint { Position = new Vec3d(15 - 8 * along.X, 0, -8 * along.Z), Tangent = along },
                new RoutePoint { Position = new Vec3d(15 + 8 * along.X, 0, 8 * along.Z), Tangent = along });

            Assert.Empty(SectionLinker.FindTooCloseAbove(under, SectionsIn));
        }
    }
}
