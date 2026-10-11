using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Перетин двох колій в одному рівні: запис про нього і шпали вузла. Основна колія йде на схід через
    /// початок координат, друга перетинає її там само під заданим кутом.
    /// </summary>
    public class CrossingTests : TrackTestBase
    {
        private const double Sleeper = TrackGauge.StandardSleeperLength;
        private List<Section> main, other;
        private Section mainCenter, otherCenter;

        // offset зсуває обидві колії вздовж себе: з нулем вони перетинаються точно на стиках секцій
        private void LayCross(double angleDeg, double offset = 0.2)
        {
            double angle = angleDeg * Math.PI / 180, cos = Math.Cos(angle), sin = Math.Sin(angle);
            main = Lay(Point(-12 + offset, 0, 1, 0), Point(12 + offset, 0, 1, 0));
            other = Lay(Point((-12 + offset) * cos, (-12 + offset) * sin, cos, sin), Point((12 + offset) * cos, (12 + offset) * sin, cos, sin));

            mainCenter = otherCenter = null;
            foreach (Section a in main)
            {
                foreach (Section b in other)
                {
                    if (!CrossingBuilder.Cross(a, b)) continue;
                    Assert.Null(mainCenter);
                    mainCenter = a;
                    otherCenter = b;
                }
            }
            Assert.NotNull(mainCenter);
        }

        private List<Section> Sections(CrossingTrack track)
        {
            var found = new List<Section>();
            foreach (SectionLink link in track.Members) found.Add(Resolve(link));
            return found;
        }

        private TimberLayout LayTimbers(RailCrossing record)
        {
            return NodeTimbers.LayCrossing(Sections(record.Tracks[0]), record.Tracks[0].Center - 1, Sections(record.Tracks[1]), record.Tracks[1].Center - 1);
        }

        private static Vec3d End(Timber timber, int sign)
        {
            return new Vec3d(timber.Position.X + timber.Across.X * timber.Length / 2 * sign, 0, timber.Position.Z + timber.Across.Z * timber.Length / 2 * sign);
        }

        // Чи перетинаються два відрізки в плані
        private static bool Cross(Vec3d a, Vec3d b, Vec3d c, Vec3d d)
        {
            double Side(Vec3d p, Vec3d q, Vec3d r) => (q.X - p.X) * (r.Z - p.Z) - (q.Z - p.Z) * (r.X - p.X);
            return Side(a, b, c) * Side(a, b, d) < 0 && Side(c, d, a) * Side(c, d, b) < 0;
        }

        private static void Ends(Section section, out Vec3d from, out Vec3d to)
        {
            Vec3d start = section.FullStartPosition, end = section.FullEndPosition;
            Vec3f normal = section.CenterNormal;
            double mx = (start.X + end.X) / 2, mz = (start.Z + end.Z) / 2;
            from = new Vec3d(mx - normal.X * Sleeper / 2, 0, mz - normal.Z * Sleeper / 2);
            to = new Vec3d(mx + normal.X * Sleeper / 2, 0, mz + normal.Z * Sleeper / 2);
        }

        [Theory]
        [InlineData(90.0)]
        [InlineData(45.0)]
        [InlineData(20.0)]
        public void RecordHoldsBothTracksThroughTheNode(double angle)
        {
            LayCross(angle);
            RailCrossing record = CrossingBuilder.Build(mainCenter, otherCenter, Resolve, out bool partsInPlace);

            Assert.True(partsInPlace);
            Assert.True(record.Complete);
            Assert.True(record.Resolved);
            Assert.Equal(mainCenter.IndexInChunk, record.Key);

            Section[] centers = { mainCenter, otherCenter };
            List<Section>[] tracks = { main, other };
            for (int k = 0; k < 2; k++)
            {
                CrossingTrack track = record.Tracks[k];
                List<Section> members = Sections(track);
                Assert.Same(centers[k], members[track.Center - 1]);

                // Секції йдуть підряд тією самою колією, у її порядку
                int first = tracks[k].IndexOf(members[0]);
                for (int n = 0; n < members.Count; n++)
                {
                    Assert.Same(tracks[k][first + n], members[n]);
                    Assert.True(track.Members[n].AtStart);
                }

                // У вузлі рівно ті, що лежать габаритом на другій колії; за ним ще запас
                for (int n = 0; n < members.Count; n++)
                {
                    bool lies = false;
                    foreach (Section crossed in tracks[1 - k]) lies |= Footprint.Overlap(members[n], crossed);
                    Assert.Equal(lies, track.InNode(n + 1));
                }
                Assert.Equal(CrossingBuilder.MarginSections, track.NodeFrom - 1);
                Assert.Equal(CrossingBuilder.MarginSections, members.Count - track.NodeTo);
            }

            // Що гостріший кут, то довший вузол
            if (angle < 30) Assert.True(record.Tracks[0].NodeTo - record.Tracks[0].NodeFrom > 12);
            if (angle > 80) Assert.True(record.Tracks[0].NodeTo - record.Tracks[0].NodeFrom < 6);
        }

        [Fact]
        public void TracksCrossingExactlyAtTheirJointsStillMakeOneCrossing()
        {
            LayCross(90, offset: 0);
            RailCrossing record = CrossingBuilder.Build(mainCenter, otherCenter, Resolve, out _);
            Assert.True(record.Complete);
        }

        [Fact]
        public void NeighboursAndTracksOnDifferentLevelsDoNotCross()
        {
            LayCross(90);
            for (int i = 0; i + 1 < main.Count; i++) Assert.False(CrossingBuilder.Cross(main[i], main[i + 1]));

            // Естакада: колія на три блоки вище
            List<Section> above = Lay(new RoutePoint { Position = new Vec3d(3.2, 3, -6), Tangent = new Vec3d(0, 0, 1) }, new RoutePoint { Position = new Vec3d(3.2, 3, 6), Tangent = new Vec3d(0, 0, 1) });
            foreach (Section a in main)
            {
                foreach (Section b in above) Assert.False(CrossingBuilder.Cross(a, b));
            }
        }

        [Fact]
        public void MissingPartInTheNodeLeavesTheCrossingUnassembled()
        {
            LayCross(60);
            RailCrossing whole = CrossingBuilder.Build(mainCenter, otherCenter, Resolve, out _);
            Section inNode = Resolve(whole.Tracks[1].Members[whole.Tracks[1].NodeFrom - 1]);
            Section outside = Resolve(whole.Tracks[1].Members[0]);

            outside.RemovePart(SectionPart.Sleeper);
            Assert.True(CrossingBuilder.Build(mainCenter, otherCenter, Resolve, out _).Complete);

            inNode.RemovePart(SectionPart.Sleeper);
            RailCrossing record = CrossingBuilder.Build(mainCenter, otherCenter, Resolve, out bool partsInPlace);
            Assert.False(partsInPlace);
            Assert.False(record.Complete);
        }

        [Fact]
        public void UnloadedChunkKeepsThePreviousRecord()
        {
            LayCross(45);
            RailCrossing whole = CrossingBuilder.Build(mainCenter, otherCenter, Resolve, out _);

            // Чанк із секцією вузла вивантажили: прочитати колію далі не можна
            Forget(Resolve(whole.Tracks[1].Members[whole.Tracks[1].NodeTo - 2]));
            RailCrossing partial = CrossingBuilder.Rebuild(whole, mainCenter, otherCenter, Resolve);

            Assert.False(partial.Resolved);
            Assert.True(partial.Complete);
            Assert.True(partial.Tracks[1].SameAs(whole.Tracks[1]));
        }

        [Theory]
        [InlineData(90.0)]
        [InlineData(80.0)]
        [InlineData(65.0)]
        public void NearRightAngleMainTrackLaysADeckTwiceAsDense(double angle)
        {
            LayCross(angle);
            RailCrossing record = CrossingBuilder.Build(mainCenter, otherCenter, Resolve, out _);
            TimberLayout layout = LayTimbers(record);

            // Усі шпали щита лежать упоперек основної колії, кроком у чверть блока, без пропусків
            var along = new List<double>();
            foreach (Timber timber in layout.Timbers)
            {
                Assert.True(Math.Abs(timber.Across.X) < 0.01, "шпала щита лежить косо");
                Assert.True(timber.Length >= Sleeper - 0.01);
                Assert.True(timber.Length <= Sleeper + 2 * NodeTimbers.DeckReach);
                along.Add(timber.Position.X);
            }
            along.Sort();
            Assert.True(along.Count >= 7);
            for (int i = 1; i < along.Count; i++) Assert.InRange(along[i] - along[i - 1], 0.24, 0.26);

            // Щит накриває другу колію на всю ширину її шпал
            double half = Sleeper / 2 / Math.Sin(angle * Math.PI / 180);
            Assert.True(along[0] <= -half + 0.3 && along[along.Count - 1] >= half - 0.3);

            // Друга колія у вузлі без власних шпал, далі зі своїми, і щит на них не лягає
            Assert.Contains(otherCenter, layout.Hidden);
            int own = 0;
            foreach (Section section in other)
            {
                if (layout.Hidden.Contains(section)) continue;
                own++;
                Ends(section, out Vec3d from, out Vec3d to);
                foreach (Timber timber in layout.Timbers)
                    Assert.False(Cross(End(timber, -1), End(timber, 1), from, to), "шпала щита перетинає шпалу другої колії");
            }
            Assert.True(own > other.Count - 12);

            // Шпали основної колії поза щитом лишаються її власними
            Assert.DoesNotContain(main[0], layout.Hidden);
            Assert.Contains(mainCenter, layout.Hidden);
        }

        [Fact]
        public void SkewedDeckReachesOutToTheFirstSleepersOfTheOtherTrack()
        {
            LayCross(70);
            TimberLayout layout = LayTimbers(CrossingBuilder.Build(mainCenter, otherCenter, Resolve, out _));

            int stretched = 0;
            foreach (Timber timber in layout.Timbers)
            {
                if (timber.Length > Sleeper + 0.3) stretched++;
            }
            Assert.True(stretched >= 4, "щит не дотягується до шпал другої колії: подовжених " + stretched);
        }

        [Theory]
        [InlineData(45.0)]
        [InlineData(25.0)]
        public void AcuteCrossingLaysBeamsAcrossTheBisector(double angle)
        {
            LayCross(angle);
            TimberLayout layout = LayTimbers(CrossingBuilder.Build(mainCenter, otherCenter, Resolve, out _));
            Assert.True(layout.Timbers.Count >= 8);

            // Бісектриса гострого кута і напрямок упоперек неї
            double half = angle * Math.PI / 360;
            double bx = Math.Cos(half), bz = Math.Sin(half);
            Vec3d crossAt = new Vec3d(0, 0, 0);
            foreach (Timber timber in layout.Timbers)
            {
                Assert.True(Math.Abs(timber.Across.X * bx + timber.Across.Z * bz) < 0.02, "брус не впоперек бісектриси");
            }

            // Обидві колії віддали вузол під бруси, порівну з обох боків, і далі кладуть власні шпали
            Assert.Contains(mainCenter, layout.Hidden);
            Assert.Contains(otherCenter, layout.Hidden);
            Assert.DoesNotContain(main[0], layout.Hidden);
            Assert.DoesNotContain(other[other.Count - 1], layout.Hidden);

            foreach (List<Section> track in new[] { main, other })
            {
                foreach (Section section in track)
                {
                    if (layout.Hidden.Contains(section)) continue;
                    Ends(section, out Vec3d from, out Vec3d to);
                    foreach (Timber timber in layout.Timbers)
                        Assert.False(Cross(End(timber, -1), End(timber, 1), from, to), "брус перетинає власну шпалу колії");
                }
            }

            // Біля місця перетину осей брус тримає обидві колії: довший за шпалу, але не безмежний
            Timber nearest = layout.Timbers[0];
            foreach (Timber timber in layout.Timbers)
            {
                if (timber.Position.HorizontalSquareDistanceTo(0, 0) < nearest.Position.HorizontalSquareDistanceTo(0, 0)) nearest = timber;
            }
            Assert.InRange(nearest.Length, Sleeper, Sleeper + 0.6);
        }

        [Fact]
        public void UnassembledCrossingKeepsEachTrackOnItsOwnSleepers()
        {
            LayCross(90);
            // Розкладку питають лише в зібраного перетину; тут перевіряємо, що без перетнутої секції вона порожня
            TimberLayout layout = NodeTimbers.LayCrossing(main, -1, other, 0);
            Assert.Empty(layout.Timbers);
            Assert.Empty(layout.Hidden);
        }
    }
}
