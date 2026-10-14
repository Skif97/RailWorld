using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// SwitchGeometry: де сходяться внутрішні рейки двох гілок стрілки і скільки секцій займає гостряк.
    /// Стрілка в тестах це дві колії з однієї точки: пряма на схід і дуга, що відходить від неї на південь.
    /// </summary>
    public class SwitchGeometryTests : TrackTestBase
    {
        // Геометрія гілки mine стрілки, обидві гілки якої виходять зі стику своїм початком. withPartner: чи
        // друга гілка відома
        private BranchGeometry Branch(List<Section> mine, List<Section> partner, bool withPartner)
        {
            SwitchSides.Of(mine[0], true, partner[0], true, Resolve, out List<bool> mineSides, out List<bool> partnerSides);
            return SwitchGeometry.Branch(mine[0], true, mineSides[0], partner[0], withPartner ? true : (bool?)null, partnerSides[0], Resolve);
        }

        [Fact]
        public void InnerRailRunsOnTheSideOfTheOtherBranch()
        {
            List<Section> straight = Straight(), arc = Arc();

            BranchGeometry geometry = Branch(straight, arc, true);

            // Дуга відходить у бік +Z, тож внутрішня рейка прямої лежить за пів колії в той самий бік
            RailFrame first = geometry.Frames[0];
            Assert.Equal(TrackGauge.StandardWidth / 2, first.Center.Z, 3);
            Assert.Equal(1, first.Side.Z, 3);
            Assert.Equal(1, first.Along.X, 3);
        }

        [Fact]
        public void FrogSitsWhereTheInnerRailsCross()
        {
            List<Section> straight = Straight(), arc = Arc();

            BranchGeometry geometry = Branch(straight, arc, true);

            Assert.True(geometry.Throat >= 0, "хрестовину не знайдено");
            Assert.True(geometry.Throat < geometry.Tip, "осердя має починатися після горла");
            Assert.True(geometry.Tip <= geometry.Crossing);
            Assert.True(geometry.Crossing <= geometry.Full);
            Assert.True(geometry.HideFrom <= geometry.HideTo);

            // Рейки перетинаються там, де дуга відійшла від прямої на ширину колії:
            // R * (1 - cos a) = ширина, уздовж прямої це R * sin a. Перерізи стоять через чверть блока
            double angle = Math.Acos(1 - TrackGauge.StandardWidth / ArcRadius);
            double expected = ArcRadius * Math.Sin(angle) / 0.25;
            Assert.InRange(geometry.Crossing, expected - 2, expected + 2);
        }

        [Fact]
        public void BothBranchesAgreeOnTheFrog()
        {
            List<Section> straight = Straight(), arc = Arc();

            BranchGeometry fromStraight = Branch(straight, arc, true);
            BranchGeometry fromArc = Branch(arc, straight, true);

            Assert.Equal(fromStraight.Throat, fromArc.Throat);
            Assert.Equal(fromStraight.Tip, fromArc.Tip);
            Assert.Equal(fromStraight.Crossing, fromArc.Crossing);
            Assert.Equal(fromStraight.Full, fromArc.Full);
            Assert.Equal(fromStraight.HideFrom, fromArc.HideFrom);
            Assert.Equal(fromStraight.HideTo, fromArc.HideTo);
        }

        [Fact]
        public void CommonStationIsTheSameFromEitherRail()
        {
            List<Section> straight = Straight(), arc = Arc();
            BranchGeometry geometry = Branch(straight, arc, true);

            for (int q = 0; q < geometry.Across.Length; q += 5)
            {
                RailFrame mine = SwitchGeometry.CommonStation(geometry.Frames[q], geometry.Other[q]);
                RailFrame theirs = SwitchGeometry.CommonStation(geometry.Other[q], geometry.Frames[q]);

                // Та сама площина, бік рівно протилежний
                Assert.Equal(mine.Center.X, theirs.Center.X);
                Assert.Equal(mine.Center.Z, theirs.Center.Z);
                Assert.Equal(mine.Along.X, theirs.Along.X);
                Assert.Equal(mine.Along.Z, theirs.Along.Z);
                Assert.Equal(mine.Side.X, -theirs.Side.X);
                Assert.Equal(mine.Side.Z, -theirs.Side.Z);
            }
        }

        [Fact]
        public void WithoutTheOtherBranchThereIsNoFrog()
        {
            List<Section> straight = Straight(), arc = Arc();

            BranchGeometry geometry = Branch(straight, arc, false);

            Assert.NotEmpty(geometry.Frames);
            Assert.Null(geometry.Other);
            Assert.Equal(-1, geometry.Throat);
        }

        [Fact]
        public void BladeLengthIsLimitedAndStopsAtTheFrog()
        {
            // Корінь зовсім близько: гостряк не коротший за найменший. Дуже далеко: не довший за найбільший
            Assert.Equal(SwitchGeometry.MinBladeSections, SwitchGeometry.BladeSections(0.1));
            Assert.Equal(SwitchGeometry.MaxBladeSections, SwitchGeometry.BladeSections(100));

            // Хрестовина починається з третьої секції: гостряк закінчується перед нею
            var steep = new BranchGeometry { Throat = 3, HideFrom = 3 };
            Assert.Equal(2, SwitchGeometry.BladeSections(100, steep));
        }
    }
}
