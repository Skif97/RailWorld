using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Готові форми колії з меню: один блок, пряма, поворот на 45 і 90 градусів. Шаблон сам колії не кладе:
    /// з блока, напрямку погляду і параметрів меню він ставить ті самі точки розмітки, які гравець поставив би
    /// руками в режимі «по точках». Далі з ними працює той самий код: перегляд, правила, побудова на сервері.
    /// </summary>
    public static class RouteTemplate
    {
        public const string SingleBlock = "SingleBlock";
        public const string Straight = "Straight";
        public const string Turn90 = "Turn90";
        public const string Turn45 = "Turn45";

        public static bool IsTemplate(string mode)
        {
            return mode == SingleBlock || mode == Straight || mode == Turn90 || mode == Turn45;
        }

        /// <summary>
        /// Точки шаблону. start це блок першої точки, yaw кут погляду гравця; напрямок округлюється до 45 градусів,
        /// як і в точок на блоці. lengthOrRadius це довжина прямої або радіус повороту в блоках, climb на скільки
        /// блоків кінець вищий за початок, left у який бік повертати.
        /// </summary>
        public static List<RouteMarker> Build(BlockPos start, double yaw, string mode, int lengthOrRadius, int climb, bool left)
        {
            int direction = Direction(yaw);
            var markers = new List<RouteMarker> { RouteMarker.OnBlock(start.Copy(), Yaw(direction)) };

            if (mode == Turn90 || mode == Turn45)
            {
                int eighths = mode == Turn90 ? 2 : 1;
                int turned = GameMath.Mod(direction + (left ? eighths : -eighths), 8);

                // Поворот це кут ламаної, який маршрут сам скруглює дугою. Дуга радіуса R забирає від кожної
                // з двох прямих R * tan(кут / 2); прямі беремо такі, щоб цього вистачило. Кут стоїть на блоці,
                // тому прямі виходять трохи довші, а радіус трохи більший за заданий, але не менший
                double radius = Math.Max(lengthOrRadius, Math.Ceiling(RouteRules.MinRadius));
                double cut = radius * Math.Tan(eighths * Math.PI / 8);

                BlockPos corner = Along(start, direction, Steps(cut, direction), (int)Math.Round(climb / 2.0));
                BlockPos end = Along(corner, turned, Steps(cut, turned), climb - (int)Math.Round(climb / 2.0));

                markers.Add(RouteMarker.OnBlock(corner, Yaw(direction)));
                markers.Add(RouteMarker.OnBlock(end, Yaw(turned)));
                return markers;
            }

            // Пряма займає стільки блоків, скільки задано; один блок це найкоротша пряма.
            // По діагоналі блок довший, тож їх треба менше
            int blocks = mode == Straight ? Math.Max(1, lengthOrRadius) : 1;
            int steps = (int)Math.Round((blocks - 1) / StepLength(direction));
            markers.Add(RouteMarker.OnBlock(Along(start, direction, steps, mode == Straight ? climb : 0), Yaw(direction)));
            return markers;
        }

        // Скільки кроків по блоках треба пройти в цьому напрямку, щоб дуга забрала від прямої cut блоків.
        // Кінець прямої стоїть на краю блока, це ще пів кроку; а блок прямої кут лишає сусідній точці
        // (RouteBuilder.RoundCorners), тому до cut додається один
        private static int Steps(double cut, int direction)
        {
            return Math.Max(1, (int)Math.Ceiling((cut + 1) / StepLength(direction) - 0.5));
        }

        // Один з восьми напрямків, до якого округлюється погляд: номер k це кут k * 45 градусів
        private static int Direction(double yaw)
        {
            Vec3d heading = ModMath.GetDirectionVectorXZ(yaw);
            return GameMath.Mod((int)Math.Round(Math.Atan2(-heading.Z, heading.X) / (Math.PI / 4)), 8);
        }

        // Кут погляду, який округлюється саме до цього напрямку. Трохи вбік від межі між секторами
        private static double Yaw(int direction)
        {
            return direction * Math.PI / 4 + 0.01;
        }

        private static double StepLength(int direction)
        {
            return direction % 2 == 0 ? 1 : Math.Sqrt(2);
        }

        // Блок за steps кроків у цьому напрямку і на rise блоків вище
        private static BlockPos Along(BlockPos from, int direction, int steps, int rise)
        {
            double angle = direction * Math.PI / 4;
            int dx = (int)Math.Round(Math.Cos(angle) * 1.4), dz = (int)Math.Round(-Math.Sin(angle) * 1.4);
            return new BlockPos(from.X + dx * steps, from.Y + rise, from.Z + dz * steps, from.dimension);
        }
    }
}
