using System;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace RailWorld
{
    /// <summary>
    /// Поки гравець сидить у вагонетці, нахиляє його камеру разом із нею: на ухилі й у повороті з нахилом полотна
    /// горизонт іде так, ніби гравець жорстко сидить у кузові. Крутити головою при цьому можна як завжди.
    /// Вбудовується в Camera.Update гри, одразу після того як вона порахувала матрицю камери.
    /// </summary>
    public static class TrolleyCameraPatch
    {
        private static ICoreClientAPI capi;
        private static bool patched;
        private static Matrixf tmp = new Matrixf();
        private static double[] axis = new double[3];

        public static void Init(Harmony harmony, ICoreClientAPI api)
        {
            capi = api;
            if (patched) return;
            patched = true;

            harmony.Patch(AccessTools.Method(typeof(Camera), "Update"),
                postfix: new HarmonyMethod(typeof(TrolleyCameraPatch), nameof(UpdatePostfix)));
        }

        public static void UpdatePostfix(Camera __instance)
        {
            EntityPlayer player = capi?.World?.Player?.Entity;
            if (!(player?.MountedOn is TrolleySeat seat) || seat.Entity == null) return;

            TrolleySeat.GetTrolleyFrame(seat.Entity, tmp, out _, out Vec3d up);

            // Найкоротший поворот, що переводить вертикаль у верх вагонетки: вісь = вертикаль × верх
            double angle = Math.Acos(GameMath.Clamp(up.Y, -1, 1));
            double axisLength = Math.Sqrt(up.Z * up.Z + up.X * up.X);
            if (angle < 1e-4 || axisLength < 1e-6) return;

            axis[0] = up.Z / axisLength;
            axis[1] = 0;
            axis[2] = -up.X / axisLength;

            // Камера жорстко сидить у нахиленій вагонетці, тому світ для неї повернутий у зворотний бік
            Mat4d.Rotate(__instance.CameraMatrixOrigin, __instance.CameraMatrixOrigin, -angle, axis);
            for (int i = 0; i < 16; i++)
            {
                __instance.CameraMatrixOriginf[i] = (float)__instance.CameraMatrixOrigin[i];
            }
        }
    }
}
