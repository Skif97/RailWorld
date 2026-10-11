using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Габарит колії: прямокутник секції в плані завширшки зі шпалу плюс запас. Ним відповідають на питання,
    /// чи лежать дві колії одна на одній.
    /// </summary>
    public class FootprintTests : TrackTestBase
    {
        private static bool Overlap(double ax, double az, double bx, double bz, double cx, double cz, double dx, double dz)
        {
            return Footprint.Overlap(new Vec3d(ax, 0, az), new Vec3d(bx, 0, bz), Footprint.StandardWidth,
                new Vec3d(cx, 0, cz), new Vec3d(dx, 0, dz), Footprint.StandardWidth, out _, out _);
        }

        [Fact]
        public void ParallelTracksOverlapUntilTheirFootprintsPart()
        {
            // Дві паралельні секції. Габарит це шпала 1.75 плюс запас 0.2, тобто 1.95 блока:
            // колії з осями ближче за це лежать одна на одній, далі вже ні
            Assert.True(Overlap(0, 0, 0.5, 0, 0, 1.9, 0.5, 1.9));
            Assert.False(Overlap(0, 0, 0.5, 0, 0, 2.0, 0.5, 2.0));

            // Між кінцями самих шпал при цьому ще лишається щілина: запас її перекриває
            Assert.True(1.9 > TrackGauge.StandardSleeperLength);
        }

        [Fact]
        public void SectionsFarAlongTheTrackDoNotOverlap()
        {
            // Та сама вісь, але одна секція далі вздовж колії
            Assert.False(Overlap(0, 0, 0.5, 0, 0.6, 0, 1.1, 0));
            Assert.True(Overlap(0, 0, 0.5, 0, 0.4, 0, 0.9, 0));
        }

        [Fact]
        public void CrossingAtAnAngleIsFoundByItsCorner()
        {
            // Секція впоперек, яка осі першої не перетинає і центром до неї не дістає, але кутом габариту заходить
            Assert.True(Overlap(0, 0, 0.5, 0, 1.3, -1, 1.3, 1));
            Assert.False(Overlap(0, 0, 0.5, 0, 1.6, -1, 1.6, 1));
        }

        [Fact]
        public void MiddleOfTheOverlapLiesInsideBothFootprints()
        {
            Assert.True(Footprint.Overlap(new Vec3d(0, 0, 0), new Vec3d(0.5, 0, 0), Footprint.StandardWidth,
                new Vec3d(0, 0, 1.5), new Vec3d(0.5, 0, 1.5), Footprint.StandardWidth, out double x, out double z));
            Assert.InRange(x, 0, 0.5);
            // Спільна смуга від 1.5 - 0.975 до 0.975
            Assert.InRange(z, 0.525, 0.975);
        }

        [Fact]
        public void NeighboursOnACurveAreOneTrackNotTwo()
        {
            // На крутій дузі габарити сусідніх секцій заходять один на одного кутами зсередини повороту.
            // Це та сама колія: такі секції розпізнаються за спільним стиком
            double angle = Math.PI / 2;
            List<Section> arc = Lay(Point(0, 0, 1, 0),
                Point(RouteRules.MinRadius * Math.Sin(angle), RouteRules.MinRadius * (1 - Math.Cos(angle)), Math.Cos(angle), Math.Sin(angle)));

            for (int i = 0; i + 1 < arc.Count; i++)
            {
                Assert.True(Footprint.Joined(arc[i], arc[i + 1]));
                // А через одну вже ні стику, ні перекриття: інакше крута дуга лягала б сама на себе
                if (i + 2 < arc.Count)
                {
                    Assert.False(Footprint.Joined(arc[i], arc[i + 2]));
                    Assert.False(Footprint.Overlap(arc[i], arc[i + 2]), "секції " + i + " і " + (i + 2) + " найкрутішої дуги перекриваються");
                }
            }
        }

        [Fact]
        public void SwitchBodyEndsWhereTheFootprintsPart()
        {
            // Пряма і дуга радіуса 20 з одного стику. Осі розходяться на ширину габариту, 1.95 блока,
            // там, де 20 * (1 - cos a) = 1.95, тобто за 8.7 блока від стику, це секція 17 чи 18
            List<Section> trunk = Trunk();
            Section last = trunk[trunk.Count - 1];
            List<Section> straight = Straight(), arc = Arc();
            Join(last, straight[0]);
            Join(last, arc[0]);

            int onStraight = SwitchZone.OverlapSections(last, false, arc[0], Resolve);
            int onArc = SwitchZone.OverlapSections(last, false, straight[0], Resolve);
            Assert.InRange(onStraight, 17, 19);
            Assert.InRange(onArc, 17, 19);

            // Остання секція тіла ще лежить на сусідній гілці, перша за ним уже ні
            Assert.Contains(straight, section => Footprint.Overlap(arc[onStraight - 1], section));
            Assert.DoesNotContain(straight, section => Footprint.Overlap(arc[onStraight], section));
        }
    }
}
