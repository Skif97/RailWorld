using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Зона стрілки: де нову стрілку ставити не можна і де маршрут стає на відкритий край колії.
    /// Стрілка в тестах та сама: спільна колія із заходу, пряма гілка на схід і дуга.
    /// </summary>
    public class SwitchZoneTests : TrackTestBase
    {
        private List<Section> trunk, straight, arc;

        private void LaySwitch()
        {
            trunk = Trunk();
            straight = Straight();
            arc = Arc();
            Section last = trunk[trunk.Count - 1];
            Join(last, straight[0]);
            Join(last, arc[0]);
        }

        // Відгалуження від прямої гілки зі стику після секції з таким номером: дуга в інший бік від стрілки
        private List<Section> BranchAfter(int index)
        {
            Vec3d joint = straight[index].FullEndPosition;
            double angle = Math.PI / 3;
            return Plan(Point(joint.X, joint.Z, 1, 0),
                Point(joint.X + ArcRadius * Math.Sin(angle), joint.Z - ArcRadius * (1 - Math.Cos(angle)), Math.Cos(angle), -Math.Sin(angle)));
        }

        [Fact]
        public void BranchInsideTheZoneOfASwitchIsRefused()
        {
            LaySwitch();

            // За дві секції від стрілки, там ще гостряк
            Assert.NotNull(SectionLinker.FindSwitchProblem(BranchAfter(1), SectionsIn, Resolve, out int at));
            Assert.Equal(0, at);
        }

        [Fact]
        public void BranchFarFromTheSwitchIsAllowed()
        {
            LaySwitch();
            RailSwitch built = SwitchBuilder.Build(trunk[trunk.Count - 1], false, Resolve);
            int beyond = built.Branches[0].ZoneSections + SwitchZone.ClearSections + 3;

            Assert.Null(SectionLinker.FindSwitchProblem(BranchAfter(beyond), SectionsIn, Resolve, out _));
        }

        [Fact]
        public void SwitchRightBehindTheBodyOfAnotherFacingAwayIsAllowed()
        {
            LaySwitch();
            RailSwitch built = SwitchBuilder.Build(trunk[trunk.Count - 1], false, Resolve);

            // Одразу за тілом першої стрілки, і розходиться далі від неї: тіла не накладаються,
            // запасу між ними не потрібно
            int behind = built.Branches[0].ZoneSections + 1;
            Assert.Null(SectionLinker.FindSwitchProblem(BranchAfter(behind), SectionsIn, Resolve, out _));
        }

        [Fact]
        public void SwitchFacingBackIntoTheBodyOfAnotherIsRefused()
        {
            LaySwitch();
            RailSwitch built = SwitchBuilder.Build(trunk[trunk.Count - 1], false, Resolve);

            // Колія приходить ззаду збоку і вливається в пряму гілку за вісім секцій після тіла першої стрілки.
            // Стрілка там дивиться назад, і її тіло тягнеться назустріч першій: вони накладаються,
            // хоча сам стик далеко за тілом
            int index = built.Branches[0].ZoneSections + 8;
            Vec3d joint = straight[index].FullEndPosition;
            double angle = Math.PI / 3;
            List<Section> merging = Plan(
                Point(joint.X - ArcRadius * Math.Sin(angle), joint.Z - ArcRadius * (1 - Math.Cos(angle)), Math.Cos(angle), Math.Sin(angle)),
                Point(joint.X, joint.Z, 1, 0));

            string problem = SectionLinker.FindSwitchProblem(merging, SectionsIn, Resolve, out _);
            Assert.NotNull(problem);
            Assert.Contains("стрілки", problem);
        }

        [Fact]
        public void ContinuingATrackFromItsOpenEndIsNotASwitch()
        {
            // Колія з відкритим кінцем одразу біля стрілки не буває; беремо просто пряму
            straight = Straight();
            Section last = straight[straight.Count - 1];
            Vec3d end = last.FullEndPosition;

            List<Section> more = Plan(Point(end.X, end.Z, 1, 0), Point(end.X + 6, end.Z, 1, 0));
            Assert.Null(SectionLinker.FindSwitchProblem(more, SectionsIn, Resolve, out _));
        }

        [Fact]
        public void LoopThatNeverLeavesTheTrackIsRefused()
        {
            // Довга пряма без стрілок. Маршрут відходить від неї на пів блока і тут же повертається:
            // шпали двох колій весь час перекриваються, і дві його стрілки стоять одна в одній
            List<Section> line = Lay(Point(0, 0, 1, 0), Point(60, 0, 1, 0));
            List<Section> loop = Plan(Point(JointNear(line, 10), 0, 1, 0), Point(13, 0.5, 1, 0), Point(JointNear(line, 16), 0, 1, 0));

            Assert.NotNull(SectionLinker.FindSwitchProblem(loop, SectionsIn, Resolve, out _));
        }

        [Fact]
        public void SidingThatLeavesTheTrackProperlyIsAllowed()
        {
            // Роз'їзд: відходить на три блоки вбік, іде поруч і повертається за сорок блоків
            Lay(Point(0, 0, 1, 0), Point(60, 0, 1, 0));
            List<Section> siding = Plan(Point(10, 0, 1, 0), Point(20, 3, 1, 0), Point(40, 3, 1, 0), Point(50, 0, 1, 0));

            Assert.Null(SectionLinker.FindSwitchProblem(siding, SectionsIn, Resolve, out _));
        }

        [Fact]
        public void TryingAStretchDoesNotTouchTheLaidTrack()
        {
            List<Section> main = Lay(Point(0, 0, 1, 0), Point(60, 0, 1, 0));
            List<Section> siding = Plan(Point(10, 0, 1, 0), Point(20, 3, 1, 0), Point(40, 3, 1, 0), Point(50, 0, 1, 0));

            SectionLinker.FindSwitchProblem(siding, SectionsIn, Resolve, out _);

            // Примірка йшла на копіях: у покладених секцій сусідів стільки ж, скільки було
            foreach (Section section in main)
            {
                Assert.True(section.GetLinks(true).Count <= 1);
                Assert.True(section.GetLinks(false).Count <= 1);
            }
        }

        // Точка маршруту на заданій висоті
        private static RoutePoint High(double x, double y, double z, double tangentX, double tangentZ)
        {
            return new RoutePoint { Position = new Vec3d(x, y, z), Tangent = new Vec3d(tangentX, 0, tangentZ).Normalize() };
        }

        [Fact]
        public void TrackLaidAlongAnotherWithinItsGaugeIsRefused()
        {
            // Нова колія за пів блока вбік від покладеної, тридцять блоків поспіль
            Lay(Point(0, 0, 1, 0), Point(60, 0, 1, 0));
            List<Section> along = Plan(Point(10, 0.5, 1, 0), Point(40, 0.5, 1, 0));

            Assert.True(SectionLinker.FindLongOverlap(along, SectionsIn, Resolve, out int from, out int to));
            // Накладання це вся ділянка, від першої секції до останньої
            Assert.Equal(0, from);
            Assert.Equal(along.Count - 1, to);
        }

        [Fact]
        public void ShortOverlapsOfASwitchAndACrossingAreAllowed()
        {
            Lay(Point(0, 0, 1, 0), Point(60, 0, 1, 0));

            // Відгалуження дугою: лежить на колії, поки не відійде на довжину шпали
            Vec3d joint = new Vec3d(10, 0, 0);
            double angle = Math.PI / 3;
            List<Section> branch = Plan(Point(joint.X, joint.Z, 1, 0),
                Point(joint.X + ArcRadius * Math.Sin(angle), joint.Z + ArcRadius * (1 - Math.Cos(angle)), Math.Cos(angle), Math.Sin(angle)));
            Assert.False(SectionLinker.FindLongOverlap(branch, SectionsIn, Resolve, out _, out _));

            // Перетин під тридцять градусів
            double dx = Math.Cos(Math.PI / 6), dz = Math.Sin(Math.PI / 6);
            List<Section> crossing = Plan(Point(30 - 10 * dx, -10 * dz, dx, dz), Point(30 + 10 * dx, 10 * dz, dx, dz));
            Assert.False(SectionLinker.FindLongOverlap(crossing, SectionsIn, Resolve, out _, out _));
        }

        [Fact]
        public void LongOverlapCannotBeAssembledFromShortPieces()
        {
            // Уздовж покладеної колії вже лежить шматок у 30 секцій: сам він у межу вкладався
            Lay(Point(0, 0, 1, 0), Point(60, 0, 1, 0));
            Lay(Point(10, 0.5, 1, 0), Point(25, 0.5, 1, 0));

            // Продовжити його ще на 15 секцій не можна: разом це 45
            List<Section> more = Plan(Point(25, 0.5, 1, 0), Point(32.5, 0.5, 1, 0));
            Assert.True(SectionLinker.FindLongOverlap(more, SectionsIn, Resolve, out int from, out int to));
            Assert.Equal(0, from);
            Assert.Equal(more.Count - 1, to);

            // А на 5 секцій можна: разом 35
            List<Section> little = Plan(Point(25, 0.5, 1, 0), Point(27.5, 0.5, 1, 0));
            Assert.False(SectionLinker.FindLongOverlap(little, SectionsIn, Resolve, out _, out _));

            // І з другого боку шматка так само
            List<Section> before = Plan(Point(2.5, 0.5, 1, 0), Point(10, 0.5, 1, 0));
            Assert.True(SectionLinker.FindLongOverlap(before, SectionsIn, Resolve, out _, out _));
        }

        [Theory]
        [InlineData(1.0, true)]    // блок над колією: шпали однієї в рейках другої
        [InlineData(2.0, true)]    // два блоки: менше за просвіт
        [InlineData(3.0, false)]   // міст на три блоки, найнижчий на рівній землі
        [InlineData(3.5, false)]   // міст
        [InlineData(0.0, false)]   // у тій самій площині: це вже не «над», а поруч
        public void TrackCloselyAboveAnotherIsRefused(double height, bool refused)
        {
            // Коротка ділянка вздовж покладеної колії, за пів блока вбік і на заданій висоті.
            // Осі не перетинаються, і накладання коротке: інші правила тут мовчать
            Lay(Point(0, 0, 1, 0), Point(60, 0, 1, 0));
            List<Section> above = Plan(High(10, height, 0.5, 1, 0), High(15, height, 0.5, 1, 0));

            Assert.False(SectionLinker.FindLongOverlap(above, SectionsIn, Resolve, out _, out _));
            List<int> close = SectionLinker.FindTooCloseAbove(above, SectionsIn);
            // Уся ділянка лежить над колією, тож порушує кожна її секція, а не лише перша
            Assert.Equal(refused ? above.Count : 0, close.Count);
        }

        [Fact]
        public void TrackOverTheVeryEndOfAnotherIsRefused()
        {
            // Покладена колія закінчується в точці (20, 0). Нова проходить упоперек над самим її краєм,
            // на блок вище: осі не перетинаються, бо колія під нею вже скінчилась
            Lay(Point(0, 0, 1, 0), Point(20, 0, 1, 0));
            List<Section> over = Plan(High(20.5, 1, -6, 0, 1), High(20.5, 1, 6, 0, 1));

            List<int> close = SectionLinker.FindTooCloseAbove(over, SectionsIn);
            // Порушують лише ті секції, чиї шпали дістають до покладеної колії, а не вся ділянка
            Assert.True(close.Count > 0 && close.Count < over.Count);

            // Та сама колія на два блоки далі за край уже нічого не зачіпає
            List<Section> clear = Plan(High(22.5, 1, -6, 0, 1), High(22.5, 1, 6, 0, 1));
            Assert.Empty(SectionLinker.FindTooCloseAbove(clear, SectionsIn));
        }

        [Fact]
        public void BridgeRightOverATrackIsNotAnOverlap()
        {
            // Та сама лінія, але на п'ять блоків вище
            Lay(Point(0, 0, 1, 0), Point(60, 0, 1, 0));
            List<Section> bridge = Plan(High(10, 5, 0, 1, 0), High(40, 5, 0, 1, 0));

            Assert.False(SectionLinker.FindLongOverlap(bridge, SectionsIn, Resolve, out _, out _));
        }

        [Fact]
        public void SwitchWithoutAFrogIsRefused()
        {
            List<Section> line = Lay(Point(0, 0, 1, 0), Point(60, 0, 1, 0));
            double at = JointNear(line, 20);

            // Гілка відходить дугою, але обривається за два блоки: рейки двох колій ще не перетнулися,
            // і хрестовині стати ніде
            double angle = 2.0 / ArcRadius;
            List<Section> stub = Plan(Point(at, 0, 1, 0),
                Point(at + ArcRadius * Math.Sin(angle), ArcRadius * (1 - Math.Cos(angle)), Math.Cos(angle), Math.Sin(angle)));
            string problem = SectionLinker.FindSwitchProblem(stub, SectionsIn, Resolve, out _);
            Assert.NotNull(problem);
            Assert.Contains("хрестовини", problem);

            // Та сама дуга, доведена до кінця: хрестовина є
            double full = Math.PI / 3;
            List<Section> branch = Plan(Point(at, 0, 1, 0),
                Point(at + ArcRadius * Math.Sin(full), ArcRadius * (1 - Math.Cos(full)), Math.Cos(full), Math.Sin(full)));
            Assert.Null(SectionLinker.FindSwitchProblem(branch, SectionsIn, Resolve, out _));
        }

        [Fact]
        public void SwitchOnACrossingIsRefused()
        {
            // Довга колія на схід, і друга перетинає її навскіс у точці (30, 0)
            List<Section> line = Lay(Point(0, 0, 1, 0), Point(60, 0, 1, 0));
            double k = Math.Sqrt(0.5);
            Lay(Point(30 - 8 * k, -8 * k, 1, 1), Point(30 + 8 * k, 8 * k, 1, 1));

            // Відгалуження від першої за два блоки до перетину: стрілка стала б просто на ньому
            double angle = Math.PI / 3;
            double close = JointNear(line, 28), away = JointNear(line, 5);
            List<Section> near = Plan(Point(close, 0, 1, 0),
                Point(close + ArcRadius * Math.Sin(angle), -ArcRadius * (1 - Math.Cos(angle)), Math.Cos(angle), -Math.Sin(angle)));
            string problem = SectionLinker.FindSwitchProblem(near, SectionsIn, Resolve, out _);
            Assert.NotNull(problem);
            Assert.Contains("перетину", problem);

            // Те саме відгалуження далеко від перетину будується
            List<Section> far = Plan(Point(away, 0, 1, 0),
                Point(away + ArcRadius * Math.Sin(angle), -ArcRadius * (1 - Math.Cos(angle)), Math.Cos(angle), -Math.Sin(angle)));
            Assert.Null(SectionLinker.FindSwitchProblem(far, SectionsIn, Resolve, out _));
        }

        [Fact]
        public void LeverGoesToTheFreeSideAndTheSwitchIsRefusedWhenThereIsNone()
        {
            LaySwitch();
            Section last = trunk[trunk.Count - 1];

            // Спершу важіль стає із зовнішнього боку прямої гілки, не між коліями
            RailSwitch record = SwitchBuilder.Build(last, false, Resolve);
            int preferred = SectionLinker.ChooseLeverSide(last, false, record, Resolve, SectionsIn);
            Assert.NotEqual(0, preferred);
            Vec3d there = SectionPicker.LeverPosition(last, false, preferred);
            Vec3d opposite = SectionPicker.LeverPosition(last, false, -preferred);
            // Дуга відходить у бік +Z, тож зовнішній бік прямої це -Z
            Assert.True(there.Z < 0);

            // На тому місці вже лежить інша колія: важіль переходить на другий бік
            Lay(Point(there.X - 4, there.Z - 0.3, 1, 0), Point(there.X + 4, there.Z - 0.3, 1, 0));
            Assert.Equal(-preferred, SectionLinker.ChooseLeverSide(last, false, record, Resolve, SectionsIn));

            // Перенести важіль на зайнятий бік не можна, на вільний можна
            Assert.False(SectionLinker.LeverSideFree(last, false, preferred, record, SectionsIn));
            Assert.True(SectionLinker.LeverSideFree(last, false, -preferred, record, SectionsIn));

            // І з другого боку теж колія: важелю стати ніде
            Lay(High(opposite.X - 4, 0, opposite.Z + 0.3, 1, 0), High(opposite.X + 4, 0, opposite.Z + 0.3, 1, 0));
            Assert.Equal(0, SectionLinker.ChooseLeverSide(last, false, record, Resolve, SectionsIn));
        }

        [Fact]
        public void LeverSideChosenByThePlayerIsRemembered()
        {
            LaySwitch();
            Section last = trunk[trunk.Count - 1];

            last.SetLeverSide(false, 1);
            Assert.False(last.IsLeverManual(false));

            last.SetLeverSide(false, -1, manual: true);
            Assert.True(last.IsLeverManual(false));
            Assert.Equal(-1, last.GetLeverSide(false));

            // Стрілки не стало: наступна на цьому місці починає з чистого
            last.ResetSwitch(false);
            Assert.False(last.IsLeverManual(false));
        }

        [Fact]
        public void TrackLaidOnAnotherInTheSamePlaneIsRefused()
        {
            // Коротка ділянка вздовж покладеної колії, за пів блока вбік, на тій самій висоті. Осі не
            // перетинаються, стрілки тут немає: нова колія просто лягає шпалами на шпали
            Lay(Point(0, 0, 1, 0), Point(60, 0, 1, 0));
            List<Section> beside = Plan(Point(10, 0.5, 1, 0), Point(15, 0.5, 1, 0));

            string problem = SectionLinker.FindSwitchProblem(beside, SectionsIn, Resolve, out int at);
            Assert.NotNull(problem);
            Assert.Contains("без стрілки чи перетину", problem);
            Assert.Equal(0, at);
        }

        [Fact]
        public void CrossingAndSwitchAreTheLegitimateWaysToLieOnATrack()
        {
            Lay(Point(0, 0, 1, 0), Point(60, 0, 1, 0));

            // Перетин упоперек і навскіс
            Assert.Null(SectionLinker.FindSwitchProblem(Plan(Point(30, -6, 0, 1), Point(30, 6, 0, 1)), SectionsIn, Resolve, out _));
            double dx = Math.Cos(Math.PI / 6), dz = Math.Sin(Math.PI / 6);
            Assert.Null(SectionLinker.FindSwitchProblem(Plan(Point(30 - 10 * dx, -10 * dz, dx, dz), Point(30 + 10 * dx, 10 * dz, dx, dz)), SectionsIn, Resolve, out _));

            // Відгалуження: його перші секції лежать на основній колії, і це стрілка
            double angle = Math.PI / 3;
            List<Section> branch = Plan(Point(20, 0, 1, 0),
                Point(20 + ArcRadius * Math.Sin(angle), ArcRadius * (1 - Math.Cos(angle)), Math.Cos(angle), Math.Sin(angle)));
            Assert.Null(SectionLinker.FindSwitchProblem(branch, SectionsIn, Resolve, out _));
        }

        [Fact]
        public void SwitchOverAStrayPieceOfTrackIsRefused()
        {
            // Поруч з колією лежить окремий шматочок: ні з чим не з'єднаний і нічого не перетинає
            List<Section> line = Lay(Point(0, 0, 1, 0), Point(60, 0, 1, 0));
            Lay(Point(23, 1.3, 1, 0), Point(26, 1.3, 1, 0));

            // Відгалуження в його бік: тіло стрілки лягло б просто на нього
            double angle = Math.PI / 3, at = JointNear(line, 20);
            List<Section> branch = Plan(Point(at, 0, 1, 0),
                Point(at + ArcRadius * Math.Sin(angle), ArcRadius * (1 - Math.Cos(angle)), Math.Cos(angle), Math.Sin(angle)));

            Assert.NotNull(SectionLinker.FindSwitchProblem(branch, SectionsIn, Resolve, out _));
        }

        [Fact]
        public void OpenEndIsFoundWithinAFewSections()
        {
            straight = Straight();
            int last = straight.Count - 1;

            // Третя секція від краю: точка стає на сам край
            Assert.True(SwitchZone.FindOpenEnd(straight[last - 2], straight[last - 2].GetGlobalPos(), Resolve, out Section found, out bool atStart));
            Assert.Same(straight[last], found);
            Assert.False(atStart);

            // Четверта секція від краю: вже ні
            Assert.False(SwitchZone.FindOpenEnd(straight[last - 3], straight[last - 3].GetGlobalPos(), Resolve, out _, out _));

            // Далеко від обох країв: прив'язки немає
            Assert.False(SwitchZone.FindOpenEnd(straight[straight.Count / 2], straight[straight.Count / 2].GetGlobalPos(), Resolve, out _, out _));
        }

        [Fact]
        public void OpenEndBehindASwitchIsNotOffered()
        {
            LaySwitch();

            // Спільна колія коротка, її західний край відкритий. Але з першої секції гілки до нього стрілка
            Assert.False(SwitchZone.FindOpenEnd(straight[0], straight[0].GetGlobalPos(), Resolve, out _, out _));
            Assert.True(SwitchZone.FindOpenEnd(trunk[0], trunk[0].GetGlobalPos(), Resolve, out Section found, out bool atStart));
            Assert.Same(trunk[0], found);
            Assert.True(atStart);
        }
    }
}
