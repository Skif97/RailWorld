using Vintagestory.API.Config;
using Vintagestory.API.Client;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using RailWorld.src.RailWay;

namespace RailWorld
{
    /// <summary>
    /// Блок колії: блок під секцією. Гравець бачить його під прицілом, тож не може крізь нього ненавмисно
    /// зламати землю під колією, але зробити з ним нічого не може: ні зламати, ні взаємодіяти, ні замінити.
    /// Форму і колізію він бере зі своєї сутності. Сам блок навмисно не наслідує блок чізлених блоків гри,
    /// бо той несе всю взаємодію з гравцем; від чізлених блоків узята лише сутність із вокселями.
    /// </summary>
    public class BlockTrackBed : Block
    {
        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return (blockAccessor.GetBlockEntity(pos) as BlockEntityTrackBed)?.TrackCollisionBoxes;
        }

        public override Cuboidf[] GetParticleCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return GetCollisionBoxes(blockAccessor, pos);
        }

        // Виділяється те саме, що має колізію. Клітинка без деталей і підсипки порожня і для прицілу
        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return GetCollisionBoxes(blockAccessor, pos);
        }

        // Клік по верхній грані «оброблено», тому гра не йде далі: на колію зверху нічого не ставиться
        // і предмет із руки не застосовується. До бічних і нижньої граней блоки приставляти можна
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            return blockSel?.Face == BlockFacing.UP;
        }

        // Ламання у виживанні не просувається
        public override float OnGettingBroken(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter)
        {
            return remainingResistance;
        }

        // Миттєвий злам у креативі і будь-який інший злам нічого не робить. Блок прибирає лише його секція
        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            return null;
        }

        // Середня кнопка в креативі: з блока з підсипкою береться цілий блок гравію тієї самої породи
        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            BlockEntityTrackBed bed = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityTrackBed;
            if (bed == null || bed.VoxelCount == 0) return null;

            Block gravel = world.GetBlock(new AssetLocation("game", "gravel-" + bed.BallastRock));
            return gravel == null ? null : new ItemStack(gravel);
        }

        // Різновиди блока за кількістю снігу: без снігу і чотири ступені. Індекс це ступінь
        private static readonly string[] CoverStates = { "free", "snow", "snow2", "snow3", "snow4" };
        private Block[] snowStages;

        // Скільки накопиченого снігу (число погоди гри) потрібно для кожного ступеня. Гра кладе на землю
        // шари у 2, 4 і 6 вокселів при 1.1, 2.1 і 3.1; наші ступені 2, 3 і 4 перемикаються разом із ними,
        // а перший, легка припорошка, з'являється трохи раніше. Останній поріг 3.0, а не 3.1:
        // блокові гра передає число, обрізане до 3, і до 3.1 воно не доходить ніколи
        private static readonly float[] StageThresholds = { 0f, 0.6f, 1.1f, 2.1f, 3.0f };

        /// <summary>Ступінь снігу на цьому різновиді блока: 0 без снігу, далі від 1 до 4.</summary>
        public int SnowStage { get; private set; }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            snowStages = new Block[CoverStates.Length];
            for (int i = 0; i < CoverStates.Length; i++)
            {
                snowStages[i] = api.World.GetBlock(CodeWithVariant("cover", CoverStates[i]));
                if (snowStages[i] == this) SnowStage = i;
            }
            snowLevel = SnowStage;
        }

        // Який різновид блока відповідає такій кількості снігу. Гра сама міняє блок на нього
        public override Block GetSnowCoveredVariant(BlockPos pos, float level)
        {
            int stage = 0;
            for (int i = 1; i < StageThresholds.Length; i++)
            {
                if (level >= StageThresholds[i]) stage = i;
            }
            return snowStages[stage] ?? this;
        }

        // Це число гра питає двічі з різною метою. Без координат питає тесселятор: звичайний сніг поруч малює
        // свою бічну грань, лише якщо в сусіда рівень менший. Наш сніг тонший, ніж каже ступінь, тому,
        // як і чізлений блок гри, відповідаємо 0.5, інакше поруч із колією в снігу буде дірка.
        // З координатами питає погода, коли не знає, скільки снігу тут накопичено: їй потрібен справжній ступінь
        public override float GetSnowLevel(BlockPos pos)
        {
            if (SnowStage == 0) return 0;
            if (pos == null) return 0.5f;
            return StageThresholds[SnowStage] + 0.25f;
        }

        public override bool ShouldReceiveServerGameTicks(IWorldAccessor world, BlockPos pos, System.Random offThreadRandom, out object extra)
        {
            extra = null;
            if (!GlobalConstants.MeltingFreezingEnabled || SnowStage == 0) return false;

            float temperature = world.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, world.Calendar.TotalDays).Temperature;
            if (temperature <= 4) return false;

            extra = "melt";
            return true;
        }

        // Поруч щось змінилося: можливо, прибрали або поставили блок на шляху сусідньої секції.
        // Базовий метод не викликаємо: він знімає сніг через SetBlock, а це стерло б сутність блока
        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            if (world.Side != EnumAppSide.Server) return;

            // Прямо над блоком поставили щось, що не пропускає дощ: снігу під ним лежати нема чого
            bool above = neibpos.X == pos.X && neibpos.Z == pos.Z && neibpos.Y == pos.Y + 1;
            if (above && SnowStage > 0 && !world.BlockAccessor.GetBlock(neibpos).RainPermeable) SetSnowStage(world, pos, 0);

            TrackBed.RecheckAround(world, neibpos);
        }

        // Сніг тане на один ступінь. Базовий блок міняє себе через SetBlock, а це знищило б сутність разом
        // із підсипкою і прив'язаними секціями. Тому, як у чізлених блоків гри, блок міняється на місці
        public override void OnServerGameTick(IWorldAccessor world, BlockPos pos, object extra = null)
        {
            if (extra is string reason && reason == "melt" && SnowStage > 0) SetSnowStage(world, pos, SnowStage - 1);
        }

        /// <summary>
        /// Зчищає сніг до заданого ступеня, якщо його зараз більше. Разом із блоком зменшує число накопиченого
        /// снігу, яке гра веде для цієї колонки: без цього вона при наступному оновленні погоди повернула б
        /// попередній ступінь. Далі сніг наростає сам, разом зі снігопадом. Тільки на сервері.
        /// </summary>
        public void ReduceSnow(IWorldAccessor world, BlockPos pos, int stage)
        {
            if (SnowStage <= stage) return;

            float[] accum = world.BlockAccessor.GetMapChunkAtBlockPos(pos)?.SnowAccum;
            if (accum != null)
            {
                // Середина діапазону потрібного ступеня. Гра віднімає від числа поправку за координатами колонки,
                // щоб сніг лягав плямами, тому тут вона додається назад
                float upper = stage + 1 < StageThresholds.Length ? StageThresholds[stage + 1] : StageThresholds[stage] + 0.5f;
                float level = (StageThresholds[stage] + upper) / 2;
                accum[(pos.Z & 31) * 32 + (pos.X & 31)] = level + GameMath.MurmurHash3Mod(pos.X, 0, pos.Z, 150) / 300f;
            }

            SetSnowStage(world, pos, stage);
        }

        private void SetSnowStage(IWorldAccessor world, BlockPos pos, int stage)
        {
            Block target = snowStages[stage];
            if (target == null || target.Id == Id) return;

            world.BlockAccessor.ExchangeBlock(target.Id, pos);
            world.BlockAccessor.GetBlockEntity(pos)?.MarkDirty(true);
        }
    }

    /// <summary>
    /// Сніг на блоці колії. Лежить на підсипці, а де її немає, на дні блока, якщо під блоком суцільна опора.
    /// Чим більше снігу, тим грубіше він повторює рельєф під собою: на першому ступені точно, далі блок
    /// ділиться на дедалі більші клітинки, і в кожній сніг має одну висоту.
    /// Малюється разом із мешем чанка. Колізії сніг не має.
    /// </summary>
    public class BEBehaviorTrackBedSnowCover : BlockEntityBehavior, IMicroblockBehavior
    {
        private const int N = TrackBed.Voxels;

        // Для ступенів від 1 до 4: товщина снігу над підсипкою у вокселях і сторона клітинки усереднення.
        // Мірило це сніг на землі поруч: гра кладе його шарами у 2, 4 і 6 вокселів. Підсипка на рівній колії
        // сама піднята на воксель, тому над нею снігу на воксель менше, і верх виходить урівень із землею.
        // Перший ступінь це тонка припорошка, поки на землі снігу ще немає. Другий на чверть вокселя вищий
        // за сніг на землі: рівно врівень він лежав би точно в площині верху шпал і мерехтів би з ними
        private static readonly float[] Thickness = { 0f, 0.75f, 1.25f, 3f, 5f };
        private static readonly int[] CellSize = { 1, 1, 2, 4, 8 };

        // Колонка без підсипки для розрахунку рівня снігу вважається такою, ніби підсипка заввишки
        // у воксель там є: тоді сніг по землі в блоці колії лежить на тій самій висоті, що й на землі поруч
        private const int GroundAsSurface = 1;

        // На грубих ступенях сніг над найвищим місцем клітинки не тонший за це
        private const float MinCover = 1f;

        // На крутому підйомі блоки колії стоять стовпчиком. Скільки блоків угору і вниз від себе блок переглядає
        private const int StackReach = 4;

        // Позначка в масиві низу снігу: під цією колонкою немає підсипки іншого блока колії
        private const int NoLower = int.MinValue;

        // Останній зібраний меш і з чого він зібраний. Збирати дорого, а чанк перемальовується часто
        private MeshData cachedMesh;
        private long cachedKey = -1;

        public BEBehaviorTrackBedSnowCover(BlockEntity blockentity) : base(blockentity)
        {
        }

        // Чи змінилася підсипка, видно з ключа кешу, тож окремо стежити за цим не треба
        public void RotateModel(int degrees, EnumAxis? flipAroundAxis) { }
        public void RebuildCuboidList(BoolArray16x16x16 voxels, byte[,,] voxelMaterial) { }
        public void RegenMesh() { }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            BlockEntityTrackBed bed = Blockentity as BlockEntityTrackBed;
            IBlockAccessor accessor = Api.World.BlockAccessor;
            if (bed == null) return false;

            // На крутому підйомі блоки колії стоять один над одним, до п'яти в стовпчику, і гра засніжує
            // лише верхній. Тому сніг у стовпчику ділиться по клітинках: клітинку бере найвищий блок,
            // у якого в ній є підсипка, а ступінь снігу всі беруть у верхнього
            int stage = (accessor.GetBlock(Pos) as BlockTrackBed)?.SnowStage ?? 0;
            int[] upperTops = null;
            BlockPos at = Pos.Copy();

            // Якщо сніг лежить на самому блоці, він верхній і вище дивитися нема чого
            for (int k = 1; k <= StackReach && stage == 0; k++)
            {
                at.Up();
                BlockTrackBed above = accessor.GetBlock(at) as BlockTrackBed;
                if (above == null) break;

                // Підсипка всіх блоків над нами разом: де вона є, клітинка не наша
                int[] aboveTops = (accessor.GetBlockEntity(at) as BlockEntityTrackBed)?.GetColumnTops();
                if (aboveTops != null)
                {
                    if (upperTops == null) upperTops = new int[N * N];
                    for (int i = 0; i < upperTops.Length; i++) upperTops[i] = System.Math.Max(upperTops[i], aboveTops[i]);
                }

                stage = above.SnowStage;
            }
            if (stage == 0) return false;

            // Найближча підсипка блоків колії під нами в кожній колонці, у вокселях від нашої підлоги (від'ємна)
            int[] lowerBottom = null;
            at.Set(Pos);
            for (int k = 1; k <= StackReach; k++)
            {
                at.Down();
                int[] belowTops = (accessor.GetBlockEntity(at) as BlockEntityTrackBed)?.GetColumnTops();
                if (belowTops == null) break;

                if (lowerBottom == null)
                {
                    lowerBottom = new int[N * N];
                    for (int i = 0; i < lowerBottom.Length; i++) lowerBottom[i] = NoLower;
                }
                for (int i = 0; i < lowerBottom.Length; i++)
                {
                    if (lowerBottom[i] == NoLower && belowTops[i] > 0) lowerBottom[i] = belowTops[i] - N * k;
                }
            }

            int[] tops = bed.GetColumnTops();
            bool groundSolid = accessor.IsSideSolid(Pos.X, Pos.Y - 1, Pos.Z, BlockFacing.UP);

            long key = Key(tops, upperTops, lowerBottom, stage, groundSolid);
            if (key != cachedKey)
            {
                cachedMesh = GenMesh(tops, upperTops, lowerBottom, stage, groundSolid);
                cachedKey = key;
            }

            if (cachedMesh != null) mesher.AddMeshData(cachedMesh);
            return false;
        }

        // Усе, від чого залежить форма снігу, згорнуте в одне число
        private static long Key(int[] tops, int[] upperTops, int[] lowerBottom, int stage, bool groundSolid)
        {
            unchecked
            {
                long key = stage * 2 + (groundSolid ? 1 : 0);
                key = Mix(key, tops, 31);
                key = Mix(key, upperTops, 37);
                key = Mix(key, lowerBottom, 41);
                return key & long.MaxValue;
            }
        }

        private static long Mix(long key, int[] values, int factor)
        {
            unchecked
            {
                if (values == null) return key * factor;
                for (int i = 0; i < values.Length; i++) key = key * factor + values[i] + 1;
                return key;
            }
        }

        /// <summary>
        /// Збирає сніг блока. tops це підсипка самого блока. upperTops це підсипка блоків колії над ним разом,
        /// якщо вони є: клітинки, де вона є, пропускаються. lowerBottom це верх найближчої підсипки блоків колії
        /// під ним у кожній колонці: до неї опускається сніг там, де власної підсипки немає.
        /// </summary>
        private MeshData GenMesh(int[] tops, int[] upperTops, int[] lowerBottom, int stage, bool groundSolid)
        {
            // Низ і верх снігу в кожній колонці, у вокселях від підлоги блока. Верх NaN означає, що снігу тут немає
            float[] bottom = new float[N * N];
            float[] top = new float[N * N];
            for (int i = 0; i < top.Length; i++) top[i] = float.NaN;

            int cell = CellSize[stage];

            for (int cz = 0; cz < N; cz += cell)
            {
                for (int cx = 0; cx < N; cx += cell)
                {
                    // Клітинка вирішується цілком, а не по колонках: інакше межа підсипки, що йде навскоси,
                    // ріже клітинку, і край снігу виходить дрібними зубцями
                    bool ownedByUpper = false, anySurface = false;
                    int sum = 0, highest = 0;

                    for (int z = cz; z < cz + cell; z++)
                    {
                        for (int x = cx; x < cx + cell; x++)
                        {
                            int i = x + z * N;
                            if (upperTops != null && upperTops[i] > 0) ownedByUpper = true;
                            if (tops[i] > 0 || groundSolid) anySurface = true;

                            // Припорошка лягає на те, що є; для глибшого снігу гола земля рахується як підсипка
                            int surface = stage == 1 ? tops[i] : System.Math.Max(tops[i], GroundAsSurface);
                            sum += surface;
                            if (surface > highest) highest = surface;
                        }
                    }

                    // Снігу нема на що лягти, або цю клітинку накриває блок вище
                    if (ownedByUpper || !anySurface) continue;

                    // На першому ступені клітинка це одна колонка, і сніг просто лежить на ній. Далі рельєф
                    // клітинки усереднюється із заокругленням угору, але найвище місце однаково під снігом
                    int count = cell * cell;
                    float level = stage == 1
                        ? highest + Thickness[stage]
                        : System.Math.Max((sum + count - 1) / count + Thickness[stage], highest + MinCover);

                    for (int z = cz; z < cz + cell; z++)
                    {
                        for (int x = cx; x < cx + cell; x++)
                        {
                            int i = x + z * N;
                            top[i] = level;

                            // Сніг починається від підсипки. Де її немає, від підлоги блока; а якщо там нема
                            // й опори, то від підсипки блока колії під нами, щоб під снігом не лишалося щілини
                            if (tops[i] > 0 || groundSolid) bottom[i] = tops[i];
                            else if (lowerBottom != null && lowerBottom[i] != NoLower) bottom[i] = lowerBottom[i];
                            else bottom[i] = 0;
                        }
                    }
                }
            }

            // Сусідні колонки з однаковими низом і верхом зливаються в одну коробку.
            // Коробки з однаковими низом і товщиною збираються в меш разом
            var groups = new Dictionary<long, List<uint>>();
            var groupBottom = new Dictionary<long, float>();
            var groupThickness = new Dictionary<long, float>();
            bool[] done = new bool[N * N];

            for (int z = 0; z < N; z++)
            {
                for (int x = 0; x < N; x++)
                {
                    int i = x + z * N;
                    if (done[i] || float.IsNaN(top[i])) continue;

                    int x2 = x + 1;
                    while (x2 < N && Same(bottom, top, done, i, x2 + z * N)) x2++;

                    int z2 = z + 1;
                    while (z2 < N && RowSame(bottom, top, done, i, x, x2, z2)) z2++;

                    for (int zz = z; zz < z2; zz++)
                        for (int xx = x; xx < x2; xx++)
                            done[xx + zz * N] = true;

                    float thickness = top[i] - bottom[i];
                    if (thickness <= 0) continue;

                    // Чверті вокселя вистачає, щоб розрізнити всі можливі низ і товщину
                    long group = (long)System.Math.Round(bottom[i] * 4) * 1000 + (long)System.Math.Round(thickness * 4);
                    if (!groups.TryGetValue(group, out List<uint> cuboids))
                    {
                        cuboids = new List<uint>();
                        groups[group] = cuboids;
                        groupBottom[group] = bottom[i];
                        groupThickness[group] = thickness;
                    }

                    cuboids.Add(BlockEntityMicroBlock.ToUint(x, 0, z, x2, WholeVoxels(thickness), z2, 0));
                }
            }

            MeshData result = null;
            foreach (var entry in groups)
            {
                MeshData part = Boxes(entry.Value, groupBottom[entry.Key], groupThickness[entry.Key]);
                if (part == null) continue;
                if (result == null) result = part;
                else result.AddMeshData(part);
            }

            return result;
        }

        private static bool Same(float[] bottom, float[] top, bool[] done, int a, int b)
        {
            return !done[b] && top[b] == top[a] && bottom[b] == bottom[a];
        }

        private static bool RowSame(float[] bottom, float[] top, bool[] done, int a, int x1, int x2, int z)
        {
            for (int x = x1; x < x2; x++)
            {
                if (!Same(bottom, top, done, a, x + z * N)) return false;
            }
            return true;
        }

        // На скільки цілих вокселів заввишки будується коробка перед тим, як її стиснути до потрібної товщини
        private static int WholeVoxels(float thickness)
        {
            return GameMath.Clamp((int)System.Math.Ceiling(thickness), 1, N);
        }

        // Коробки снігу однакової товщини на однаковій висоті. Меш будує збирач чізлених блоків гри,
        // тому текстура снігу лягає так само, як на них. Він уміє лише цілі вокселі в межах блока, тож коробки
        // будуються від підлоги цілими вокселями, а потім стискаються по висоті й піднімаються на своє місце.
        // Щоб текстура на боках при цьому не стискалася разом із коробкою, її шматок на боках підрізається
        private MeshData Boxes(List<uint> cuboids, float bottom, float thickness)
        {
            ICoreClientAPI capi = Api as ICoreClientAPI;
            Block snow = Api.World.GetBlock(new AssetLocation("game", "snowlayer-1"));
            if (capi == null || snow == null) return null;

            // Без позиції: збирач не відсікає грані об сусідні блоки, а наші коробки стоять не на своїй висоті
            MeshData mesh = BlockEntityMicroBlock.CreateMesh(capi, cuboids, new int[] { snow.Id }, null);
            float squeeze = thickness / WholeVoxels(thickness);
            if (squeeze < 1) TrimSideTextures(mesh, squeeze);

            mesh.Scale(new Vec3f(0, 0, 0), 1, squeeze, 1);
            mesh.Translate(0, bottom / N, 0);
            return mesh;
        }

        // Бічні грані стануть нижчими в squeeze разів. Щоб малюнок снігу на них лишився того самого масштабу,
        // що й на решті блоків, шматок текстури на кожній такій грані вкорочується по висоті так само.
        // Грань це чотири вершини поспіль; у текстурі її висоті відповідає та з двох координат,
        // яка міняється разом із висотою вершини
        private static void TrimSideTextures(MeshData mesh, float squeeze)
        {
            for (int first = 0; first + 3 < mesh.VerticesCount; first += 4)
            {
                float yLow = float.MaxValue, yHigh = float.MinValue;
                for (int v = first; v < first + 4; v++)
                {
                    float y = mesh.xyz[v * 3 + 1];
                    if (y < yLow) yLow = y;
                    if (y > yHigh) yHigh = y;
                }

                // Верхня або нижня грань: її текстура не стискається
                if (yHigh - yLow < 1e-6f) continue;

                float middle = (yLow + yHigh) / 2;
                int low = first, high = first;
                for (int v = first; v < first + 4; v++)
                {
                    if (mesh.xyz[v * 3 + 1] < middle) low = v;
                    else high = v;
                }

                // 0 якщо висоті грані відповідає перша координата текстури, 1 якщо друга
                int axis = System.Math.Abs(mesh.Uv[high * 2] - mesh.Uv[low * 2]) > System.Math.Abs(mesh.Uv[high * 2 + 1] - mesh.Uv[low * 2 + 1]) ? 0 : 1;
                float atLow = mesh.Uv[low * 2 + axis];
                float atHigh = atLow + (mesh.Uv[high * 2 + axis] - atLow) * squeeze;

                for (int v = first; v < first + 4; v++)
                {
                    if (mesh.xyz[v * 3 + 1] >= middle) mesh.Uv[v * 2 + axis] = atHigh;
                }
            }
        }
    }

    /// <summary>
    /// Дані блока колії. Наслідує сутність чізленого блока гри: звідти вокселі 16×16×16, їх зберігання,
    /// меш у меші чанка і колізія по вокселях. Вокселі це підсипка; поки її немає, блок порожній і невидимий.
    /// Понад це блок знає, які секції через нього проходять, і висоту колії в кожній своїй колонці.
    /// </summary>
    public class BlockEntityTrackBed : BlockEntityMicroBlock
    {
        // Матеріал вокселів, поки підсипку не вибрано. Сутність чізленого блока не вміє жити без матеріалу
        private const string DefaultRock = "granite";

        // Секції, що проходять через блок: чанк і номер у чанку
        private List<int[]> sections = new List<int[]>();

        // Висота колії в кожній колонці над підлогою блока, або TrackBed.NoHeight
        private float[] heights = TrackBed.NewHeights();

        // Порода гравію, з якого зараз підсипка в цьому блоці
        private string ballastRock = DefaultRock;

        public string BallastRock => ballastRock;

        /// <summary>
        /// Скільки шарів вокселів гравію зараз лежить у кожній колонці блока, рахуючи від підлоги без пропусків.
        /// Індекс колонки x + z * 16.
        /// </summary>
        public int[] GetColumnTops()
        {
            int[] tops = new int[TrackBed.Voxels * TrackBed.Voxels];
            if (VoxelCuboids.Count == 0) return tops;

            ConvertToVoxels(out BoolArray16x16x16 voxels, out _);
            for (int z = 0; z < TrackBed.Voxels; z++)
            {
                for (int x = 0; x < TrackBed.Voxels; x++)
                {
                    int top = 0;
                    while (top < TrackBed.Voxels && voxels[x, top, z]) top++;
                    tops[x + z * TrackBed.Voxels] = top;
                }
            }
            return tops;
        }

        // Колізія самої колії: по одному боксу на чверть блока, заввишки з найвищу колонку в цій чверті.
        // Грубіше за форму колії, бо колізії в грі лише вирівняні по осях
        private Cuboidf[] railBoxes;

        /// <summary>
        /// Уся колізія блока: колія плюс вокселі підсипки.
        /// </summary>
        public Cuboidf[] TrackCollisionBoxes { get; private set; }

        /// <summary>
        /// Скільки вокселів підсипки в блоці.
        /// </summary>
        public int VoxelCount => VoxelCuboids.Count == 0 ? 0 : totalVoxels;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            if (BlockIds == null || BlockIds.Length == 0) BlockIds = new int[] { GravelBlockId(api.World, ballastRock) };
            RebuildCollision();

            // Дані блока могли прийти раніше, ніж клієнт створив саму сутність. Тоді чанк уже намальований
            // без підсипки, і сам він не перемалюється
            if (api.Side == EnumAppSide.Client) RequestRedraw(api.World);
        }

        // Просить клієнт заново зібрати меш чанка з цим блоком
        private void RequestRedraw(IWorldAccessor world)
        {
            MarkMeshDirty();
            world.BlockAccessor.MarkBlockDirty(Pos);
        }

        private static int GravelBlockId(IWorldAccessor world, string rock)
        {
            Block block = world.GetBlock(new AssetLocation("game", "gravel-" + rock))
                ?? world.GetBlock(new AssetLocation("game", "gravel-" + DefaultRock));
            return block?.Id ?? 0;
        }

        public void AddSection(Section section)
        {
            Vec3i chunk = section.ChunkAddres;
            if (IndexOf(section) < 0) sections.Add(new int[] { chunk.X, chunk.Y, chunk.Z, section.IndexInChunk });
            Recompute();
        }

        public void RemoveSection(Section section)
        {
            int index = IndexOf(section);
            if (index >= 0) sections.RemoveAt(index);
            Recompute();
        }

        private int IndexOf(Section section)
        {
            Vec3i chunk = section.ChunkAddres;
            for (int i = 0; i < sections.Count; i++)
            {
                int[] s = sections[i];
                if (s[3] == section.IndexInChunk && s[0] == chunk.X && s[1] == chunk.Y && s[2] == chunk.Z) return i;
            }
            return -1;
        }

        /// <summary>
        /// Збирає форму блока з усіх секцій, що через нього проходять: висоти колії для колізії
        /// і вокселі підсипки там, де секція її має. Якщо секцій не лишилося, блок зникає,
        /// а підсипка, що в ньому була, лишається чізленим блоком тієї самої форми.
        /// </summary>
        public void Recompute()
        {
            if (Api == null || Api.Side != EnumAppSide.Server) return;

            float[] result = TrackBed.NewHeights();
            // Висота підсипки в кожній колонці вокселів і наскільки далеко від неї секція, що цю висоту дала
            int voxelColumns = TrackBed.Voxels * TrackBed.Voxels;
            float[] ballastHeight = new float[voxelColumns];
            float[] ballastBeyond = new float[voxelColumns];
            for (int v = 0; v < voxelColumns; v++) ballastBeyond[v] = float.MaxValue;
            bool anyBallast = false;
            string rock = null;

            for (int i = sections.Count - 1; i >= 0; i--)
            {
                int[] s = sections[i];
                Vec3i chunk = new Vec3i(s[0], s[1], s[2]);

                // Чанк секції вивантажений: лишаємо посилання, форму від неї зараз не дізнатися
                if (Api.World.BlockAccessor.GetChunk(chunk.X, chunk.Y, chunk.Z) == null) continue;

                DataInChunk data = DataInChunk.Get(Api.World, chunk);
                Section section = null;
                data?.RailWaySections.TryGetValue(s[3], out section);

                // Секції більше немає або вона без деталей: клітинок вона не тримає
                if (section == null || section.IsEmpty)
                {
                    sections.RemoveAt(i);
                    continue;
                }

                // Висоти колії беремо на сітці вокселів і зводимо до грубіших колонок колізії: найвища в кожній
                if (TrackBed.Rasterize(section, TrackBed.Voxels).TryGetValue(Pos, out float[] own))
                {
                    int scale = TrackBed.Voxels / TrackBed.Columns;
                    for (int z = 0; z < TrackBed.Voxels; z++)
                    {
                        for (int x = 0; x < TrackBed.Voxels; x++)
                        {
                            float h = own[x + z * TrackBed.Voxels];
                            int c = x / scale + z / scale * TrackBed.Columns;
                            if (h > result[c]) result[c] = h;
                        }
                    }
                }

                if (!section.BallastInstalled) continue;
                // Блок засипається цілком. Кожну колонку визначає секція, на чиєму відрізку вона лежить,
                // а якщо такої немає (кут блока на повороті чи діагоналі), то найближча: її площина продовжується
                float[] fine = TrackBed.BlockHeights(section, Pos, TrackBed.Voxels, out float[] beyond);
                rock = rock ?? section.BallastMaterial;
                anyBallast = true;

                for (int v = 0; v < voxelColumns; v++)
                {
                    bool closer = beyond[v] < ballastBeyond[v] - 1e-6f;
                    bool sameAndHigher = beyond[v] <= ballastBeyond[v] + 1e-6f && fine[v] > ballastHeight[v];
                    if (!closer && !sameAndHigher) continue;

                    ballastBeyond[v] = beyond[v];
                    ballastHeight[v] = fine[v];
                }
            }

            BoolArray16x16x16 voxels = new BoolArray16x16x16();
            bool anyVoxel = false;

            if (anyBallast)
            {
                for (int z = 0; z < TrackBed.Voxels; z++)
                {
                    for (int x = 0; x < TrackBed.Voxels; x++)
                    {
                        float h = ballastHeight[x + z * TrackBed.Voxels];

                        int top = TrackBed.BallastTop(h);
                        if (top == 0) continue;

                        for (int y = 0; y < top; y++) voxels[x, y, z] = true;
                        anyVoxel = true;
                    }
                }
            }

            if (sections.Count == 0)
            {
                Release();
                return;
            }

            heights = result;

            if (anyVoxel)
            {
                ballastRock = rock ?? ballastRock;
                BlockIds = new int[] { GravelBlockId(Api.World, ballastRock) };
                RebuildCuboidList(voxels, new byte[TrackBed.Voxels, TrackBed.Voxels, TrackBed.Voxels]);
            }
            else
            {
                VoxelCuboids.Clear();
            }

            RegenSelectionBoxes(Api.World, null);
            RebuildCollision();
            MarkDirty(true);
        }

        // Блок більше не потрібен жодній секції. Підсипка лишається у світі звичайним чізленим блоком гри
        // тієї самої форми і з того самого гравію: далі з ним можна робити все, що з будь-яким чізленим блоком
        private void Release()
        {
            IBlockAccessor accessor = Api.World.BlockAccessor;

            if (VoxelCount == 0)
            {
                accessor.SetBlock(0, Pos);
                return;
            }

            Block chiseled = Api.World.GetBlock(new AssetLocation("game", "chiseledblock"));
            Block gravel = Api.World.GetBlock(GravelBlockId(Api.World, ballastRock));
            if (chiseled == null || gravel == null)
            {
                accessor.SetBlock(0, Pos);
                return;
            }

            // Запам'ятовуємо форму до заміни блока: разом із блоком зникне і ця сутність
            List<uint> cuboids = new List<uint>(VoxelCuboids);
            int voxelCount = VoxelCount;
            BlockPos pos = Pos.Copy();
            IWorldAccessor world = Api.World;

            accessor.SetBlock(chiseled.Id, pos);

            BlockEntityMicroBlock placed = accessor.GetBlockEntity(pos) as BlockEntityMicroBlock;
            if (placed == null) return;

            placed.BlockIds = new int[] { gravel.Id };
            placed.VoxelCuboids = cuboids;
            placed.BlockName = gravel.GetPlacedBlockName(world, pos);
            // Чізлений блок пам'ятає, скільки вокселів матеріалу в ньому є, і долотом можна доточити форму
            // лише до цієї кількості. Записуємо рівно стільки, скільки гравію тут лежало: інакше гра вважала б,
            // що матеріалу на цілий блок, і з жменьки підсипки можна було б нарощувати повний блок гравію
            if (placed is BlockEntityChisel chisel)
            {
                chisel.AvailMaterialQuantities = new ushort[] { (ushort)System.Math.Min(voxelCount, ushort.MaxValue) };
            }

            placed.RebuildCuboidList();
            placed.RegenSelectionBoxes(world, null);
            placed.MarkDirty(true);
        }

        private void RebuildCollision()
        {
            var boxes = new List<Cuboidf>();
            int half = TrackBed.Columns / 2;

            for (int qz = 0; qz < 2; qz++)
            {
                for (int qx = 0; qx < 2; qx++)
                {
                    float top = TrackBed.NoHeight;
                    for (int z = qz * half; z < (qz + 1) * half; z++)
                    {
                        for (int x = qx * half; x < (qx + 1) * half; x++)
                        {
                            float h = heights[x + z * TrackBed.Columns];
                            if (h > top) top = h;
                        }
                    }

                    if (top <= 0) continue;
                    boxes.Add(new Cuboidf(qx * 0.5f, 0f, qz * 0.5f, qx * 0.5f + 0.5f, top, qz * 0.5f + 0.5f));
                }
            }
            railBoxes = boxes.ToArray();

            // Вокселі підсипки дають колізію так само, як у чізленого блока
            if (VoxelCuboids.Count > 0 && Api != null)
            {
                Cuboidf[] voxelBoxes = base.GetCollisionBoxes(Api.World.BlockAccessor, Pos);
                if (voxelBoxes != null) boxes.AddRange(voxelBoxes);
            }

            TrackCollisionBoxes = boxes.Count == 0 ? null : boxes.ToArray();
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            int[] flat = new int[sections.Count * 4];
            for (int i = 0; i < sections.Count; i++)
            {
                sections[i].CopyTo(flat, i * 4);
            }
            tree["railSections"] = new IntArrayAttribute(flat);
            tree["railHeights"] = new FloatArrayAttribute(heights);
            tree.SetString("railBallast", ballastRock);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            sections.Clear();
            int[] flat = (tree["railSections"] as IntArrayAttribute)?.value;
            if (flat != null)
            {
                for (int i = 0; i + 3 < flat.Length; i += 4)
                {
                    sections.Add(new int[] { flat[i], flat[i + 1], flat[i + 2], flat[i + 3] });
                }
            }

            float[] saved = (tree["railHeights"] as FloatArrayAttribute)?.value;
            heights = saved != null && saved.Length == TrackBed.ColumnCount ? saved : TrackBed.NewHeights();
            ballastRock = tree.GetString("railBallast", DefaultRock);
            RebuildCollision();

            // Сутність чізленого блока просить перемалювати чанк лише коли вже під'єднана до світу.
            // При масовому укладанні дані часто приходять раніше, і підсипка лишалася ненамальованою,
            // доки чанк не перемалює щось інше. Тому просимо самі, завжди
            if (worldAccessForResolve.Side == EnumAppSide.Client) RequestRedraw(worldAccessForResolve);
        }
    }
}
