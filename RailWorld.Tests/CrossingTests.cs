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

        private CrossingRails LayRails(double angle)
        {
            LayCross(angle);
            RailCrossing record = CrossingBuilder.Build(mainCenter, otherCenter, Resolve, out _);
            var tracks = new IList<Section>[2];
            var entered = new IList<bool>[2];
            var centers = new int[2];
            for (int k = 0; k < 2; k++)
            {
                tracks[k] = Sections(record.Tracks[k]);
                var flags = new List<bool>();
                foreach (SectionLink link in record.Tracks[k].Members) flags.Add(link.AtStart);
                entered[k] = flags;
                centers[k] = record.Tracks[k].Center - 1;
            }
            return CrossingGeometry.Build(tracks, entered, centers);
        }

        // Точка на осі рейки в місці at: номер перерізу лінії з дробовою частиною
        private static Vec3d Where(CrossingRail rail, double at)
        {
            int index = Math.Max(0, Math.Min((int)Math.Floor(at), rail.Frames.Count - 2));
            Vec3d a = rail.Frames[index].Center, b = rail.Frames[index + 1].Center;
            double part = at - index;
            return new Vec3d(a.X + (b.X - a.X) * part, 0, a.Z + (b.Z - a.Z) * part);
        }

        // Довжина ділянки вздовж рейки між двома краями, по її осі, блоків
        private static double Gap(CrossingRail rail, RailEdge from, RailEdge to)
        {
            return RouteBuilder.HorizontalDistance(Where(rail, from.Middle), Where(rail, to.Middle));
        }

        private static List<RailSpan> Sorted(CrossingRail rail, RailPiece piece)
        {
            var spans = new List<RailSpan>(rail.Of(piece));
            spans.Sort((x, y) => x.From.Middle.CompareTo(y.From.Middle));
            return spans;
        }

        [Theory]
        [InlineData(90.0)]
        [InlineData(45.0)]
        [InlineData(20.0)]
        public void RunningRailsGetAFlangewayBesideEachRailOfTheOtherTrack(double angle)
        {
            CrossingRails rails = LayRails(angle);
            Assert.NotNull(rails);
            Assert.Equal(4, rails.Rails.Count);
            double sin = Math.Sin(angle * Math.PI / 180);
            double head = RailProfile.HeadHalfWidth * 2, groove = SwitchGeometry.FrogFlangeway;
            double foot = RailProfile.FootHalfWidth * 2;

            foreach (CrossingRail rail in rails.Rails)
            {
                // Головка з трьох шматків: два розриви, по одному біля кожної рейки іншої колії
                List<RailSpan> heads = Sorted(rail, RailPiece.Head);
                Assert.Equal(3, heads.Count);
                Assert.False(heads[0].CapFrom || heads[2].CapTo);

                // Основна колія: лише жолоб, стінки з обох боків. Друга: жолоб і головка рейки основної,
                // стінка лише з боку жолоба, з іншого рейка впирається в рейку
                double cut = rail.Track == 0 ? groove : groove + head;
                for (int n = 0; n < 2; n++) Assert.InRange(Gap(rail, heads[n].To, heads[n + 1].From), cut / sin - 0.01, cut / sin + 0.01);
                if (rail.Track == 0) Assert.True(heads[0].CapTo && heads[1].CapFrom && heads[1].CapTo && heads[2].CapFrom);
                else Assert.True(heads[1].CapFrom && heads[1].CapTo && !heads[0].CapTo && !heads[2].CapFrom);

                // Між двома розривами: ширина колії без головки і двох жолобів
                double between = rail.Owner.TrackWidth - head - 2 * groove;
                Assert.InRange(Gap(rail, heads[1].From, heads[1].To), between / sin - 0.01, between / sin + 0.01);

                // Підошва і шийка основної рейки суцільні. Другої обриваються на рейці основної, без торців,
                // і шийка доходить далі за підошву: ззовні до шийки основної рейки
                List<RailSpan> feet = Sorted(rail, RailPiece.Foot), webs = Sorted(rail, RailPiece.Web);
                Assert.Equal(rail.Track == 0 ? 1 : 3, feet.Count);
                Assert.Equal(rail.Track == 0 ? 1 : 3, webs.Count);
                if (rail.Track == 1)
                {
                    for (int n = 0; n < 2; n++)
                    {
                        Assert.InRange(Gap(rail, feet[n].To, feet[n + 1].From), foot / sin - 0.01, foot / sin + 0.01);
                        Assert.InRange(Gap(rail, webs[n].To, webs[n + 1].From), (head + groove) / sin - 0.01, (head + groove) / sin + 0.01);
                        Assert.False(feet[n].CapTo || feet[n + 1].CapFrom || webs[n].CapTo || webs[n + 1].CapFrom);
                    }
                    Assert.True(webs[0].To.Middle > feet[0].To.Middle && webs[2].From.Middle < feet[2].From.Middle);
                }

                // Починається й кінчається на межі секцій, рівним краєм
                Assert.Equal(0, heads[0].From.Minus % 2);
                Assert.Equal(heads[0].From.Minus, heads[0].From.Plus);
                Assert.Equal(0, heads[2].To.Plus % 2);
            }

            // Під кутом край розриву косий: з двох боків рейки він на різній відстані вздовж неї
            CrossingRail any = rails.Rails[0];
            RailEdge wall = Sorted(any, RailPiece.Head)[0].To;
            double skew = RouteBuilder.HorizontalDistance(Where(any, wall.Plus), Where(any, wall.Minus));
            double expectedSkew = Math.Abs(2 * CrossingGeometry.EdgeHalf / Math.Tan(angle * Math.PI / 180));
            Assert.InRange(skew, expectedSkew - 0.01, expectedSkew + 0.01);
        }

        [Theory]
        [InlineData(90.0)]
        [InlineData(30.0)]
        public void OrdinaryRailsAreHiddenOnlyWhereTheCrossingRailsReplaceThem(double angle)
        {
            CrossingRails rails = LayRails(angle);

            foreach (List<Section> track in new[] { main, other })
            {
                int hidden = 0;
                foreach (Section section in track)
                {
                    bool first = rails.Hides(section, SectionPart.FirstRail), second = rails.Hides(section, SectionPart.SecondRail);
                    if (first || second) hidden++;
                }
                // Перетнута секція і трохи довкола, а не вся колія
                Assert.True(hidden >= 2 && hidden < track.Count / 2, "сховано секцій: " + hidden);
                Assert.False(rails.Hides(track[0], SectionPart.FirstRail) || rails.Hides(track[0], SectionPart.SecondRail));
            }
            Assert.True(rails.Hides(mainCenter, SectionPart.FirstRail) || rails.Hides(mainCenter, SectionPart.SecondRail));
        }

        [Theory]
        [InlineData(90.0)]
        [InlineData(70.0)]
        [InlineData(45.0)]
        [InlineData(20.0)]
        public void EveryRunningRailGetsAGuardRailThroughTheCrossingAtAnyAngle(double angle)
        {
            CrossingRails rails = LayRails(angle);
            double sin = Math.Sin(angle * Math.PI / 180);
            double head = RailProfile.HeadHalfWidth, groove = SwitchGeometry.FrogFlangeway;
            double line = mainCenter.TrackWidth / 2 - 2 * head - groove;

            foreach (CrossingRail rail in rails.Rails)
            {
                // Три шматки: на одному плечі, всередині ромба, на другому плечі
                List<RailSpan> heads = Sorted(rail, RailPiece.GuardHead), webs = Sorted(rail, RailPiece.GuardWeb), feet = Sorted(rail, RailPiece.GuardFoot);
                Assert.Equal(3, heads.Count);
                Assert.Equal(3, webs.Count);
                Assert.Equal(3, feet.Count);

                // На плечах до рейки іншої колії впритул, без торця; вільний кінець зрізано під прямим кутом,
                // з торцем. Найкоротший бік контррейки пів блока; під гострим кутом другий бік довший, бо ближній
                // кінець косий
                double skew = 0.45 / Math.Tan(angle * Math.PI / 180);
                Assert.InRange(Gap(rail, heads[0].From, heads[0].To), CrossingGeometry.GuardLeg - 0.01, CrossingGeometry.GuardLeg + skew + 0.05);
                Assert.InRange(Gap(rail, heads[2].From, heads[2].To), CrossingGeometry.GuardLeg - 0.01, CrossingGeometry.GuardLeg + skew + 0.05);
                Assert.Equal(heads[0].From.Minus, heads[0].From.Plus);
                Assert.Equal(heads[2].To.Minus, heads[2].To.Plus);

                // Рівний торець ніде не перетинається з косим зрізом ближнього кінця
                double outer = rail.Side * RailProfile.FootHalfWidth, inner = -rail.Side * CrossingGeometry.Reach;
                Assert.True(heads[0].From.Plus <= Math.Min(heads[0].To.At(outer), heads[0].To.At(inner)));
                Assert.True(heads[2].To.Plus >= Math.Max(heads[2].From.At(outer), heads[2].From.At(inner)));
                Assert.True(heads[0].CapFrom && heads[2].CapTo && !heads[0].CapTo && !heads[2].CapFrom);

                // Усередині: контррейки основної колії на всю раму, другої між ними
                double reach = rail.Track == 0 ? line + head : line - head;
                Assert.InRange(Gap(rail, heads[1].From, heads[1].To), 2 * reach / sin - 0.01, 2 * reach / sin + 0.01);

                // Розрив між шматками: рейка іншої колії з жолобом лише з внутрішнього боку
                double gap = (rail.Track == 0 ? 2 * head + groove : 4 * head + groove) / sin;
                Assert.InRange(Gap(rail, heads[0].To, heads[1].From), gap - 0.01, gap + 0.01);

                // Проставка лежить під усім цим: від кінця однієї ніжки до кінця другої
                List<RailSpan> spacers = Sorted(rail, RailPiece.Spacer);
                Assert.Equal(rail.Track == 0 ? 1 : 3, spacers.Count);
                Assert.Equal(heads[0].From.Middle, spacers[0].From.Middle, 6);
                Assert.Equal(heads[2].To.Middle, spacers[spacers.Count - 1].To.Middle, 6);
                Assert.True(spacers[0].CapFrom && spacers[spacers.Count - 1].CapTo);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(-1)]
        public void OutlineGoesAroundTheWholeProfileWithNothingInside(int side)
        {
            var boxes = new List<double[]>();
            double area = 0;
            for (int piece = 0; piece < CrossingGeometry.PieceCount; piece++)
            {
                double[] box = CrossingGeometry.Box((RailPiece)piece, side);
                boxes.Add(box);
                area += (box[1] - box[0]) * (box[3] - box[2]);
            }

            // Ходова рейка, проставка і контррейка разом це одна фігура з одним контуром
            List<double[]> outline = Assert.Single(CrossingGeometry.Outline(boxes));
            Assert.Equal(outline[0][0], outline[outline.Count - 1][0]);
            Assert.Equal(outline[0][1], outline[outline.Count - 1][1]);

            // Обходить за годинниковою стрілкою, від лівого нижнього кута вгору, і охоплює рівно всі складові
            double signed = 0;
            for (int n = 0; n + 1 < outline.Count; n++) signed += outline[n][0] * outline[n + 1][1] - outline[n + 1][0] * outline[n][1];
            Assert.InRange(-signed / 2, area - 1e-9, area + 1e-9);
            Assert.Equal(0, outline[0][1], 9);
            Assert.True(outline[1][1] > outline[0][1] && outline[1][0] == outline[0][0]);

            // Стінки між шийкою рейки і проставкою немає: з внутрішнього боку шийки контур не проходить
            double web = RailProfile.WebHalfWidth;
            for (int n = 0; n + 1 < outline.Count; n++)
            {
                bool vertical = outline[n][0] == outline[n + 1][0];
                if (vertical && Math.Abs(Math.Abs(outline[n][0]) - web) < 1e-9)
                    Assert.True(outline[n][0] * side > 0, "стінка між шийкою рейки і проставкою лишилася");
            }
        }

        [Fact]
        public void WithoutGuardRailTheSpacerIsClosedFromBelow()
        {
            // Ромб поза тупими кутами: ходова рейка з проставкою, контррейки поруч немає
            var boxes = new List<double[]>();
            foreach (RailPiece piece in new[] { RailPiece.Foot, RailPiece.Web, RailPiece.Head, RailPiece.Spacer })
                boxes.Add(CrossingGeometry.Box(piece, 1));

            List<double[]> outline = Assert.Single(CrossingGeometry.Outline(boxes));
            // Проставка виступає за підошву рейки: її низ видно, тож контур проходить і під нею
            double overhang = 0;
            for (int n = 0; n + 1 < outline.Count; n++)
            {
                bool low = Math.Abs(outline[n][1] - RailProfile.FootTop) < 1e-9 && Math.Abs(outline[n + 1][1] - RailProfile.FootTop) < 1e-9;
                if (low && outline[n][0] < -RailProfile.FootHalfWidth + 1e-9 && outline[n + 1][0] < -RailProfile.FootHalfWidth + 1e-9)
                    overhang += Math.Abs(outline[n + 1][0] - outline[n][0]);
            }
            Assert.True(overhang > 0.01, "низ проставки за підошвою рейки не замкнений");
        }

        [Theory]
        [InlineData(90.0)]
        [InlineData(25.0)]
        public void WholeCrossingIsBuiltFromWholeContours(double angle)
        {
            CrossingRails rails = LayRails(angle);
            int parts = 0;
            foreach (CrossingRail rail in rails.Rails)
            {
                foreach (LoftPart part in CrossingPartsBuilder.Parts(rail))
                {
                    parts++;
                    Assert.True(part.Stations.Count >= 2);
                    int points = part.Stations[0].Points.Length;
                    foreach (LoftStation station in part.Stations) Assert.Equal(points, station.Points.Length);
                }
            }
            Assert.True(parts >= 12);
        }

        [Theory]
        [InlineData(90.0)]
        [InlineData(30.0)]
        public void SecondTrackRailIsAbsentWhereItBreaksOnTheMainRail(double angle)
        {
            CrossingRails rails = LayRails(angle);

            foreach (CrossingRail rail in rails.Rails)
            {
                List<RailSpan> feet = Sorted(rail, RailPiece.Foot);
                Vec3d along = rail.Frames[rail.Frames.Count / 2].Along;

                // Посередині кожного шматка підошви рейка є; далеко за вузлом теж, там звичайна рейка секції
                foreach (RailSpan span in feet) Assert.True(rails.HasRail(Where(rail, (span.From.Middle + span.To.Middle) / 2), along));
                Assert.True(rails.HasRail(Where(rail, 1.3), along));

                // У розриві рейки другої колії її немає; в основної розривів підошви немає зовсім
                for (int n = 0; n + 1 < feet.Count; n++)
                    Assert.False(rails.HasRail(Where(rail, (feet[n].To.Middle + feet[n + 1].From.Middle) / 2), along), "рейка знайшлася там, де вона обірвана");
                Assert.Equal(rail.Track == 0 ? 1 : 3, feet.Count);
            }
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
