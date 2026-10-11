using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Зона стрілки: де нову стрілку ставити не можна і де маршрут стає на відкритий край колії.
    /// Стрілка в тестах та сама: спільна колія із заходу, пряма гілка на схід і дуга.
    /// </summary>
    public class SwitchZoneTests : TrackTestBase
    {
        private List<Section> trunk, straight, arc;

        private void LaySwitch()
        {
            trunk = Trunk();
            straight = Straight();
            arc = Arc();
            Section last = trunk[trunk.Count - 1];
            Join(last, straight[0]);
            Join(last, arc[0]);
        }

        // Відгалуження від прямої гілки зі стику після секції з таким номером: дуга в інший бік від стрілки
        private List<Section> BranchAfter(int index)
        {
            Vec3d joint = straight[index].FullEndPosition;
            double angle = Math.PI / 3;
            return Plan(Point(joint.X, joint.Z, 1, 0),
                Point(joint.X + ArcRadius * Math.Sin(angle), joint.Z - ArcRadius * (1 - Math.Cos(angle)), Math.Cos(angle), -Math.Sin(angle)));
        }

        [Fact]
        public void BranchInsideTheZoneOfASwitchIsRefused()
        {
            LaySwitch();

            // За дві секції від стрілки, там ще гостряк
            Assert.Equal(0, SectionLinker.FindSwitchInZone(BranchAfter(1), SectionsIn, Resolve));
        }

        [Fact]
        public void BranchFarFromTheSwitchIsAllowed()
        {
            LaySwitch();
            RailSwitch built = SwitchBuilder.Build(trunk[trunk.Count - 1], false, Resolve);
            int beyond = built.Branches[0].ZoneSections + SwitchZone.ClearSections + 3;

            Assert.Equal(-1, SectionLinker.FindSwitchInZone(BranchAfter(beyond), SectionsIn, Resolve));
        }

        [Fact]
        public void ContinuingATrackFromItsOpenEndIsNotASwitch()
        {
            // Колія з відкритим кінцем одразу біля стрілки не буває; беремо просто пряму
            straight = Straight();
            Section last = straight[straight.Count - 1];
            Vec3d end = last.FullEndPosition;

            List<Section> more = Plan(Point(end.X, end.Z, 1, 0), Point(end.X + 6, end.Z, 1, 0));
            Assert.Equal(-1, SectionLinker.FindSwitchInZone(more, SectionsIn, Resolve));
        }

        [Fact]
        public void OpenEndIsFoundWithinAFewSections()
        {
            straight = Straight();
            int last = straight.Count - 1;

            // Третя секція від краю: точка стає на сам край
            Assert.True(SwitchZone.FindOpenEnd(straight[last - 2], straight[last - 2].GetGlobalPos(), Resolve, out Section found, out bool atStart));
            Assert.Same(straight[last], found);
            Assert.False(atStart);

            // Далеко від обох країв: прив'язки немає
            Assert.False(SwitchZone.FindOpenEnd(straight[straight.Count / 2], straight[straight.Count / 2].GetGlobalPos(), Resolve, out _, out _));
        }

        [Fact]
        public void OpenEndBehindASwitchIsNotOffered()
        {
            LaySwitch();

            // Спільна колія коротка, її західний край відкритий. Але з першої секції гілки до нього стрілка
            Assert.False(SwitchZone.FindOpenEnd(straight[0], straight[0].GetGlobalPos(), Resolve, out _, out _));
            Assert.True(SwitchZone.FindOpenEnd(trunk[0], trunk[0].GetGlobalPos(), Resolve, out Section found, out bool atStart));
            Assert.Same(trunk[0], found);
            Assert.True(atStart);
        }
    }
}
