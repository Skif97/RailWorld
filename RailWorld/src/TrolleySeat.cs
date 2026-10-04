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
        /// Куди дивиться перед вагонетки і куди її верх, у світових координатах.
        /// Рахується з кутів сутності тим самим поворотом, яким рендер ставить її модель:
        /// Rx(Pitch) * Ry(Yaw + 90°) * Rz(Roll), перед моделі це її вісь -X.
        /// </summary>
        public static void GetTrolleyFrame(Entity trolley, Matrixf tmp, out Vec3d forward, out Vec3d up)
        {
            tmp.Identity();
            tmp.RotateX(trolley.Pos.Pitch);
            tmp.RotateY(trolley.Pos.Yaw + GameMath.PIHALF);
            tmp.RotateZ(trolley.Pos.Roll);

            Vec4f f = tmp.TransformVector(new Vec4f(-1, 0, 0, 0));
            Vec4f u = tmp.TransformVector(new Vec4f(0, 1, 0, 0));
            forward = new Vec3d(f.X, f.Y, f.Z);
            up = new Vec3d(u.X, u.Y, u.Z);
        }

        public override EntityPos SeatPosition
        {
            get
            {
                Entity trolley = Entity;
                float half = trolley.SelectionBox.Y2 / 2;

                GetTrolleyFrame(trolley, modelmat, out Vec3d forward, out _);

                // Точка сидіння нахиляється разом із вагонеткою: рендер обертає модель навколо середини її висоти
                modelmat.Identity();
                modelmat.Translate(0f, half, 0f);
                modelmat.RotateX(trolley.Pos.Pitch);
                modelmat.RotateY(trolley.Pos.Yaw + GameMath.PIHALF);
                modelmat.RotateZ(trolley.Pos.Roll);
                modelmat.Translate(0f, -half, 0f);
                if (config?.RiderOffset != null) modelmat.Translate(config.RiderOffset);

                Vec4f offset = modelmat.TransformVector(new Vec4f(0, 0, 0, 1));
                seatPos.SetFrom(mountedEntity.Position).Add(offset.X, offset.Y, offset.Z);

                // Кут сидіння це курс вагонетки по горизонталі. Він не має залежати від гравця: гра додає
                // до погляду гравця зміну цього кута, і якщо брати його з гравця, погляд розкручується сам від себе.
                // Нахили вагонетки камері передає TrolleyCameraPatch, тому тут їх немає
                seatPos.Pitch = 0;
                seatPos.Roll = 0;
                seatPos.Yaw = (float)Math.Atan2(forward.X, forward.Z);
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
