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

        // Поруч щось змінилося: можливо, прибрали або поставили блок на шляху сусідньої секції
        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            if (world.Side == EnumAppSide.Server) TrackBed.RecheckAround(world, neibpos);
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
