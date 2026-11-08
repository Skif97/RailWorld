using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Ширина колії: відстань між осями рейок і довжина шпали. Від неї ж і правила, що міряються в ширинах колії:
    /// найменший радіус, допуск радіуса, наростання нахилу полотна, довжина гостряка, габарит. Кожна секція
    /// зберігає свою ширину (Section.TrackWidth, SleeperLength); колії різної ширини не з'єднуються, але можуть
    /// перетинатися в одному рівні, і кожна тримається своїх правил. Блок це метр.
    /// </summary>
    public sealed class TrackGauge
    {
        /// <summary>Вузька колія, 750 мм. Шпала трохи більша за блок, тому колія займає смугу в три блоки.</summary>
        public static readonly TrackGauge Narrow = new TrackGauge("narrow", 0.78f, 1.75f, SectionBox.SleeperHalfWidth * 2, SectionBox.SleeperDepth, 0);

        /// <summary>
        /// Широка колія, 1520 мм, шпала як справжня під неї: 2.75 м, переріз 0.25 на 0.2. Крок шпал той самий, пів
        /// блока, як і справжній під 1520 (близько 0.54 м), тож проміжок між шпалами дорівнює ширині шпали, як на
        /// справжній колії.
        /// </summary>
        public static readonly TrackGauge Broad = new TrackGauge("broad", 1.52f, 2.75f, 0.25, 0.2, 1);

        public static readonly TrackGauge[] All = { Narrow, Broad };

        /// <summary>Під цим ключем ширина нової колії лежить у властивостях інструмента (меню колії).</summary>
        public const string AttributeKey = "trackGauge";

        /// <summary>Ширина, вибрана в меню колії на цьому інструменті.</summary>
        public static TrackGauge OfTool(Vintagestory.API.Common.ItemStack tool)
        {
            return ByCode(tool?.Attributes?.GetString(AttributeKey));
        }

        /// <summary>Код для збереження в предметі й пакеті.</summary>
        public readonly string Code;

        /// <summary>Відстань між осями рейок.</summary>
        public readonly float Width;

        /// <summary>Довжина шпали.</summary>
        public readonly float SleeperLength;

        /// <summary>
        /// Переріз шпали: ширина вздовж колії і товщина. Верх шпали в усіх колій на одній висоті, під підошвою рейки,
        /// тож рейки однакові по висоті, а товща шпала глибше сидить у землі.
        /// </summary>
        public readonly double SleeperWidth, SleeperDepth;

        /// <summary>
        /// На скільки блоків контррейка стрілки може зайти в бік стрілки на ділянку гостряка (SwitchGeometry.CheckRail).
        /// На вузькій ні: там у цьому місці до зовнішньої рейки вже прилягає гостряк сусідньої гілки. На широкій між
        /// ними ще досить місця.
        /// </summary>
        public readonly double CheckRailOverBlade;

        private TrackGauge(string code, float width, float sleeperLength, double sleeperWidth, double sleeperDepth, double checkRailOverBlade)
        {
            Code = code;
            Width = width;
            SleeperLength = sleeperLength;
            SleeperWidth = sleeperWidth;
            SleeperDepth = sleeperDepth;
            CheckRailOverBlade = checkRailOverBlade;
        }

        /// <summary>Найменший радіус повороту в плані і перелому по висоті, блоків.</summary>
        public double MinRadius => Width * TrackRules.MinRadiusGauges;

        /// <summary>Крива між точками не ідеальне коло, тому радіус трохи менший за межу ще не порушення.</summary>
        public double RadiusTolerance => Width * 0.3;

        /// <summary>На скільки блоків від кінця маршруту, що примикає до нахиленої колії, нахил полотна сходить до нуля.</summary>
        public double CantRampLength => Width * 8.0;

        /// <summary>Ширина габариту колії: шпала із запасом (Footprint).</summary>
        public double FootprintWidth => SleeperLength + Footprint.Margin;

        /// <summary>
        /// Межі довжини гостряка в секціях: від 3.5 до 8 ширин колії, як на справжній залізниці. При колії 0.78
        /// це від 3 до 6 блоків.
        /// </summary>
        public int MinBladeSections => (int)Math.Ceiling(Width * 3.5 / 0.5);
        public int MaxBladeSections => (int)Math.Floor(Width * 8.0 / 0.5);

        /// <summary>Ширина за кодом; невідомий код це вузька колія.</summary>
        public static TrackGauge ByCode(string code)
        {
            foreach (TrackGauge gauge in All)
            {
                if (gauge.Code == code) return gauge;
            }
            return Narrow;
        }

        /// <summary>Ширина, з якою покладено секцію.</summary>
        public static TrackGauge Of(Section section)
        {
            return OfWidth(section.TrackWidth);
        }

        /// <summary>Ширина з такою відстанню між рейками; найближча, якщо точно такої немає.</summary>
        public static TrackGauge OfWidth(double width)
        {
            TrackGauge best = Narrow;
            foreach (TrackGauge gauge in All)
            {
                if (Math.Abs(gauge.Width - width) < Math.Abs(best.Width - width)) best = gauge;
            }
            return best;
        }

        /// <summary>Чи дві секції однієї ширини: лише такі з'єднуються стиком і стрілкою.</summary>
        public static bool Same(Section a, Section b)
        {
            return Math.Abs(a.TrackWidth - b.TrackWidth) < 1e-3;
        }

        /// <summary>Нова секція цієї ширини.</summary>
        public Section NewSection(PointOnBezierCurve start, PointOnBezierCurve middle, PointOnBezierCurve end)
        {
            return new Section(start, middle, end, Width, SleeperLength);
        }
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
        // Важіль стрілки: на скільки він відступає від краю шпали і пів ширини місця, яке він займає біля колії.
        // Модель важеля (shapes/part/switchlever.json) намальована під цей відступ: якщо його змінити, тяга
        // не дійде до гостряків
        public const double LeverGap          = 0.75;
        // На скільки блоків важіль стоїть далі за стик, з якого колія розходиться: одна секція
        public const double LeverAhead        = 0.5;
        // Місце, яке важіль займає разом з ходом ручки, вантажу й покажчика: на скільки блоків від своєї точки
        // він сягає впоперек колії і вздовж неї. Пораховано з моделі за всіма кадрами переведення; числа друкує
        // tools/make_lever_scene.py, і якщо модель змінено, їх треба звірити
        public const double LeverReachAcross  = 0.62;
        public const double LeverReachAlong   = 0.47;
        // Бокс виділення важеля: пів ширини основи і висота до покажчика на щоглі
        public const double LeverBoxHalfWidth = 0.3;
        public const double LeverHeight       = 1.3;

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

        /// <summary>Чи лежить точка в боксі, із запасом tolerance блоків з кожного боку.</summary>
        public bool Contains(Vec3d p, double tolerance)
        {
            double dx = p.X - Center.X, dy = p.Y - Center.Y, dz = p.Z - Center.Z;
            double side = AxisSide.X * dx + AxisSide.Y * dy + AxisSide.Z * dz;
            double up = AxisUp.X * dx + AxisUp.Y * dy + AxisUp.Z * dz;
            double along = AxisAlong.X * dx + AxisAlong.Y * dy + AxisAlong.Z * dz;
            return Math.Abs(side) <= HalfSide + tolerance && Math.Abs(along) <= HalfAlong + tolerance
                && up >= MinUp - tolerance && up <= MaxUp + tolerance;
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
            TrackGauge gauge = TrackGauge.Of(s);
            boxes.Add(new SectionBox(chunkCoord, index, s, SectionPart.Sleeper, middle, chord, s.CenterNormal,
                gauge.SleeperWidth / 2, s.SleeperLength / 2, -gauge.SleeperDepth, 0)
            {
                Installed = s.SleeperInstalled
            });

            // Бокс на всю секцію: від краю до краю шпали, від низу шпали до верху рейки
            boxes.Add(new SectionBox(chunkCoord, index, s, SectionPart.Whole, middle, chord, s.CenterNormal,
                chord.Length() / 2, s.SleeperLength / 2, -gauge.SleeperDepth, SectionBox.RailHeight)
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
                    SectionBox.LeverBoxHalfWidth, SectionBox.LeverBoxHalfWidth, 0, SectionBox.LeverHeight)
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
        /// Де стоїть важіль стрілки на цьому кінці секції: навпроти вістер гостряків, збоку від колії,
        /// на рівні підошви рейки. З якого боку, записано в самій секції: із зовнішнього боку прямішої
        /// з двох колій, щоб важіль не опинився між ними. Те саме місце беруть і бокс виділення, і рендер.
        /// </summary>
        public static Vec3d LeverPosition(Section s, bool atStart)
        {
            return LeverPosition(s, atStart, s.GetLeverSide(atStart));
        }

        /// <summary>
        /// Місце, яке займає важіль стрілки з боку side, прямокутником у плані: відрізок від from до to вздовж
        /// колії крізь точку важеля, завширшки SectionBox.LeverReachAcross у кожен бік від нього.
        /// </summary>
        public static void LeverFootprint(Section s, bool atStart, int side, out Vec3d from, out Vec3d to)
        {
            Vec3d lever = LeverPosition(s, atStart, side);
            Vec3f outward = s.GetOutwardDirection(atStart);
            double length = Math.Sqrt(outward.X * outward.X + outward.Z * outward.Z);
            double x = length < 1e-9 ? 1 : outward.X / length, z = length < 1e-9 ? 0 : outward.Z / length;

            from = new Vec3d(lever.X - x * SectionBox.LeverReachAlong, lever.Y, lever.Z - z * SectionBox.LeverReachAlong);
            to = new Vec3d(lever.X + x * SectionBox.LeverReachAlong, lever.Y, lever.Z + z * SectionBox.LeverReachAlong);
        }

        /// <summary>Де стояв би важіль стрілки з боку side: 1 з боку нормалі секції, -1 з протилежного.</summary>
        public static Vec3d LeverPosition(Section s, bool atStart, int side)
        {
            Vec3d end = s.GetEndPosition(atStart);
            Vec3f normal = atStart ? s.StartNormal : s.EndNormal;
            double offset = (s.SleeperLength / 2 + SectionBox.LeverGap) * side;

            // Важіль стоїть не на самому стику, з якого колія розходиться, а на секцію далі: навпроти стику
            // першої і другої секцій стрілки. Там між гостряками лежить тяга, і там же проміжок між шпалами.
            // Місце рахується продовженням спільної колії: за одну секцію гілки ще не встигають відійти від нього
            Vec3f outward = s.GetOutwardDirection(atStart);
            double along = SectionBox.LeverAhead;

            return new Vec3d(end.X + outward.X * along + normal.X * offset, end.Y + outward.Y * along + normal.Y * offset,
                end.Z + outward.Z * along + normal.Z * offset);
        }
    }

    /// <summary>
    /// Бокси виділення секції: усі бокси її чанка одним списком і місце в ньому, де лежать її. false, якщо
    /// боксів секції немає.
    /// </summary>
    public delegate bool SectionBoxes(Section section, out List<SectionBox> boxes, out int first, out int count);

    /// <summary>
    /// Шукає деталь секції під прицілом, як GameMain.RayTraceForSelection шукає блок: лише вздовж променя.
    /// Промінь іде відрізками по клітинці сітки колії (ChunkTrackGrid), і бокси перевіряються лише в секцій,
    /// середина яких лежить у клітинках поруч із відрізком. Буфери пошуку живуть між кадрами.
    /// </summary>
    public class SectionRayPicker
    {
        private const double Step = ChunkTrackGrid.CellSize;

        private readonly TrackIndex track;
        // Секції, бокси яких уже перевірено: сусідні відрізки променя беруть ті самі клітинки
        private readonly HashSet<Section> seen = new HashSet<Section>();

        public SectionRayPicker(Func<Vec3i, ChunkTrackGrid> gridIn)
        {
            track = new TrackIndex(gridIn);
        }

        /// <summary>
        /// Найближча деталь на промені не далі за range. dir має бути нормалізований. reach це найдальше, куди
        /// бокси секції сягають від її середини. filter вирішує, які бокси взагалі можна виділити.
        /// </summary>
        public SectionSelection Pick(Vec3d origin, Vec3d dir, double range, double reach, SectionBoxes boxesOf, Func<SectionBox, bool> filter)
        {
            seen.Clear();
            SectionBox best = null;
            double bestDist = range;

            for (double from = 0; from < range; from += Step)
            {
                // Як гра зупиняється на першому блоці, у який влучила. Середина секції, у бокс якої промінь влучає
                // на відстані d, не далі за reach від точки влучання, тож її бере відрізок, на який припадає d.
                // Секції, яких іще не брали, дають влучання не ближче за початок цього відрізка: якщо знайдене
                // ближче, далі шукати нема чого
                if (best != null && bestDist <= from) break;

                double to = Math.Min(from + Step, range);
                double ax = origin.X + dir.X * from, ay = origin.Y + dir.Y * from, az = origin.Z + dir.Z * from;
                double bx = origin.X + dir.X * to, by = origin.Y + dir.Y * to, bz = origin.Z + dir.Z * to;

                List<Section> near = track.Within(Math.Min(ax, bx) - reach, Math.Min(ay, by) - reach, Math.Min(az, bz) - reach,
                    Math.Max(ax, bx) + reach, Math.Max(ay, by) + reach, Math.Max(az, bz) + reach);
                foreach (Section section in near)
                {
                    if (!seen.Add(section) || !boxesOf(section, out List<SectionBox> boxes, out int first, out int count)) continue;

                    for (int i = first; i < first + count; i++)
                    {
                        SectionBox box = boxes[i];
                        if (!filter(box)) continue;
                        if (box.Intersect(origin, dir, bestDist, out double dist))
                        {
                            best = box;
                            bestDist = dist;
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
