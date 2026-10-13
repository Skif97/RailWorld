using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Поверхня полотна колії (TrackSurface): по ній гілка стрілки йде поверх основної колії, поки не зійде з неї.
    /// </summary>
    public class TrackSurfaceTests
    {
        private const double Step = 0.25;
        private static readonly Vec3d Origin = new Vec3d(513600, 130, 510800);

        // Колія, що спускається зі схилу slope (блоків униз на блок шляху) і повертає з радіусом radius.
        // radius 0 це пряма. Нормаль горизонтальна, як у колії без нахилу полотна
        private static List<Section> Track(double slope, double radius, int count)
        {
            var points = new List<PointOnBezierCurve>();
            for (int i = 0; i <= count * 2; i++) points.Add(Point(slope, radius, i * Step));

            var track = new List<Section>();
            for (int i = 0; i + 2 < points.Count; i += 2)
                track.Add(new Section(points[i], points[i + 1], points[i + 2], TrackGauge.StandardWidth, TrackGauge.StandardSleeperLength));
            return track;
        }

        private static PointOnBezierCurve Point(double slope, double radius, double run)
        {
            double angle = radius > 0 ? run / radius : 0;
            double x = radius > 0 ? radius * Math.Sin(angle) : run, z = radius > 0 ? radius * (1 - Math.Cos(angle)) : 0;
            double length = Math.Sqrt(1 + slope * slope);
            return new PointOnBezierCurve
            {
                position = new Vec3d(Origin.X + x, Origin.Y - slope * run, Origin.Z + z),
                tangent = new Vec3f((float)(Math.Cos(angle) / length), (float)(-slope / length), (float)(Math.Sin(angle) / length)),
                normal = new Vec3f((float)-Math.Sin(angle), 0, (float)Math.Cos(angle))
            };
        }

        // Точка збоку від осі на aside блоків, там, де колія пройшла run блоків
        private static Vec3d Beside(double slope, double radius, double run, double aside)
        {
            PointOnBezierCurve on = Point(slope, radius, run);
            return new Vec3d(on.position.X + on.normal.X * aside, 0, on.position.Z + on.normal.Z * aside);
        }

        private static double Height(List<Section> track, int index, Vec3d p)
        {
            Section before = index > 0 ? track[index - 1] : null, after = index + 1 < track.Count ? track[index + 1] : null;
            Assert.True(TrackSurface.At(track[index], before, after, p, 0, 0, out double height, out _));
            return height;
        }

        [Fact]
        public void On_the_axis_the_surface_is_the_track_itself()
        {
            List<Section> track = Track(0.8, 6, 4);
            for (double run = 0.5; run <= 1.5; run += 0.125)
            {
                Vec3d p = Beside(0.8, 6, run, 0);
                Assert.Equal(Origin.Y - 0.8 * run, Height(track, 1, p), 2);
            }
        }

        [Fact]
        public void On_a_straight_track_it_is_the_plane_of_the_section()
        {
            List<Section> track = Track(0.6, 0, 4);
            for (double aside = -0.6; aside <= 0.6; aside += 0.3)
            {
                Vec3d p = Beside(0.6, 0, 0.8, aside);
                Assert.True(RouteCurve.CrossPlane(track[1], p, 1, 0.5, out double planeY, out double planeCant));
                Assert.True(TrackSurface.At(track[1], track[0], track[2], p, 1, 0.5, out double height, out double cant));
                Assert.Equal(planeY, height, 4);
                Assert.Equal(planeCant, cant, 3);
            }
        }

        [Fact]
        public void Beside_a_steep_curve_it_has_no_steps_between_sections()
        {
            // Крутий схил і різкий поворот: тут площини сусідніх секцій збоку від осі розходяться по висоті
            const double slope = 0.8, radius = 6, aside = 0.5;
            List<Section> track = Track(slope, radius, 6);

            double worstSurface = 0, worstPlane = 0;
            double previousSurface = double.NaN, previousPlane = double.NaN;
            const double step = 0.005;
            for (double run = 0.3; run < 2.7; run += step)
            {
                int index = Math.Min((int)(run / (2 * Step)), track.Count - 1);
                Vec3d p = Beside(slope, radius, run, aside);

                double surface = Height(track, index, p);
                Assert.True(RouteCurve.CrossPlane(track[index], p, 0, 0, out double plane, out _));
                if (!double.IsNaN(previousSurface))
                {
                    worstSurface = Math.Max(worstSurface, Math.Abs(surface - previousSurface));
                    worstPlane = Math.Max(worstPlane, Math.Abs(plane - previousPlane));
                }
                previousSurface = surface;
                previousPlane = plane;
            }

            // За один крок колія опускається на slope * step, з запасом на те, що збоку від осі шлях інший
            Assert.True(worstSurface < slope * step * 2, "поверхня має сходинку " + worstSurface);
            // А площини секцій сходинку мають: саме через неї тест і написано
            Assert.True(worstPlane > slope * step * 3, "площини секцій тут мали б розходитися, а найбільший крок " + worstPlane);
        }

        [Theory]
        [InlineData(0.0, 0.3)]
        [InlineData(0.85, 0.45)]
        [InlineData(-0.85, -0.45)]
        [InlineData(0.4, -0.2)]
        public void Normal_with_cant_lies_on_the_deck_even_on_a_slope(double slope, double rise)
        {
            // Полотно: уздовж руху (уздовж +X) піднімається на slope, ліворуч від руху на tan(rise). Ліворуч
            // у моді це +Z: так рахують і CrossPlane, і нормаль точки
            double length = Math.Sqrt(1 + slope * slope);
            var tangent = new Vec3f((float)(1 / length), (float)(slope / length), 0);
            var deckUp = new Vec3d(-slope, 1, -Math.Tan(rise)).Normalize();

            Vec3f normal = RouteCurve.NormalWithCant(tangent, RouteCurve.CantAbout(tangent, rise));

            // Нормаль лежить на полотні: рейка, відсунута вздовж неї від осі, лягає на нього, а не висить над ним
            Assert.Equal(0, normal.X * deckUp.X + normal.Y * deckUp.Y + normal.Z * deckUp.Z, 4);
            // і дивиться ліворуч від руху, туди ж, куди й без нахилу
            Assert.True(normal.Z > 0);
        }

        [Fact]
        public void Near_a_joint_both_sections_give_the_same_height()
        {
            const double slope = 0.8, radius = 6;
            List<Section> track = Track(slope, radius, 6);

            // Точку біля стику маршрут може віднести і до однієї секції, і до сусідньої
            foreach (double run in new[] { 0.97, 1.0, 1.03 })
            {
                foreach (double aside in new[] { -0.5, 0.5 })
                {
                    Vec3d p = Beside(slope, radius, run, aside);
                    Assert.Equal(Height(track, 1, p), Height(track, 2, p), 3);
                }
            }
        }
    }
}
