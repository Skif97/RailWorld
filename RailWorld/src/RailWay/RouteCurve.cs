using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Точка маршруту: де проходить колія і в якому напрямку вона там іде.
    /// </summary>
    public class RoutePoint
    {
        public Vec3d Position;

        /// <summary>Напрямок руху по маршруту в цій точці, одиничний вектор.</summary>
        public Vec3d Tangent;
    }

    /// <summary>
    /// Маршрут через контрольні точки. Між сусідніми точками лежить кубічна крива Безьє, а керівні вектори
    /// двох кривих, що сходяться в точці, лежать на одній прямій, тож перехід між кривими плавний.
    /// Той самий розрахунок іде на клієнті для показу і на сервері для побудови.
    /// </summary>
    public static class RouteCurve
    {
        // Вага середніх точок кривої: та сама, що й у старих режимів укладання
        public const double Weight = 0.8;

        // На відрізки якої довжини нарізається крива. Секція це два таких відрізки
        public const double PieceLength = 0.25;

        // Точки, ближчі за це, кривою не з'єднуються: нарізці нема чого різати
        public const double MinSpan = 1;

        /// <summary>
        /// Довжина керівного вектора як частка відстані між точками. Залежить від того, на який кут маршрут
        /// повертає між двома точками: з такою довжиною крива з вагою Weight проходить через середину дуги кола,
        /// яке торкається обох дотичних, і радіус уздовж неї майже сталий, від плавного вигину до півкола.
        /// Для повороту на 90° виходить 0.415, майже ті самі 0.4, що були тут сталою.
        /// </summary>
        public static double HandleFactor(Vec3d from, Vec3d to)
        {
            double straight = (2 + 6 * Weight) / (24 * Weight);

            double lengths = System.Math.Sqrt((from.X * from.X + from.Z * from.Z) * (to.X * to.X + to.Z * to.Z));
            if (lengths < 1e-9) return straight;

            double cos = GameMath.Clamp((from.X * to.X + from.Z * to.Z) / lengths, -1, 1);
            double turn = System.Math.Acos(cos);
            if (turn < 1e-3) return straight;

            return System.Math.Tan(turn / 4) * (2 + 6 * Weight) / (12 * Weight * System.Math.Sin(turn / 2));
        }

        /// <summary>
        /// Нарізає весь маршрут на точки для секцій: кожні три поспіль точки з кроком два це одна секція.
        /// Нахил полотна рахується по всьому маршруту разом, а не окремо по кожній кривій.
        /// </summary>
        public static List<PointOnBezierCurve> BuildPoints(List<RoutePoint> route)
        {
            var all = new List<PointOnBezierCurve>();
            if (route == null) return all;

            for (int i = 0; i + 1 < route.Count; i++)
            {
                RoutePoint a = route[i];
                RoutePoint b = route[i + 1];

                double span = a.Position.DistanceTo(b.Position);
                if (span < MinSpan) continue;

                double handle = span * HandleFactor(a.Tangent, b.Tangent);
                CubicBezierCurve3d curve = new CubicBezierCurve3d(
                    a.Position.X, a.Position.Y, a.Position.Z,
                    a.Position.X + a.Tangent.X * handle, a.Position.Y + a.Tangent.Y * handle, a.Position.Z + a.Tangent.Z * handle,
                    b.Position.X - b.Tangent.X * handle, b.Position.Y - b.Tangent.Y * handle, b.Position.Z - b.Tangent.Z * handle,
                    b.Position.X, b.Position.Y, b.Position.Z,
                    1, Weight, Weight, 1);

                List<PointOnBezierCurve> points = curve.CutIntoEqualPieces(PieceLength);

                // Нарізка інколи дає зайву точку впритул до кінцевої. Тут це зсунуло б усі наступні секції
                // на пів секції, тому таку точку прибираємо, і точок у кривої завжди непарна кількість
                if (points.Count % 2 == 0 && points.Count >= 2) points.RemoveAt(points.Count - 2);
                if (points.Count < 3) continue;

                // Кінець попередньої кривої і початок цієї це та сама точка
                if (all.Count > 0) points.RemoveAt(0);
                all.AddRange(points);
            }

            ModMath.ApplyCant(all);
            return all;
        }
    }
}
