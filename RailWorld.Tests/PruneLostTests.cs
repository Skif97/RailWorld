using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Прибирання того, що веде в нікуди: зв'язків із секціями, яких уже немає, і посилань на зниклі записи.
    /// Так лишається, коли колію міняли, поки чанк секції не був завантажений.
    /// </summary>
    public class PruneLostTests : TrackTestBase
    {
        // Колія через межу чанків на x = 32
        private List<Section> LayAcrossChunks()
        {
            List<Section> track = Lay(Point(24, 4, 1, 0), Point(40, 4, 1, 0));
            Assert.NotEqual(track[0].ChunkAddres, track[track.Count - 1].ChunkAddres);
            return track;
        }

        private static int Boundary(List<Section> track)
        {
            return track.FindIndex(section => !section.ChunkAddres.Equals(track[0].ChunkAddres));
        }

        [Fact]
        public void LinkToARemovedSectionInALoadedChunkIsRemoved()
        {
            List<Section> track = LayAcrossChunks();
            int across = Boundary(track);
            Section last = track[across - 1];
            // Сусіда за межею прибрали, поки чанк last не був завантажений: зв'язок на нього лишився
            Forget(track[across]);

            Assert.True(SectionLinker.PruneLost(DataIn(last.ChunkAddres), chunk => true, DataIn));
            Assert.Empty(last.GetLinks(false));
        }

        [Fact]
        public void LinkIntoAnUnloadedChunkStays()
        {
            List<Section> track = LayAcrossChunks();
            int across = Boundary(track);
            Section last = track[across - 1];
            Vec3i far = track[across].ChunkAddres;

            // Про незавантажений чанк сказати нічого не можна
            Assert.False(SectionLinker.PruneLost(DataIn(last.ChunkAddres), chunk => !chunk.Equals(far), DataIn));
            Assert.Single(last.GetLinks(false));
        }

        [Fact]
        public void ReferencesToRecordsThatAreGoneAreRemoved()
        {
            List<Section> track = Lay(Point(4, 4, 1, 0), Point(12, 4, 1, 0));
            Vec3i chunk = track[0].ChunkAddres;
            DataInChunk data = DataIn(chunk);

            // Посилання на стрілку й перетин, записів яких у чанку немає, і на стрілку, запис якої є
            track[1].SetSwitchRef(true, new SwitchRef { ChunkX = chunk.X, ChunkY = chunk.Y, ChunkZ = chunk.Z, Key = 999 });
            track[2].SetCrossingRef(new CrossingRef { ChunkX = chunk.X, ChunkY = chunk.Y, ChunkZ = chunk.Z, Key = 998 });
            data.Switches[7] = new RailSwitch { TrunkIndex = 3, AtStart = false };
            track[3].SetSwitchRef(true, new SwitchRef { ChunkX = chunk.X, ChunkY = chunk.Y, ChunkZ = chunk.Z, Key = 7 });

            Assert.True(SectionLinker.PruneLost(data, c => true, DataIn));
            Assert.Null(track[1].GetSwitchRef(true));
            Assert.Null(track[2].GetCrossingRef());
            Assert.NotNull(track[3].GetSwitchRef(true));
            // Зв'язки цілої колії не чіпаються
            Assert.Single(track[1].GetLinks(true));
            Assert.Single(track[1].GetLinks(false));
        }

        [Fact]
        public void ChangeInTheMarginOfACrossingFindsItsRecord()
        {
            // Перетин біля межі чанків на x = 32: запас основної колії за вузлом заходить у сусідній чанк, а запис
            // лежить у чанку перетнутої секції. Посилань на запис у секцій запасу немає, тож зміну там знаходить
            // лише пошук по секціях записів у сусідніх чанках
            List<Section> main = Lay(Point(20, 4, 1, 0), Point(44, 4, 1, 0));
            List<Section> across = Lay(Point(31.1, -6, 0, 1), Point(31.1, 14, 0, 1));
            Section center = main.Find(m => across.Exists(o => AxisCrossing.OfLaid(m, o)));
            Section other = across.Find(o => AxisCrossing.OfLaid(center, o));
            RailCrossing record = CrossingBuilder.Build(center, other, Resolve, out _);
            Vec3i home = center.ChunkAddres;
            DataIn(home).Crossings[record.Key] = record;

            var next = new Vec3i(home.X + 1, home.Y, home.Z);
            Assert.Contains(record.Tracks[0].Members, link => link.Chunk.Equals(next));
            Assert.Contains(new SectionKey(home, record.Key), CrossingRegistry.TouchedBy(new[] { next }, new SectionKey[0], DataIn));

            // Секція запасу в сусідньому чанку знаходить запис і сама, без свого чанка
            SectionLink margin = record.Tracks[0].Members.Find(link => link.Chunk.Equals(next));
            Assert.Contains(new SectionKey(home, record.Key), CrossingRegistry.TouchedBy(new Vec3i[0], new[] { SectionKey.Of(margin) }, DataIn));

            // Чанк і секція, яких запис не торкається, його не знаходять
            var far = new Vec3i(home.X, home.Y, home.Z + 3);
            Assert.Empty(CrossingRegistry.TouchedBy(new[] { far }, new[] { new SectionKey(far, 0) }, DataIn));
        }
    }
}
