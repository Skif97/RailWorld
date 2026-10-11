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
        public void ChunkIsReadOnce()
        {
            Lay(Point(10, 5, 1, 0), Point(20, 5, 1, 0));

            int reads = 0;
            var track = new TrackIndex(chunk =>
            {
                reads++;
                return StoredIn(chunk);
            });

            for (int i = 0; i < 50; i++) track.Around(new Vec3d(10 + i * 0.2, 0, 5), 3, 2);
            // Точки лежать в одному чанку і поруч із його сусідами: кожен із них прочитано раз, а не п'ятдесят
            Assert.InRange(reads, 1, 8);
        }
    }
}
