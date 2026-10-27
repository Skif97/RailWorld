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

        // arcFirst: дуга стоїть у записі стрілки першою гілкою, а не другою
        private TimberLayout LaySwitch(double radius = ArcRadius, Vec3d lever = null, bool arcFirst = false)
        {
            List<Section> trunk = Trunk();
            Section last = trunk[trunk.Count - 1];
            straight = Straight();
            double angle = Math.PI / 3;
            arc = Lay(Point(0, 0, 1, 0), Point(radius * Math.Sin(angle), radius * (1 - Math.Cos(angle)), Math.Cos(angle), Math.Sin(angle)));
            Join(last, arcFirst ? arc[0] : straight[0]);
            Join(last, arcFirst ? straight[0] : arc[0]);
            RailSwitch record = SwitchBuilder.Build(last, false, Resolve);
            return arcFirst ? NodeTimbers.Lay(arc, straight, record.Shape, lever) : NodeTimbers.Lay(straight, arc, record.Shape, lever);
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
            TimberLayout swapped = LaySwitch(arcFirst: true);

            // Секції тут нові, тож порівнюємо, де вони лежать
            Assert.Equal(layout.Timbers.Count, swapped.Timbers.Count);
            for (int i = 0; i < layout.Timbers.Count; i++)
            {
                Assert.Equal(0, layout.Timbers[i].Position.DistanceTo(swapped.Timbers[i].Position), 6);
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

            RailSwitch record = SwitchBuilder.Build(last, false, Resolve);
            Assert.Equal(SwitchShape.Fork, record.Shape);
            TimberLayout layout = NodeTimbers.Lay(left, right, record.Shape);
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
        public void CrossoverBranchThatTurnsBackParallelIsStillTheBranch()
        {
            // З'їзд: гілка відходить від прямої і далі знову йде паралельно їй. Від початку до кінця вона не
            // повертає зовсім, але в тілі стрілки вона повертає, а пряма ні: основна пряма
            List<Section> trunk = Trunk();
            Section last = trunk[trunk.Count - 1];
            List<Section> crossover = Lay(Point(0, 0, 1, 0), Point(10, 4, 1, 0));
            List<Section> line = Straight();
            Join(last, crossover[0]);
            Join(last, line[0]);

            Assert.Equal(SwitchShape.MainSecond, SwitchBuilder.Build(last, false, Resolve).Shape);
        }

        [Theory]
        [InlineData(8)]
        [InlineData(12)]
        public void CrossoverToACloseParallelTrackIsTheBranch(double length)
        {
            // З'їзд на паралельну колію за 2.9 блока: тіло стрілки закінчується, коли гілка вже повертає назад
            List<Section> trunk = Trunk();
            Section last = trunk[trunk.Count - 1];
            List<Section> crossover = Lay(Point(0, 0, 1, 0), Point(length, 2.9, 1, 0));
            List<Section> line = Straight();
            Join(last, crossover[0]);
            Join(last, line[0]);

            Assert.Equal(SwitchShape.MainSecond, SwitchBuilder.Build(last, false, Resolve).Shape);
        }

        [Theory]
        [InlineData(2.9, 8)]
        [InlineData(2.9, 12)]
        [InlineData(-2.9, 8)]
        [InlineData(-2.9, 12)]
        public void CrossoverOnACurveToAConcentricTrackIsTheBranch(double offset, double length)
        {
            // Основна колія дугою радіуса 20 на -Z, з'їзд на концентричну колію за offset блоків: додатний назовні
            // кривої, від'ємний усередину. У кінці тіла відвертає і основна колія, а з'їзд уже вирівнюється
            const double radius = 20;
            // Спільна колія теж на цій дузі: крива вся лінія, а не лише колія за стрілкою
            double b = Math.PI / 6;
            List<Section> trunk = Lay(Point(-radius * Math.Sin(b), -radius * (1 - Math.Cos(b)), Math.Cos(b), Math.Sin(b)), Point(0, 0, 1, 0));
            Section last = trunk[trunk.Count - 1];
            double a = Math.PI / 3;
            List<Section> main = Lay(Point(0, 0, 1, 0), Point(radius * Math.Sin(a), -radius * (1 - Math.Cos(a)), Math.Cos(a), -Math.Sin(a)));
            double outer = radius + offset, phi = length / outer;
            List<Section> crossover = Lay(Point(0, 0, 1, 0), Point(outer * Math.Sin(phi), -radius + outer * Math.Cos(phi), Math.Cos(phi), -Math.Sin(phi)));
            Join(last, crossover[0]);
            Join(last, main[0]);

            Assert.Equal(SwitchShape.MainSecond, SwitchBuilder.Build(last, false, Resolve).Shape);
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

            RailSwitch record = SwitchBuilder.Build(last, false, Resolve);
            Assert.Equal(SwitchShape.MainFirst, record.Shape);
            TimberLayout layout = NodeTimbers.Lay(main, branch, record.Shape);
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
        [Fact]
        public void EveryBeamOfACrossingCarriesARailOfASectionItReplaces()
        {
            // Перетин з тестового світу: кінці секцій обох колій, як їх записала гра, відносно (513500, 510957).
            // Крайній брус там обрізався об власну шпалу колії до 0,55 блока і лежав між коліями без рейки
            List<Section> main = Through((8.0040, -0.4637), (7.5051, -0.4907), (7.0024, -0.5028), (6.4937, -0.5025), (5.9914, -0.4904), (5.4960, -0.4673), (4.9953, -0.4332), (4.4892, -0.3883), (3.9908, -0.3342), (3.5005, -0.2718), (2.9925, -0.1980), (2.5062, -0.1191), (2.0157, -0.0317), (1.5211, 0.0639), (1.0225, 0.1677), (0.5337, 0.2761), (0.0550, 0.3883), (-0.4407, 0.5105), (-0.9259, 0.6357), (-1.4001, 0.7630), (-1.8911, 0.8996), (-2.3707, 1.0375), (-2.8386, 1.1759), (-3.3229, 1.3230), (-3.7952, 1.4697));
            List<Section> other = Through((-2.5760, 3.9245), (-2.1971, 3.5999), (-1.8135, 3.2740), (-1.4251, 2.9467), (-1.0425, 2.6269), (-0.6559, 2.3059), (-0.2652, 1.9839), (0.1184, 1.6695), (0.5055, 1.3542), (0.8958, 1.0380), (1.2890, 0.7210), (1.6740, 0.4120), (2.0725, 0.0936), (2.4623, -0.2167), (2.8540, -0.5275), (3.2476, -0.8388), (3.6427, -1.1506), (4.0392, -1.4627), (4.4255, -1.7662), (4.8240, -2.0789), (5.2119, -2.3829), (5.6115, -2.6959), (6.0114, -3.0089), (6.3999, -3.3131), (6.7996, -3.6261));

            TimberLayout layout = NodeTimbers.LayCrossing(main, 11, other, 12);

            Assert.NotEmpty(layout.Timbers);
            foreach (Timber timber in layout.Timbers)
            {
                bool carries = false;
                foreach (Section section in layout.Hidden) carries |= CarriesRailOf(timber, section);
                Assert.True(carries, "брус завдовжки " + timber.Length.ToString("0.00") + " не тримає жодної рейки");
            }
        }

        // Чи рейка секції в плані проходить над брусом
        private static bool CarriesRailOf(Timber timber, Section section)
        {
            double half = timber.Length / 2;
            double ax = timber.Position.X - timber.Across.X * half, az = timber.Position.Z - timber.Across.Z * half;
            Vec3d start = section.FullStartPosition, end = section.FullEndPosition;
            Vec3f normal = section.CenterNormal;
            for (int side = -1; side <= 1; side += 2)
            {
                double offset = section.TrackWidth / 2 * side;
                if (PlanLines.Meet(start.X + normal.X * offset, start.Z + normal.Z * offset, end.X - start.X, end.Z - start.Z,
                        ax, az, timber.Across.X * timber.Length, timber.Across.Z * timber.Length, out double t, out double u)
                    && t >= 0 && t <= 1 && u >= 0 && u <= 1) return true;
            }
            return false;
        }
    }
}
