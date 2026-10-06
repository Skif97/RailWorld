using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Блоки колії під секціями. Кожна секція займає клітинки світу, через які проходить, невидимим блоком:
    /// він не дає поставити туди інший блок і дає колізію за формою колії. Блок пам'ятає всі секції,
    /// що через нього проходять, і зникає, коли не лишилося жодної. Працює тільки на сервері.
    /// </summary>
    public static class TrackBed
    {
        /// <summary>На скільки колонок поділений блок уздовж кожної горизонтальної осі.</summary>
        public const int Columns = 4;
        public const int ColumnCount = Columns * Columns;

        /// <summary>Сітка вокселів підсипки: як у чізлених блоків, 16 на блок.</summary>
        public const int Voxels = 16;
        public const int VoxelsPerBlock = Voxels * Voxels * Voxels;

        /// <summary>Наскільки нижче за площину колії закінчується підсипка: шпала втоплена в неї наполовину.</summary>
        public const double BallastBelowTrack = 1.0 / Voxels;

        /// <summary>Колонки без колії мають цю висоту.</summary>
        public const float NoHeight = -1f;

        // Найменша висота колонки з колією, щоб бокс колізії не вироджувався
        private const float MinHeight = 1 / 16f;

        /// <summary>
        /// Блок вважається вільним місцем, якщо гра дозволяє його замінити не гірше за це: повітря, висока трава,
        /// шар снігу. Квіти сюди не потрапляють, у них це число менше. Те саме число бере розмітка маршруту,
        /// коли вирішує, крізь які блоки дивитися.
        /// </summary>
        public const int ReplaceableThreshold = 6000;

        private static Block GetBedBlock(IWorldAccessor world)
        {
            return world.GetBlock(new AssetLocation("railworld", "trackbed-free"));
        }

        /// <summary>
        /// Розгортає секцію в клітинки світу на сітці res колонок на блок. Для кожної клітинки повертає висоти її колонок над підлогою блока:
        /// висота площини колії (низ рейки, верх шпали) в центрі колонки, або NoHeight там, де секції немає.
        /// Контур секції на сітці колонок проводиться алгоритмом Брезенхема, середина заливається по рядках.
        /// </summary>
        public static Dictionary<BlockPos, float[]> Rasterize(Section section, int res = Columns, HashSet<BlockPos> below = null)
        {
            var cells = new Dictionary<BlockPos, float[]>();

            Vec3d start = section.FullStartPosition;
            Vec3d end = section.FullEndPosition;

            // Горизонтальний слід секції: прямокутник уздовж хорди завширшки зі шпалу
            double ax = end.X - start.X, az = end.Z - start.Z;
            double length = Math.Sqrt(ax * ax + az * az);
            if (length < 1e-9) return cells;
            ax /= length; az /= length;
            double sx = -az, sz = ax;
            double halfLength = length / 2;
            double halfWidth = section.SleeperLength / 2;

            double cx = (start.X + end.X) / 2, cy = (start.Y + end.Y) / 2, cz = (start.Z + end.Z) / 2;

            // Площина колії: через середину секції, з нормаллю вгору від полотна (з ухилом і нахилом)
            Vec3d chord = new Vec3d(end.X - start.X, end.Y - start.Y, end.Z - start.Z).Normalize();
            Vec3d side = new Vec3d(section.CenterNormal.X, section.CenterNormal.Y, section.CenterNormal.Z);
            side.Sub(chord.Clone().Mul(side.Dot(chord))).Normalize();
            Vec3d up = side.Cross(chord);
            if (Math.Abs(up.Y) < 1e-6) return cells;

            // Кути прямокутника в координатах сітки колонок
            int[] gx = new int[4];
            int[] gz = new int[4];
            for (int i = 0; i < 4; i++)
            {
                double a = (i == 0 || i == 3) ? -halfLength : halfLength;
                double s = i < 2 ? -halfWidth : halfWidth;
                gx[i] = (int)Math.Floor((cx + ax * a + sx * s) * res);
                gz[i] = (int)Math.Floor((cz + az * a + sz * s) * res);
            }

            // Для кожного рядка сітки найлівіша і найправіша колонка контуру
            var rows = new Dictionary<int, int[]>();
            BresenHam.PlotDelegate2DAA plot = (x, z, aa) =>
            {
                int ix = (int)x, iz = (int)z;
                if (!rows.TryGetValue(iz, out int[] range)) rows[iz] = new int[] { ix, ix };
                else
                {
                    if (ix < range[0]) range[0] = ix;
                    if (ix > range[1]) range[1] = ix;
                }
            };
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                BresenHam.BresenHamPlotLineWidth(gx[i], gz[i], gx[j], gz[j], 0, plot);
            }

            // Колонку беремо, лише якщо її центр лежить у секції. Якщо брати й ті, яких секція тільки торкається,
            // торець колії займає блок попереду себе, і там уже нічого не збудувати
            const double margin = 1e-6;

            foreach (var row in rows)
            {
                for (int ix = row.Value[0]; ix <= row.Value[1]; ix++)
                {
                    double wx = (ix + 0.5) / res;
                    double wz = (row.Key + 0.5) / res;
                    double dx = wx - cx, dz = wz - cz;

                    if (Math.Abs(dx * ax + dz * az) > halfLength + margin) continue;
                    if (Math.Abs(dx * sx + dz * sz) > halfWidth + margin) continue;

                    double y = cy - (up.X * dx + up.Z * dz) / up.Y;

                    // У якому блоці по висоті лежить колія, вирішує середина шпали (вісь колії), а не її край.
                    // Край на повороті з нахилом полотна трохи нижчий або вищий, і якби судити по ньому,
                    // майже рівна колія вимагала б блоків під собою через кілька сотих блока різниці
                    double alongOffset = dx * ax + dz * az;
                    double axisY = cy - (up.X * ax + up.Z * az) * alongOffset / up.Y;
                    int blockY = (int)Math.Floor(axisY + 1e-6);
                    int blockX = (int)Math.Floor((double)ix / res);
                    int blockZ = (int)Math.Floor((double)row.Key / res);

                    BlockPos pos = new BlockPos(blockX, blockY, blockZ, 0);
                    if (!cells.TryGetValue(pos, out float[] heights))
                    {
                        heights = NewHeights(res);
                        cells[pos] = heights;
                    }

                    int column = (ix - blockX * res) + (row.Key - blockZ * res) * res;
                    heights[column] = GameMath.Clamp((float)(y - blockY), MinHeight, 1f);

                    // Шпала посередині втоплена в блок нижче більш ніж наполовину: тоді потрібен і він.
                    // Меншим заглибленням нехтуємо, щоб пологий ухил не вимагав копати під майже рівною колією
                    if (below != null && axisY - blockY < SectionBox.SleeperDepth / 2 - 1e-4)
                    {
                        below.Add(new BlockPos(blockX, blockY - 1, blockZ, 0));
                    }
                }
            }

            below?.ExceptWith(cells.Keys);
            return cells;
        }

        /// <summary>
        /// Усі клітинки, які секція може зайняти: ті, через які проходить площина колії, і ті під ними,
        /// куди на пологому переході між блоками заходить низ шпали. Нижні потрібні, щоб там теж лежала підсипка.
        /// Правила для них ті самі, що й для решти: якщо там стоїть чужий блок, секція заблокована, доки його не приберуть.
        /// </summary>
        public static List<BlockPos> AllCells(Section section)
        {
            var below = new HashSet<BlockPos>();
            // На сітці вокселів, тій самій, що й підсипка. Грубіша сітка пропускає блок, у який секція заходить
            // лише вузькою смужкою, і тоді ця смужка лишається без гравію
            var cells = new List<BlockPos>(Rasterize(section, Voxels, below).Keys);
            cells.AddRange(below);
            return cells;
        }

        /// <summary>
        /// Висоти площини колії над підлогою одного блока в кожній його колонці, по всьому блоку,
        /// а не лише під шпалою. Потрібно для підсипки: блок, який уже тримає шпалу, засипається цілком,
        /// щоб у ньому не лишалося порожніх кутів, які потім нічим заповнити.
        /// Площина секції продовжена за її межі; beyond каже, наскільки колонка лежить за кінцем секції
        /// вздовж колії (0, якщо на її відрізку). Той, хто збирає блок із кількох секцій, бере для колонки
        /// секцію з найменшим beyond. Висота може бути від'ємною або більшою за 1.
        /// </summary>
        public static float[] BlockHeights(Section section, BlockPos pos, int res, out float[] beyond)
        {
            float[] heights = new float[res * res];
            beyond = new float[res * res];

            Vec3d start = section.FullStartPosition;
            Vec3d end = section.FullEndPosition;

            double ax = end.X - start.X, az = end.Z - start.Z;
            double length = Math.Sqrt(ax * ax + az * az);
            Vec3d chord = new Vec3d(end.X - start.X, end.Y - start.Y, end.Z - start.Z).Normalize();
            Vec3d side = new Vec3d(section.CenterNormal.X, section.CenterNormal.Y, section.CenterNormal.Z);
            side.Sub(chord.Clone().Mul(side.Dot(chord))).Normalize();
            Vec3d up = side.Cross(chord);

            if (length < 1e-9 || Math.Abs(up.Y) < 1e-6)
            {
                for (int i = 0; i < heights.Length; i++)
                {
                    heights[i] = NoHeight;
                    beyond[i] = float.MaxValue;
                }
                return heights;
            }

            ax /= length; az /= length;
            double halfLength = length / 2;
            double cx = (start.X + end.X) / 2, cy = (start.Y + end.Y) / 2, cz = (start.Z + end.Z) / 2;

            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    double dx = pos.X + (x + 0.5) / res - cx;
                    double dz = pos.Z + (z + 0.5) / res - cz;

                    int i = x + z * res;
                    beyond[i] = (float)Math.Max(0, Math.Abs(dx * ax + dz * az) - halfLength);
                    heights[i] = (float)(cy - (up.X * dx + up.Z * dz) / up.Y - pos.Y);
                }
            }

            return heights;
        }

        public static float[] NewHeights(int res = Columns)
        {
            float[] heights = new float[res * res];
            for (int i = 0; i < heights.Length; i++) heights[i] = NoHeight;
            return heights;
        }

        /// <summary>
        /// Скільки шарів вокселів гравію має бути в колонці, де площина колії проходить на висоті h
        /// над підлогою блока. 0 означає, що в цьому блоці для колонки гравію немає.
        /// </summary>
        public static int BallastTop(float h)
        {
            // Підсипка доходить до половини товщини шпали, тобто на воксель нижче за площину колії
            int top = (int)Math.Floor((h - BallastBelowTrack) * Voxels + 1e-4);

            if (top < 1)
            {
                // Колія лежить низько над підлогою блока, і за цим правилом гравію не виходить жодного шару.
                // Якщо один шар іще не вищий за саму колію, кладемо його: інакше на пологому підйомі
                // цілі секції лишаються без видимої підсипки. Якщо вищий, пропускаємо: він виліз би
                // крізь шпали й рейки, а під такою колонкою підсипку тримає блок нижче
                return h < BallastBelowTrack - 1e-4 ? 0 : 1;
            }

            return Math.Min(top, Voxels);
        }

        /// <summary>
        /// Чи вже лежить під секцією вся підсипка, яка їй потрібна. Так буває, коли сусідню секцію засипали:
        /// блоки засипаються цілком, тож сусідам часто вже нічого досипати.
        /// </summary>
        public static bool IsBallastCovered(IWorldAccessor world, Section section)
        {
            foreach (BlockPos pos in AllCells(section))
            {
                float[] heights = BlockHeights(section, pos, Voxels, out float[] beyond);
                int[] tops = (world.BlockAccessor.GetBlockEntity(pos) as BlockEntityTrackBed)?.GetColumnTops();

                for (int z = 0; z < Voxels; z++)
                {
                    for (int x = 0; x < Voxels; x++)
                    {
                        int i = x + z * Voxels;
                        // Секція відповідає лише за колонки на своєму відрізку
                        if (beyond[i] > 1e-6f) continue;

                        int needed = BallastTop(heights[i]);
                        if (needed == 0) continue;
                        if (tops == null || tops[i] < needed) return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Скільки вокселів підсипки зараз лежить у блоках під секцією.
        /// </summary>
        public static int CountVoxels(IWorldAccessor world, Section section)
        {
            int total = 0;
            foreach (BlockPos pos in AllCells(section))
            {
                total += (world.BlockAccessor.GetBlockEntity(pos) as BlockEntityTrackBed)?.VoxelCount ?? 0;
            }
            return total;
        }

        /// <summary>До якого ступеня вагонетка зчищає сніг із колії, якою проїхала: на третьому рейки ледь виступають зі снігу.</summary>
        public const int SnowAfterTrolley = 3;

        // На крутому підйомі блоки колії стоять стовпчиком, і сніг тримає верхній. Скільки блоків угору його шукати
        private const int StackReach = 4;

        /// <summary>
        /// Зчищає сніг під секцією до заданого ступеня. Ступінь снігу це властивість цілого блока,
        /// тому чистяться всі блоки колії під секцією цілком, а не смуга під колесами.
        /// </summary>
        public static void ClearSnow(IWorldAccessor world, Section section, int stage)
        {
            IBlockAccessor accessor = world.BlockAccessor;
            BlockPos at = new BlockPos(0);

            foreach (BlockPos pos in AllCells(section))
            {
                BlockTrackBed top = accessor.GetBlock(pos) as BlockTrackBed;
                if (top == null) continue;

                at.Set(pos);
                for (int k = 0; k < StackReach; k++)
                {
                    BlockTrackBed above = accessor.GetBlock(at.Up()) as BlockTrackBed;
                    if (above == null)
                    {
                        at.Down();
                        break;
                    }
                    top = above;
                }

                top.ReduceSnow(world, at, stage);
            }
        }

        private static bool IsFree(Block block, Block bed)
        {
            return block.Id == 0 || block is BlockTrackBed || block.Replaceable >= ReplaceableThreshold;
        }

        /// <summary>
        /// Ламає чужі блоки в клітинках, які потрібні секції, ніби їх зламав гравець: з випадінням предметів
        /// і з перевіркою його прав на цю територію. Блоки, які ламати не можна, лишаються.
        /// </summary>
        public static void ClearObstructions(IWorldAccessor world, Section section, IPlayer byPlayer)
        {
            Block bed = GetBedBlock(world);
            if (bed == null) return;

            foreach (BlockPos pos in AllCells(section))
            {
                if (IsFree(world.BlockAccessor.GetBlock(pos), bed)) continue;
                if (!world.Claims.TryAccess(byPlayer, pos, EnumBlockAccessFlags.BuildOrBreak)) continue;
                world.BlockAccessor.BreakBlock(pos, byPlayer);
            }
        }

        /// <summary>
        /// Чи всі клітинки під секцією вільні. Якщо ні, в секцію не можна ставити деталі.
        /// </summary>
        public static bool CanAttach(IWorldAccessor world, Section section)
        {
            Block bed = GetBedBlock(world);
            if (bed == null) return true;

            foreach (BlockPos pos in AllCells(section))
            {
                if (!IsFree(world.BlockAccessor.GetBlock(pos), bed)) return false;
            }
            return true;
        }

        /// <summary>
        /// У цій клітинці змінився блок: його зламали, поставили або замінили. Перевіряє секції поруч в обидва боки:
        /// якщо під заблокованою секцією більше нічого не заважає, знімає заборону і займає її клітинки;
        /// якщо під вільною секцією з'явився чужий блок, ставить заборону. Зміни розсилаються клієнтам,
        /// і підсвічування порожніх місць міняється між червоним і звичайним.
        /// </summary>
        public static void RecheckAround(IWorldAccessor world, BlockPos changed)
        {
            // Секція зберігається в чанку свого центру, а центр не далі ніж за півтора блока від її клітинок
            var chunks = new HashSet<Vec3i>();
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                        chunks.Add(ModMath.FindChunk(changed.X + 0.5 + dx * 1.5, changed.Y + 0.5 + dy * 1.5, changed.Z + 0.5 + dz * 1.5));

            foreach (Vec3i chunkCoord in chunks)
            {
                DataInChunk data = DataInChunk.Get(world, chunkCoord);
                if (data == null) continue;

                var freed = new List<Section>();
                bool changedAny = false;

                foreach (Section section in data.RailWaySections.Values)
                {
                    Vec3d center = section.GetGlobalPos();
                    if (Math.Abs(center.X - changed.X - 0.5) > 2.5 || Math.Abs(center.Y - changed.Y - 0.5) > 2.5 || Math.Abs(center.Z - changed.Z - 0.5) > 2.5) continue;

                    bool free = CanAttach(world, section);
                    if (free == !section.Blocked) continue;

                    // Прапорець міняємо до встановлення блоків: воно саме викликає зміни сусідів
                    section.Blocked = !free;
                    changedAny = true;
                    if (free) freed.Add(section);
                }

                if (!changedAny) continue;

                DataInChunk.Save(world, chunkCoord, data);
                foreach (Section section in freed) Attach(world, section);
                RailWorld.SendChunkDataToClients(chunkCoord, data);
            }
        }

        /// <summary>
        /// Займає клітинки під секцією блоками колії і записує секцію в кожен із них.
        /// Клітинки, зайняті чужим блоком, пропускаються. Секція вже має бути збережена в даних чанка.
        /// Секція без жодної деталі клітинок не займає: це лише намічена траса.
        /// </summary>
        public static void Attach(IWorldAccessor world, Section section)
        {
            if (section.IsEmpty) return;

            Block bed = GetBedBlock(world);
            if (bed == null) return;
            IBlockAccessor accessor = world.BlockAccessor;

            foreach (BlockPos pos in AllCells(section))
            {
                Block block = accessor.GetBlock(pos);
                // Блок колії буває двох різновидів, звичайний і засніжений, тому перевіряємо клас, а не код
                if (!(block is BlockTrackBed))
                {
                    if (!IsFree(block, bed)) continue;
                    accessor.SetBlock(bed.Id, pos);
                }

                (accessor.GetBlockEntity(pos) as BlockEntityTrackBed)?.AddSection(section);
            }
        }

        /// <summary>
        /// Ще раз надсилає клієнтам блоки під секціями. Після укладання довгої ділянки блоки ставляться
        /// і змінюються десятками за один тік, і частина оновлень може дійти до клієнта раніше за сам блок.
        /// </summary>
        public static void Resend(IWorldAccessor world, List<Section> sections)
        {
            var sent = new HashSet<BlockPos>();
            foreach (Section section in sections)
            {
                foreach (BlockPos pos in AllCells(section))
                {
                    if (!sent.Add(pos)) continue;
                    (world.BlockAccessor.GetBlockEntity(pos) as BlockEntityTrackBed)?.MarkDirty(true);
                }
            }
        }

        /// <summary>
        /// Прибирає секцію з блоків колії. Блок, через який більше не проходить жодна секція, зникає.
        /// </summary>
        public static void Detach(IWorldAccessor world, Section section)
        {
            foreach (BlockPos pos in AllCells(section))
            {
                (world.BlockAccessor.GetBlockEntity(pos) as BlockEntityTrackBed)?.RemoveSection(section);
            }
        }

        /// <summary>
        /// Приводить блоки під секцією у відповідність до її деталей. Викликати, коли в секції поставили
        /// або зняли деталь: з першою деталлю секція займає свої клітинки, без останньої звільняє їх.
        /// </summary>
        public static void Sync(IWorldAccessor world, Section section)
        {
            if (section.IsEmpty) Detach(world, section);
            else Attach(world, section);
        }
    }
}
