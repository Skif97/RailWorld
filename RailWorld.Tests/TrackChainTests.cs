using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Колія, якою клієнт веде вагонетку між присланими положеннями (TrackChain), і місце вагонетки на секції
    /// (RailPose): положення, поставлене сервером, клієнт має покласти назад на те саме місце колії.
    /// </summary>
    public class TrackChainTests : TrackTestBase
    {
        private const double HalfHeight = 0.4;

        // Положення вагонетки, як його ставить сервер
        private static Vec3d Placed(Section section, double s)
        {
            var pos = new EntityPos();
            RailPose.Place(pos, section, s, HalfHeight, 1, out _);
            return new Vec3d(pos.X, pos.Y, pos.Z);
        }

        [Fact]
        public void PlacedPositionReadsBackOnArc()
        {
            List<Section> track = Arc();
            foreach (Section section in track)
            {
                double s = RailPose.Length(section) * 0.3;
                double distance = RailPose.Distance(section, Placed(section, s), HalfHeight, out double back);
                Assert.True(distance < 1e-6, "відстань " + distance);
                Assert.Equal(s, back, 6);
            }
        }

        [Fact]
        public void ChainReachesBothWays()
        {
            List<Section> track = Straight();
            Section middle = track[track.Count / 2];
            var chain = new TrackChain();
            chain.Build(middle, 3, Resolve);

            // Від середньої секції не менше трьох блоків у кожен бік
            Assert.True(chain.At(-3, out Section behind, out _, out _));
            Assert.True(chain.At(3 + RailPose.Length(middle), out Section ahead, out _, out _));
            Assert.True(behind.GetGlobalPos().X < middle.GetGlobalPos().X - 2.5);
            Assert.True(ahead.GetGlobalPos().X > middle.GetGlobalPos().X + 2.5);
        }

        [Fact]
        public void PositionOnChainGoesBackToSameSpot()
        {
            List<Section> track = Arc();
            Section middle = track[track.Count / 2];
            var chain = new TrackChain();
            chain.Build(middle, 4, Resolve);

            Section other = track[track.Count / 2 + 3];
            double s = RailPose.Length(other) * 0.7;
            Assert.True(chain.Locate(Placed(other, s), HalfHeight, 0.5, out double at));
            Assert.True(chain.At(at, out Section found, out double back, out bool reversed));
            Assert.Same(other, found);
            Assert.Equal(s, back, 6);
            Assert.False(reversed);
        }

        [Fact]
        public void HalfwayAlongChainStaysOnTrack()
        {
            List<Section> track = Arc();
            Section middle = track[track.Count / 2];
            var chain = new TrackChain();
            chain.Build(middle, 4, Resolve);

            Section a = track[track.Count / 2 - 2], b = track[track.Count / 2 + 2];
            Assert.True(chain.Locate(Placed(a, 0.1), HalfHeight, 0.5, out double atA));
            Assert.True(chain.Locate(Placed(b, 0.1), HalfHeight, 0.5, out double atB));
            Assert.True(chain.At((atA + atB) / 2, out Section here, out double s, out _));

            // Посередині шляху колією вагонетка на колії; навпростець між тими самими положеннями вона була б
            // убік від неї
            Vec3d onTrack = Placed(here, s);
            Assert.True(RailPose.Distance(here, onTrack, HalfHeight, out _) < 1e-6);
            Assert.Same(middle, here);
        }

        [Fact]
        public void ChainFollowsSectionLaidTowardsIt()
        {
            // Дві колії сходяться кінцями: друга покладена назустріч першій
            List<Section> east = Lay(Point(0, 0, 1, 0), Point(6, 0, 1, 0));
            List<Section> west = Lay(Point(12, 0, -1, 0), Point(6, 0, -1, 0));
            Section last = east[east.Count - 1], met = west[west.Count - 1];
            last.AddLink(false, new SectionLink(met, false));
            met.AddLink(false, new SectionLink(last, false));

            var chain = new TrackChain();
            chain.Build(last, 4, Resolve);

            double s = RailPose.Length(met) * 0.25;
            Assert.True(chain.Locate(Placed(met, s), HalfHeight, 0.5, out double at));
            // Далі за кінцем першої колії координата росте і на другій
            Assert.True(at > RailPose.Length(last));
            Assert.True(chain.At(at, out Section found, out double back, out bool reversed));
            Assert.Same(met, found);
            Assert.Equal(s, back, 6);
            Assert.True(reversed);
        }

        [Fact]
        public void RingIsTakenOnce()
        {
            List<Section> ring = Lay(Through((0, 0), (2, 0), (2, 2), (0, 2), (0, 0)));
            Join(ring[ring.Count - 1], ring[0]);

            var chain = new TrackChain();
            chain.Build(ring[0], 50, Resolve);
            Assert.Equal(ring.Count, chain.Count);
        }

        [Fact]
        public void FarPositionIsNotOnChain()
        {
            List<Section> track = Straight();
            var chain = new TrackChain();
            chain.Build(track[track.Count / 2], 3, Resolve);
            Vec3d aside = Placed(track[track.Count / 2], 0.1).Add(0, 0, 2);
            Assert.False(chain.Locate(aside, HalfHeight, 0.5, out _));
        }
    }
}
