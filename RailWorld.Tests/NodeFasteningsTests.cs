using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>Скріплення рейок на брусах стрілки і перетину: там, де рейка лежить на брусі.</summary>
    public class NodeFasteningsTests : TrackTestBase
    {
        private static List<RailRun> Rails(params List<Section>[] tracks)
        {
            var rails = new List<RailRun>();
            foreach (List<Section> track in tracks)
            {
                foreach (Section section in track)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        double offset = section.TrackWidth / 2 * side;
                        Vec3d Shifted(Vec3d p, Vec3f n) => new Vec3d(p.X + n.X * offset, p.Y + n.Y * offset, p.Z + n.Z * offset);
                        rails.Add(new RailRun
                        {
                            Section = section, Part = side > 0 ? SectionPart.FirstRail : SectionPart.SecondRail,
                            From = Shifted(section.FullStartPosition, section.StartNormal), Middle = Shifted(section.GetGlobalPos(), section.CenterNormal),
                            To = Shifted(section.FullEndPosition, section.EndNormal)
                        });
                    }
                }
            }
            return rails;
        }

        // Чи точка лежить над брусом, з невеликим запасом
        private static bool OnTimber(Timber timber, Vec3d p)
        {
            double dx = p.X - timber.Position.X, dz = p.Z - timber.Position.Z;
            double lengthwise = dx * timber.Across.X + dz * timber.Across.Z, sideways = -dx * timber.Across.Z + dz * timber.Across.X;
            return Math.Abs(lengthwise) <= timber.Length / 2 + 1e-6 && Math.Abs(sideways) <= SectionBox.SleeperHalfWidth + 1e-6;
        }

        [Fact]
        public void EveryRailOfATurnoutIsFastenedWhereItLiesOnABeam()
        {
            List<Section> straight = Straight();
            double angle = Math.PI / 3, radius = 10;
            List<Section> arc = Lay(Point(0, 0, 1, 0), Point(radius * Math.Sin(angle), radius * (1 - Math.Cos(angle)), Math.Cos(angle), Math.Sin(angle)));
            TimberLayout layout = NodeTimbers.Lay(straight, arc, SwitchShape.MainFirst);
            List<NodeFastening> fastenings = NodeFastenings.Lay(layout.Timbers, Rails(straight, arc));

            // Кожне стоїть на брусі і дивиться вздовж своєї рейки
            foreach (NodeFastening fastening in fastenings)
            {
                Assert.Contains(layout.Timbers, timber => OnTimber(timber, fastening.Position));
                Vec3d chord = fastening.Section.FullEndPosition.SubCopy(fastening.Section.FullStartPosition).Normalize();
                Assert.True(fastening.Along.Dot(chord) > 0.99, "скріплення повернуте не вздовж рейки");
            }

            // Обидві рейки прямої колії на кожному брусі, і рейки дуги теж на тих, що під нею лежать
            int onStraight = 0, onArc = 0;
            foreach (NodeFastening fastening in fastenings)
            {
                if (straight.Contains(fastening.Section)) onStraight++;
                else onArc++;
            }
            Assert.Equal(layout.Timbers.Count * 2, onStraight);
            Assert.True(onArc >= layout.Timbers.Count, "рейки відгалуження майже не скріплені: " + onArc);
        }

        [Theory]
        [InlineData(10.0)]
        [InlineData(4.0)]
        public void NoFasteningHangsOffTheEndOfABeam(double radius)
        {
            List<Section> straight = Straight();
            double angle = Math.PI / 3;
            List<Section> arc = Lay(Point(0, 0, 1, 0), Point(radius * Math.Sin(angle), radius * (1 - Math.Cos(angle)), Math.Cos(angle), Math.Sin(angle)));
            TimberLayout layout = NodeTimbers.Lay(straight, arc, SwitchShape.MainFirst);
            List<NodeFastening> fastenings = NodeFastenings.Lay(layout.Timbers, Rails(straight, arc));
            Assert.True(fastenings.Count >= layout.Timbers.Count * 2);

            // Підкладка може трохи звисати, але всі місця, куди забито костилі, лежать над брусами
            foreach (NodeFastening fastening in fastenings)
            {
                for (int lengthwise = -1; lengthwise <= 1; lengthwise += 2)
                {
                    for (int sideways = -1; sideways <= 1; sideways += 2)
                    {
                        double forward = NodeFastenings.SpikeAlong * lengthwise, aside = NodeFastenings.SpikeAside * sideways;
                        var nail = new Vec3d(fastening.Position.X + fastening.Along.X * forward + fastening.Along.Z * aside, 0,
                            fastening.Position.Z + fastening.Along.Z * forward - fastening.Along.X * aside);
                        Assert.Contains(layout.Timbers, timber => OnTimber(timber, nail));
                    }
                }
            }
        }

        [Fact]
        public void PlatesNeverOverlapWhereRailsComeTogetherSpikesHoldThem()
        {
            List<Section> straight = Straight();
            double angle = Math.PI / 3, radius = 10;
            List<Section> arc = Lay(Point(0, 0, 1, 0), Point(radius * Math.Sin(angle), radius * (1 - Math.Cos(angle)), Math.Cos(angle), Math.Sin(angle)));
            TimberLayout layout = NodeTimbers.Lay(straight, arc, SwitchShape.MainFirst);
            List<NodeFastening> fastenings = NodeFastenings.Lay(layout.Timbers, Rails(straight, arc));

            // Біля стику рейки двох гілок ідуть майже одна по одній: підкладкам там місця немає
            int plates = 0, spikes = 0;
            foreach (NodeFastening fastening in fastenings)
            {
                if (fastening.Kind == FasteningKind.Plate) plates++;
                else if (fastening.Kind == FasteningKind.Spikes) spikes++;
            }
            Assert.True(spikes >= 4, "біля стику стрілки підкладки не поступилися костилям: " + spikes);
            Assert.True(plates >= 4, "підкладок не лишилося зовсім");

            // Жодні дві підкладки не лежать одна на одній
            double reach = Math.Min(NodeFastenings.PlateHalfLength, NodeFastenings.PlateHalfWidth) * 2;
            for (int a = 0; a < fastenings.Count; a++)
            {
                for (int b = a + 1; b < fastenings.Count; b++)
                {
                    if (fastenings[a].Kind != FasteningKind.Plate || fastenings[b].Kind != FasteningKind.Plate) continue;
                    double apart = RouteBuilder.HorizontalDistance(fastenings[a].Position, fastenings[b].Position);
                    Assert.True(apart > reach, "дві підкладки за " + apart.ToString("0.00") + " блока одна від одної");
                }
            }
        }

        [Fact]
        public void RailWithAGuardRailBesideItIsHeldBySpikesOnTheOuterSides()
        {
            List<Section> straight = Straight();
            double angle = Math.PI / 3, radius = 10;
            List<Section> arc = Lay(Point(0, 0, 1, 0), Point(radius * Math.Sin(angle), radius * (1 - Math.Cos(angle)), Math.Cos(angle), Math.Sin(angle)));
            TimberLayout layout = NodeTimbers.Lay(straight, arc, SwitchShape.MainFirst);

            // Контррейка стоїть біля другої рейки прямої колії на кількох секціях
            // На крайніх секціях лежать її відігнуті кінці: вони до бруса не кріпляться
            FasteningKind? Guarded(RailRun rail, Vec3d at)
            {
                int index = straight.IndexOf(rail.Section);
                if (rail.Part != SectionPart.SecondRail || index < 6 || index > 9) return null;
                return index == 6 || index == 9 ? FasteningKind.OuterSpikes : FasteningKind.GuardSpikes;
            }
            List<NodeFastening> fastenings = NodeFastenings.Lay(layout.Timbers, Rails(straight, arc), Guarded);

            int guarded = 0, flared = 0;
            foreach (NodeFastening fastening in fastenings)
            {
                int index = straight.IndexOf(fastening.Section);
                bool beside = fastening.Part == SectionPart.SecondRail && index >= 6 && index <= 9;
                bool end = beside && (index == 6 || index == 9);

                Assert.Equal(beside && !end, fastening.Kind == FasteningKind.GuardSpikes);
                if (end) Assert.Equal(FasteningKind.OuterSpikes, fastening.Kind);
                if (beside && !end) guarded++;
                if (end) flared++;
            }
            Assert.True(guarded >= 2);
            Assert.True(flared >= 1);
        }

        [Fact]
        public void RailLyingAlongTheDeckIsFastenedInTheMiddleOfEachSection()
        {
            // Перетин під прямим кутом: друга колія лежить уздовж брусів щита
            List<Section> main = Lay(Point(-11.8, 0, 1, 0), Point(12.2, 0, 1, 0));
            List<Section> other = Lay(Point(0, -11.8, 0, 1), Point(0, 12.2, 0, 1));
            int mainCenter = -1, otherCenter = -1;
            for (int a = 0; a < main.Count; a++)
            {
                for (int b = 0; b < other.Count; b++)
                {
                    if (!CrossingBuilder.Cross(main[a], other[b])) continue;
                    mainCenter = a;
                    otherCenter = b;
                }
            }
            TimberLayout layout = NodeTimbers.LayCrossing(main, mainCenter, other, otherCenter);
            List<NodeFastening> fastenings = NodeFastenings.Lay(layout.Timbers, Rails(main, other));

            int along = 0, across = 0;
            foreach (NodeFastening fastening in fastenings)
            {
                Assert.Contains(layout.Timbers, timber => OnTimber(timber, fastening.Position));
                if (other.Contains(fastening.Section))
                {
                    // Посередині своєї секції
                    Vec3d middle = fastening.Section.GetGlobalPos();
                    Assert.True(Math.Abs(fastening.Position.Z - middle.Z) < 1e-6);
                    along++;
                }
                else across++;
            }
            Assert.True(along >= 4, "рейки другої колії на щиті не скріплені: " + along);
            // Рейки основної колії на кожному брусі щита
            Assert.Equal(layout.Timbers.Count * 2, across);
        }

        // Брус упоперек діагональної рейки, що проходить через початок координат на схід-північ
        private static Timber AcrossDiagonal(Vec3d at)
        {
            double h = Math.Sqrt(0.5);
            return new Timber { Position = at, Across = new Vec3d(-h, 0, h), Length = TrackGauge.StandardSleeperLength };
        }

        [Fact]
        public void RailCrossingATimberExactlyAtAPieceBoundaryGetsOneFastening()
        {
            // Діагональна рейка з двох секцій; брус лежить рівно на межі двох шматків першої секції і ще один
            // рівно на стику секцій. На діагоналі арифметика неточна, і межа дає похибку в обидва боки
            double h = Math.Sqrt(0.5);
            Vec3d Along(double d) => new Vec3d(d * h, 0, d * h);
            var rails = new List<RailRun>
            {
                new RailRun { From = Along(0), Middle = Along(0.25), To = Along(0.5) },
                new RailRun { From = Along(0.5), Middle = Along(0.75), To = Along(1) }
            };
            var timbers = new List<Timber> { AcrossDiagonal(Along(0.25)), AcrossDiagonal(Along(0.5)) };

            List<NodeFastening> found = NodeFastenings.Lay(timbers, rails);

            Assert.Equal(2, found.Count);
            Assert.Equal(0, found[0].Position.DistanceTo(Along(0.25)), 6);
            Assert.Equal(0, found[1].Position.DistanceTo(Along(0.5)), 6);
        }

        [Fact]
        public void PointPastTheEndOfATiltedTimberIsNotOverIt()
        {
            // Брус довжиною 2 нахилений уздовж себе на 6°: у плані він коротший, його кінець на x = 0.9945
            double tilt = 6 * Math.PI / 180;
            var timber = new Timber { Position = new Vec3d(0, 0, 0), Across = new Vec3d(Math.Cos(tilt), Math.Sin(tilt), 0), Length = 2 };

            Assert.True(NodeFastenings.Under(timber, new Vec3d(0.99 * Math.Cos(tilt), 0.99 * Math.Sin(tilt), 0)));
            // За кінцем бруса, на його ж лінії: у плані x = 0.999, тобто ще в межах довжини 2, але вже не над брусом
            Assert.False(NodeFastenings.Under(timber, new Vec3d(1.0045 * Math.Cos(tilt), 1.0045 * Math.Sin(tilt), 0)));
        }
    }
}
