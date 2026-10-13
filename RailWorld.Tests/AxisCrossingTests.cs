using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Одне правило, чи перетинаються осі колій, і особливо що воно каже про перетин рівно на стику секцій:
    /// його мають однаково бачити запис перетину на сервері, правила прокладання і вирівнювання маршруту.
    /// Покладена колія в тестах іде із заходу на схід через початок координат; offset зсуває її вздовж осі,
    /// щоб початок координат припав на стик секцій (0) чи на середину секції (0.25).
    /// </summary>
    public class AxisCrossingTests : TrackTestBase
    {
        private List<Section> LayEastward(double offset)
        {
            return Lay(Point(-6 + offset, 0, 1, 0), Point(6 + offset, 0, 1, 0));
        }

        // Колія з півночі на південь через початок координат
        private static RoutePoint[] Southward(double offset)
        {
            return new[] { Point(0, -6 + offset, 0, 1), Point(0, 6 + offset, 0, 1) };
        }

        private static int Count(List<Section> planned, List<Section> laid)
        {
            int count = 0;
            foreach (Section a in planned)
            {
                foreach (Section b in laid)
                {
                    if (AxisCrossing.OfPlanned(a, b)) count++;
                }
            }
            return count;
        }

        [Fact]
        public void CrossingInTheMiddleOfSectionsIsFoundOnce()
        {
            List<Section> laid = LayEastward(0.25);
            Assert.Equal(1, Count(Plan(Southward(0.25)), laid));
        }

        [Fact]
        public void CrossingExactlyOnJointsIsSeenByTheRulesOnBothSectionsAtTheJoint()
        {
            List<Section> laid = LayEastward(0);
            // Дві нові секції сходяться в початку координат, дві покладені теж
            Assert.Equal(4, Count(Plan(Southward(0)), laid));
        }

        [Fact]
        public void CrossingExactlyOnJointsIsSeenByTheServerRecord()
        {
            List<Section> main = LayEastward(0);
            List<Section> other = Lay(Southward(0));

            int count = 0;
            foreach (Section a in main)
            {
                foreach (Section b in other)
                {
                    if (AxisCrossing.OfLaid(a, b)) count++;
                }
            }
            Assert.Equal(4, count);
        }

        [Fact]
        public void RoutePieceEndingOnAJointStillHitsTheTrack()
        {
            LayEastward(0);
            List<SegmentHit> before = TrackSurvey.Hits(new Vec3d(0, 0, -0.25), new Vec3d(0, 0, 0), SectionsIn);
            List<SegmentHit> after = TrackSurvey.Hits(new Vec3d(0, 0, 0), new Vec3d(0, 0, 0.25), SectionsIn);

            Assert.NotNull(before);
            Assert.NotNull(after);
            Assert.Equal(1, before[0].T, 6);
            Assert.Equal(0, after[0].T, 6);
        }

        [Fact]
        public void TrackButtingIntoTheSideOfAnotherDoesNotCrossIt()
        {
            List<Section> laid = LayEastward(0.25);
            // Колія з півночі, що кінчається на осі покладеної: за її кінцем нічого немає
            List<Section> butting = Lay(Point(0, -6, 0, 1), Point(0, 0, 0, 1));

            foreach (Section a in butting)
            {
                foreach (Section b in laid) Assert.False(AxisCrossing.OfLaid(a, b));
            }
        }

        [Fact]
        public void OpenEndOfALaidTrackIsNotCrossedByARouteThroughIt()
        {
            // Покладена колія кінчається в початку координат, маршрут проходить рівно через її край
            List<Section> laid = Lay(Point(-6, 0, 1, 0), Point(0, 0, 1, 0));
            Assert.Equal(0, Count(Plan(Southward(0.25)), laid));
        }

        [Fact]
        public void NewSectionContinuingALaidTrackDoesNotCrossIt()
        {
            List<Section> laid = Lay(Point(-6, 0, 1, 0), Point(0, 0, 1, 0), Point(6, 0, 1, 0));
            // Відгалуження зі стику посеред колії: дуга, що починається в її напрямку
            List<Section> branch = Plan(Point(0, 0, 1, 0), Point(ArcRadius, ArcRadius, 0, 1));
            Assert.Equal(0, Count(branch, laid));
        }

        [Fact]
        public void NeighbouringSectionsOfOneTrackDoNotCross()
        {
            List<Section> laid = LayEastward(0);
            for (int i = 0; i + 1 < laid.Count; i++) Assert.False(AxisCrossing.OfLaid(laid[i], laid[i + 1]));
        }
    }
}
