using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Бруси перетину під гострим кутом на схилі: вони лежать на полотні колії, а не рівно.
    /// </summary>
    public class CrossingOnSlopeTests
    {
        private const double Step = 0.25;
        private static readonly Vec3d Origin = new Vec3d(513600, 130, 510800);

        // Схил: що далі на схід (+X), то нижче, на slope блоків за блок. Обидві колії лежать на ньому
        private static double Ground(double slope, double x)
        {
            return Origin.Y - slope * (x - Origin.X);
        }

        // Пряма колія на схилі крізь точку Origin, у плані під кутом angleDeg до осі X
        private static List<Section> Track(double slope, double angleDeg, double offset)
        {
            double angle = angleDeg * Math.PI / 180, cos = Math.Cos(angle), sin = Math.Sin(angle);
            // Уздовж колії схил опускається на slope * cos, ліворуч від руху піднімається на slope * sin
            double along = -slope * cos, length = Math.Sqrt(1 + along * along);
            var tangent = new Vec3f((float)(cos / length), (float)(along / length), (float)(sin / length));
            Vec3f normal = RouteCurve.NormalWithCant(tangent, RouteCurve.CantAbout(tangent, Math.Atan(slope * sin)));

            var points = new List<PointOnBezierCurve>();
            for (int i = 0; i <= 96; i++)
            {
                double run = -12 + offset + i * Step;
                double x = Origin.X + run * cos, z = Origin.Z + run * sin;
                points.Add(new PointOnBezierCurve { position = new Vec3d(x, Ground(slope, x), z), tangent = tangent, normal = normal });
            }

            var track = new List<Section>();
            for (int i = 0; i + 2 < points.Count; i += 2)
                track.Add(new Section(points[i], points[i + 1], points[i + 2], TrackGauge.StandardWidth, TrackGauge.StandardSleeperLength));
            return track;
        }

        private static TimberLayout LayCrossing(double slope, double angleDeg)
        {
            List<Section> main = Track(slope, 0, 0.2), other = Track(slope, angleDeg, 0.2);
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
            Assert.True(mainCenter >= 0, "колії не перетнулися");
            return NodeTimbers.LayCrossing(main, mainCenter, other, otherCenter);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.5)]
        [InlineData(1.0)]
        public void Both_ends_of_every_beam_lie_on_the_slope(double slope)
        {
            TimberLayout layout = LayCrossing(slope, 30);
            Assert.True(layout.Timbers.Count >= 4, "брусів замало: " + layout.Timbers.Count);

            foreach (Timber timber in layout.Timbers)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    double half = timber.Length / 2 * sign;
                    var end = new Vec3d(timber.Position.X + timber.Across.X * half, timber.Position.Y + timber.Across.Y * half, timber.Position.Z + timber.Across.Z * half);
                    Assert.Equal(Ground(slope, end.X), end.Y, 2);
                }
            }
        }

        [Fact]
        public void On_a_slope_there_are_as_many_beams_as_on_the_flat()
        {
            // Бруси замінюють шпали тих самих секцій, що й на рівному, і йдуть тим самим кроком. Крок міряється
            // вздовж схилу: якби в плані, на схилі брусів було б помітно менше, ніж шпал, які вони замінили
            TimberLayout flat = LayCrossing(0, 30), steep = LayCrossing(1, 30);
            Assert.True(Math.Abs(steep.Timbers.Count - flat.Timbers.Count) <= 1, "на рівному брусів " + flat.Timbers.Count + ", на схилі " + steep.Timbers.Count);
        }
    }
}
