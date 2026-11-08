using System;
using RailWorld.src.RailWay;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace RailWorld
{
    /// <summary>
    /// Вагон на двох візках. Їде так само, як вагонетка: сервер веде одну точку колії під серединою вагона, з тим
    /// самим мотором, ударами й сидінням. Різниця на екрані: кузов стоїть не на одній точці, а на двох, під візками
    /// за половину бази вперед і назад уздовж колії, і лежить на хорді між ними. На кривій середина кузова тоді
    /// виходить усередину, а кінці назовні, як у справжнього вагона. Кожен візок повертається відносно кузова за
    /// колією під ним: анімація повороту з нульовою швидкістю, кадр якої ставиться з кута, як колеса вагонетки.
    /// </summary>
    public class EntityWagon : EntityTrolley
    {
        // Половина бази вагона, від середини до шворня візка, блоків. З властивості bogieBase сутності
        private double bogieHalf = DefaultBogieBase / 2;
        private const double DefaultBogieBase = 12.6;

        // Анімації візків (shapes/entity/wagon-passenger.json), задній A, передній B: поворот убік від -MaxBogieTurn до
        // +MaxBogieTurn градусів і нахил уздовж колії від -MaxBogiePitch до +MaxBogiePitch за всі кадри
        private static readonly BogieAnimations BackBogie = new BogieAnimations("bogiea"), FrontBogie = new BogieAnimations("bogieb");
        private const double MaxBogieTurn = 30, MaxBogiePitch = 15;

        private sealed class BogieAnimations
        {
            public readonly string Turn, Pitch;
            public readonly string[] TurnOnly, PitchOnly;
            public BogieAnimations(string code)
            {
                Turn = code;
                Pitch = code + "pitch";
                TurnOnly = new[] { Turn };
                PitchOnly = new[] { Pitch };
            }
        }

        protected override double TrackReach => base.TrackReach + bogieHalf;

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);
            bogieHalf = (properties.Attributes?["bogieBase"].AsDouble(DefaultBogieBase) ?? DefaultBogieBase) / 2;
        }

        protected override void PlaceBody(double at, Section here, in SectionChord chord, double s, int facing, double halfHeight, out float cant)
        {
            // Куди дивиться перед вагона у світі: уздовж секції під серединою, у бік facing
            double noseX = chord.AlongX * facing, noseY = chord.AlongY * facing, noseZ = chord.AlongZ * facing;
            if (!BogieAt(at + bogieHalf, out double ax, out double ay, out double az, out SectionChord aChord)
                || !BogieAt(at - bogieHalf, out double bx, out double by, out double bz, out SectionChord bChord))
            {
                base.PlaceBody(at, here, chord, s, facing, halfHeight, out cant);
                return;
            }
            // Котра з двох точок попереду: ланцюжок може йти назустріч переду
            if ((ax - bx) * noseX + (ay - by) * noseY + (az - bz) * noseZ < 0)
            {
                (ax, bx) = (bx, ax); (ay, by) = (by, ay); (az, bz) = (bz, az);
                (aChord, bChord) = (bChord, aChord);
            }

            // Верх: середній між полотном під двома візками
            double ux = aChord.UpX + bChord.UpX, uy = aChord.UpY + bChord.UpY, uz = aChord.UpZ + bChord.UpZ;
            RailPose.PlaceOnBogies(Pos, bx, by, bz, ax, ay, az, ux, uy, uz, halfHeight, out cant);

            if (AnimManager?.Animator == null) return;
            // Рамка кузова: уздовж k від заднього візка до переднього, вгору v перпендикулярно до нього, убік вісь Z
            // моделі (уздовж × вгору)
            double kx = ax - bx, ky = ay - by, kz = az - bz;
            double length = Math.Sqrt(kx * kx + ky * ky + kz * kz);
            if (length < 1e-6) return;
            kx /= length; ky /= length; kz /= length;
            double dot = ux * kx + uy * ky + uz * kz;
            double vx = ux - kx * dot, vy = uy - ky * dot, vz = uz - kz * dot;
            double up = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            if (up < 1e-6) return;
            vx /= up; vy /= up; vz /= up;
            double sx = ky * vz - kz * vy, sy = kz * vx - kx * vz, sz = kx * vy - ky * vx;
            TurnBogie(FrontBogie, aChord, kx, ky, kz, vx, vy, vz, sx, sy, sz);
            TurnBogie(BackBogie, bChord, kx, ky, kz, vx, vy, vz, sx, sy, sz);
        }

        // Точка на осі колії за координатою ланцюжка at і хорда секції там
        private bool BogieAt(double at, out double x, out double y, out double z, out SectionChord chord)
        {
            x = y = z = 0;
            if (!Chain.At(at, out _, out chord, out double s, out _)) return false;
            x = chord.StartX + chord.AlongX * s;
            y = chord.StartY + chord.AlongY * s;
            z = chord.StartZ + chord.AlongZ * s;
            return true;
        }

        // Ставить візок за колією під ним: напрям хорди секції під візком у рамці кузова (уздовж k, вгору v, убік s)
        // дає кут повороту вбік і кут нахилу вздовж колії
        private void TurnBogie(BogieAnimations bogie, in SectionChord chord, double kx, double ky, double kz,
            double vx, double vy, double vz, double sx, double sy, double sz)
        {
            double along = chord.AlongX * kx + chord.AlongY * ky + chord.AlongZ * kz;
            double rise = chord.AlongX * vx + chord.AlongY * vy + chord.AlongZ * vz;
            double across = chord.AlongX * sx + chord.AlongY * sy + chord.AlongZ * sz;
            // Хорда може дивитися назад відносно кузова: візку однаково, повертається він на менший кут
            if (along < 0) { along = -along; rise = -rise; across = -across; }
            double flat = Math.Sqrt(along * along + across * across);
            SetFrame(bogie.Turn, bogie.TurnOnly, Math.Atan2(-across, along) * GameMath.RAD2DEG, MaxBogieTurn);
            SetFrame(bogie.Pitch, bogie.PitchOnly, Math.Atan2(rise, flat) * GameMath.RAD2DEG, MaxBogiePitch);
        }

        // Кадр анімації з нульовою швидкістю, що від першого до останнього кадру повертає від -max до +max градусів
        private void SetFrame(string code, string[] only, double degrees, double max)
        {
            if (!AnimManager.IsAnimationActive(only)) AnimManager.StartAnimation(code);
            RunningAnimation state = AnimManager.GetAnimationState(code);
            if (state == null) return;
            double clamped = GameMath.Clamp(degrees, -max, max);
            state.CurrentFrame = (float)((clamped + max) / (2 * max) * (state.Animation.QuantityFrames - 1));
            state.BlendedWeight = 1f;
            state.EasingFactor = 1f;
        }
    }
}
