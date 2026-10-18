using System;
using System.Collections.Generic;
using RailWorld.src.RailWay;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// З якого боку від кожної секції гілки стрілки лежить друга гілка.
    /// </summary>
    public class SwitchSidesTests : TrackTestBase
    {
        // Чи внутрішня рейка кожної секції гілки дивиться в бік +Z, де лежить друга гілка
        private static void AssertInnerTowardPlusZ(List<Section> branch, List<bool> innerFirst)
        {
            Assert.Equal(branch.Count, innerFirst.Count);
            for (int n = 0; n < branch.Count; n++)
            {
                Vec3f normal = branch[n].CenterNormal;
                double inner = innerFirst[n] ? normal.Z : -normal.Z;
                Assert.True(inner > 0, "секція " + (n + 1) + ": внутрішня рейка не з боку другої гілки");
            }
        }

        [Fact]
        public void InnerRailOfAnOrdinarySwitchFacesTheOtherBranch()
        {
            List<Section> straight = Straight(), arc = Arc();
            List<bool> sides = SwitchSides.InnerFirst(straight, arc);
            AssertInnerTowardPlusZ(straight, sides);
        }

        [Fact]
        public void BranchesLyingOnEachOtherTakeTheSideFromWhereTheyPart()
        {
            // Друга гілка перші три блоки йде по прямій, на кілька міліметрів то з одного, то з другого боку
            // від неї, і лише потім відходить дугою на +Z
            List<Section> straight = Straight();
            List<Section> weaving = Lay(Point(0, 0, 1, 0), Point(1, 0.004, 1, 0), Point(2, -0.004, 1, 0), Point(3, 0, 1, 0),
                Point(9, 1.5, 0.9, 0.44));

            AssertInnerTowardPlusZ(straight, SwitchSides.InnerFirst(straight, weaving));

            // З боку другої гілки бік протилежний: пряма лежить на -Z
            List<bool> back = SwitchSides.InnerFirst(weaving, straight);
            for (int n = 0; n < weaving.Count; n++)
            {
                Vec3f normal = weaving[n].CenterNormal;
                Assert.True((back[n] ? normal.Z : -normal.Z) < 0, "секція " + (n + 1));
            }
        }

        [Fact]
        public void BranchThatLoopsBackStopsBeforeTheSwitch()
        {
            // Замкнене кільце колії: вісімка кутів навколо (0, 10). Зі стику між першою і другою секцією
            // вбік відходить ще одна колія, і там стрілка. Гілка кільцем обходить коло і вертається до стрілки:
            // далі це вже не вона, а спільна колія самої стрілки
            var corners = new (double X, double Z)[9];
            for (int k = 0; k <= 8; k++) corners[k] = (10 * Math.Sin(k * Math.PI / 4), 10 - 10 * Math.Cos(k * Math.PI / 4));
            List<Section> ring = Lay(Through(corners));
            Join(ring[ring.Count - 1], ring[0]);

            Section trunk = ring[0];
            Vec3d joint = trunk.FullEndPosition;
            List<Section> spur = Lay(Through((joint.X, joint.Z), (joint.X + 6, joint.Z - 6)));
            Join(trunk, spur[0]);

            Assert.True(SwitchSides.Branches(trunk, false, Resolve, out List<Section> first, out List<Section> second));
            Assert.Equal(ring.Count - 1, first.Count);
            Assert.DoesNotContain(trunk, first);
            Assert.Equal(spur.Count, second.Count);
        }

        [Fact]
        public void BothBranchesOfALoopGetTheirOwnHalf()
        {
            // Стрілка замикає петлю: обидві гілки це те саме коло, пройдене в різні боки. Кожна дістає свою половину
            // і жодна не порожня й не заходить на іншу
            var corners = new (double X, double Z)[9];
            for (int k = 0; k <= 8; k++) corners[k] = (10 * Math.Sin(k * Math.PI / 4), 10 - 10 * Math.Cos(k * Math.PI / 4));
            List<Section> ring = Lay(Through(corners));
            Section trunk = Lay(Through((-6, 0), (0, 0)))[0];
            Join(trunk, ring[0]);
            // Кінець кільця теж приходить у стик: друга гілка стрілки це кільце навпаки
            Section last = ring[ring.Count - 1];
            trunk.AddLink(false, new SectionLink(last, false));
            last.AddLink(false, new SectionLink(trunk, false));

            Assert.True(SwitchSides.Branches(trunk, false, Resolve, out List<Section> first, out List<Section> second));
            Assert.NotEmpty(first);
            Assert.NotEmpty(second);
            Assert.Equal(ring.Count, first.Count + second.Count);
            Assert.Empty(first.FindAll(second.Contains));
        }
    }
}
