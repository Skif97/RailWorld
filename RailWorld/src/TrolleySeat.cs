using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace RailWorld
{
    /// <summary>
    /// Місце у вагонетці. Працює зі стандартною поведінкою seatable, але, на відміну від сидінь човна чи тварини,
    /// не прив'язане до точки кріплення в моделі: гравець сидить у точці riderOffset з налаштувань сидіння.
    /// </summary>
    public class TrolleySeat : EntityRideableSeat
    {
        private const string DefaultAnimation = "sneakidle";

        public TrolleySeat(IMountable mountable, string seatId, SeatConfig config) : base(mountable, seatId, config)
        {
        }

        private string Animation => config?.Animation ?? DefaultAnimation;

        // Гравець вільно крутить головою, а поворот вагонетки додається до його погляду
        public override EnumMountAngleMode AngleMode => config?.AngleMode ?? EnumMountAngleMode.PushYaw;

        public override Vec3f LocalEyePos
        {
            get
            {
                eyePos.Set(config?.EyeOffsetX ?? 0f, config?.EyeHeight ?? 1.1f, 0f);
                return eyePos;
            }
        }

        /// <summary>
        /// Як нахилене полотно під вагонеткою відносно погляду пасажира, що дивиться з курсом yaw (кут гравця):
        /// pitch це підйом полотна вздовж погляду, радіани, додатний, якщо попереду вище; roll це підйом полотна
        /// впоперек погляду, додатний, якщо праворуч вище. Пасажир сидить у кузові жорстко, тож тангаж погляду
        /// йде за pitch, а горизонт хилиться на roll (обидва ставить TrolleyCameraPatch).
        /// </summary>
        public static void ViewTilt(Entity trolley, Matrixf tmp, double yaw, out double pitch, out double roll)
        {
            // Куди верх вагонетки у світових координатах: той самий поворот, яким рендер ставить її модель, а верх
            // моделі це її вісь +Y, тобто другий стовпець матриці. Береться прямо з матриці: кличеться щокадру
            tmp.Identity();
            RotateLikeRenderer(trolley, tmp);
            double upX = tmp.Values[4], upY = tmp.Values[5], upZ = tmp.Values[6];
            // Погляд гравця по горизонталі, як у EntityPos.GetViewVector при горизонтальному тангажі, і праворуч від нього
            double hx = Math.Sin(yaw), hz = Math.Cos(yaw);
            double rx = -hz, rz = hx;
            // Полотно це площина з нормаллю up: на одиницю шляху в напрямку d вона піднімається на -(up · d) / up.Y
            upY = Math.Max(upY, 1e-6);
            pitch = Math.Atan(-(upX * hx + upZ * hz) / upY);
            roll = Math.Atan(-(upX * rx + upZ * rz) / upY);
        }

        // Поворот моделі вагонетки, як його збирає рендер: Rx(Pitch) * Ry(Yaw + 90°) * Rz(Roll) * Rx(нахил полотна).
        // Нахил полотна лежить не в кутах сутності, а окремо, див. ModMath.TrackFrameToEntityAngles
        private static void RotateLikeRenderer(Entity trolley, Matrixf matrix)
        {
            matrix.RotateX(trolley.Pos.Pitch);
            matrix.RotateY(trolley.Pos.Yaw + GameMath.PIHALF);
            matrix.RotateZ(trolley.Pos.Roll);
            matrix.RotateX((trolley as EntityTrolley)?.Cant ?? 0f);
        }

        public override EntityPos SeatPosition
        {
            get
            {
                Entity trolley = Entity;
                float half = trolley.SelectionBox.Y2 / 2;

                // Куди дивиться перед: перший стовпець повороту, бо перед моделі це її вісь +X. Гра множить матрицю на
                // вектор стовпцями (Mat4f.MulWithVec4), тож вісь X моделі після повороту це числа 0, 1, 2
                modelmat.Identity();
                RotateLikeRenderer(trolley, modelmat);
                float forwardX = modelmat.Values[0], forwardZ = modelmat.Values[2];

                // Точка сидіння нахиляється разом із вагонеткою: рендер обертає модель навколо середини її висоти
                modelmat.Identity();
                modelmat.Translate(0f, half, 0f);
                RotateLikeRenderer(trolley, modelmat);
                modelmat.Translate(0f, -half, 0f);
                if (config?.RiderOffset != null) modelmat.Translate(config.RiderOffset);

                // Куди переходить початок координат: останній стовпець матриці
                float[] matrix = modelmat.Values;
                seatPos.SetFrom(mountedEntity.Position).Add(matrix[12], matrix[13], matrix[14]);

                // Кут сидіння це курс вагонетки по горизонталі. Він не має залежати від гравця: гра додає
                // до погляду гравця зміну цього кута, і якщо брати його з гравця, погляд розкручується сам від себе.
                // Нахили вагонетки до погляду переносить TrolleyCameraPatch, тому тут їх немає
                seatPos.Pitch = 0;
                seatPos.Roll = 0;
                seatPos.Yaw = (float)Math.Atan2(forwardX, forwardZ);
                return seatPos;
            }
        }

        public override Matrixf RenderTransform
        {
            get { return modelmat.Identity(); }
        }

        public override void DidMount(EntityAgent entityAgent)
        {
            if (Passenger != null && Passenger != entityAgent)
            {
                (Passenger as EntityAgent)?.TryUnmount();
                return;
            }

            Passenger = entityAgent;
            entityAgent.AnimManager?.StartAnimation(Animation);

            (mountedEntity as IMountableListener)?.DidMount(entityAgent);
            (Entity as IMountableListener)?.DidMount(entityAgent);
            Entity.Api.Event.TriggerEntityMounted(entityAgent, this);
        }

        public override void DidUnmount(EntityAgent entityAgent)
        {
            Passenger?.AnimManager?.StopAnimation(Animation);
            base.DidUnmount(entityAgent);
        }
    }
}
