using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Ключі секції й кінця секції: ними секції, стрілки й перетини впізнаються в множинах і словниках.
    /// </summary>
    public class SectionKeyTests : TrackTestBase
    {
        [Fact]
        public void SectionAndLinkToItGiveTheSameKey()
        {
            List<Section> track = Lay(Point(0, 0, 1, 0), Point(40, 0, 1, 0));
            Section section = track[track.Count - 1];

            Assert.Equal(SectionKey.Of(section), SectionKey.Of(new SectionLink(section, true)));
            Assert.NotEqual(SectionKey.Of(section), SectionKey.Of(track[0]));

            var seen = new HashSet<SectionKey> { SectionKey.Of(section) };
            Assert.Contains(new SectionKey(section.ChunkAddres.Clone(), section.IndexInChunk), seen);
        }

        [Fact]
        public void SwitchKeyIsTheEndOfItsTrunk()
        {
            var chunk = new Vec3i(3, 4, 5);
            foreach (bool atStart in new[] { true, false })
            {
                Assert.Equal(new SectionEndKey(chunk, 7, atStart), SectionEndKey.OfSwitch(chunk, RailSwitch.KeyOf(7, atStart)));
            }
            Assert.NotEqual(new SectionEndKey(chunk, 7, true), new SectionEndKey(chunk, 7, false));
        }
    }
}
