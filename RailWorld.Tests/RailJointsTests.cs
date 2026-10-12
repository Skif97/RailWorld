using System.Collections.Generic;
using RailWorld.src.RailWay;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>Стики рейок: на кожній двадцятій секції, розставляються при укладанні.</summary>
    public class RailJointsTests : TrackTestBase
    {
        // Номери секцій зі стиком, з нуля, уздовж колії
        private static List<int> Joints(List<Section> track)
        {
            var found = new List<int>();
            for (int n = 0; n < track.Count; n++)
            {
                if (track[n].RailJoint) found.Add(n);
            }
            return found;
        }

        // Стик на кожній двадцятій секції, починаючи з секції first
        private static void AssertEveryRail(List<int> joints, int sections, int first)
        {
            var expected = new List<int>();
            for (int n = first; n < sections; n += RailJoints.RailSections) expected.Add(n);
            Assert.Equal(expected, joints);
        }

        // У нової колії перша рейка ні з чим не стикується: перший стик на її двадцятій секції
        private const int NewTrack = RailJoints.RailSections - 1;

        [Fact]
        public void LongStretchGetsAJointOnEveryTwentiethSection()
        {
            List<Section> stretch = Lay(Point(0, 5, 1, 0), Point(120, 5, 1, 0));
            RailJoints.Mark(stretch, Resolve);

            AssertEveryRail(Joints(stretch), stretch.Count, NewTrack);
        }

        [Theory]
        [InlineData(2.0)]
        [InlineData(1.0)]
        [InlineData(7.0)]
        public void TrackBuiltInShortPiecesGetsTheSameJoints(double piece)
        {
            // Колію кладуть шматками, кожен коротший за рейку, і прикладають до вже покладеної
            var track = new List<Section>();
            for (double from = 0; from < 84; from += piece)
            {
                List<Section> stretch = Lay(Point(from, 5, 1, 0), Point(from + piece, 5, 1, 0));
                if (track.Count > 0) Join(track[track.Count - 1], stretch[0]);
                RailJoints.Mark(stretch, Resolve);
                track.AddRange(stretch);
            }

            AssertEveryRail(Joints(track), track.Count, NewTrack);
        }

        [Fact]
        public void StretchLaidOntoOldTrackWithoutJointsStartsWithOne()
        {
            // Колія, покладена до появи стиків: довша за рейку, і жодного стику на ній немає
            List<Section> old = Lay(Point(0, 5, 1, 0), Point(40, 5, 1, 0));
            List<Section> stretch = Lay(Point(40, 5, 1, 0), Point(70, 5, 1, 0));
            Join(old[old.Count - 1], stretch[0]);
            RailJoints.Mark(stretch, Resolve);

            // Позаду колія є, а стику на ній немає: нова ділянка починається зі стику
            Assert.Empty(Joints(old));
            AssertEveryRail(Joints(stretch), stretch.Count, 0);
        }

        [Fact]
        public void PieceLaidBetweenTwoTracksDoesNotPutAJointNextToAnother()
        {
            // Дві колії і проміжок між ними, який закривають останнім. Рахунок першої колії випадає на проміжок,
            // але одразу за ним на другій колії вже стоїть стик
            List<Section> first = Lay(Point(0, 5, 1, 0), Point(39, 5, 1, 0));
            RailJoints.Mark(first, Resolve);
            List<Section> second = Lay(Point(43, 5, 1, 0), Point(83, 5, 1, 0));
            second[3].RailJoint = true;

            List<Section> gap = Lay(Point(39, 5, 1, 0), Point(43, 5, 1, 0));
            Join(first[first.Count - 1], gap[0]);
            Join(gap[gap.Count - 1], second[0]);
            RailJoints.Mark(gap, Resolve);

            Assert.Empty(Joints(gap));
        }

        [Fact]
        public void PieceLaidBetweenTwoTracksKeepsTheCountOfTheFirst()
        {
            // Те саме, але найближчий стик другої колії далеко: проміжок отримує свій, за рахунком першої
            List<Section> first = Lay(Point(0, 5, 1, 0), Point(39, 5, 1, 0));
            RailJoints.Mark(first, Resolve);
            List<Section> second = Lay(Point(43, 5, 1, 0), Point(83, 5, 1, 0));
            RailJoints.Mark(second, Resolve);

            List<Section> gap = Lay(Point(39, 5, 1, 0), Point(43, 5, 1, 0));
            Join(first[first.Count - 1], gap[0]);
            Join(gap[gap.Count - 1], second[0]);
            RailJoints.Mark(gap, Resolve);

            var track = new List<Section>(first);
            track.AddRange(gap);
            List<int> joints = Joints(track);
            AssertEveryRail(joints, track.Count, NewTrack);
            Assert.Single(Joints(gap));
        }

        [Fact]
        public void SameStretchAlwaysGetsTheSameJoints()
        {
            List<Section> first = Lay(Point(3, 9, 1, 0), Point(83, 9, 1, 0));
            List<Section> second = Lay(Point(3, 9, 1, 0), Point(83, 9, 1, 0));
            RailJoints.Mark(first, Resolve);
            RailJoints.Mark(second, Resolve);
            Assert.Equal(Joints(first), Joints(second));
        }

        [Fact]
        public void NewTrackHasNoJointUntilItIsARailLong()
        {
            // Позаду нічого немає: перша рейка ні з чим не стикується
            List<Section> stretch = Lay(Point(0, 0, 1, 0), Point(9, 0, 1, 0));
            RailJoints.Mark(stretch, Resolve);
            Assert.Empty(Joints(stretch));
        }
    }
}
