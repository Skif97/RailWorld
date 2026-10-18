using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Які сусіди перемальовуються після зміни колії в чанку: лише ті, вигляд колії в яких бере щось із нього.
    /// </summary>
    public class ChunkDependencyTests : TrackTestBase
    {
        [Fact]
        public void PlainTrackAcrossTheChunkBorderDoesNotTieTheChunks()
        {
            List<Section> track = Lay(Point(24, 4, 1, 0), Point(40, 4, 1, 0));
            Vec3i a = track[0].ChunkAddres, b = track[track.Count - 1].ChunkAddres;
            Assert.NotEqual(a, b);

            Assert.False(ChunkDependency.Reads(DataIn(a), b, DataIn));
            Assert.False(ChunkDependency.Reads(DataIn(b), a, DataIn));
        }

        [Fact]
        public void SwitchWithABranchInTheNextChunkTiesBothWays()
        {
            // Стрілка біля межі чанків на x = 32: спільна колія і запис у першому чанку, гілки заходять у другий
            List<Section> trunk = Lay(Point(20, 4, 1, 0), Point(28, 4, 1, 0));
            Section last = trunk[trunk.Count - 1];
            List<Section> straight = Lay(Point(28, 4, 1, 0), Point(44, 4, 1, 0));
            double angle = System.Math.PI / 4;
            List<Section> arc = Lay(Point(28, 4, 1, 0),
                Point(28 + ArcRadius * System.Math.Sin(angle), 4 + ArcRadius * (1 - System.Math.Cos(angle)), System.Math.Cos(angle), System.Math.Sin(angle)));
            Join(last, straight[0]);
            Join(last, arc[0]);

            RailSwitch record = SwitchBuilder.Build(last, false, Resolve);
            Vec3i home = last.ChunkAddres, next = new Vec3i(home.X + 1, home.Y, home.Z);
            DataIn(home).Switches[record.Key] = record;
            // Посилання секцій гілок на запис, як їх ставить сервер
            for (int k = 0; k < 2; k++)
            {
                for (int n = 0; n < record.Branches[k].Members.Count; n++)
                {
                    Section member = Resolve(record.Branches[k].Members[n]);
                    member.SetSwitchRef(record.Branches[k].Members[n].AtStart, new SwitchRef { ChunkX = home.X, ChunkY = home.Y, ChunkZ = home.Z, Key = record.Key, Branch = k, Number = n + 1 });
                }
            }
            Assert.Contains(record.Branches[0].Members, link => link.Chunk.Equals(next));

            Assert.True(ChunkDependency.Reads(DataIn(home), next, DataIn));
            Assert.True(ChunkDependency.Reads(DataIn(next), home, DataIn));
            // Чанк, якого стрілка не торкається, ні від чого не залежить
            Assert.False(ChunkDependency.Reads(DataIn(home), new Vec3i(home.X, home.Y, home.Z + 1), DataIn));
        }

        [Fact]
        public void CrossingRecordReadsTheChunkOfItsMargin()
        {
            // Перетин біля межі: запас основної колії заходить у сусідній чанк. Запис бере звідти секції, а сам
            // сусід, де лежить лише запас без посилань на запис, від чанка перетину не залежить
            List<Section> main = Lay(Point(20, 4, 1, 0), Point(44, 4, 1, 0));
            List<Section> across = Lay(Point(31.1, -6, 0, 1), Point(31.1, 14, 0, 1));
            Section center = main.Find(m => across.Exists(o => AxisCrossing.OfLaid(m, o)));
            Section other = across.Find(o => AxisCrossing.OfLaid(center, o));
            RailCrossing record = CrossingBuilder.Build(center, other, Resolve, out _);
            Vec3i home = center.ChunkAddres, next = new Vec3i(home.X + 1, home.Y, home.Z);
            DataIn(home).Crossings[record.Key] = record;

            Assert.True(ChunkDependency.Reads(DataIn(home), next, DataIn));
            Assert.False(ChunkDependency.Reads(DataIn(next), home, DataIn));
        }
    }
}
