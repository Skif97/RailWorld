using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>Чанки з недорахованими записами, що чекають на завантаження сусідів.</summary>
    public class PendingChunksTests
    {
        [Fact]
        public void ChunkWaitsWhileItHasAnUnresolvedRecordAndIsFoundFromNeighbours()
        {
            var pending = new PendingChunks();
            var chunk = new Vec3i(10, 3, 20);
            pending.Track(chunk, true);

            // З колонки самого чанка і з сусідніх, на будь-якій висоті
            Assert.Contains(chunk, pending.Near(10, 20));
            Assert.Contains(chunk, pending.Near(11, 19));
            Assert.Empty(pending.Near(12, 20));

            // Записи дораховано: чанк більше не чекає
            pending.Track(chunk, false);
            Assert.Empty(pending.Near(10, 20));
        }

        [Fact]
        public void ChunksOfOneColumnWaitSeparately()
        {
            var pending = new PendingChunks();
            var low = new Vec3i(10, 2, 20);
            var high = new Vec3i(10, 5, 20);
            pending.Track(low, true);
            pending.Track(high, true);

            // Дорахований чанк колонки не забирає з собою інший
            pending.Track(low, false);
            Assert.Equal(new[] { high }, pending.Near(10, 20));
        }
    }
}
