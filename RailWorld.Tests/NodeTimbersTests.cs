using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Шпали стрілки: основна колія подовжує свої шпали під відгалуження, а те кладе власні, щойно вони
    /// перестають зачіпати шпали основної. Пряма гілка йде на схід, дуга відходить у бік +Z; її центр
    /// у точці (0, радіус).
    /// </summary>
    public class NodeTimbersTests : TrackTestBase
    {
        private const double Sleeper = TrackGauge.StandardSleeperLength;
        private List<Section> straight, arc;

        private TimberLayout LaySwitch(double radius = ArcRadius, Vec3d lever = null)
        {
            List<Section> trunk = Trunk();
            Section last = trunk[trunk.Count - 1];
            straight = Straight();
            double angle = Math.PI / 3;
            arc = Lay(Point(0, 0, 1, 0), Point(radius * Math.Sin(angle), radius * (1 - Math.Cos(angle)), Math.Cos(angle), Math.Sin(angle)));
            Join(last, straight[0]);
            Join(last, arc[0]);
            return NodeTimbers.Lay(straight, arc, lever);
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

        // Найменша відстань у плані від точки до відрізка
        private static double Distance(Vec3d p, Vec3d a, Vec3d b)
        {
            double dx = b.X - a.X, dz = b.Z - a.Z;
            double t = GameMath.Clamp(((p.X - a.X) * dx + (p.Z - a.Z) * dz) / (dx * dx + dz * dz), 0, 1);
            double x = a.X + dx * t - p.X, z = a.Z + dz * t - p.Z;
            return Math.Sqrt(x * x + z * z);
        }

        // Два кінці власної шпали секції
        private static void Ends(Section section, out Vec3d from, out Vec3d to)
        {
            Vec3d start = section.FullStartPosition, end = section.FullEndPosition;
            Vec3f normal = section.CenterNormal;
            double mx = (start.X + end.X) / 2, mz = (start.Z + end.Z) / 2;
            from = new Vec3d(mx - normal.X * Sleeper / 2, 0, mz - normal.Z * Sleeper / 2);
            to = new Vec3d(mx + normal.X * Sleeper / 2, 0, mz + normal.Z * Sleeper / 2);
        }

        [Theory]
        [InlineData(ArcRadius)]
        [InlineData(8.0)]
        [InlineData(3.7)]
        public void MainTrackKeepsItsSleepersAndStretchesThemUnderTheBranch(double radius)
        {
            TimberLayout layout = LaySwitch(radius);
            Assert.True(layout.Timbers.Count >= 5);

            double longest = 0;
            for (int i = 0; i < layout.Timbers.Count; i++)
            {
                Timber timber = layout.Timbers[i];
                // Упоперек прямої колії, на місці її шпали, з її боку звичайної довжини
                Assert.True(Math.Abs(timber.Across.X) < 0.01, "брус " + i + " лежить косо до основної колії");
                Assert.InRange(timber.Position.X, 0.25 + 0.5 * i - 0.02, 0.25 + 0.5 * i + 0.02);
                Assert.InRange(End(timber, -1).Z, -Sleeper / 2 - 0.02, -Sleeper / 2 + 0.02);
                Assert.Same(straight[i], timber.Owner);
                Assert.True(timber.Length >= Sleeper - 0.01);
                longest = Math.Max(longest, timber.Length);
            }

            // Біля стику колії ще разом: брус як звичайна шпала. Далі він дістає до зовнішнього краю шпал дуги:
            // пряма лежить зовні дуги, тож цей край з боку її центра
            Assert.InRange(layout.Timbers[0].Length, Sleeper, Sleeper + 0.1);
            Vec3d onArc = End(layout.Timbers[3], 1);
            double fromCentre = Math.Sqrt(onArc.X * onArc.X + (onArc.Z - radius) * (onArc.Z - radius));
            Assert.InRange(fromCentre, radius - Sleeper / 2 - 0.05, radius - Sleeper / 2 + 0.05);

            // Останній брус вкорочений: поступається місцем першій шпалі відгалуження
            Assert.True(longest > Sleeper + 0.5, "жоден брус не тримає відгалуження");
            Assert.True(layout.Timbers[layout.Timbers.Count - 1].Length < longest - 0.2, "останній брус не вкорочений");
        }

        [Theory]
        [InlineData(ArcRadius)]
        [InlineData(8.0)]
        [InlineData(3.7)]
        public void BranchLaysItsOwnFullSleepersAsSoonAsTheyClearTheMainTrack(double radius)
        {
            TimberLayout layout = LaySwitch(radius);

            // Сховано початок дуги, без пропусків
            int own = 0;
            while (own < arc.Count && layout.Hidden.Contains(arc[own])) own++;
            Assert.True(own >= 3);
            for (int i = own; i < arc.Count; i++) Assert.DoesNotContain(arc[i], layout.Hidden);

            // Перша власна шпала дуги не зачіпає шпал прямої колії, а попередня ще зачіпала б
            Ends(arc[own], out Vec3d from, out Vec3d to);
            Assert.True(Math.Min(from.Z, to.Z) >= Sleeper / 2 - 0.2, "перша шпала відгалуження лежить на шпалах основної колії");
            Ends(arc[own - 1], out from, out to);
            Assert.True(Math.Min(from.Z, to.Z) < Sleeper / 2 + NodeTimbers.ClipGap + 0.15, "відгалуження лишилося без шпали, хоч місце для неї вже було");

            // Жоден брус не лягає на власні шпали дуги
            for (int i = own; i < arc.Count; i++)
            {
                Ends(arc[i], out from, out to);
                foreach (Timber timber in layout.Timbers)
                {
                    Assert.False(Cross(End(timber, -1), End(timber, 1), from, to), "брус перетинає шпалу відгалуження " + i);
                    Assert.True(Distance(End(timber, 1), from, to) >= SectionBox.SleeperHalfWidth, "брус упирається в шпалу відгалуження " + i);
                }
            }
        }

        [Theory]
        [InlineData(ArcRadius)]
        [InlineData(3.7)]
        public void MainSectionsUnderBeamsLoseTheirOwnSleepers(double radius)
        {
            TimberLayout layout = LaySwitch(radius);

            // Під кожним брусом сховано шпалу прямої колії, і більше нічого: порожніх місць під рейками немає
            for (int i = 0; i < straight.Count; i++)
            {
                if (i < layout.Timbers.Count) Assert.Contains(straight[i], layout.Hidden);
                else Assert.DoesNotContain(straight[i], layout.Hidden);
            }
        }

        [Fact]
        public void MainTrackIsTheStraighterOneWhicheverComesFirst()
        {
            TimberLayout layout = LaySwitch();
            TimberLayout swapped = NodeTimbers.Lay(arc, straight);

            Assert.Equal(layout.Timbers.Count, swapped.Timbers.Count);
            for (int i = 0; i < layout.Timbers.Count; i++)
            {
                Assert.Same(layout.Timbers[i].Owner, swapped.Timbers[i].Owner);
                Assert.Equal(layout.Timbers[i].Length, swapped.Timbers[i].Length, 6);
            }
        }

        [Theory]
        [InlineData(ArcRadius)]
        [InlineData(8.0)]
        [InlineData(3.7)]
        public void SleeperBesideTheFirstBranchSleeperIsStretchedOnlyIfItReachesTheRail(double radius)
        {
            TimberLayout layout = LaySwitch(radius);
            int own = 0;
            while (layout.Hidden.Contains(arc[own])) own++;

            // Де дуга вже має свої шпали, брус або тримає її ближню рейку з хвостиком на кріплення, або його немає
            Vec3d firstOwn = arc[own].FullStartPosition;
            foreach (Timber timber in layout.Timbers)
            {
                if (timber.Position.X < firstOwn.X) continue;
                Vec3d end = End(timber, 1);
                double fromCentre = Math.Sqrt(end.X * end.X + (end.Z - radius) * (end.Z - radius));
                // Ближня до прямої колії рейка дуги лежить зовні: далі від центра на пів ширини колії
                Assert.True(fromCentre <= radius + TrackGauge.StandardWidth / 2 - NodeTimbers.RailTail * 0.5,
                    "подовжена шпала не дістає до рейки відгалуження: до центра " + fromCentre.ToString("0.00"));
            }
        }

        [Theory]
        [InlineData(10.0, 10.0)]
        [InlineData(5.0, 5.0)]
        [InlineData(8.0, 6.0)]
        public void ForkLaysBeamsAcrossTheMiddleLine(double leftRadius, double rightRadius)
        {
            // Дві дуги в різні боки з одного стику
            List<Section> trunk = Trunk();
            Section last = trunk[trunk.Count - 1];
            double angle = Math.PI / 3;
            List<Section> left = Lay(Point(0, 0, 1, 0), Point(leftRadius * Math.Sin(angle), leftRadius * (1 - Math.Cos(angle)), Math.Cos(angle), Math.Sin(angle)));
            List<Section> right = Lay(Point(0, 0, 1, 0), Point(rightRadius * Math.Sin(angle), -rightRadius * (1 - Math.Cos(angle)), Math.Cos(angle), -Math.Sin(angle)));
            Join(last, left[0]);
            Join(last, right[0]);

            TimberLayout layout = NodeTimbers.Lay(left, right);
            Assert.True(layout.Timbers.Count >= 4);

            // Обидві гілки віддали початок під бруси, порівну
            int own = 0;
            while (layout.Hidden.Contains(left[own])) own++;
            Assert.True(own >= 4);
            for (int i = 0; i < left.Count; i++) Assert.Equal(i < own, layout.Hidden.Contains(left[i]));
            for (int i = 0; i < right.Count; i++) Assert.Equal(i < own, layout.Hidden.Contains(right[i]));

            bool symmetric = leftRadius == rightRadius;
            foreach (Timber timber in layout.Timbers)
            {
                // Жодна з гілок не основна: брус лежить упоперек спільного напрямку, а не котроїсь із дуг
                Assert.True(Math.Abs(timber.Across.X) < (symmetric ? 0.02 : 0.2), "брус стоїть косо");
                if (symmetric) Assert.True(Math.Abs(timber.Position.Z) < 0.02, "брус зсунутий убік");

                // І не лягає на власні шпали гілок
                foreach (List<Section> branch in new[] { left, right })
                {
                    for (int i = own; i < branch.Count; i++)
                    {
                        Ends(branch[i], out Vec3d from, out Vec3d to);
                        Assert.False(Cross(End(timber, -1), End(timber, 1), from, to), "брус перетинає шпалу гілки " + i);
                    }
                }
            }

            // Перший брус як звичайна шпала, далі вони тримають обидві гілки
            Assert.InRange(layout.Timbers[0].Length, Sleeper, Sleeper + 0.1);
            Assert.True(layout.Timbers[own / 2].Length > Sleeper + 0.2);
        }

        [Fact]
        public void GentleCurveAgainstASteepOneIsATurnoutNotAFork()
        {
            // Основна колія ледь відвертає в один бік, відгалуження круто в інший: шпали основної лишаються впоперек неї
            List<Section> trunk = Trunk();
            Section last = trunk[trunk.Count - 1];
            double angle = Math.PI / 3, gentleAngle = Math.PI / 12, gentle = 40, steep = 6;
            List<Section> main = Lay(Point(0, 0, 1, 0), Point(gentle * Math.Sin(gentleAngle), -gentle * (1 - Math.Cos(gentleAngle)), Math.Cos(gentleAngle), -Math.Sin(gentleAngle)));
            List<Section> branch = Lay(Point(0, 0, 1, 0), Point(steep * Math.Sin(angle), steep * (1 - Math.Cos(angle)), Math.Cos(angle), Math.Sin(angle)));
            Join(last, main[0]);
            Join(last, branch[0]);

            TimberLayout layout = NodeTimbers.Lay(branch, main);
            Assert.True(layout.Timbers.Count >= 4);
            foreach (Timber timber in layout.Timbers)
            {
                Vec3f normal = timber.Owner.CenterNormal;
                Assert.Contains(timber.Owner, main);
                Assert.True(Math.Abs(timber.Across.X * normal.Z - timber.Across.Z * normal.X) < 0.01, "брус не впоперек основної колії");
            }
        }

        [Fact]
        public void TwoTimbersByTheLeverReachOutToCarryTheMechanism()
        {
            // Важіль стоїть за пів блока від стику, збоку від прямої гілки, з боку -Z
            var lever = new Vec3d(0.5, 0, -(Sleeper / 2 + SectionBox.LeverGap));
            TimberLayout layout = LaySwitch(ArcRadius, lever);

            // Два найближчі бруси виступають за важіль
            for (int i = 0; i < 2; i++)
            {
                Assert.InRange(End(layout.Timbers[i], -1).Z, lever.Z - NodeTimbers.LeverBase - 0.02, lever.Z - NodeTimbers.LeverBase + 0.02);
                Assert.True(layout.Timbers[i].Length > Sleeper + 0.5, "брус біля важеля не помітно довший");
            }
            // Третій уже звичайний
            Assert.InRange(End(layout.Timbers[2], -1).Z, -Sleeper / 2 - 0.02, -Sleeper / 2 + 0.02);
        }
    }
}
