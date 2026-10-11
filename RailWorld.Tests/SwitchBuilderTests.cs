using System.Collections.Generic;
using RailWorld.src.RailWay;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// SwitchBuilder: запис про стрілку, який зберігається в чанку. Стрілка в тестах це спільна колія,
    /// з кінця якої виходять пряма гілка (основна) і дуга.
    /// </summary>
    public class SwitchBuilderTests : TrackTestBase
    {
        private List<Section> trunk, straight, arc;

        private RailSwitch BuildSwitch()
        {
            trunk = Trunk();
            straight = Straight();
            arc = Arc();

            Section last = trunk[trunk.Count - 1];
            Join(last, straight[0]);
            Join(last, arc[0]);
            return SwitchBuilder.Build(last, false, Resolve);
        }

        [Fact]
        public void PlainTrackEndIsNotASwitch()
        {
            trunk = Trunk();
            straight = Straight();
            Join(trunk[trunk.Count - 1], straight[0]);

            Assert.Null(SwitchBuilder.Build(trunk[trunk.Count - 1], false, Resolve));
        }

        [Fact]
        public void BranchesKeepTheOrderOfTheLinks()
        {
            RailSwitch built = BuildSwitch();

            Assert.Equal(2, built.Branches.Count);
            Assert.True(built.Branches[0].Members[0].PointsTo(straight[0]));
            Assert.True(built.Branches[1].Members[0].PointsTo(arc[0]));
            // Обидві гілки приєднані до стрілки своїм початком
            Assert.True(built.Branches[0].Members[0].AtStart);
            Assert.True(built.Branches[1].Members[0].AtStart);
        }

        [Fact]
        public void MembersFollowEachBranchInOrder()
        {
            RailSwitch built = BuildSwitch();

            for (int k = 0; k < 2; k++)
            {
                List<Section> track = k == 0 ? straight : arc;
                SwitchBranch branch = built.Branches[k];

                // Зона стрілки і ще кілька секцій за нею, закритих для нових стрілок
                Assert.Equal(branch.ZoneSections + SwitchZone.ClearSections, branch.Members.Count);
                for (int n = 0; n < branch.Members.Count; n++) Assert.True(branch.Members[n].PointsTo(track[n]));
            }
        }

        [Fact]
        public void BothBranchesAgreeOnBladeAndFrog()
        {
            RailSwitch built = BuildSwitch();
            SwitchBranch first = built.Branches[0], second = built.Branches[1];

            Assert.True(first.BladeSections > 0);
            Assert.True(first.FrogFrom > first.BladeSections, "хрестовина починається після гостряка");
            Assert.True(first.FrogFrom <= first.FrogTo);

            Assert.Equal(first.BladeSections, second.BladeSections);
            Assert.Equal(first.FrogFrom, second.FrogFrom);
            Assert.Equal(first.FrogTo, second.FrogTo);
            Assert.Equal(first.PairSections, second.PairSections);
        }

        [Fact]
        public void OppositeIsTheSectionWithTheSameNumberOnTheOtherBranch()
        {
            RailSwitch built = BuildSwitch();

            var onStraight = new SwitchRef { Branch = 0, Number = 3 };
            var onArc = new SwitchRef { Branch = 1, Number = 3 };
            Assert.True(built.Opposite(onStraight).PointsTo(arc[2]));
            Assert.True(built.Opposite(onArc).PointsTo(straight[2]));

            // Далі, ніж записана друга гілка, сусідки немає
            Assert.Null(built.Opposite(new SwitchRef { Branch = 0, Number = built.Branches[1].Members.Count + 1 }));
            // Посилання на гілку, якої в записі немає
            Assert.Null(built.Opposite(new SwitchRef { Branch = 2, Number = 1 }));
        }

        [Fact]
        public void InnerRailIsReplacedUnderTheBladeAndTheFrogOnly()
        {
            SwitchBranch branch = BuildSwitch().Branches[0];

            Assert.False(branch.ReplacesInnerRail(0));
            Assert.True(branch.ReplacesInnerRail(1));
            Assert.True(branch.ReplacesInnerRail(branch.BladeSections));
            // Між гостряком і хрестовиною звичайна рейка
            Assert.False(branch.ReplacesInnerRail(branch.BladeSections + 1));
            Assert.True(branch.ReplacesInnerRail(branch.FrogFrom));
            Assert.True(branch.ReplacesInnerRail(branch.FrogTo));
            Assert.False(branch.ReplacesInnerRail(branch.FrogTo + 1));
        }

        [Fact]
        public void BranchWithoutAFrogReplacesTheRailUnderTheBladeOnly()
        {
            var branch = new SwitchBranch { BladeSections = 4 };

            Assert.True(branch.ReplacesInnerRail(4));
            Assert.False(branch.ReplacesInnerRail(5));
        }

        [Fact]
        public void SleepersAreSharedWhileTheBranchesLieOnEachOther()
        {
            SwitchBranch branch = BuildSwitch().Branches[0];

            Assert.True(branch.PairSections > 0);
            Assert.True(branch.SharesSleeper(1));
            Assert.True(branch.SharesSleeper(branch.PairSections));
            Assert.False(branch.SharesSleeper(branch.PairSections + 1));
        }

        [Fact]
        public void FullyLaidSwitchIsComplete()
        {
            RailSwitch built = BuildSwitch();

            Assert.True(built.Resolved);
            Assert.True(built.Complete);
        }

        [Fact]
        public void MissingRailInsideTheZoneMakesItIncomplete()
        {
            BuildSwitch();
            Section last = trunk[trunk.Count - 1];

            // Рейку знято на другій секції дуги: це ще гостряк
            arc[1].RemovePart(SectionPart.FirstRail);

            Assert.False(SwitchBuilder.Build(last, false, Resolve).Complete);
        }

        [Fact]
        public void MissingRailBeyondTheZoneDoesNotMatter()
        {
            RailSwitch built = BuildSwitch();
            Section last = trunk[trunk.Count - 1];

            // За зоною стрілки це вже звичайна колія
            int beyond = built.Branches[0].ZoneSections + 1;
            straight[beyond].RemovePart(SectionPart.FirstRail);

            Assert.True(SwitchBuilder.Build(last, false, Resolve).Complete);
        }

        [Fact]
        public void UnloadedBranchSectionLeavesTheSwitchUnresolvedAndIncomplete()
        {
            BuildSwitch();
            Section last = trunk[trunk.Count - 1];

            // Чанк із третьою секцією дуги не завантажений
            Forget(arc[2]);
            RailSwitch built = SwitchBuilder.Build(last, false, Resolve);

            Assert.False(built.Resolved);
            Assert.False(built.Complete);
        }

        [Fact]
        public void RebuildKeepsAFullRecordWhileABranchChunkIsUnloaded()
        {
            RailSwitch full = BuildSwitch();
            Section last = trunk[trunk.Count - 1];

            Forget(arc[2]);
            RailSwitch rebuilt = SwitchBuilder.Rebuild(full, last, false, Resolve);

            // Дані лишилися від повного розрахунку, але запис чекає на перерахунок
            Assert.False(rebuilt.Resolved);
            Assert.True(rebuilt.Complete);
            Assert.Equal(full.Branches[1].Members.Count, rebuilt.Branches[1].Members.Count);
            Assert.Equal(full.Branches[0].FrogTo, rebuilt.Branches[0].FrogTo);
        }

        [Fact]
        public void RebuildSeesARailMissingInTheLoadedPart()
        {
            RailSwitch full = BuildSwitch();
            Section last = trunk[trunk.Count - 1];

            Forget(arc[2]);
            // Рейку знято на першій секції прямої гілки: вона завантажена, і це ще гостряк
            straight[0].RemovePart(SectionPart.FirstRail);

            Assert.False(SwitchBuilder.Rebuild(full, last, false, Resolve).Complete);
        }

        [Fact]
        public void RebuildDropsARecordOfADifferentTrack()
        {
            RailSwitch full = BuildSwitch();
            Section last = trunk[trunk.Count - 1];

            // Старий запис про іншу колію: його гілка починалася з іншої секції
            full.Branches[1].Members[0] = new SectionLink(arc[5], true);
            Forget(arc[2]);
            RailSwitch rebuilt = SwitchBuilder.Rebuild(full, last, false, Resolve);

            Assert.False(rebuilt.Resolved);
            Assert.False(rebuilt.Complete);
            Assert.True(rebuilt.Branches[1].Members[0].PointsTo(arc[0]));
        }

        [Fact]
        public void RebuildOfAnUnchangedSwitchGivesTheSameRecord()
        {
            RailSwitch full = BuildSwitch();
            Section last = trunk[trunk.Count - 1];

            Assert.True(SwitchBuilder.Rebuild(full, last, false, Resolve).SameAs(full));

            arc[1].RemovePart(SectionPart.FirstRail);
            Assert.False(SwitchBuilder.Rebuild(full, last, false, Resolve).SameAs(full));
        }

        [Fact]
        public void UnloadedFirstSectionStillGivesASwitch()
        {
            BuildSwitch();
            Section last = trunk[trunk.Count - 1];

            Forget(arc[0]);
            RailSwitch built = SwitchBuilder.Build(last, false, Resolve);

            Assert.NotNull(built);
            Assert.Equal(2, built.Branches.Count);
            Assert.False(built.Resolved);
            Assert.False(built.Complete);
        }
    }
}
