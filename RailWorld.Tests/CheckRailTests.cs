using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Контррейка стрілки (SwitchGeometry.CheckRail): стоїть навпроти розриву хрестовини з запасом на ширину колії, і
    /// кріплення ходової рейки з її боку стоять рівно там, де вона лежить (CheckRailLine.Beside).
    /// </summary>
    public class CheckRailTests : TrackTestBase
    {
        // Колія заданої ширини по маршруту, покладена і зв'язана
        private List<Section> LayGauge(TrackGauge gauge, params RoutePoint[] route)
        {
            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(new List<RoutePoint>(route), null, out _, gauge: gauge);
            var track = new List<Section>();
            for (int i = 0; i + 2 < points.Count; i += 2) track.Add(new Section(points[i], points[i + 1], points[i + 2], gauge.Width, gauge.SleeperLength));
            return Lay(track);
        }

        // Стрілка: спільна колія із заходу, пряма гілка на схід і дуга радіуса radius на 60 градусів. Гілка k
        private (BranchGeometry own, SwitchBranch branch, double width) Switch(TrackGauge gauge, double radius, int k)
        {
            List<Section> trunk = LayGauge(gauge, Point(-8, 0, 1, 0), Point(0, 0, 1, 0));
            List<Section> straight = LayGauge(gauge, Point(0, 0, 1, 0), Point(40, 0, 1, 0));
            double angle = Math.PI / 3;
            List<Section> arc = LayGauge(gauge, Point(0, 0, 1, 0), Point(radius * Math.Sin(angle), radius * (1 - Math.Cos(angle)), Math.Cos(angle), Math.Sin(angle)));
            Section last = trunk[trunk.Count - 1];
            Join(last, straight[0]);
            Join(last, arc[0]);
            RailSwitch built = SwitchBuilder.Build(last, false, Resolve);
            SwitchBranch branch = built.Branches[k], other = built.Branches[1 - k];
            Section first = k == 0 ? straight[0] : arc[0], partner = k == 0 ? arc[0] : straight[0];
            BranchGeometry own = SwitchGeometry.Branch(first, true, branch.InnerIsFirst(1).Value, partner, true, other.InnerIsFirst(1).Value, Resolve);
            return (own, branch, gauge.Width);
        }

        [Theory]
        [InlineData("narrow", 20, 3)]
        [InlineData("broad", 30, 6)]
        public void CheckRailStandsAcrossTheGapWithTheTrackWidthOnBothSides(string code, double radius, int margin)
        {
            // Розрив хрестовини від Throat до Tip; контррейка на ширину колії (margin перерізів) далі з кожного боку,
            // однаково до стрілки і до гілок
            TrackGauge gauge = code == "broad" ? TrackGauge.Broad : TrackGauge.Narrow;
            (BranchGeometry own, SwitchBranch branch, double width) = Switch(gauge, radius, 1);
            CheckRailLine line = SwitchGeometry.CheckRail(own, gauge, branch.BladeSections);
            Assert.NotNull(line);
            // Запас до стрілки не впирається в гостряк, тож він той самий, що й до гілок
            Assert.True(own.Throat - margin > branch.BladeSections * 2, "запас до стрілки обрізано гостряком");
            Assert.Equal(own.Tip - own.Throat + 2 * margin + 1, line.Axis.Count);
        }

        [Theory]
        [InlineData("narrow", 4)]
        [InlineData("broad", 14)]
        public void CheckRailGoesOverTheBladeOnlyAsFarAsTheGaugeAllows(string code, double radius)
        {
            // Крута стрілка: запас до стрілки впирається в ділянку гостряка. На широкій колії контррейка заходить на неї
            // не далі, ніж на TrackGauge.CheckRailOverBlade (блок), на вузькій не заходить зовсім
            TrackGauge gauge = code == "broad" ? TrackGauge.Broad : TrackGauge.Narrow;
            (BranchGeometry own, SwitchBranch branch, double width) = Switch(gauge, radius, 1);
            CheckRailLine line = SwitchGeometry.CheckRail(own, gauge, branch.BladeSections);
            int margin = (int)Math.Round(width / RouteCurve.PieceLength);
            Assert.True(own.Throat - margin <= branch.BladeSections * 2, "запас до стрілки тут на гостряк не впирається, тест нічого не перевіряє");

            int first = own.Tip + margin - (line.Axis.Count - 1);
            int bladeEnd = branch.BladeSections * 2;
            Assert.Equal(bladeEnd + 1 - (int)Math.Round(gauge.CheckRailOverBlade / RouteCurve.PieceLength), first);
        }

        [Theory]
        [InlineData("narrow", 20)]
        [InlineData("broad", 30)]
        public void CheckRailIsAcrossTheTrackFromItsInnerRail(string code, double radius)
        {
            // Кожна точка контррейки лежить від своєї точки на внутрішній рейці строго впоперек колії, там, де друге
            // колесо тієї самої осі, а не вздовж косого спільного перерізу двох колій
            TrackGauge gauge = code == "broad" ? TrackGauge.Broad : TrackGauge.Narrow;
            (BranchGeometry own, SwitchBranch branch, double width) = Switch(gauge, radius, 1);
            CheckRailLine line = SwitchGeometry.CheckRail(own, gauge, branch.BladeSections);
            for (int i = 0; i < line.Axis.Count; i++)
            {
                RailFrame inner = line.Inner[i];
                Vec3d axis = line.Axis[i];
                double along = (axis.X - inner.Center.X) * inner.Along.X + (axis.Z - inner.Center.Z) * inner.Along.Z;
                Assert.True(Math.Abs(along) < 1e-6, "контррейка на перерізі " + i + " зсунута вздовж колії на " + along.ToString("0.000"));
            }
        }

        [Theory]
        [InlineData("narrow", 20)]
        [InlineData("broad", 30)]
        public void GuardSpikesStandOnlyWhereTheCheckRailLies(string code, double radius)
        {
            TrackGauge gauge = code == "broad" ? TrackGauge.Broad : TrackGauge.Narrow;
            (BranchGeometry own, SwitchBranch branch, double width) = Switch(gauge, radius, 1);
            CheckRailLine line = SwitchGeometry.CheckRail(own, gauge, branch.BladeSections);

            // Точка на осі зовнішньої рейки навпроти перерізу i контррейки, зсунута на along уздовж неї: від осі
            // контррейки поперек неї на GuardAxis у бік від колії (біля відігнутих кінців ще на відгин далі)
            int n = line.Axis.Count;
            Vec3d Outer(int i, double along)
            {
                int segment = Math.Clamp(i, 1, n - 3);
                Vec3d a = line.Axis[segment], b = line.Axis[segment + 1];
                double dx = b.X - a.X, dz = b.Z - a.Z, length = Math.Sqrt(dx * dx + dz * dz);
                dx /= length;
                dz /= length;
                double px = -dz, pz = dx;
                Vec3d axis = line.Axis[i], inner = line.Inner[i].Center;
                if (px * (axis.X - inner.X) + pz * (axis.Z - inner.Z) < 0) { px = -px; pz = -pz; }
                double aside = CrossingGeometry.GuardAxis + (i == 0 || i == n - 1 ? 0.04 : 0);
                return new Vec3d(axis.X + px * aside + dx * along, axis.Y, axis.Z + pz * aside + dz * along);
            }

            Assert.Equal(FasteningKind.GuardSpikes, line.Beside(Outer(n / 2, 0)));
            // Біля відігнутого кінця лише ззовні, а за кінцем контррейки її немає зовсім
            Assert.Equal(FasteningKind.OuterSpikes, line.Beside(Outer(n - 1, -0.05)));
            Assert.Null(line.Beside(Outer(n - 1, 0.3)));
            Assert.Null(line.Beside(Outer(0, -0.3)));
        }
    }
}
