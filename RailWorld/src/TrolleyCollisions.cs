using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace RailWorld
{
    /// <summary>
    /// Сервер: удари вагонеток на колії одна об одну. Раз на тік, в основному потоці, після кроку фізики (його гра
    /// робить у своєму обробнику такту, тож тут жодна вагонетка не рухається). Удар по прямій між центрами, маси
    /// однакові, імпульс зберігається, а відскок RestitutionCoefficient: колона стоячих вагонеток упритул поводиться
    /// як колиска Ньютона, крайня вилітає, середні ледь зсуваються, та, що в'їхала, трохи відскакує. Удари
    /// розв'язуються кількома проходами за тік, поки не лишиться жодної пари, що зближується: так імпульс проходить
    /// колоною за один тік, а не по вагонетці за тік.
    /// </summary>
    public class TrolleyCollisions
    {
        /// <summary>Яку частку швидкості зближення вагонетки повертають, розлітаючись після удару.</summary>
        public const double RestitutionCoefficient = 0.9;

        /// <summary>
        /// Зазор, на якому стоячі впритул вагонетки ще вважаються сусідами, блоків. Торкаються вони, коли між центрами
        /// сума їхніх половин довжини по буферах (EntityTrolley.HalfLength) і цей зазор.
        /// </summary>
        public const double ContactGap = 0.02;

        /// <summary>
        /// Як близько мають зійтися осі двох вагонеток, щоб удар рахувався, блоків. Вісь це ламана від кінця до кінця
        /// через середину на осі колії (EntityTrolley.RailEndA, RailCenter, RailEndB). Відстань між центрами довгих
        /// вагонів буває меншою за суму їхніх половин і тоді, коли вони стоять поруч на паралельних коліях; осі ж там
        /// щонайменше за міжколійя. На своїй колії осі сходяться, і коли вагонетки торкаються, і коли вже заїхали одна
        /// в одну, тож швидкі вагонетки удару не проскочать.
        /// </summary>
        public const double AxisReach = 1.0;

        // Запобіжник на кількість проходів за тік на кожну вагонетку
        private const int PassesPerTrolley = 2;

        private readonly HashSet<EntityTrolley> trolleys = new HashSet<EntityTrolley>();
        private readonly List<EntityTrolley> onRail = new List<EntityTrolley>();
        private readonly List<double> velocities = new List<double>();
        private readonly List<bool> changed = new List<bool>();

        public TrolleyCollisions(ICoreServerAPI sapi)
        {
            sapi.Event.RegisterGameTickListener(_ => Resolve(), 0);
        }

        public void Add(EntityTrolley trolley) => trolleys.Add(trolley);

        // Чи осі двох вагонеток ближчі одна до одної, ніж на AxisReach
        private static bool AxesMeet(EntityTrolley a, EntityTrolley b)
        {
            double reachSq = AxisReach * AxisReach;
            return SegmentsSq(a.RailEndA, a.RailCenter, b.RailEndA, b.RailCenter) < reachSq
                || SegmentsSq(a.RailEndA, a.RailCenter, b.RailCenter, b.RailEndB) < reachSq
                || SegmentsSq(a.RailCenter, a.RailEndB, b.RailEndA, b.RailCenter) < reachSq
                || SegmentsSq(a.RailCenter, a.RailEndB, b.RailCenter, b.RailEndB) < reachSq;
        }

        // Квадрат найменшої відстані між відрізками p0-p1 і q0-q1
        private static double SegmentsSq(Vec3d p0, Vec3d p1, Vec3d q0, Vec3d q1)
        {
            double ux = p1.X - p0.X, uy = p1.Y - p0.Y, uz = p1.Z - p0.Z;
            double vx = q1.X - q0.X, vy = q1.Y - q0.Y, vz = q1.Z - q0.Z;
            double wx = p0.X - q0.X, wy = p0.Y - q0.Y, wz = p0.Z - q0.Z;
            double a = ux * ux + uy * uy + uz * uz, b = ux * vx + uy * vy + uz * vz, c = vx * vx + vy * vy + vz * vz;
            double d = ux * wx + uy * wy + uz * wz, e = vx * wx + vy * wy + vz * wz;
            double denominator = a * c - b * b;
            double s = a < 1e-12 ? 0 : denominator > 1e-12 ? GameMath.Clamp((b * e - c * d) / denominator, 0, 1) : 0;
            double t = c < 1e-12 ? 0 : GameMath.Clamp((b * s + e) / c, 0, 1);
            // Найближча точка другого відрізка могла впертися в його край: тоді перераховуємо точку першого
            s = a < 1e-12 ? 0 : GameMath.Clamp((b * t - d) / a, 0, 1);
            double dx = wx + ux * s - vx * t, dy = wy + uy * s - vy * t, dz = wz + uz * s - vz * t;
            return dx * dx + dy * dy + dz * dz;
        }
        public void Remove(EntityTrolley trolley) => trolleys.Remove(trolley);

        private void Resolve()
        {
            onRail.Clear();
            velocities.Clear();
            changed.Clear();
            foreach (EntityTrolley trolley in trolleys)
            {
                if (!trolley.UpdateRailMotion(out double velocity)) continue;
                onRail.Add(trolley);
                velocities.Add(velocity);
                changed.Add(false);
            }
            int count = onRail.Count;
            if (count < 2) return;

            for (int pass = 0, limit = count * PassesPerTrolley; pass < limit; pass++)
            {
                bool any = false;
                for (int i = 0; i < count; i++)
                    for (int j = i + 1; j < count; j++)
                    {
                        Vec3d a = onRail[i].RailCenter, b = onRail[j].RailCenter;
                        double nx = b.X - a.X, ny = b.Y - a.Y, nz = b.Z - a.Z;
                        double distSq = nx * nx + ny * ny + nz * nz;
                        double contact = onRail[i].HalfLength + onRail[j].HalfLength + ContactGap;
                        if (distSq > contact * contact || distSq < 1e-12) continue;
                        if (!AxesMeet(onRail[i], onRail[j])) continue;

                        double dist = Math.Sqrt(distSq);
                        nx /= dist; ny /= dist; nz /= dist;

                        // Наскільки напрямок колії кожної дивиться вздовж прямої між центрами, і швидкість зближення
                        Vec3d ua = onRail[i].RailAlong, ub = onRail[j].RailAlong;
                        double ka = ua.X * nx + ua.Y * ny + ua.Z * nz, kb = ub.X * nx + ub.Y * ny + ub.Z * nz;
                        double closing = velocities[i] * ka - velocities[j] * kb;
                        double reach = ka * ka + kb * kb;
                        if (closing <= 1e-6 || reach < 1e-6) continue;

                        // Однакові маси, і кожна рухається лише вздовж своєї колії: імпульс J такий, щоб після удару
                        // вони розходилися зі швидкістю e * closing; на одній колії це половина (1 + e) * closing кожній
                        double impulse = (1 + RestitutionCoefficient) * closing / reach;
                        velocities[i] -= impulse * ka;
                        velocities[j] += impulse * kb;
                        changed[i] = changed[j] = any = true;
                    }
                if (!any) break;
            }

            for (int i = 0; i < count; i++)
            {
                if (changed[i]) onRail[i].SetRailVelocity(velocities[i]);
            }
        }
    }
}
