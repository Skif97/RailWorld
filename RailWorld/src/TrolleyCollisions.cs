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
        /// Відстань між центрами, на якій вагонетки торкаються буферами, блоків: довжина моделі від буфера до буфера
        /// (shapes/entity/trolley.json, 30/16), і зазор, на якому стоячі впритул ще вважаються сусідами.
        /// </summary>
        public const double ContactDistance = 30.0 / 16 + 0.02;

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

            double contactSq = ContactDistance * ContactDistance;
            for (int pass = 0, limit = count * PassesPerTrolley; pass < limit; pass++)
            {
                bool any = false;
                for (int i = 0; i < count; i++)
                    for (int j = i + 1; j < count; j++)
                    {
                        Vec3d a = onRail[i].RailCenter, b = onRail[j].RailCenter;
                        double nx = b.X - a.X, ny = b.Y - a.Y, nz = b.Z - a.Z;
                        double distSq = nx * nx + ny * ny + nz * nz;
                        if (distSq > contactSq || distSq < 1e-12) continue;

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
