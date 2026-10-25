using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Чи запис стрілки чи перетину справді має секцію там, куди вказує її посилання. На клієнті дані чанків
    /// приходять окремо, і посилання та запис можуть бути з різних версій колії.
    /// </summary>
    public class RecordHoldsTests : TrackTestBase
    {
        [Fact]
        public void SwitchHoldsOnlyTheSectionItsReferenceNames()
        {
            List<Section> trunk = Lay(Point(4, 4, 1, 0), Point(12, 4, 1, 0));
            Section last = trunk[trunk.Count - 1];
            List<Section> straight = Lay(Point(12, 4, 1, 0), Point(24, 4, 1, 0));
            double angle = System.Math.PI / 4;
            List<Section> arc = Lay(Point(12, 4, 1, 0),
                Point(12 + ArcRadius * System.Math.Sin(angle), 4 + ArcRadius * (1 - System.Math.Cos(angle)), System.Math.Cos(angle), System.Math.Sin(angle)));
            Join(last, straight[0]);
            Join(last, arc[0]);
            RailSwitch record = SwitchBuilder.Build(last, false, Resolve);
            Vec3i home = last.ChunkAddres;

            SectionLink first = record.Branches[0].Members[0];
            Section member = Resolve(first);
            var place = new SwitchRef { ChunkX = home.X, ChunkY = home.Y, ChunkZ = home.Z, Key = record.Key, Branch = 0, Number = 1 };
            Assert.True(record.Holds(place, member));

            // Посилання з іншої версії колії: під цим номером у записі інша секція
            var stale = new SwitchRef { ChunkX = home.X, ChunkY = home.Y, ChunkZ = home.Z, Key = record.Key, Branch = 0, Number = 2 };
            Assert.False(record.Holds(stale, member));
            // Номер за кінцем гілки
            var beyond = new SwitchRef { ChunkX = home.X, ChunkY = home.Y, ChunkZ = home.Z, Key = record.Key, Branch = 0, Number = record.Branches[0].Members.Count + 1 };
            Assert.False(record.Holds(beyond, member));
        }

        [Fact]
        public void CrossingHoldsOnlyTheSectionItsReferenceNames()
        {
            List<Section> main = Lay(Point(4, 4, 1, 0), Point(28, 4, 1, 0));
            List<Section> across = Lay(Point(15.1, -8, 0, 1), Point(15.1, 16, 0, 1));
            Section center = main.Find(m => across.Exists(o => AxisCrossing.OfLaid(m, o)));
            Section other = across.Find(o => AxisCrossing.OfLaid(center, o));
            RailCrossing record = CrossingBuilder.Build(center, other, Resolve, out _);
            Vec3i home = center.ChunkAddres;

            CrossingTrack track = record.Tracks[0];
            int number = track.Members.FindIndex(link => link.PointsTo(center)) + 1;
            var place = new CrossingRef { ChunkX = home.X, ChunkY = home.Y, ChunkZ = home.Z, Key = record.Key, Track = 0, Number = number };
            Assert.True(record.Holds(place, center));

            var stale = new CrossingRef { ChunkX = home.X, ChunkY = home.Y, ChunkZ = home.Z, Key = record.Key, Track = 0, Number = number + 1 };
            Assert.False(record.Holds(stale, center));
            var wrongTrack = new CrossingRef { ChunkX = home.X, ChunkY = home.Y, ChunkZ = home.Z, Key = record.Key, Track = 1, Number = number };
            Assert.False(record.Holds(wrongTrack, center));
        }
    }
}
