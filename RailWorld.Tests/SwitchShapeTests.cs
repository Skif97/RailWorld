using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Чи гілки нової стрілки розходяться так, як може стрілка: гостряку є куди лягти, і, розійшовшись, гілки
    /// не сходяться знову. Колія приходить у точку стрілки (0, 0) із заходу.
    /// </summary>
    public class SwitchShapeTests : TrackTestBase
    {
        // Основна колія від точки стрілки на схід: перші чотири блоки прямо, далі повертає на -Z
        private List<Section> LayMainTurningAway()
        {
            List<Section> trunk = Trunk();
            List<Section> main = Lay(Point(0, 0, 1, 0), Point(4, 0, 1, 0), Point(10, -3, 0.7, -0.7));
            Join(trunk[trunk.Count - 1], main[0]);
            return main;
        }

        [Fact]
        public void SwitchFromTheTestWorldWhereBranchesLieOnEachOtherIsRefused()
        {
            // Стрілка з тестового світу: кінці секцій обох гілок, як їх записала гра, відносно стику. Гілки
            // 2,8 блока лежать одна на одній ближче за сантиметр і переходять одна через одну, далі основна
            // повертає в інший бік. Стрілка тут не на своєму місці: колії розходяться на 2,8 блока далі
            (double, double)[] main =
            {
                (0, 0), (-0.425, -0.262), (-0.888, -0.466), (-1.365, -0.604), (-1.856, -0.675), (-2.366, -0.680),
                (-2.863, -0.615), (-3.366, -0.552), (-3.876, -0.562), (-4.379, -0.645), (-4.874, -0.804),
                (-5.327, -1.026), (-5.757, -1.321), (-6.132, -1.669), (-6.447, -2.059), (-6.713, -2.507), (-6.905, -2.974)
            };
            (double, double)[] side =
            {
                (0, 0), (-0.488, -0.294), (-1.017, -0.510), (-1.563, -0.641), (-2.131, -0.687), (-2.690, -0.646),
                (-3.246, -0.518), (-3.764, -0.311), (-4.243, -0.052), (-4.690, 0.236), (-5.115, 0.561),
                (-5.515, 0.926), (-5.878, 1.324), (-6.202, 1.756), (-6.475, 2.215), (-6.696, 2.702)
            };
            List<Section> trunk = Lay(Through((4.25, 2.62), (3.4, 2.096), (2.55, 1.572), (1.7, 1.048), (0.85, 0.524), (0, 0)));
            List<Section> laid = Lay(Through(main));
            Join(trunk[trunk.Count - 1], laid[0]);

            string problem = SectionLinker.FindSwitchProblem(Through(side), SectionsIn, Resolve, out _);
            Assert.NotNull(problem);
            Assert.Contains("розходяться далі по ходу", problem);
        }

        // Колія, що від стику trunkEnd перші три блоки йде точно по кривій маршруту з цих точок, а далі
        // повертає в інший бік. atEnd: стик це остання точка маршруту, а не перша. Повертає місце, де вони
        // розходяться
        private Vec3d LayAlongThenAway(Section trunkEnd, List<RouteMarker> markers, bool atEnd = false)
        {
            List<PointOnBezierCurve> route = RouteCurve.BuildPoints(RouteBuilder.Build(markers));
            if (atEnd) route.Reverse();
            var shared = new (double, double)[7];
            for (int i = 0; i < shared.Length; i++) shared[i] = (route[i * 2].position.X, route[i * 2].position.Z);
            List<Section> together = Lay(Through(shared));
            Vec3d parting = route[12].position;
            Vec3f heading = route[12].tangent;
            if (atEnd) heading = new Vec3f(-heading.X, -heading.Y, -heading.Z);
            List<Section> away = Lay(Point(parting.X, parting.Z, heading.X, heading.Z), Point(parting.X + 6, parting.Z - 3, 0.8, -0.6));
            Join(trunkEnd, together[0]);
            Join(together[together.Count - 1], away[0]);
            return parting;
        }

        private static RouteMarker Free(double x, double z, double headingX, double headingZ, bool exact = false)
        {
            return new RouteMarker { Position = new Vec3d(x, 0, z), Heading = new Vec3d(headingX, 0, headingZ).Normalize(), Block = new BlockPos((int)x, 0, (int)z, 0), Exact = exact };
        }

        [Fact]
        public void SwitchMovesToWhereTheTracksPart()
        {
            List<Section> trunk = Trunk();
            Section trunkEnd = trunk[trunk.Count - 1];
            RouteMarker far = Free(12, 6, 0.6, 0.8);
            Vec3d parting = LayAlongThenAway(trunkEnd, new List<RouteMarker> { RouteMarker.OnJoint(trunkEnd, false, 1, true, Resolve), far });

            var markers = new List<RouteMarker> { RouteMarker.OnJoint(trunkEnd, false, 1, true, Resolve), far };
            List<RouteMarker> settled = RouteBuilder.SettleSwitches(markers, Resolve);

            // Стрілка переїхала на стик основної колії там, де колії розходяться
            Assert.NotSame(markers, settled);
            Assert.Equal(2, settled.Count);
            Assert.True(settled[0].Snapped);
            Assert.InRange(settled[0].Position.DistanceTo(parting), 0, 0.6);
            Assert.NotNull(settled[0].Track);
        }

        [Fact]
        public void SwitchAtTheEndOfARouteWithACornerMovesToo()
        {
            // Маршрут іде з вільної точки через кут і закінчується, вливаючись у колію; на кутах складений
            // маршрут має більше точок, ніж поставив гравець
            List<Section> trunk = Trunk();
            Section trunkEnd = trunk[trunk.Count - 1];
            var markers = new List<RouteMarker> { Free(30, 4, -1, 0), Free(20, 9, -1, 0), Free(12, 6, -0.6, -0.8, true), RouteMarker.OnJoint(trunkEnd, false, -1, false, Resolve) };
            Vec3d parting = LayAlongThenAway(trunkEnd, markers, true);
            markers[markers.Count - 1] = RouteMarker.OnJoint(trunkEnd, false, -1, false, Resolve);
            List<RouteMarker> settled = RouteBuilder.SettleSwitches(markers, Resolve);

            // Колія тут складена з прямих відрізків, а маршрут крива: між ними кілька міліметрів то в один бік, то
            // в інший, і на такому шумі розходження видно на секцію-дві раніше. Стрілка там уже вістря леза
            Assert.Equal(markers.Count, settled.Count);
            Assert.True(settled[settled.Count - 1].Snapped);
            Assert.InRange(settled[settled.Count - 1].Position.DistanceTo(parting), 0, 1.2);
        }

        [Fact]
        public void SwitchLeavingRightAwayStaysWhereItIs()
        {
            List<Section> main = LayMainTurningAway();
            Section trunkEnd = Resolve(main[0].GetLinks(true)[0]);
            var markers = new List<RouteMarker>
            {
                RouteMarker.OnJoint(trunkEnd, false, 1, true, Resolve),
                new RouteMarker { Position = new Vec3d(10, 0, 4), Heading = new Vec3d(0.8, 0, 0.6).Normalize(), Block = new BlockPos(10, 0, 4, 0) }
            };

            Assert.Same(markers, RouteBuilder.SettleSwitches(markers, Resolve));
        }

        [Fact]
        public void BranchRunningAlongsideBeforeItPartsIsRefused()
        {
            List<Section> trunk = Trunk();
            List<Section> straight = Lay(Point(0, 0, 1, 0), Point(30, 0, 1, 0));
            Join(trunk[trunk.Count - 1], straight[0]);

            // Гілка відходить на пів колії і йде поруч з прямою кілька блоків, рейки через одну, і лише потім
            // розходиться з нею: кут між гілками біля стрілки більший, ніж далі
            List<Section> branch = Plan(Point(0, 0, 1, 0), Point(3, 0.4, 1, 0), Point(9, 0.4, 1, 0), Point(15, 3, 0.8, 0.6));

            Assert.NotNull(SectionLinker.FindSwitchProblem(branch, SectionsIn, Resolve, out _));
        }

        [Fact]
        public void BranchLeavingRightAwayIsAllowed()
        {
            LayMainTurningAway();
            // Та сама основна колія, але гілка відходить від неї одразу від стику
            List<Section> branch = Plan(Point(0, 0, 1, 0), Point(10, 4, 0.8, 0.6));

            Assert.Null(SectionLinker.FindSwitchProblem(branch, SectionsIn, Resolve, out _));
        }

        [Fact]
        public void BranchWeavingAcrossAWavyTrackIsRefused()
        {
            List<Section> trunk = Trunk();
            // Основна колія виляє на блок у кожен бік, гілка йде прямо: розійшовшись, вони знову сходяться
            List<Section> main = Lay(Point(0, 0, 1, 0), Point(5, 1.2, 1, 0), Point(10, -1.2, 1, 0), Point(15, 1.2, 1, 0), Point(20, 0, 1, 0));
            Join(trunk[trunk.Count - 1], main[0]);
            List<Section> branch = Plan(Point(0, 0, 1, 0), Point(20, 0, 1, 0));

            string problem = SectionLinker.FindSwitchProblem(branch, SectionsIn, Resolve, out _);
            Assert.NotNull(problem);
            Assert.Contains("одна через одну", problem);
        }

        [Fact]
        public void BranchThatFirstCrossesIntoTheTrackIsRefused()
        {
            List<Section> trunk = Trunk();
            // Основна колія спершу відхиляється на +Z, а потім іде вниз, на -Z. Пряма гілка спершу опиняється
            // всередині неї, з боку -Z, і лише потім розходиться з нею з боку +Z
            List<Section> main = Lay(Point(0, 0, 1, 0), Point(4, 0.4, 1, 0), Point(12, -4, 0.7, -0.7));
            Join(trunk[trunk.Count - 1], main[0]);
            List<Section> branch = Plan(Point(0, 0, 1, 0), Point(16, 0, 1, 0));

            string problem = SectionLinker.FindSwitchProblem(branch, SectionsIn, Resolve, out _);
            Assert.NotNull(problem);
            Assert.Contains("одна через одну", problem);
        }

        // Відгалуження від прямої дугою такого радіуса на кут angle, градуси
        private List<Section> ArcFromStraight(double radius, double angleDeg)
        {
            double angle = angleDeg * System.Math.PI / 180;
            return Plan(Point(0, 0, 1, 0), Point(radius * System.Math.Sin(angle), radius * (1 - System.Math.Cos(angle)), System.Math.Cos(angle), System.Math.Sin(angle)));
        }

        [Fact]
        public void TooGentleSwitchIsRefused()
        {
            List<Section> trunk = Trunk();
            List<Section> straight = Lay(Point(0, 0, 1, 0), Point(30, 0, 1, 0));
            Join(trunk[trunk.Count - 1], straight[0]);

            // Дуга радіусом 200: гостряк найбільшої довжини не встигає відійти від рамної рейки на зазор
            string problem = SectionLinker.FindSwitchProblem(ArcFromStraight(200, 9), SectionsIn, Resolve, out _);
            Assert.NotNull(problem);
            Assert.Contains("гостряк не вміщається", problem);
        }

        [Fact]
        public void FrogWhereTheBranchWobblesIsRefused()
        {
            List<Section> trunk = Trunk();
            List<Section> straight = Lay(Point(0, 0, 1, 0), Point(30, 0, 1, 0));
            Join(trunk[trunk.Count - 1], straight[0]);

            // Гілка відходить від прямої, а там, де її рейка перетинає рейку прямої, на кілька сантиметрів
            // повертає назад і лише потім іде далі
            List<Section> branch = Plan(Point(0, 0, 1, 0), Point(6, 0.62, 1, 0.16), Point(8, 0.92, 1, 0), Point(10, 0.84, 1, -0.03),
                Point(15, 2.2, 0.9, 0.44));

            string problem = SectionLinker.FindSwitchProblem(branch, SectionsIn, Resolve, out _);
            Assert.NotNull(problem);
            Assert.Contains("розходяться далі по ходу", problem);
        }

        [Fact]
        public void GentleSwitchWithinTheLongestBladeIsAllowed()
        {
            List<Section> trunk = Trunk();
            List<Section> straight = Lay(Point(0, 0, 1, 0), Point(30, 0, 1, 0));
            Join(trunk[trunk.Count - 1], straight[0]);

            // Дуга радіусом 100: гостряк ще вміщається
            Assert.Null(SectionLinker.FindSwitchProblem(ArcFromStraight(100, 14), SectionsIn, Resolve, out _));
        }

        [Fact]
        public void CrossoverBetweenParallelTracksIsAllowed()
        {
            // Дві паралельні колії за чотири блоки одна від одної і з'їзд між ними: дві звичайні стрілки
            Lay(Point(-10, 0, 1, 0), Point(30, 0, 1, 0));
            Lay(Point(-10, 4, 1, 0), Point(30, 4, 1, 0));
            List<Section> crossover = Plan(Point(0, 0, 1, 0), Point(16, 4, 1, 0));

            Assert.Null(SectionLinker.FindSwitchProblem(crossover, SectionsIn, Resolve, out _));
        }
    }
}
