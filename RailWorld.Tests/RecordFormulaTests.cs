using System;
using System.Collections.Generic;
using System.Text;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Еталон записів стрілок і перетинів. Записи зберігаються у світі пораховані, і запис, пораховано старою
    /// версією розрахунку, перераховується лише тоді, коли в ньому інша версія (RailSwitch.CurrentFormula,
    /// RailCrossing.CurrentFormula). Цей тест рахує кілька знайомих стрілок і перетинів і порівнює все
    /// пораховане з записаним тут. Якщо він упав, розрахунок змінився: треба збільшити версію і записати
    /// новий еталон, інакше у світах лишаться записи, пораховані по-старому.
    /// </summary>
    public class RecordFormulaTests : TrackTestBase
    {
        private const string SwitchHint = "Змінився розрахунок запису стрілки: збільш RailSwitch.CurrentFormula і запиши новий еталон.";
        private const string CrossingHint = "Змінився розрахунок запису перетину: збільш RailCrossing.CurrentFormula і запиши новий еталон.";

        private const string SwitchExpected =
            "formula 3 shape MainFirst complete True resolved True | members 23 pair 17 overlap 18 blade 6 frog 10-13 inner 11111111111111111111111 | members 22 pair 17 overlap 17 blade 6 frog 10-13 inner 0000000000000000000000"
            + "\n" + "formula 3 shape Fork complete True resolved True | members 18 pair 13 overlap 13 blade 6 frog 7-9 inner 000000000000000000 | members 18 pair 13 overlap 13 blade 6 frog 7-9 inner 111111111111111111"
            + "\n" + "formula 3 shape MainFirst complete True resolved True | members 36 pair 30 overlap 31 blade 9 frog 16-23 inner 000000000000000000000000000000000000 | members 35 pair 30 overlap 30 blade 9 frog 16-23 inner 11111111111111111111111111111111111";

        private const string CrossingExpected =
            "formula 1 complete True resolved True | members 13 center 7 node 5-9 | members 12 center 6 node 5-8"
            + "\n" + "formula 1 complete True resolved True | members 18 center 10 node 5-14 | members 18 center 9 node 5-14"
            + "\n" + "formula 1 complete True resolved True | members 26 center 14 node 5-22 | members 26 center 13 node 5-22";

        // Стрілка на кінці спільної колії з двома гілками, покладеними з точки (0, 0) на схід
        private RailSwitch SwitchOf(List<Section> first, List<Section> second)
        {
            List<Section> trunk = Trunk();
            Section last = trunk[trunk.Count - 1];
            Join(last, first[0]);
            Join(last, second[0]);
            return SwitchBuilder.Build(last, false, Resolve);
        }

        // Дуга з точки (0, 0) на схід, що відходить убік на angle; side 1 ліворуч, -1 праворуч
        private List<Section> ArcOf(double radius, double angle, int side)
        {
            return Lay(Point(0, 0, 1, 0),
                Point(radius * Math.Sin(angle), side * radius * (1 - Math.Cos(angle)), Math.Cos(angle), side * Math.Sin(angle)));
        }

        private static string Describe(RailSwitch record)
        {
            var text = new StringBuilder();
            text.Append("formula ").Append(record.Formula).Append(" shape ").Append(record.Shape)
                .Append(" complete ").Append(record.Complete).Append(" resolved ").Append(record.Resolved);
            foreach (SwitchBranch branch in record.Branches)
            {
                text.Append(" | members ").Append(branch.Members.Count).Append(" pair ").Append(branch.PairSections)
                    .Append(" overlap ").Append(branch.OverlapSections).Append(" blade ").Append(branch.BladeSections)
                    .Append(" frog ").Append(branch.FrogFrom).Append("-").Append(branch.FrogTo).Append(" inner ");
                foreach (bool inner in branch.InnerFirst) text.Append(inner ? '1' : '0');
            }
            return text.ToString();
        }

        private static string Describe(RailCrossing record)
        {
            var text = new StringBuilder();
            text.Append("formula ").Append(record.Formula).Append(" complete ").Append(record.Complete).Append(" resolved ").Append(record.Resolved);
            foreach (CrossingTrack track in record.Tracks)
            {
                text.Append(" | members ").Append(track.Members.Count).Append(" center ").Append(track.Center)
                    .Append(" node ").Append(track.NodeFrom).Append("-").Append(track.NodeTo);
            }
            return text.ToString();
        }

        [Fact]
        public void SwitchRecordsAreComputedAsRecorded()
        {
            var found = new List<string>();
            found.Add(Describe(SwitchOf(Straight(), Arc())));
            found.Add(Describe(SwitchOf(ArcOf(ArcRadius, Math.PI / 4, 1), ArcOf(ArcRadius, Math.PI / 4, -1))));
            found.Add(Describe(SwitchOf(Lay(Point(0, 0, 1, 0), Point(40, 0, 1, 0)), ArcOf(60, Math.PI / 6, -1))));

            Assert.True(SwitchExpected == string.Join("\n", found), SwitchHint + "\nЗараз виходить:\n" + string.Join("\n", found));
        }

        [Fact]
        public void CrossingRecordsAreComputedAsRecorded()
        {
            var found = new List<string>();
            foreach (double angleDeg in new[] { 90.0, 45.0, 25.0 })
            {
                // Основна колія на схід; друга перетинає її в точці (15.1, 0) посеред секцій обох колій
                List<Section> main = Lay(Point(0, 40 * found.Count, 1, 0), Point(30, 40 * found.Count, 1, 0));
                double a = angleDeg * Math.PI / 180;
                var along = new Vec3d(Math.Cos(a), 0, Math.Sin(a));
                var at = new Vec3d(15.1, 0, 40 * found.Count);
                List<Section> other = Lay(new RoutePoint { Position = at.AddCopy(along.X * -10.1, 0, along.Z * -10.1), Tangent = along },
                    new RoutePoint { Position = at.AddCopy(along.X * 10.1, 0, along.Z * 10.1), Tangent = along });

                RailCrossing record = null;
                foreach (Section m in main)
                {
                    foreach (Section o in other)
                    {
                        if (record == null && AxisCrossing.OfLaid(m, o)) record = CrossingBuilder.Build(m, o, Resolve, out _);
                    }
                }
                Assert.NotNull(record);
                found.Add(Describe(record));
            }

            Assert.True(CrossingExpected == string.Join("\n", found), CrossingHint + "\nЗараз виходить:\n" + string.Join("\n", found));
        }

        [Fact]
        public void RecordFromBeforeTheFormulaFieldIsOutdated()
        {
            // У записах, збережених до появи номера версії, його немає, і після читання там 0
            Assert.True(new RailSwitch().Outdated);
            Assert.True(new RailCrossing().Outdated);

            RailSwitch record = SwitchOf(Straight(), Arc());
            Assert.False(record.Outdated);
            Assert.False(ProtoBuf.Serializer.DeepClone(record).Outdated);
        }

        [Fact]
        public void SwitchDismantledBeforeItsFrogIsNotComplete()
        {
            // Дугову гілку розібрано з далекого кінця так, що хрестовини вже немає: це дві колії одна на одній,
            // і малюються вони звичайними рейками, а не гостряками
            List<Section> trunk = Trunk();
            Section last = trunk[trunk.Count - 1];
            List<Section> straight = Straight(), arc = Arc();
            Join(last, straight[0]);
            Join(last, arc[0]);
            Assert.True(SwitchBuilder.Build(last, false, Resolve).Complete);

            for (int n = arc.Count - 1; n >= 3; n--)
            {
                arc[n - 1].RemoveLink(false, arc[n]);
                Forget(arc[n]);
            }
            RailSwitch record = SwitchBuilder.Build(last, false, Resolve);
            Assert.True(record.Resolved);
            Assert.False(record.Complete);
        }
    }
}
