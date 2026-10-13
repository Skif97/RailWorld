using System;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Прямі в плані, тобто на вигляді згори: висота не враховується.
    /// </summary>
    internal static class PlanLines
    {
        // Векторний добуток напрямків, менший за це, означає, що прямі паралельні. Напрямки тут це відрізки
        // колії завдовжки від чверті блока, тож до похибки обчислень звідси ще далеко
        private const double Parallel = 1e-12;

        /// <summary>
        /// Де перетинаються дві прямі. Перша проходить через точку (ax, az) у напрямку (rx, rz), друга через
        /// (cx, cz) у напрямку (sx, sz). t і u це місце перетину на першій і другій, у довжинах їхніх напрямків:
        /// якщо напрямок це відрізок від початку до кінця, то 0 це його початок, 1 кінець, а що поза цими
        /// межами, лежить на продовженні відрізка. Чи рахувати самі кінці, вирішує той, хто питає.
        /// false, якщо прямі паралельні.
        /// </summary>
        public static bool Meet(double ax, double az, double rx, double rz, double cx, double cz, double sx, double sz,
            out double t, out double u)
        {
            t = 0;
            u = 0;
            double denominator = rx * sz - rz * sx;
            if (Math.Abs(denominator) < Parallel) return false;

            t = ((cx - ax) * sz - (cz - az) * sx) / denominator;
            u = ((cx - ax) * rz - (cz - az) * rx) / denominator;
            return true;
        }
    }
}
