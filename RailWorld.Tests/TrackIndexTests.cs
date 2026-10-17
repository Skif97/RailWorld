using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Покладена колія, розкладена по клітинках: правила питають у неї, які секції лежать біля точки.
    /// </summary>
    public class TrackIndexTests : TrackTestBase
    {
        [Fact]
        public void FindsEverySectionNearThePointAndNothingFarAway()
        {
            // Довга колія через кілька чанків, у тому числі через межу чанка на x = 32 і x = 64
            List<Section> laid = Lay(Point(10, 5, 1, 0), Point(90, 5, 1, 0));
            TrackIndex track = SectionsIn;

            foreach (double x in new[] { 12.0, 31.5, 32.5, 50, 63.9, 64.1, 88 })
            {
                var point = new Vec3d(x, 0, 5);
                List<Section> near = track.Around(point, 3, 2);

                foreach (Section section in laid)
                {
                    double away = Math.Abs(section.GetGlobalPos().X - x);
                    // Усе, що в межах запитаних трьох блоків, знайдено
                    if (away <= 3) Assert.Contains(section, near);
                    // А далекого немає: інакше це знову перебір усієї колії
                    if (away > 4) Assert.DoesNotContain(section, near);
                }
            }
        }

        [Fact]
        public void TrackOnAnotherLevelIsFoundOnlyWhenAskedThatFar()
        {
            // Колія на 40 блоків вище: інший чанк по висоті
            Lay(new RoutePoint { Position = new Vec3d(10, 40, 5), Tangent = new Vec3d(1, 0, 0) },
                new RoutePoint { Position = new Vec3d(20, 40, 5), Tangent = new Vec3d(1, 0, 0) });

            Assert.Empty(SectionsIn.Around(new Vec3d(15, 0, 5), 3, 2));
            Assert.NotEmpty(SectionsIn.Around(new Vec3d(15, 38, 5), 3, 2));
        }

        [Fact]
        public void RemovedSectionIsNotFoundAnyMore()
        {
            List<Section> laid = Lay(Point(10, 5, 1, 0), Point(20, 5, 1, 0));
            Section gone = laid[laid.Count / 2];
            Vec3d middle = gone.GetGlobalPos();
            Assert.Contains(gone, SectionsIn.Around(middle, 1, 1));

            Forget(gone);

            List<Section> near = SectionsIn.Around(middle, 1, 1);
            Assert.DoesNotContain(gone, near);
            // Сусіди на місці
            Assert.Contains(laid[laid.Count / 2 - 1], near);
        }

        [Fact]
        public void GridIsRebuiltAfterReading()
        {
            // Сітка не зберігається: прочитані дані чанка розкладають секції по клітинках заново
            var data = new DataInChunk();
            List<Section> laid = Plan(Point(10, 5, 1, 0), Point(20, 5, 1, 0));
            foreach (Section section in laid) data.Add(section);

            DataInChunk read = ProtoBuf.Serializer.DeepClone(data);
            var track = new TrackIndex(chunk => chunk.Equals(laid[0].ChunkAddres) ? read.Grid : null);

            List<Section> near = track.Around(laid[0].GetGlobalPos(), 30, 2);
            Assert.Equal(read.RailWaySections.Count, near.Count);
            foreach (Section section in read.RailWaySections.Values) Assert.Contains(section, near);
        }
    }
}
