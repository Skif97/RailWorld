using RailWorld;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Осі деталі в площині колії: ті самі для кутів деталей (InPlane) і для матриці важеля стрілки.
    /// </summary>
    public class PlaneBasisTests
    {
        private static void AssertUnit(Vec3d v)
        {
            Assert.InRange(v.Length(), 1 - 1e-9, 1 + 1e-9);
        }

        [Fact]
        public void AxesLieInTheTrackPlaneAndUpLooksUp()
        {
            // Колія йде вгору по осі Z і нахилена впоперек
            var along = new Vec3d(0, 0.2, 1).Normalize();
            var sideways = new Vec3d(1, 0.1, 0).Normalize();
            RailWayRendererSystem.PlaneBasis(sideways, along, out Vec3d x, out Vec3d y, out Vec3d z);

            AssertUnit(x);
            AssertUnit(y);
            AssertUnit(z);
            Assert.InRange(x.Dot(y), -1e-9, 1e-9);
            Assert.InRange(x.Dot(z), -1e-9, 1e-9);
            Assert.InRange(y.Dot(z), -1e-9, 1e-9);
            Assert.True(y.Y > 0);
            // x дивиться туди ж, куди sideways, а z лежить на лінії колії
            Assert.InRange(x.Dot(sideways), 1 - 1e-9, 1 + 1e-9);
            Assert.InRange(System.Math.Abs(z.Dot(along)), 0.99, 1 + 1e-9);
        }

        [Fact]
        public void NoPlaneMeansAFlatTurnWithoutNaN()
        {
            // Убік уздовж самої колії, а потім нульовий: площини не скласти, деталь лише повертається в плані
            foreach (Vec3d sideways in new[] { new Vec3d(0, 0, 1), new Vec3d(0, 0, 0) })
            {
                RailWayRendererSystem.PlaneBasis(sideways, new Vec3d(0, 0, 1), out Vec3d x, out Vec3d y, out Vec3d z);
                foreach (Vec3d axis in new[] { x, y, z })
                {
                    Assert.False(double.IsNaN(axis.X) || double.IsNaN(axis.Y) || double.IsNaN(axis.Z));
                    AssertUnit(axis);
                }
                Assert.Equal(1, y.Y, 9);
                Vec3f angles = RailWayRendererSystem.InPlane(sideways, new Vec3d(0, 0, 1));
                Assert.Equal(0, angles.X, 6);
                Assert.Equal(0, angles.Z, 6);
            }
        }
    }
}
