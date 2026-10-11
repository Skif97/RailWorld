using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Точка розмітки маршруту: те, що поставив гравець. Буває двох видів: на блоці, тоді положення
    /// й напрямок беруться з блока і погляду, і на стику вже покладеної колії, тоді все береться з колії.
    /// З таких точок RouteBuilder складає маршрут. Клієнт робить це для показу, сервер для побудови,
    /// і мережею їдуть саме точки, а не готовий маршрут: що з них вийде, вирішує сервер.
    /// </summary>
    public class RouteMarker
    {
        public BlockPos Block;
        public Vec3d Position;
        /// <summary>Напрямок руху по маршруту в цій точці.</summary>
        public Vec3d Heading;
        /// <summary>Точка взята зі стику вже покладеної колії.</summary>
        public bool Snapped;
        /// <summary>Маршрут має пройти точно через цю точку. Інакше проміжна точка це кут, який маршрут скруглює.</summary>
        public bool Exact;
        /// <summary>Нахил полотна в цьому стику; NaN для точки не на колії.</summary>
        public double Cant = double.NaN;
        /// <summary>Секція покладеної колії, поверх якої маршрут лежить одразу за цим стиком, і котрим кінцем вона в стику.</summary>
        public Section Track;
        public bool TrackJointAtStart;

        /// <summary>Для точки на блоці: кут погляду гравця, з якого взято напрямок.</summary>
        public double Yaw;

        /// <summary>
        /// Для точки на колії: секція, на кінці якої вона стоїть, котрий це кінець, і в який бік відносно
        /// виходу із секції йде маршрут: 1 назовні, -1 всередину.
        /// </summary>
        public Section Joint;
        public bool JointAtStart;
        public int Sign;

        /// <summary>
        /// Точка на блоці: над його серединою, на товщину шпали над підлогою, як і в інших режимах укладання.
        /// Напрямок це погляд гравця, округлений до 45 градусів.
        /// </summary>
        public static RouteMarker OnBlock(BlockPos block, double yaw)
        {
            return new RouteMarker
            {
                Block = block,
                Position = new Vec3d(block.X + 0.5, block.Y + SectionBox.SleeperDepth, block.Z + 0.5),
                Heading = ModMath.GetDirectionVectorXZ(yaw).Normalize(),
                Yaw = yaw
            };
        }

        /// <summary>
        /// Точка на стику вже покладеної колії: кінець atStart секції section. Маршрут може від нього почати,
        /// це відгалуження, або ним закінчити, це примикання. first каже, чи це перша точка маршруту.
        /// Положення, напрямок і нахил полотна беруться з секції.
        /// </summary>
        public static RouteMarker OnJoint(Section section, bool atStart, int sign, bool first, System.Func<SectionLink, Section> resolve)
        {
            Vec3d position = section.GetEndPosition(atStart);
            Vec3f o = section.GetOutwardDirection(atStart);
            Vec3d outward = new Vec3d(o.X, o.Y, o.Z).Normalize();
            Vec3d heading = outward.Clone().Mul(sign);

            // Поверх якої секції маршрут піде за стиком. Чіпляється він до одного кінця, а лежить на тому,
            // що по інший бік стику: якщо причепився до кінця цієї секції, то на її сусіді, і навпаки
            bool ownEnd = first ? sign > 0 : sign < 0;
            Section track = section;
            bool trackJointAtStart = atStart;
            if (ownEnd)
            {
                List<SectionLink> links = section.GetLinks(atStart);
                track = links.Count == 1 ? resolve(links[0]) : null;
                trackJointAtStart = links.Count == 1 && links[0].AtStart;
            }

            // Нахил полотна в стику, у знаку маршруту: його нормаль дивиться ліворуч від напрямку руху,
            // а нормаль секції може дивитися і в інший бік
            Vec3f normal = atStart ? section.StartNormal : section.EndNormal;
            double sameSide = normal.X * -heading.Z + normal.Z * heading.X >= 0 ? 1 : -1;
            double cant = Math.Asin(GameMath.Clamp(sameSide * normal.Y, -1, 1));

            return new RouteMarker
            {
                Block = position.AsBlockPos,
                Position = position,
                Heading = heading,
                Snapped = true,
                Cant = cant,
                Track = track,
                TrackJointAtStart = trackJointAtStart,
                Joint = section,
                JointAtStart = atStart,
                Sign = sign
            };
        }
    }
}
