using System;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Як вагонетка стоїть на секції колії. Сервер веде її по хорді секції, від початку до кінця: місце це
    /// відстань s від початку вздовж хорди, колесами на головці рейки. Тут і те, як поставити вагонетку на це
    /// місце, і зворотне: яке місце на секції відповідає положенню вагонетки.
    /// </summary>
    public static class RailPose
    {
        /// <summary>Довжина секції, якою її проїжджає вагонетка: хорда від початку до кінця.</summary>
        public static double Length(Section section)
        {
            return section.FullStartPosition.DistanceTo(section.FullEndPosition);
        }

        /// <summary>
        /// Ставить вагонетку на відстані s від початку секції, з поворотом, ухилом і нахилом полотна хорди.
        /// halfHeight це половина висоти боксу сутності, facing каже, в який бік секції дивиться перед: 1 у бік
        /// кінця, -1 у бік початку. Так її ставить сервер.
        /// </summary>
        public static void Place(EntityPos pos, Section section, double s, double halfHeight, int facing, out float cant)
        {
            cant = 0;
            if (!ChordFrame(section, out Vec3d along, out Vec3d side, out Vec3d up)) return;

            SetAngles(pos, along, side, up, facing, out cant);
            SetPosition(pos, section, s, halfHeight, along, up);
        }

        /// <summary>
        /// Те саме місце, але кути беруться з колії в цьому місці: між трьома точками секції вона гнеться
        /// плавно, тож і кути міняються плавно, а не сходинкою на межі секцій. Так вагонетку малює клієнт.
        /// </summary>
        public static void PlaceSmooth(EntityPos pos, Section section, double s, double halfHeight, int facing, out float cant)
        {
            cant = 0;
            if (!ChordFrame(section, out Vec3d chord, out _, out Vec3d chordUp)) return;

            // Котра половина секції і яка частка її: середина секції лежить на кривій, а не на хорді, тож частку
            // беремо від довжини всієї хорди
            double length = Length(section);
            double u = GameMath.Clamp(s / length, 0, 1);
            int part = u < 0.5 ? 0 : 1;
            double t = part == 0 ? u * 2 : u * 2 - 1;

            Vec3f[] tangents = { section.StartTangent, section.CenterTangent, section.EndTangent };
            Vec3f[] normals = { section.StartNormal, section.CenterNormal, section.EndNormal };

            Vec3d along = Mixed(tangents[part], tangents[part + 1], t);
            Vec3d side = Mixed(normals[part], normals[part + 1], t);
            if (along.Length() < 1e-6)
            {
                Place(pos, section, s, halfHeight, facing, out cant);
                return;
            }
            along.Normalize();
            // Дотична має дивитися від початку секції до кінця
            if (along.Dot(chord) < 0) along.Mul(-1);

            side.Sub(along.Clone().Mul(side.Dot(along)));
            if (side.Length() < 1e-6)
            {
                Place(pos, section, s, halfHeight, facing, out cant);
                return;
            }
            side.Normalize();
            Vec3d up = side.Cross(along);

            SetAngles(pos, along, side, up, facing, out cant);
            // Місце те саме, що й на сервері: на хорді
            SetPosition(pos, section, s, halfHeight, chord, chordUp);
        }

        /// <summary>
        /// Зворотне до Place: на якій відстані s від початку секції стоїть вагонетка з місцем position, і як
        /// далеко це місце від секції. s обрізається до самої секції.
        /// </summary>
        public static double Distance(Section section, Vec3d position, double halfHeight, out double s)
        {
            s = 0;
            if (!ChordFrame(section, out Vec3d along, out _, out Vec3d up)) return double.MaxValue;

            // Те, що Place додав до точки на хорді, віднімаємо
            double lift = Lift(halfHeight);
            Vec3d start = section.FullStartPosition;
            double x = position.X - up.X * lift - start.X;
            double y = position.Y - up.Y * lift + halfHeight - start.Y;
            double z = position.Z - up.Z * lift - start.Z;

            s = GameMath.Clamp(x * along.X + y * along.Y + z * along.Z, 0, Length(section));
            x -= along.X * s;
            y -= along.Y * s;
            z -= along.Z * s;
            return Math.Sqrt(x * x + y * y + z * z);
        }

        // Рендер обертає модель навколо середини її висоти, тому позиція вища за вісь колії на рейку і пів
        // висоти, уздовж верху полотна, і нижча на пів висоти: тоді після нахилу низ моделі лишається на осі колії
        private static double Lift(double halfHeight)
        {
            return SectionBox.RailHeight + halfHeight;
        }

        // Рамка хорди секції: уздовж (від початку до кінця), убік (з нахилом полотна) і вгору
        private static bool ChordFrame(Section section, out Vec3d along, out Vec3d side, out Vec3d up)
        {
            Vec3d start = section.FullStartPosition;
            Vec3d end = section.FullEndPosition;
            along = new Vec3d(end.X - start.X, end.Y - start.Y, end.Z - start.Z);
            side = null;
            up = null;
            if (along.Length() < 1e-9) return false;
            along.Normalize();

            side = new Vec3d(section.CenterNormal.X, section.CenterNormal.Y, section.CenterNormal.Z);
            side.Sub(along.Clone().Mul(side.Dot(along))).Normalize();
            up = side.Cross(along);
            return true;
        }

        private static void SetAngles(EntityPos pos, Vec3d along, Vec3d side, Vec3d up, int facing, out float cant)
        {
            // Розворот переду це поворот на 180° навколо верху: уздовж і вбік міняють знак, верх лишається
            Vec3d forward = facing < 0 ? along.Clone().Mul(-1) : along;
            Vec3d right = facing < 0 ? side.Clone().Mul(-1) : side;

            // Ухил іде в Roll: рендер застосовує його одразу після курсу, тобто навколо поперечної осі вагонетки.
            // Нахил полотна повертається окремо, Pitch лишається нулем
            ModMath.TrackFrameToEntityAngles(forward, right, up, out float yaw, out float tilt, out cant);
            pos.Yaw = yaw;
            pos.Pitch = 0;
            pos.Roll = tilt;
        }

        private static void SetPosition(EntityPos pos, Section section, double s, double halfHeight, Vec3d along, Vec3d up)
        {
            Vec3d start = section.FullStartPosition;
            double lift = Lift(halfHeight);
            pos.X = start.X + along.X * s + up.X * lift;
            pos.Y = start.Y + along.Y * s + up.Y * lift - halfHeight;
            pos.Z = start.Z + along.Z * s + up.Z * lift;
        }

        private static Vec3d Mixed(Vec3f from, Vec3f to, double t)
        {
            return new Vec3d(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t, from.Z + (to.Z - from.Z) * t);
        }
    }
}
