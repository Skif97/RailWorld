using System.Collections.Generic;
using RailWorld.src.RailWay;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Положення важеля стрілки: останній кадр моделі це гілка з боку важеля, з якого б боку колії він не стояв.
    /// </summary>
    public class LeverPoseTests : TrackTestBase
    {
        // Стрілка: спільна колія із заходу, пряма гілка на схід і дуга, що відходить у бік +Z.
        // Гілки приєднані в цьому порядку, тож пряма це положення 0, дуга положення 1
        private Section LaySwitch()
        {
            List<Section> trunk = Trunk(), straight = Straight(), arc = Arc();
            Section last = trunk[trunk.Count - 1];
            Join(last, straight[0]);
            Join(last, arc[0]);
            return last;
        }

        // З котрого боку нормалі спільної секції лежить +Z, куди відходить дуга
        private static int SideOfArc(Section trunk)
        {
            return trunk.EndNormal.Z > 0 ? 1 : -1;
        }

        [Fact]
        public void The_branch_at_the_lever_is_the_one_that_turns_to_its_side()
        {
            Section trunk = LaySwitch();
            int arcSide = SideOfArc(trunk);

            // Важіль з боку дуги: з його боку дуга, положення 1. З протилежного: пряма, положення 0
            Assert.Equal(1, SectionLinker.BranchToward(trunk, false, SwitchBuilder.Build(trunk, false, Resolve), arcSide, Resolve));
            Assert.Equal(0, SectionLinker.BranchToward(trunk, false, SwitchBuilder.Build(trunk, false, Resolve), -arcSide, Resolve));
        }

        [Fact]
        public void The_branch_is_unknown_while_a_branch_is_not_loaded()
        {
            Section trunk = LaySwitch();
            Forget(Resolve(trunk.GetLinks(false)[1]));

            Assert.Equal(-1, SectionLinker.BranchToward(trunk, false, SwitchBuilder.Build(trunk, false, Resolve), 1, Resolve));
        }

        [Theory]
        // гілка з боку важеля, увімкнена гілка, очікуваний кадр
        [InlineData(1, 1, 1.0)]
        [InlineData(1, 0, 0.0)]
        [InlineData(0, 0, 1.0)]
        [InlineData(0, 1, 0.0)]
        public void The_last_frame_is_the_branch_at_the_lever(int branchAtLever, int position, double expected)
        {
            // Стрілка вже стала в положення: рух закінчено
            Assert.Equal(expected, LeverPose.Thrown(position, 1, branchAtLever), 6);
        }

        [Fact]
        public void Moving_the_lever_to_the_other_side_turns_it_over()
        {
            Section trunk = LaySwitch();
            int arcSide = SideOfArc(trunk);

            // Увімкнена дуга. Важіль з її боку стоїть на останньому кадрі, з протилежного на першому:
            // тяга в обох випадках відводить гостряки від дуги
            double beside = LeverPose.Thrown(1, 1, SectionLinker.BranchToward(trunk, false, SwitchBuilder.Build(trunk, false, Resolve), arcSide, Resolve));
            double across = LeverPose.Thrown(1, 1, SectionLinker.BranchToward(trunk, false, SwitchBuilder.Build(trunk, false, Resolve), -arcSide, Resolve));
            Assert.Equal(1, beside, 6);
            Assert.Equal(0, across, 6);
        }

        [Fact]
        public void Partway_through_the_lever_is_partway()
        {
            // Стрілку щойно перевели на гілку з боку важеля: рух лише почався, важіль ще на першому кадрі
            Assert.Equal(0, LeverPose.Thrown(1, 0, 1), 6);
            Assert.Equal(0.25, LeverPose.Thrown(1, 0.25, 1), 6);
            // і так само, якщо перевели на гілку з далекого боку: з останнього кадру назад
            Assert.Equal(0.75, LeverPose.Thrown(0, 0.25, 1), 6);
        }

        [Fact]
        public void With_the_branch_unknown_the_lever_stands_as_for_the_first()
        {
            Assert.Equal(LeverPose.Thrown(1, 1, 0), LeverPose.Thrown(1, 1, -1), 6);
            Assert.Equal(LeverPose.Thrown(0, 1, 0), LeverPose.Thrown(0, 1, -1), 6);
        }
    }
}
