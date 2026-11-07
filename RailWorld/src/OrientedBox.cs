using System;

namespace RailWorld
{
    /// <summary>
    /// Коробка, повернута як завгодно: центр, три взаємно перпендикулярні одиничні осі і половини розмірів уздовж
    /// них. Нею кузов вагонетки стоїть на колії: уздовж колії, впоперек і вгору від полотна, з ухилом і нахилом
    /// полотна. Гра вміє перевіряти лише коробки, вирівняні по осях світу (CollisionTester), а вагонетка на кривій
    /// чи схилі в таку не вміщається.
    /// </summary>
    public readonly struct OrientedBox
    {
        public readonly double CX, CY, CZ;
        // Осі: a уздовж, b впоперек, c вгору; ha, hb, hc половини розмірів уздовж них
        private readonly double ax, ay, az, bx, by, bz, cx, cy, cz;
        private readonly double ha, hb, hc;

        public OrientedBox(double centerX, double centerY, double centerZ,
            double alongX, double alongY, double alongZ, double halfAlong,
            double sideX, double sideY, double sideZ, double halfSide,
            double upX, double upY, double upZ, double halfUp)
        {
            CX = centerX; CY = centerY; CZ = centerZ;
            ax = alongX; ay = alongY; az = alongZ; ha = halfAlong;
            bx = sideX; by = sideY; bz = sideZ; hb = halfSide;
            cx = upX; cy = upY; cz = upZ; hc = halfUp;
        }

        /// <summary>Наскільки коробка сягає від центру вздовж осі світу: по X (0), Y (1) чи Z (2).</summary>
        public double Reach(int axis)
        {
            return axis == 0 ? Math.Abs(ax) * ha + Math.Abs(bx) * hb + Math.Abs(cx) * hc
                : axis == 1 ? Math.Abs(ay) * ha + Math.Abs(by) * hb + Math.Abs(cy) * hc
                : Math.Abs(az) * ha + Math.Abs(bz) * hb + Math.Abs(cz) * hc;
        }

        /// <summary>
        /// Чи перетинається з коробкою, вирівняною по осях світу (як коробки зіткнення блоків). Теорема про
        /// розділову вісь: коробки не перетинаються, якщо їхні проєкції розходяться хоч на одній із 15 осей (три осі
        /// світу, три свої і дев'ять їхніх векторних добутків). Дотик перетином не вважається.
        /// </summary>
        public bool Overlaps(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
        {
            // Центр і половини розмірів другої коробки, відносно центру цієї
            double ex = (maxX - minX) / 2, ey = (maxY - minY) / 2, ez = (maxZ - minZ) / 2;
            double tx = (minX + maxX) / 2 - CX, ty = (minY + maxY) / 2 - CY, tz = (minZ + maxZ) / 2 - CZ;

            // Осі світу
            if (Math.Abs(tx) >= ex + Reach(0)) return false;
            if (Math.Abs(ty) >= ey + Reach(1)) return false;
            if (Math.Abs(tz) >= ez + Reach(2)) return false;

            // Свої осі
            if (Separates(ax, ay, az, tx, ty, tz, ex, ey, ez)) return false;
            if (Separates(bx, by, bz, tx, ty, tz, ex, ey, ez)) return false;
            if (Separates(cx, cy, cz, tx, ty, tz, ex, ey, ez)) return false;

            // Добутки своїх осей з осями світу: для осі світу X це (0, -v.z, v.y), для Y (v.z, 0, -v.x), для Z (-v.y, v.x, 0)
            for (int k = 0; k < 3; k++)
            {
                double vx = k == 0 ? ax : k == 1 ? bx : cx;
                double vy = k == 0 ? ay : k == 1 ? by : cy;
                double vz = k == 0 ? az : k == 1 ? bz : cz;
                if (Separates(0, -vz, vy, tx, ty, tz, ex, ey, ez)) return false;
                if (Separates(vz, 0, -vx, tx, ty, tz, ex, ey, ez)) return false;
                if (Separates(-vy, vx, 0, tx, ty, tz, ex, ey, ez)) return false;
            }
            return true;
        }

        // Чи вісь (lx, ly, lz) розділяє коробки: відстань між центрами вздовж неї не менша за суму проєкцій
        private bool Separates(double lx, double ly, double lz, double tx, double ty, double tz, double ex, double ey, double ez)
        {
            double length = Math.Sqrt(lx * lx + ly * ly + lz * lz);
            // Вироджена вісь (добуток паралельних осей) нічого не розділяє
            if (length < 1e-9) return false;

            double distance = Math.Abs(tx * lx + ty * ly + tz * lz);
            double mine = ha * Math.Abs(ax * lx + ay * ly + az * lz) + hb * Math.Abs(bx * lx + by * ly + bz * lz) + hc * Math.Abs(cx * lx + cy * ly + cz * lz);
            double theirs = ex * Math.Abs(lx) + ey * Math.Abs(ly) + ez * Math.Abs(lz);
            return distance >= (mine + theirs) - 1e-9 * length;
        }
    }
}
