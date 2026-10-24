using System;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Хорда секції, якою по ній їде вагонетка: від початку до кінця, з рамкою колії на ній (уздовж, убік з
    /// нахилом полотна, вгору). Геометрія секції після створення не міняється, тож хорду можна порахувати раз
    /// і тримати: клієнт рахує по ній щокадру, і нових об'єктів на це не йде.
    /// </summary>
    public readonly struct SectionChord
    {
        public readonly double StartX, StartY, StartZ;
        public readonly double AlongX, AlongY, AlongZ;
        public readonly double SideX, SideY, SideZ;
        public readonly double UpX, UpY, UpZ;
        public readonly double Length;
        /// <summary>false, якщо секція нульової довжини: напрямку в неї немає.</summary>
        public readonly bool Valid;

        public SectionChord(Section section)
        {
            FastVec3d start = section.Start;
            FastVec3d end = section.End;
            StartX = start.X; StartY = start.Y; StartZ = start.Z;

            double ax = end.X - start.X, ay = end.Y - start.Y, az = end.Z - start.Z;
            Length = Math.Sqrt(ax * ax + ay * ay + az * az);
            Valid = Length >= 1e-9;
            if (!Valid)
            {
                AlongX = AlongY = AlongZ = SideX = SideY = SideZ = UpX = UpY = UpZ = 0;
                return;
            }
            AlongX = ax / Length; AlongY = ay / Length; AlongZ = az / Length;

            // Убік: нормаль середини секції без складової вздовж хорди
            Vec3f normal = section.CenterNormal;
            double dot = normal.X * AlongX + normal.Y * AlongY + normal.Z * AlongZ;
            double sx = normal.X - AlongX * dot, sy = normal.Y - AlongY * dot, sz = normal.Z - AlongZ * dot;
            double sideLength = Math.Sqrt(sx * sx + sy * sy + sz * sz);
            if (sideLength > 1e-12) { sx /= sideLength; sy /= sideLength; sz /= sideLength; }
            SideX = sx; SideY = sy; SideZ = sz;

            // Вгору: убік × уздовж
            UpX = sy * AlongZ - sz * AlongY;
            UpY = sz * AlongX - sx * AlongZ;
            UpZ = sx * AlongY - sy * AlongX;
        }
    }

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
            return Math.Sqrt(section.Start.DistanceSq(section.End));
        }

        /// <summary>
        /// Ставить вагонетку на відстані s від початку секції, з поворотом, ухилом і нахилом полотна хорди.
        /// halfHeight це половина висоти боксу сутності, facing каже, в який бік секції дивиться перед: 1 у бік
        /// кінця, -1 у бік початку. Так її ставить сервер.
        /// </summary>
        public static void Place(EntityPos pos, Section section, double s, double halfHeight, int facing, out float cant)
        {
            Place(pos, new SectionChord(section), s, halfHeight, facing, out cant);
        }

        private static void Place(EntityPos pos, in SectionChord chord, double s, double halfHeight, int facing, out float cant)
        {
            cant = 0;
            if (!chord.Valid) return;

            SetAngles(pos, chord.AlongX, chord.AlongY, chord.AlongZ, chord.UpX, chord.UpY, chord.UpZ, facing, out cant);
            SetPosition(pos, chord, s, halfHeight);
        }

        /// <summary>
        /// Те саме місце, але кути беруться з колії в цьому місці: між трьома точками секції вона гнеться
        /// плавно, тож і кути міняються плавно, а не сходинкою на межі секцій. Так вагонетку малює клієнт.
        /// chord це хорда секції section.
        /// </summary>
        public static void PlaceSmooth(EntityPos pos, Section section, in SectionChord chord, double s, double halfHeight, int facing, out float cant)
        {
            cant = 0;
            if (!chord.Valid) return;

            // Котра половина секції і яка частка її: середина секції лежить на кривій, а не на хорді, тож частку
            // беремо від довжини всієї хорди
            double u = GameMath.Clamp(s / chord.Length, 0, 1);
            bool first = u < 0.5;
            double t = first ? u * 2 : u * 2 - 1;

            Vec3f tangentFrom = first ? section.StartTangent : section.CenterTangent;
            Vec3f tangentTo = first ? section.CenterTangent : section.EndTangent;
            Vec3f normalFrom = first ? section.StartNormal : section.CenterNormal;
            Vec3f normalTo = first ? section.CenterNormal : section.EndNormal;

            double ax = Mix(tangentFrom.X, tangentTo.X, t), ay = Mix(tangentFrom.Y, tangentTo.Y, t), az = Mix(tangentFrom.Z, tangentTo.Z, t);
            double sx = Mix(normalFrom.X, normalTo.X, t), sy = Mix(normalFrom.Y, normalTo.Y, t), sz = Mix(normalFrom.Z, normalTo.Z, t);

            double alongLength = Math.Sqrt(ax * ax + ay * ay + az * az);
            if (alongLength < 1e-6)
            {
                Place(pos, chord, s, halfHeight, facing, out cant);
                return;
            }
            ax /= alongLength; ay /= alongLength; az /= alongLength;
            // Дотична має дивитися від початку секції до кінця
            if (ax * chord.AlongX + ay * chord.AlongY + az * chord.AlongZ < 0) { ax = -ax; ay = -ay; az = -az; }

            double dot = sx * ax + sy * ay + sz * az;
            sx -= ax * dot; sy -= ay * dot; sz -= az * dot;
            double sideLength = Math.Sqrt(sx * sx + sy * sy + sz * sz);
            if (sideLength < 1e-6)
            {
                Place(pos, chord, s, halfHeight, facing, out cant);
                return;
            }
            sx /= sideLength; sy /= sideLength; sz /= sideLength;

            // Вгору: убік × уздовж
            double ux = sy * az - sz * ay, uy = sz * ax - sx * az, uz = sx * ay - sy * ax;

            SetAngles(pos, ax, ay, az, ux, uy, uz, facing, out cant);
            // Місце те саме, що й на сервері: на хорді
            SetPosition(pos, chord, s, halfHeight);
        }

        /// <summary>
        /// Зворотне до Place: на якій відстані s від початку секції стоїть вагонетка з місцем (x, y, z), і як
        /// далеко це місце від секції. s обрізається до самої секції. chord це хорда секції.
        /// </summary>
        public static double Distance(in SectionChord chord, double x, double y, double z, double halfHeight, out double s)
        {
            s = 0;
            if (!chord.Valid) return double.MaxValue;

            // Те, що Place додав до точки на хорді, віднімаємо
            double lift = Lift(halfHeight);
            x -= chord.UpX * lift + chord.StartX;
            y -= chord.UpY * lift - halfHeight + chord.StartY;
            z -= chord.UpZ * lift + chord.StartZ;

            s = GameMath.Clamp(x * chord.AlongX + y * chord.AlongY + z * chord.AlongZ, 0, chord.Length);
            x -= chord.AlongX * s;
            y -= chord.AlongY * s;
            z -= chord.AlongZ * s;
            return Math.Sqrt(x * x + y * y + z * z);
        }

        // Рендер обертає модель навколо середини її висоти, тому позиція вища за вісь колії на рейку і пів
        // висоти, уздовж верху полотна, і нижча на пів висоти: тоді після нахилу низ моделі лишається на осі колії
        private static double Lift(double halfHeight)
        {
            return SectionBox.RailHeight + halfHeight;
        }

        private static void SetAngles(EntityPos pos, double ax, double ay, double az, double ux, double uy, double uz, int facing, out float cant)
        {
            // Розворот переду це поворот на 180° навколо верху: уздовж міняє знак, верх лишається
            if (facing < 0) { ax = -ax; ay = -ay; az = -az; }

            // Ухил іде в Roll: рендер застосовує його одразу після курсу, тобто навколо поперечної осі вагонетки.
            // Нахил полотна повертається окремо, Pitch лишається нулем
            ModMath.TrackFrameToEntityAngles(ax, ay, az, ux, uy, uz, out float yaw, out float tilt, out cant);
            pos.Yaw = yaw;
            pos.Pitch = 0;
            pos.Roll = tilt;
        }

        private static void SetPosition(EntityPos pos, in SectionChord chord, double s, double halfHeight)
        {
            double lift = Lift(halfHeight);
            pos.X = chord.StartX + chord.AlongX * s + chord.UpX * lift;
            pos.Y = chord.StartY + chord.AlongY * s + chord.UpY * lift - halfHeight;
            pos.Z = chord.StartZ + chord.AlongZ * s + chord.UpZ * lift;
        }

        private static double Mix(float from, float to, double t)
        {
            return from + (to - from) * t;
        }
    }
}
