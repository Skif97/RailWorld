using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Розміри типів колії. Зараз тип один; вузька шахтна колія буде ще одним набором цих двох чисел.
    /// </summary>
    public static class TrackGauge
    {
        /// <summary>Відстань між осями рейок.</summary>
        public const float StandardWidth = 0.78f;

        /// <summary>Довжина шпали. Трохи більша за блок, тому колія займає смугу в три блоки завширшки.</summary>
        public const float StandardSleeperLength = 1.75f;

        /// <summary>Довжина шпали в колії, прокладеній до появи типів.</summary>
        public const float LegacySleeperLength = 1f;
    }

    /// <summary>
    /// Деталь секції, яку можна окремо виділити, поставити і зняти.
    /// FirstRail лежить з боку нормалі, SecondRail з протилежного.
    /// </summary>
    public enum SectionPart
    {
        Sleeper = 0,
        FirstRail = 1,
        SecondRail = 2,

        /// <summary>Уся секція цілком. Виділяється лише предметом для прокладання колії.</summary>
        Whole = 3,

        /// <summary>Підсипка: гравій під колією.</summary>
        Ballast = 4,

        /// <summary>
        /// Важіль стрілки. Є в секції, з кінця якої колія розходиться на дві. Його не ставлять і не знімають:
        /// він з'являється разом із відгалуженням, а клік по ньому переводить стрілку.
        /// </summary>
        Switch = 5
    }

    /// <summary>
    /// Орієнтований бокс виділення однієї деталі секції: повернутий уздовж колії,
    /// нахилений разом з ухилом і нахилом полотна.
    /// </summary>
    public class SectionBox
    {
        // Розміри деталей відносно осі колії на рівні підошви рейки
        public const double SleeperDepth      = 0.125;
        public const double SleeperHalfWidth  = 0.125;
        public const double RailHeight        = 0.15625;
        public const double RailHalfWidth     = 0.08;
        // Важіль стрілки: на скільки він відступає від краю шпали, пів ширини і висота його бокса
        public const double LeverGap          = 0.25;
        public const double LeverHalfWidth    = 0.12;
        public const double LeverHeight       = 0.85;

        public Vec3i Chunk;
        public int Index;
        public Section Section;
        public SectionPart Part;

        /// <summary>false означає порожнє місце під деталь.</summary>
        public bool Installed;

        public Vec3d Center;
        public Vec3d AxisSide;
        public Vec3d AxisUp;
        public Vec3d AxisAlong;

        public double HalfSide;
        public double HalfAlong;
        public double MinUp;
        public double MaxUp;

        /// <param name="along">напрямок уздовж деталі, не обов'язково нормалізований</param>
        /// <param name="sideHint">приблизний напрямок убік, вирівнюється перпендикулярно до along</param>
        public SectionBox(Vec3i chunk, int index, Section section, SectionPart part, Vec3d center, Vec3d along, Vec3f sideHint,
            double halfAlong, double halfSide, double minUp, double maxUp)
        {
            Chunk = chunk;
            Index = index;
            Section = section;
            Part = part;
            Center = center;
            HalfAlong = halfAlong;
            HalfSide = halfSide;
            MinUp = minUp;
            MaxUp = maxUp;

            double ax = along.X, ay = along.Y, az = along.Z;
            double length = Math.Sqrt(ax * ax + ay * ay + az * az);
            if (length < 1e-9) { ax = 0; ay = 0; az = 1; } else { ax /= length; ay /= length; az /= length; }

            double dot = sideHint.X * ax + sideHint.Y * ay + sideHint.Z * az;
            double sx = sideHint.X - dot * ax, sy = sideHint.Y - dot * ay, sz = sideHint.Z - dot * az;
            double sl = Math.Sqrt(sx * sx + sy * sy + sz * sz);
            if (sl < 1e-9) { sx = -az; sy = 0; sz = ax; sl = Math.Sqrt(sx * sx + sz * sz); }
            sx /= sl; sy /= sl; sz /= sl;

            AxisAlong = new Vec3d(ax, ay, az);
            AxisSide = new Vec3d(sx, sy, sz);
            // Верх = бік × уздовж
            AxisUp = new Vec3d(sy * az - sz * ay, sz * ax - sx * az, sx * ay - sy * ax);
        }

        /// <summary>
        /// Перетин променя з боксом. dir має бути нормалізований.
        /// </summary>
        public bool Intersect(Vec3d origin, Vec3d dir, double maxDist, out double dist)
        {
            double tmin = 0;
            double tmax = maxDist;
            dist = 0;

            double dx = origin.X - Center.X, dy = origin.Y - Center.Y, dz = origin.Z - Center.Z;

            if (!Slab(AxisSide, dx, dy, dz, dir, -HalfSide, HalfSide, ref tmin, ref tmax)) return false;
            if (!Slab(AxisUp, dx, dy, dz, dir, MinUp, MaxUp, ref tmin, ref tmax)) return false;
            if (!Slab(AxisAlong, dx, dy, dz, dir, -HalfAlong, HalfAlong, ref tmin, ref tmax)) return false;

            dist = tmin;
            return true;
        }

        private static bool Slab(Vec3d axis, double dx, double dy, double dz, Vec3d dir, double lo, double hi, ref double tmin, ref double tmax)
        {
            double p = axis.X * dx + axis.Y * dy + axis.Z * dz;
            double f = axis.X * dir.X + axis.Y * dir.Y + axis.Z * dir.Z;

            if (Math.Abs(f) < 1e-9) return p >= lo && p <= hi;

            double t1 = (lo - p) / f;
            double t2 = (hi - p) / f;
            if (t1 > t2) { double tmp = t1; t1 = t2; t2 = tmp; }

            tmin = Math.Max(tmin, t1);
            tmax = Math.Min(tmax, t2);
            return tmin <= tmax;
        }
    }

    /// <summary>
    /// Виділена деталь секції, аналог BlockSelection.
    /// </summary>
    public class SectionSelection
    {
        public Vec3i Chunk;
        public int Index;
        public SectionPart Part;
        public Section Section;

        /// <summary>Точка влучання променя в глобальних координатах.</summary>
        public Vec3d HitPosition;

        /// <summary>Бокс деталі. Є тільки на клієнті.</summary>
        public SectionBox Box;

        public double Distance;

        public BlockPos Position => HitPosition.AsBlockPos;

        public bool SamePart(SectionSelection other)
        {
            return other != null && Index == other.Index && Part == other.Part && Chunk.Equals(other.Chunk);
        }
    }

    public static class SectionPicker
    {
        private const int ChunkSize = 32;

        /// <summary>
        /// Стандартні бокси секції: по одному на кожну деталь, встановлену чи ні.
        /// </summary>
        public static void AddSectionBoxes(List<SectionBox> boxes, Vec3i chunkCoord, int index, Section s)
        {
            Vec3d start = s.FullStartPosition;
            Vec3d end = s.FullEndPosition;

            Vec3d chord = new Vec3d(end.X - start.X, end.Y - start.Y, end.Z - start.Z);
            Vec3d middle = new Vec3d((start.X + end.X) / 2, (start.Y + end.Y) / 2, (start.Z + end.Z) / 2);

            // Шпала лежить посередині секції, під серединою рейок
            boxes.Add(new SectionBox(chunkCoord, index, s, SectionPart.Sleeper, middle, chord, s.CenterNormal,
                SectionBox.SleeperHalfWidth, s.SleeperLength / 2, -SectionBox.SleeperDepth, 0)
            {
                Installed = s.SleeperInstalled
            });

            // Бокс на всю секцію: від краю до краю шпали, від низу шпали до верху рейки
            boxes.Add(new SectionBox(chunkCoord, index, s, SectionPart.Whole, middle, chord, s.CenterNormal,
                chord.Length() / 2, s.SleeperLength / 2, -SectionBox.SleeperDepth, SectionBox.RailHeight)
            {
                Installed = true
            });

            // Підсипка: тонкий шар на всю секцію, від низу шпали до половини її товщини
            boxes.Add(new SectionBox(chunkCoord, index, s, SectionPart.Ballast, middle, chord, s.CenterNormal,
                chord.Length() / 2, s.SleeperLength / 2, -SectionBox.SleeperDepth, -TrackBed.BallastBelowTrack)
            {
                Installed = s.BallastInstalled
            });

            // Важіль стрілки на кожному кінці, з якого колія розходиться
            for (int e = 0; e < 2; e++)
            {
                bool atStart = e == 0;
                if (!s.HasSwitch(atStart)) continue;

                boxes.Add(new SectionBox(chunkCoord, index, s, SectionPart.Switch, LeverPosition(s, atStart), chord, s.CenterNormal,
                    SectionBox.LeverHalfWidth, SectionBox.LeverHalfWidth, 0, SectionBox.LeverHeight)
                {
                    Installed = true
                });
            }

            for (int side = 0; side < 2; side++)
            {
                SectionPart part = side == 0 ? SectionPart.FirstRail : SectionPart.SecondRail;
                // Та сама хорда рейки, що і в рендері
                double offset = s.TrackWidth / 2f * (side == 0 ? 1 : -1);
                Vec3d a = new Vec3d(start.X + s.StartNormal.X * offset, start.Y + s.StartNormal.Y * offset, start.Z + s.StartNormal.Z * offset);
                Vec3d b = new Vec3d(end.X + s.EndNormal.X * offset, end.Y + s.EndNormal.Y * offset, end.Z + s.EndNormal.Z * offset);
                Vec3d along = new Vec3d(b.X - a.X, b.Y - a.Y, b.Z - a.Z);
                Vec3d center = new Vec3d((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2);

                boxes.Add(new SectionBox(chunkCoord, index, s, part, center, along, s.CenterNormal,
                    along.Length() / 2, SectionBox.RailHalfWidth, 0, SectionBox.RailHeight)
                {
                    Installed = s.IsInstalled(part)
                });
            }
        }

        /// <summary>
        /// Де стоїть важіль стрілки на цьому кінці секції: збоку від колії, на рівні підошви рейки.
        /// Те саме місце беруть і бокс виділення, і рендер.
        /// </summary>
        public static Vec3d LeverPosition(Section s, bool atStart)
        {
            Vec3d end = s.GetEndPosition(atStart);
            Vec3f normal = atStart ? s.StartNormal : s.EndNormal;
            double offset = s.SleeperLength / 2 + SectionBox.LeverGap;
            return new Vec3d(end.X + normal.X * offset, end.Y + normal.Y * offset, end.Z + normal.Z * offset);
        }

        /// <summary>
        /// Шукає найближчу деталь на промені в чанку ока та сусідніх. dir має бути нормалізований.
        /// filter вирішує, які бокси взагалі можна виділити.
        /// </summary>
        public static SectionSelection Pick(Vec3d origin, Vec3d dir, double range, System.Func<Vec3i, List<SectionBox>> boxesInChunk, System.Func<SectionBox, bool> filter)
        {
            int cx = (int)Math.Floor(origin.X / ChunkSize);
            int cy = (int)Math.Floor(origin.Y / ChunkSize);
            int cz = (int)Math.Floor(origin.Z / ChunkSize);

            double reach = (range + 1) * (range + 1);
            SectionBox best = null;
            double bestDist = range;
            Vec3i coord = new Vec3i();

            for (int x = cx - 1; x <= cx + 1; x++)
            {
                for (int y = cy - 1; y <= cy + 1; y++)
                {
                    for (int z = cz - 1; z <= cz + 1; z++)
                    {
                        coord.Set(x, y, z);
                        List<SectionBox> boxes = boxesInChunk(coord);
                        if (boxes == null) continue;

                        foreach (SectionBox box in boxes)
                        {
                            if (box.Center.SquareDistanceTo(origin) > reach) continue;
                            if (!filter(box)) continue;
                            if (box.Intersect(origin, dir, bestDist, out double dist))
                            {
                                best = box;
                                bestDist = dist;
                            }
                        }
                    }
                }
            }

            if (best == null) return null;

            return new SectionSelection
            {
                Chunk = best.Chunk,
                Index = best.Index,
                Part = best.Part,
                Section = best.Section,
                Box = best,
                Distance = bestDist,
                HitPosition = new Vec3d(origin.X + dir.X * bestDist, origin.Y + dir.Y * bestDist, origin.Z + dir.Z * bestDist)
            };
        }
    }
}
