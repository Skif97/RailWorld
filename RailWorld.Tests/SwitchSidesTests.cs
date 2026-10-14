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
    }
}
