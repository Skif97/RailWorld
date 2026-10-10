using System;
using Vintagestory.API.MathTools;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// ModMath.TrackFrameToEntityAngles: кути сутності, з якими рендер гри ставить модель вагонетки
    /// носом уздовж колії і верхом уздовж верху полотна.
    /// </summary>
    public class TrackFrameTests
    {
        // Поворот моделі, як його збирає рендер гри (EntityShapeRenderer.loadModelMatrix):
        // Ry(курс + чверть оберту) * Rz(Roll) * Rx(xangle). Pitch у вагонетки нуль
        private static Vec3d Rendered(float yaw, float tilt, float cant, Vec3d modelAxis)
        {
            Vec3d v = RotateX(modelAxis, cant);
            v = RotateZ(v, tilt);
            return RotateY(v, yaw + Math.PI / 2);
        }

        private static Vec3d RotateX(Vec3d v, double a)
        {
            return new Vec3d(v.X, v.Y * Math.Cos(a) - v.Z * Math.Sin(a), v.Y * Math.Sin(a) + v.Z * Math.Cos(a));
        }

        private static Vec3d RotateY(Vec3d v, double a)
        {
            return new Vec3d(v.X * Math.Cos(a) + v.Z * Math.Sin(a), v.Y, -v.X * Math.Sin(a) + v.Z * Math.Cos(a));
        }

        private static Vec3d RotateZ(Vec3d v, double a)
        {
            return new Vec3d(v.X * Math.Cos(a) - v.Y * Math.Sin(a), v.X * Math.Sin(a) + v.Y * Math.Cos(a), v.Z);
        }

        private static void AssertSame(Vec3d expected, Vec3d actual)
        {
            Assert.Equal(expected.X, actual.X, 4);
            Assert.Equal(expected.Y, actual.Y, 4);
            Assert.Equal(expected.Z, actual.Z, 4);
        }

        // Рамка колії: уздовж, убік і вгору, взаємно перпендикулярні
        private static void Frame(Vec3d along, double cantRad, out Vec3d forward, out Vec3d side, out Vec3d up)
        {
            forward = along.Clone().Normalize();
            Vec3d level = new Vec3d(-forward.Z, 0, forward.X).Normalize();
            Vec3d upright = level.Cross(forward);
            if (upright.Y < 0) upright.Mul(-1);

            // Нахил полотна: бік і верх повернуті навколо напрямку колії
            side = level.Clone().Mul(Math.Cos(cantRad)).Add(upright.Clone().Mul(Math.Sin(cantRad)));
            up = side.Cross(forward);
            if (up.Y < 0) up.Mul(-1);
        }

        [Theory]
        // рівна колія на схід, на південь і навскоси
        [InlineData(1, 0, 0, 0)]
        [InlineData(0, 0, 1, 0)]
        [InlineData(-1, 0, 1, 0)]
        // підйом і спуск
        [InlineData(1, 0.5, 0, 0)]
        [InlineData(0, -0.3, -1, 0)]
        // нахил полотна в кривій, на рівному і на підйомі
        [InlineData(1, 0, 1, 0.1)]
        [InlineData(-1, 0.4, 0.5, -0.1)]
        public void ModelNoseFollowsTheTrackAndRoofFollowsItsTop(double x, double y, double z, double cantRad)
        {
            Frame(new Vec3d(x, y, z), cantRad, out Vec3d forward, out Vec3d side, out Vec3d up);

            ModMath.TrackFrameToEntityAngles(forward, side, up, out float yaw, out float tilt, out float cant);

            // Ніс моделі це її вісь +X, верх це +Y
            AssertSame(forward, Rendered(yaw, tilt, cant, new Vec3d(1, 0, 0)));
            AssertSame(up, Rendered(yaw, tilt, cant, new Vec3d(0, 1, 0)));
        }

        // Курс, який дає функція, це той самий курс, за яким гра рахує, куди сутність дивиться
        [Fact]
        public void YawAgreesWithTheGamesViewVector()
        {
            Frame(new Vec3d(-1, 0, 2), 0, out Vec3d forward, out Vec3d side, out Vec3d up);

            ModMath.TrackFrameToEntityAngles(forward, side, up, out float yaw, out _, out _);

            Vec3f view = Vintagestory.API.Common.Entities.EntityPos.GetViewVector(0, yaw);
            Assert.Equal(forward.X, view.X, 4);
            Assert.Equal(forward.Z, view.Z, 4);
        }
    }
}
