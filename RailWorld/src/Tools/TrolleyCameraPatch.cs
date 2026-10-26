using System;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace RailWorld
{
    /// <summary>
    /// Поки гравець сидить у вагонетці, нахиляє його погляд разом із нею (TrolleySeat.ViewTilt). Підйом полотна
    /// вздовж погляду додається до тангажу погляду гравця, тим самим кутом, яким гравець крутить мишкою: щокадру
    /// лише зміна з минулого кадру, тож мишкою він далі крутить відносно кузова, а камера, промінь вибору блока і
    /// відсікання чанків беруть його з одного місця. Ванільний режим сидіння Push, що мав би робити те саме, на
    /// крутій спіралі тягнув погляд до межі, тож зміну переносимо самі. Підйом полотна впоперек погляду хилить
    /// горизонт: камера обертається навколо напрямку погляду, а це центр екрана не зсуває. Повертаються
    /// обидві матриці камери: CameraMatrixOrigin, якою малюється світ, і CameraMatrix, з якої гра будує піраміду
    /// видимості для відсікання чанків (ClientMain, CalcFrustumEquations); інакше на краях екрана було б видно діри.
    /// Вбудовується в Camera.Update гри, одразу після того як вона порахувала обидві матриці.
    /// </summary>
    public static class TrolleyCameraPatch
    {
        private static ICoreClientAPI capi;
        private static readonly Matrixf tmp = new Matrixf();
        private static readonly double[] rotation = Mat4d.Create();
        private static readonly double[] identity = Mat4d.Create();
        // Підйом полотна вздовж погляду, уже доданий до тангажу гравця; NaN, поки гравець не в вагонетці
        private static double appliedPitch = double.NaN;

        public static void Init(Harmony harmony, ICoreClientAPI api)
        {
            var update = AccessTools.Method(typeof(Camera), "Update");
            if (update == null)
            {
                api.Logger.Error("[RailWorld] у грі немає методу Camera.Update: камера пасажира не нахилятиметься разом з вагонеткою");
                return;
            }

            capi = api;
            harmony.Patch(update, postfix: new HarmonyMethod(typeof(TrolleyCameraPatch), nameof(UpdatePostfix)));
        }

        /// <summary>Світ покинуто: латку знімає RailWaySystem, тут лише відпускаємо гру.</summary>
        public static void Release()
        {
            capi = null;
        }

        public static void UpdatePostfix(Camera __instance)
        {
            EntityPlayer player = capi?.World?.Player?.Entity;
            if (!(player?.MountedOn is TrolleySeat seat) || seat.Entity == null)
            {
                appliedPitch = double.NaN;
                return;
            }

            TrolleySeat.ViewTilt(seat.Entity, tmp, player.Pos.Yaw, out double viewPitch, out double roll);

            // Погляд іде за кузовом: додаємо лише те, на скільки підйом полотна вздовж погляду змінився з минулого
            // кадру. У гравця менший тангаж це погляд вище. Щойно сівши, нічого не додаємо: погляд лишається, як був
            if (!double.IsNaN(appliedPitch))
            {
                float pitch = GameMath.Clamp(player.Pos.Pitch - (float)(viewPitch - appliedPitch), 1.5857964f, 4.697389f);
                player.Pos.Pitch = pitch;
                capi.Input.MousePitch = pitch;
            }
            appliedPitch = viewPitch;

            if (Math.Abs(roll) < 1e-4) return;

            // Обертання в просторі камери навколо осі погляду (Z камери): множиться зліва на матрицю камери. Гравця
            // нахиляє разом із кузовом, тож світ для нього повертається у зворотний бік: де полотно вище, горизонт нижче
            Mat4d.Rotate(rotation, Mat4d.Identity(identity), -roll, 0, 0, 1);
            Mat4d.Mul(__instance.CameraMatrix, rotation, __instance.CameraMatrix);
            Mat4d.Mul(__instance.CameraMatrixOrigin, rotation, __instance.CameraMatrixOrigin);
            for (int i = 0; i < 16; i++)
            {
                __instance.CameraMatrixOriginf[i] = (float)__instance.CameraMatrixOrigin[i];
            }
        }
    }
}
