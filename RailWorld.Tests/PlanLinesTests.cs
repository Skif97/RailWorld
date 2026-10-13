using RailWorld.src.RailWay;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Перетин двох прямих у плані: де він лежить на кожній з них.
    /// </summary>
    public class PlanLinesTests
    {
        [Fact]
        public void CrossingSegmentsMeetAtTheirShares()
        {
            // Перший відрізок іде на схід від (0, 0) до (4, 0), другий на південь від (1, -1) до (1, 3)
            Assert.True(PlanLines.Meet(0, 0, 4, 0, 1, -1, 0, 4, out double t, out double u));
            Assert.Equal(0.25, t, 12);
            Assert.Equal(0.25, u, 12);
        }

        [Fact]
        public void EndsOfSegmentsAreZeroAndOne()
        {
            // Другий відрізок починається рівно в кінці першого
            Assert.True(PlanLines.Meet(0, 0, 2, 0, 2, 0, 0, 3, out double t, out double u));
            Assert.Equal(1, t, 12);
            Assert.Equal(0, u, 12);
        }

        [Fact]
        public void MeetingBeyondASegmentLiesOutsideZeroToOne()
        {
            // Прямі перетинаються в (5, 0): за кінцем першого відрізка і перед початком другого
            Assert.True(PlanLines.Meet(0, 0, 2, 0, 5, 1, 0, 1, out double t, out double u));
            Assert.Equal(2.5, t, 12);
            Assert.Equal(-1, u, 12);
        }

        [Fact]
        public void SwappingTheLinesSwapsTheShares()
        {
            Assert.True(PlanLines.Meet(0, 0, 3, 1, 2, -2, -1, 4, out double t, out double u));
            Assert.True(PlanLines.Meet(2, -2, -1, 4, 0, 0, 3, 1, out double t2, out double u2));
            Assert.Equal(t, u2, 12);
            Assert.Equal(u, t2, 12);
        }

        [Fact]
        public void ParallelLinesDoNotMeet()
        {
            Assert.False(PlanLines.Meet(0, 0, 2, 1, 0, 1, 4, 2, out _, out _));
            // Одна й та сама пряма теж: місця перетину в неї немає
            Assert.False(PlanLines.Meet(0, 0, 2, 1, 2, 1, -2, -1, out _, out _));
        }
    }
}
