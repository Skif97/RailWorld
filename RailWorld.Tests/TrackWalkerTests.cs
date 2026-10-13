using System.Collections.Generic;
using RailWorld.src.RailWay;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Обхід покладеної колії: куди він іде, де зупиняється і що про це каже.
    /// Стрілка в тестах: спільна колія із заходу, пряма гілка на схід і дуга.
    /// </summary>
    public class TrackWalkerTests : TrackTestBase
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

        [Fact]
        public void WalkVisitsEverySectionInOrderAndStopsAtTheOpenEnd()
        {
            List<Section> track = Trunk();
            TrackWalker walk = TrackWalker.Entering(track[0], true, Resolve);

            for (int i = 1; i < track.Count; i++)
            {
                Assert.True(walk.Next());
                Assert.Same(track[i], walk.Section);
                Assert.True(walk.Entered);
                Assert.Equal(WalkStop.None, walk.Stop);
            }

            Assert.False(walk.Next());
            Assert.Equal(WalkStop.End, walk.Stop);
            Assert.Same(track[track.Count - 1], walk.Section);
            Assert.Empty(walk.Ahead);
        }

        [Fact]
        public void WalkAgainstTheTrackEntersSectionsByTheirEnds()
        {
            List<Section> track = Trunk();
            TrackWalker walk = TrackWalker.Leaving(track[track.Count - 1], true, Resolve);

            for (int i = track.Count - 2; i >= 0; i--)
            {
                Assert.True(walk.Next());
                Assert.Same(track[i], walk.Section);
                Assert.False(walk.Entered);
            }
            Assert.False(walk.Next());
            Assert.Equal(WalkStop.End, walk.Stop);
        }

        [Fact]
        public void WalkStopsBeforeASwitchAndShowsBothBranches()
        {
            LaySwitch();
            Section last = trunk[trunk.Count - 1];
            TrackWalker walk = TrackWalker.Entering(last, true, Resolve);

            Assert.False(walk.Next());
            Assert.Equal(WalkStop.Fork, walk.Stop);
            Assert.Same(last, walk.Section);
            Assert.Equal(2, walk.Ahead.Count);
            Assert.Null(walk.Peek());
        }

        [Fact]
        public void WalkFromABranchPassesItsSwitchOntoTheTrunk()
        {
            LaySwitch();
            TrackWalker walk = TrackWalker.Leaving(arc[0], true, Resolve);

            Assert.True(walk.Next());
            Assert.Same(trunk[trunk.Count - 1], walk.Section);
            Assert.False(walk.Entered);
            Assert.True(walk.Section.HasSwitch(walk.Entered));
        }

        [Fact]
        public void RidingWalkTurnsWhereTheSwitchPoints()
        {
            LaySwitch();
            Section last = trunk[trunk.Count - 1];
            SectionLink active = last.GetActiveLink(false);
            List<Section> taken = active.PointsTo(straight[0]) ? straight : arc;

            TrackWalker walk = TrackWalker.Riding(last, true, Resolve);
            Assert.Same(taken[0], walk.Peek());
            Assert.Same(last, walk.Section);

            Assert.True(walk.Next());
            Assert.Same(taken[0], walk.Section);
            Assert.True(walk.Next());
            Assert.Same(taken[1], walk.Section);
        }

        [Fact]
        public void UnloadedNeighbourIsReportedApartFromTheEndOfTheTrack()
        {
            List<Section> track = Trunk();
            Forget(track[2]);
            TrackWalker walk = TrackWalker.Entering(track[0], true, Resolve);

            Assert.True(walk.Next());
            Assert.False(walk.Next());
            Assert.Equal(WalkStop.Unread, walk.Stop);
            Assert.Same(track[1], walk.Section);
            Assert.Single(walk.Ahead);
        }

        [Fact]
        public void WalkWithNothingToReadTheTrackWithGoesNowhere()
        {
            List<Section> track = Trunk();
            TrackWalker walk = TrackWalker.Entering(track[0], true, null);

            Assert.False(walk.Next());
            Assert.Equal(WalkStop.Unread, walk.Stop);
        }
    }
}
