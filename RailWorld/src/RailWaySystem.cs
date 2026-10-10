using HarmonyLib;
using ProtoBuf;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using RailWorld.src.RailWay;

namespace RailWorld
{
    public enum EnumSectionAction
    {
        Select = 0,
        Deselect = 1,
        InteractStart = 2,
        InteractStep = 3,
        InteractStop = 4,
        InteractCancel = 5,
        Break = 6
    }

    /// <summary>
    /// Клієнт повідомляє серверу, що він робить із деталлю секції.
    /// </summary>
    [ProtoContract]
    public class SectionActionPacket
    {
        [ProtoMember(1)] public int Action;
        [ProtoMember(2)] public int ChunkX;
        [ProtoMember(3)] public int ChunkY;
        [ProtoMember(4)] public int ChunkZ;
        [ProtoMember(5)] public int Index;
        [ProtoMember(6)] public int Part;
        [ProtoMember(7)] public double HitX;
        [ProtoMember(8)] public double HitY;
        [ProtoMember(9)] public double HitZ;
        [ProtoMember(10)] public int CancelReason;
    }

    public class RailWaySystem : ModSystem
    {
        private const string ChannelName = "railworld-section";

        // На скільки блоків далі за дальність взаємодії сервер ще приймає дії клієнта
        private const double ReachTolerance = 1.5;

        private ICoreAPI api;
        private ICoreClientAPI capi;
        private ICoreServerAPI sapi;

        private IClientNetworkChannel clientChannel;

        public RailWayRendererSystem Renderer { get; private set; }

        private RailSelectionRenderer selectionRenderer;
        private RoutePlanner routePlanner;
        private SectionInteractionClient interaction;

        /// <summary>
        /// Поведінка секцій при взаємодії. Щоб змінити її, підставте сюди свій нащадок SectionHandler.
        /// </summary>
        public SectionHandler Handler { get; set; } = new SectionHandler();

        // Бокси виділення деталей по чанках, тільки на клієнті
        private Dictionary<Vec3i, List<SectionBox>> clientBoxes = new Dictionary<Vec3i, List<SectionBox>>();

        // Що виділив кожен гравець і коли почав взаємодію, тільки на сервері
        private Dictionary<string, SectionActionPacket> serverSelections = new Dictionary<string, SectionActionPacket>();
        private Dictionary<string, long> serverUsingSince = new Dictionary<string, long>();

        /// <summary>
        /// Деталь, на яку зараз дивиться локальний гравець, або null. Тільки на клієнті.
        /// </summary>
        public SectionSelection CurrentSelection => interaction?.Current;

        public float CurrentBreakProgress => interaction?.BreakProgress ?? 0;

        public override bool ShouldLoad(EnumAppSide side) => true;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            this.api = api;
            Handler.OnLoaded(api);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            this.capi = api;

            clientChannel = api.Network
                .RegisterChannel(ChannelName)
                .RegisterMessageType<SectionActionPacket>();

            api.Event.BlockTexturesLoaded += OnLoaded;

            api.Event.LeaveWorld += DisposeRenderers;

            // Оновлюємо рендер коли чанк змінився
            api.Event.ChunkDirty += OnChunkDirty;

            // Harmony патч для події вивантаження чанку
            Harmony harmony = new Harmony("com.railworld.myMod");
            MyChunkEvents.Init(harmony);
            MyChunkEvents.ChunkUnloaded += OnChunkUnloaded;

            interaction = new SectionInteractionClient(api, this, harmony);

            // Камера пасажира нахиляється разом із вагонеткою
            TrolleyCameraPatch.Init(harmony, api);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            this.sapi = api;

            // Кеш статичний і міг лишитися від попереднього світу
            DataInChunk.ClearServerCache();

            api.Network
                .RegisterChannel(ChannelName)
                .RegisterMessageType<SectionActionPacket>()
                .SetMessageHandler<SectionActionPacket>(OnSectionAction);

            api.ChatCommands.Create("railinfo")
                .WithDescription("Shows the rail section you are looking at and what its ends are connected to")
                .RequiresPrivilege(Privilege.chat)
                .RequiresPlayer()
                .HandleWith(OnRailInfoCommand);

            // Гравець зламав або поставив блок: перевіряємо, чи не змінилося щось під секціями поруч
            api.Event.DidBreakBlock += (byPlayer, oldBlockId, blockSel) =>
            {
                if (blockSel?.Position != null) TrackBed.RecheckAround(api.World, blockSel.Position);
            };
            api.Event.DidPlaceBlock += (byPlayer, oldBlockId, blockSel, withItemStack) =>
            {
                if (blockSel?.Position != null) TrackBed.RecheckAround(api.World, blockSel.Position);
            };

            api.Event.PlayerDisconnect += player =>
            {
                serverSelections.Remove(player.PlayerUID);
                serverUsingSince.Remove(player.PlayerUID);
            };
        }

        private void OnLoaded()
        {
            Renderer = new RailWayRendererSystem(capi);
            selectionRenderer = new RailSelectionRenderer(capi, this);
            routePlanner = new RoutePlanner(capi, this);
        }

        // На скільки радіан важіль стрілки нахилений убік у кожному з двох положень
        private const float LeverLean = 0.45f;

        /// <summary>
        /// Деталь секції, на яку дивиться гравець. Працює і на клієнті, і на сервері,
        /// тож предмет у руці може спитати про неї в OnHeldInteractStart, де blockSel для секції дорівнює null.
        /// </summary>
        public SectionSelection GetSelection(IPlayer player)
        {
            if (player == null) return null;
            if (api.Side == EnumAppSide.Client) return CurrentSelection;

            if (!serverSelections.TryGetValue(player.PlayerUID, out SectionActionPacket packet)) return null;
            return Resolve(player, packet, out _);
        }

        // ── Клієнт ───────────────────────────────────────────────────────────

        public void SendSectionAction(EnumSectionAction action, SectionSelection sel, EnumItemUseCancelReason cancelReason = EnumItemUseCancelReason.ReleasedMouse)
        {
            clientChannel?.SendPacket(new SectionActionPacket
            {
                Action = (int)action,
                ChunkX = sel.Chunk.X,
                ChunkY = sel.Chunk.Y,
                ChunkZ = sel.Chunk.Z,
                Index = sel.Index,
                Part = (int)sel.Part,
                HitX = sel.HitPosition.X,
                HitY = sel.HitPosition.Y,
                HitZ = sel.HitPosition.Z,
                CancelReason = (int)cancelReason
            });
        }

        /// <summary>
        /// Чанки, про колію в яких знає клієнт.
        /// </summary>
        public IEnumerable<Vec3i> ClientChunkCoords => clientBoxes.Keys;

        // Секції кожного чанка за номером: щоб швидко пройти по колії через зв'язки між секціями
        private Dictionary<Vec3i, IDictionary<int, Section>> clientSections = new Dictionary<Vec3i, IDictionary<int, Section>>();

        /// <summary>
        /// Секція, на яку вказує посилання, з того, що клієнт зараз знає про колію. null, якщо її чанку немає.
        /// </summary>
        public Section FindClientSection(SectionLink link)
        {
            if (link == null || !clientSections.TryGetValue(link.Chunk, out IDictionary<int, Section> sections)) return null;
            sections.TryGetValue(link.Index, out Section section);
            return section;
        }

        public List<SectionBox> GetClientBoxes(Vec3i chunkCoord)
        {
            clientBoxes.TryGetValue(chunkCoord, out List<SectionBox> boxes);
            return boxes;
        }

        /// <summary>
        /// Замінює все, що клієнт знає про колію в чанку: бокси виділення, шпали і рейки.
        /// </summary>
        public void SetChunkData(Vec3i chunkCoord, DataInChunk data)
        {
            RemoveChunk(chunkCoord);
            if (data == null) return;

            Vec3i coord = chunkCoord.Clone();
            var boxes = new List<SectionBox>();
            foreach (var kvp in data.RailWaySections)
                Handler.GetSelectionBoxes(boxes, coord, kvp.Key, kvp.Value);
            clientBoxes[coord] = boxes;
            clientSections[coord] = data.RailWaySections;
            selectionRenderer?.MarkChunkDirty(coord);

            RebuildChunkRender(coord, data.RailWaySections);
            QueueNeighbourRender(coord);
        }

        // Чанки, вигляд колії в яких треба зібрати заново, бо змінилася колія в сусідньому
        private HashSet<Vec3i> pendingRender = new HashSet<Vec3i>();
        private bool pendingRenderScheduled;

        /// <summary>
        /// Вигляд колії в чанку залежить і від сусідніх чанків: спільна шпала стрілки малюється з двох секцій,
        /// які можуть лежати в різних чанках, а дані чанків приходять окремо і в довільному порядку.
        /// Тому після зміни колії в чанку його сусіди перемальовуються теж, трохи згодом і всі разом.
        /// </summary>
        private void QueueNeighbourRender(Vec3i chunkCoord)
        {
            if (capi == null || Renderer == null) return;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0) continue;
                        Vec3i neighbour = new Vec3i(chunkCoord.X + dx, chunkCoord.Y + dy, chunkCoord.Z + dz);
                        if (clientSections.ContainsKey(neighbour)) pendingRender.Add(neighbour);
                    }
                }
            }

            if (pendingRender.Count == 0 || pendingRenderScheduled) return;
            pendingRenderScheduled = true;
            capi.Event.RegisterCallback(dt =>
            {
                pendingRenderScheduled = false;
                var coords = new List<Vec3i>(pendingRender);
                pendingRender.Clear();

                foreach (Vec3i coord in coords)
                {
                    if (Renderer != null && clientSections.TryGetValue(coord, out IDictionary<int, Section> sections))
                        RebuildChunkRender(coord, sections);
                }
            }, 100);
        }

        // Збирає заново все, що малюється для колії чанка: шпали, важелі стрілок і рейки
        private void RebuildChunkRender(Vec3i chunkCoord, IDictionary<int, Section> sections)
        {
            if (Renderer == null) return;

            Renderer.RemoveChunkParts(chunkCoord);
            Renderer.ClearRails(chunkCoord);
            // Деталі стрілок не викидаються наперед: незмінні лишаються з готовим мешем
            Renderer.BeginParts(chunkCoord);

            var railSections = new List<Section>();
            var bladeSections = new List<Section>();
            // Розрахунки стрілок дійсні лише на час однієї перебудови: колія могла змінитися
            switchSides.Clear();

            foreach (var kvp in sections)
            {
                Section section = kvp.Value;

                // У стрілці шпали двох гілок перетинаються, тому замість двох малюється одна спільна.
                // Малює її секція першої гілки; секція другої свою шпалу пропускає
                bool sharedSleeper = false;
                if (section.SleeperInstalled && SwitchZone.FindPair(section, FindClientSection, out Section partner, out bool primary, out int fromEnd, out int pairNumber)
                    && partner.SleeperInstalled)
                {
                    sharedSleeper = true;
                    // Скільки всього пар у стрілці, відомо лише для двох останніх; для них це і потрібно
                    int pairs = fromEnd < 2 ? pairNumber + fromEnd : int.MaxValue;
                    if (primary) AddSharedSleeper(chunkCoord, section, partner, pairs < StaggerMinPairs ? 2 : fromEnd);
                }

                if (section.SleeperInstalled && !sharedSleeper)
                {
                    // Шпала лежить посередині секції, під серединою рейок, і повернута так само, як вони:
                    // поворот і ухил з хорди секції, крен середній між початком і кінцем
                    Vec3d start = section.FullStartPosition;
                    Vec3d end = section.FullEndPosition;
                    Vec3d globalPos = new Vec3d((start.X + end.X) / 2, (start.Y + end.Y) / 2, (start.Z + end.Z) / 2);
                    double dx = end.X - start.X, dy = end.Y - start.Y, dz = end.Z - start.Z;
                    float yaw = (float)Math.Atan2(dx, dz);
                    float pitch = (float)Math.Atan2(dy, Math.Sqrt(dx * dx + dz * dz));
                    float roll = (float)((Math.Asin(GameMath.Clamp(section.StartNormal.Y, -1, 1)) + Math.Asin(GameMath.Clamp(section.EndNormal.Y, -1, 1))) / 2);
                    Renderer.AddSleeper(chunkCoord, section.SleeperMaterial ?? "oak", globalPos, new Vec3f(pitch, yaw, roll), section.SleeperLength);
                }

                // Важіль стрілки на кожному кінці, з якого колія розходиться. Стоїть прямо, а нахилом убік
                // показує, котра з двох гілок увімкнена
                for (int e = 0; e < 2; e++)
                {
                    bool atStart = e == 0;
                    if (!section.HasSwitch(atStart)) continue;

                    Vec3d start = section.FullStartPosition;
                    Vec3d end = section.FullEndPosition;
                    double dx = end.X - start.X, dy = end.Y - start.Y, dz = end.Z - start.Z;
                    float yaw = (float)Math.Atan2(dx, dz);
                    float pitch = (float)Math.Atan2(dy, Math.Sqrt(dx * dx + dz * dz));
                    float lean = section.GetSwitchPosition(atStart) == 0 ? LeverLean : -LeverLean;
                    Renderer.AddLever(chunkCoord, SectionPicker.LeverPosition(section, atStart), new Vec3f(pitch, yaw, lean));
                }

                if (section.FirstRailInstalled || section.SecondRailInstalled)
                {
                    railSections.Add(section);
                    bladeSections.Add(section);
                }
            }

            if (railSections.Count > 0)
                Renderer.RebuildRails(chunkCoord, railSections, BladeShape);

            foreach (Section section in bladeSections) AddBlade(chunkCoord, section);
            Renderer.EndParts(chunkCoord);
        }

        // У стрілці, де спільних шпал менше за це, дві останні не робляться коротшими в шаховому порядку:
        // на короткій стрілці вони займали б половину її довжини
        private const int StaggerMinPairs = 6;

        /// <summary>
        /// Спільна шпала двох секцій у стрілці: одна суцільна на обидві колії.
        /// Лежить упоперек середнього напрямку двох колій, серединою між ними, тож сусідні спільні шпали
        /// майже паралельні й ідуть рівним кроком, навіть коли колії розходяться круто. Завдовжки вона така,
        /// щоб дістати до зовнішніх кінців власних шпал обох колій: за рейки виступає так само, як звичайна.
        /// Дві останні шпали стрілки коротші і лежать у шаховому порядку: передостання тримає три рейки
        /// з боку першої колії (обидві її рейки і ближню рейку другої), остання три рейки з боку другої,
        /// і за третьою рейкою виступає на звичайний хвостик. fromEnd це номер пари від краю стрілки.
        /// </summary>
        private void AddSharedSleeper(Vec3i chunkCoord, Section a, Section b, int fromEnd)
        {
            Vec3d centerA = Middle(a), centerB = Middle(b);
            Vec3d sideA = new Vec3d(a.CenterNormal.X, a.CenterNormal.Y, a.CenterNormal.Z).Normalize();
            Vec3d sideB = new Vec3d(b.CenterNormal.X, b.CenterNormal.Y, b.CenterNormal.Z).Normalize();

            // З якого боку від кожної колії лежить сусідня: зовнішній край шпали з протилежного
            Vec3d between = centerB - centerA;
            double towardB = between.X * sideA.X + between.Z * sideA.Z >= 0 ? 1 : -1;
            double towardA = between.X * sideB.X + between.Z * sideB.Z >= 0 ? -1 : 1;

            // Точки, до яких шпала має дістати з кожного боку
            Vec3d from = centerA - sideA * (towardB * a.SleeperLength / 2);
            Vec3d to = centerB - sideB * (towardA * b.SleeperLength / 2);

            // Край за третьою рейкою: ближня до сусіда рейка лежить за пів ширини колії від осі в його бік,
            // а хвостик це те, на скільки звичайна шпала виступає за рейку. Разом це зсув від осі колії
            if (fromEnd == 1) to = centerB + sideB * (towardA * (b.TrackWidth - b.SleeperLength / 2));
            else if (fromEnd == 0) from = centerA + sideA * (towardB * (a.TrackWidth - a.SleeperLength / 2));

            // Напрямок шпали: середній між нормалями двох колій, тобто впоперек середнього напрямку самих колій.
            // Нормаль другої секції може дивитися в інший бік, тоді береться з протилежним знаком
            Vec3d sameB = sideA.X * sideB.X + sideA.Z * sideB.Z >= 0 ? sideB : sideB * -1;
            Vec3d across = sideA + sameB;
            if (across.Length() < 1e-6) return;
            across.Normalize();

            // Шпала лежить на прямій через середину між коліями. Її кінці це проєкції потрібних точок на цю пряму
            Vec3d middle = new Vec3d((centerA.X + centerB.X) / 2, (centerA.Y + centerB.Y) / 2, (centerA.Z + centerB.Z) / 2);
            double first = (from.X - middle.X) * across.X + (from.Y - middle.Y) * across.Y + (from.Z - middle.Z) * across.Z;
            double second = (to.X - middle.X) * across.X + (to.Y - middle.Y) * across.Y + (to.Z - middle.Z) * across.Z;
            double low = Math.Min(first, second), high = Math.Max(first, second);
            double length = high - low;
            if (length < 1e-6) return;

            // Уздовж колії: нормаль секції лежить ліворуч від її напрямку, тож напрямок відновлюється з неї
            float yaw = (float)Math.Atan2(across.Z, -across.X);
            float roll = (float)Math.Asin(GameMath.Clamp(across.Y, -1, 1));

            Vec3d start = a.FullStartPosition, end = a.FullEndPosition;
            double dx = end.X - start.X, dy = end.Y - start.Y, dz = end.Z - start.Z;
            float pitch = (float)Math.Atan2(dy, Math.Sqrt(dx * dx + dz * dz));

            Vec3d position = middle + across * ((low + high) / 2);
            Renderer.AddSleeper(chunkCoord, a.SleeperMaterial ?? "oak", position, new Vec3f(pitch, yaw, roll), (float)length);
        }

        // Гостряк стрілки: початок внутрішньої рейки кожної з двох колій, окрема деталь зі своїм мешем.
        // Розміри взято зі справжніх стрілок і перераховано на нашу рейку, головка якої 0.075 блока.
        // Довжина: до місця, де рейка гостряка відійшла від рамної рейки на головку плюс жолоб
        // (SwitchZone.BladeHeelSeparation), але не коротше і не довше за ці межі в секціях: від 3.5 до 8 ширин
        // колії, як на справжній залізниці. При колії 0.78 це від 3 до 6 блоків
        private static readonly int MinBladeSections = (int)Math.Ceiling(TrackGauge.StandardWidth * 3.5 / 0.5);
        private static readonly int MaxBladeSections = (int)Math.Floor(TrackGauge.StandardWidth * 8.0 / 0.5);
        // Хід вістря: на скільки блоків відведений гостряк відходить від рамної рейки
        private const double BladeThrow = 0.12;

        // Котра рейка секції лежить з боку сусідньої колії стрілки: 1 перша (з боку нормалі), -1 друга
        private static double TowardPartner(Section section, Section partner)
        {
            Vec3d center = Middle(section), partnerCenter = Middle(partner);
            Vec3f normal = section.CenterNormal;
            return (partnerCenter.X - center.X) * normal.X + (partnerCenter.Z - center.Z) * normal.Z >= 0 ? 1 : -1;
        }

        // Скільки секцій завдовжки гостряк, якщо його корінь за heelAt блоків від стрілки
        private static int BladeSections(double heelAt)
        {
            return GameMath.Clamp((int)Math.Ceiling(heelAt / 0.5 - 1e-6), MinBladeSections, MaxBladeSections);
        }

        // Те саме, але гостряк не заходить у хрестовину. На крутій стрілці вона починається ближче, ніж
        // найменша довжина гостряка, і тоді гостряк коротший: закінчується там, де починається хрестовина
        private static int BladeSections(double heelAt, SwitchSide side)
        {
            int sections = BladeSections(heelAt);
            if (side != null && side.Throat >= 0) sections = Math.Min(sections, Math.Max(side.HideFrom - 1, 1));
            return sections;
        }

        // Хрестовина. Розміри зі справжніх стрілок, перераховані на нашу рейку.
        // Жолоб для гребеня колеса між вусовиком і осердям, блоків
        private const double FrogFlangeway = 0.05;
        // Як далеко від стрілки шукати хрестовину, у секціях
        private const int MaxFrogSections = 40;

        /// <summary>
        /// Усе про внутрішню рейку однієї з двох колій стрілки: її лінія і де на ній хрестовина.
        /// </summary>
        private class SwitchSide
        {
            // Перерізи внутрішньої рейки від стрілки, через чверть блока
            public List<BladeFrame> Frames;
            // Ті самі перерізи внутрішньої рейки сусідньої колії
            public List<BladeFrame> Other;
            // Де сусідня внутрішня рейка відносно своєї в кожному перерізі, блоків у бік сусідньої колії.
            // Біля стрілки вона з іншого боку, на ширину колії, далі рейки сходяться, перетинаються і розходяться
            public double[] Across;
            // Номери перерізів: де рейка підійшла до сусідньої на жолоб, де починається осердя,
            // де рейки перетинаються і де осердя набрало повну ширину. -1, якщо хрестовини немає
            public int Throat = -1, Tip, Crossing, Full;
            // Секції, на яких звичайна рейка не малюється, бо там хрестовина. Номери від стрілки, з одиниці
            public int HideFrom, HideTo;
        }

        // Розраховані колії стрілок, за першою секцією колії. Живе одну перебудову чанка
        private Dictionary<Section, SwitchSide> switchSides = new Dictionary<Section, SwitchSide>();

        /// <summary>
        /// Перерізи внутрішньої рейки колії від стрілки. first це перша секція колії, switchAtStart котрим кінцем
        /// вона до стрілки, toward з якого боку її нормалі лежить сусідня колія.
        /// </summary>
        private List<BladeFrame> RailFrames(Section first, bool switchAtStart, double toward, int sections)
        {
            var frames = new List<BladeFrame>();
            Section current = first;
            bool enteredAtStart = switchAtStart;
            Vec3d side = null;

            for (int n = 0; n < sections; n++)
            {
                // Три точки секції в порядку від стрілки. Перша збігається з останньою точкою попередньої секції
                Vec3d[] axis = { current.FullStartPosition, current.GetGlobalPos(), current.FullEndPosition };
                Vec3f[] normals = { current.StartNormal, current.CenterNormal, current.EndNormal };
                Vec3f[] tangents = { current.StartTangent, current.CenterTangent, current.EndTangent };
                double direction = enteredAtStart ? 1 : -1;
                if (!enteredAtStart)
                {
                    Array.Reverse(axis);
                    Array.Reverse(normals);
                    Array.Reverse(tangents);
                }

                for (int k = n == 0 ? 0 : 1; k < 3; k++)
                {
                    // Бік: нормаль секції, спрямована до сусідньої колії. У першій точці це відомо, далі
                    // тримаємося того самого боку: нормалі сусідніх секцій можуть дивитися в різні боки
                    Vec3d normal = new Vec3d(normals[k].X, normals[k].Y, normals[k].Z).Normalize();
                    if (side == null) normal.Mul(toward);
                    else if (normal.X * side.X + normal.Z * side.Z < 0) normal.Mul(-1);
                    side = normal;

                    Vec3d along = new Vec3d(tangents[k].X * direction, tangents[k].Y * direction, tangents[k].Z * direction);
                    along.Sub(side.Clone().Mul(along.Dot(side)));
                    if (along.Length() < 1e-6) along = new Vec3d(-side.Z, 0, side.X);
                    along.Normalize();

                    Vec3d up = side.Cross(along);
                    if (up.Y < 0) up.Mul(-1);

                    frames.Add(new BladeFrame
                    {
                        Center = new Vec3d(axis[k].X + side.X * current.TrackWidth / 2, axis[k].Y + side.Y * current.TrackWidth / 2, axis[k].Z + side.Z * current.TrackWidth / 2),
                        Side = side.Clone(),
                        Up = up,
                        Along = along
                    });
                }

                // Наступна секція колії. Якщо колія скінчилася, перерізи закінчуються там само
                List<SectionLink> links = current.GetLinks(!enteredAtStart);
                if (links.Count != 1) break;
                Section next = FindClientSection(links[0]);
                if (next == null) break;
                current = next;
                enteredAtStart = links[0].AtStart;
            }

            return frames;
        }

        /// <summary>
        /// Розраховує внутрішню рейку колії стрілки і місце хрестовини на ній. first це перша секція колії.
        /// </summary>
        private SwitchSide GetSwitchSide(Section first, bool switchAtStart, Section partnerFirst)
        {
            if (switchSides.TryGetValue(first, out SwitchSide known)) return known;

            SwitchSide result = new SwitchSide();
            switchSides[first] = result;
            result.Frames = RailFrames(first, switchAtStart, TowardPartner(first, partnerFirst), MaxFrogSections);

            if (partnerFirst == null || !SwitchZone.FindPair(partnerFirst, FindClientSection, out _, out _, out _, out int partnerNumber,
                out _, out bool partnerSwitchAtStart, out _) || partnerNumber != 1) return result;

            result.Other = RailFrames(partnerFirst, partnerSwitchAtStart, TowardPartner(partnerFirst, first), MaxFrogSections);
            int count = Math.Min(result.Frames.Count, result.Other.Count);
            result.Across = new double[count];

            double head = BladeMesh.HeadHalfWidth * 2;
            // Відстань між серединами рейок, за якої між їхніми головками лишається жолоб
            double clearance = head + FrogFlangeway;
            int tip = -1, crossing = -1, full = -1;

            // Відстань між рейками в кожному перерізі, упоперек бісектриси їхніх напрямків. Саме впоперек:
            // точки двох колій з однаковим номером не стоять одна навпроти одної, бо колії порізані на шматки
            // трохи різної довжини і відгалуження йде дугою. Пряма відстань між такими точками біля перетину
            // лишається чималою, хоча самі рейки там уже зійшлися.
            // Відстань зі знаком: до перетину сусідня рейка з боку осі своєї колії, це від'ємний бік, після
            // з протилежного. Число однакове, з якої колії не дивись, тому й межі хрестовини в обох ті самі
            double[] apart = new double[count];
            for (int q = 0; q < count; q++)
            {
                BladeFrame own = result.Frames[q], other = result.Other[q];
                double dx = other.Center.X - own.Center.X, dz = other.Center.Z - own.Center.Z;
                double ax = own.Along.X + other.Along.X, az = own.Along.Z + other.Along.Z;
                double length = Math.Sqrt(ax * ax + az * az);
                if (length < 1e-6)
                {
                    apart[q] = q > 0 ? apart[q - 1] : -Math.Sqrt(dx * dx + dz * dz);
                    continue;
                }
                double px = -az / length, pz = ax / length;
                double facing = own.Side.X * px + own.Side.Z * pz;
                apart[q] = (dx * px + dz * pz) * (facing < 0 ? -1 : 1);
            }

            for (int q = 0; q < count; q++)
            {
                double across = apart[q];
                result.Across[q] = across;

                // Вусовик починає відходити від своєї лінії заздалегідь, за два жолоби до сусідньої рейки
                if (result.Throat < 0 && across > -2 * clearance) result.Throat = q;
                // Осердя починається там, де сходяться робочі грані двох рейок: за ширину головки до перетину осей
                if (tip < 0 && across > -head * 0.8) tip = q;
                if (crossing < 0 && across >= 0) crossing = q;
                if (full < 0 && across >= head) full = q;
            }

            // Рейки так і не зійшлися в межах пройденого: хрестовини тут немає
            if (result.Throat < 0 || tip < 0 || crossing < 0)
            {
                result.Throat = -1;
                return result;
            }
            if (full < 0) full = count - 1;

            result.Tip = tip;
            result.Crossing = crossing;
            result.Full = full;
            // Переріз q лежить у секції з номером (q - 1) / 2 + 1
            result.HideFrom = Math.Max(result.Throat - 1, 0) / 2 + 1;
            result.HideTo = Math.Max(full - 1, 0) / 2 + 1;
            return result;
        }

        // Перша секція колії, на якій лежить section, і котрим кінцем вона до стрілки. number це номер section від стрілки
        private bool FindFirst(Section section, bool switchAtStart, int number, out Section first, out bool firstSwitchAtStart)
        {
            first = section;
            firstSwitchAtStart = switchAtStart;
            for (int step = 1; step < number; step++)
            {
                List<SectionLink> links = first.GetLinks(firstSwitchAtStart);
                if (links.Count != 1) return false;
                Section previous = FindClientSection(links[0]);
                if (previous == null) return false;
                first = previous;
                firstSwitchAtStart = !links[0].AtStart;
            }
            return true;
        }

        /// <summary>
        /// Каже рендеру рейок не малювати ту рейку, місце якої займає деталь стрілки: внутрішню рейку колії
        /// на секціях гостряка і на секціях хрестовини. Для решти рейок повертає null.
        /// </summary>
        private RailShape BladeShape(Section section, bool left)
        {
            // Хрестовина на крутій стрілці сягає далі, ніж спільні шпали, тому пара шукається на будь-якій відстані
            if (!SwitchZone.FindPair(section, FindClientSection, out Section partner, out _, out _, out int number, out _,
                out bool switchAtStart, out double heelAt, true) || partner == null) return null;
            if (left != TowardPartner(section, partner) > 0) return null;

            if (!FindFirst(section, switchAtStart, number, out Section first, out bool firstSwitchAtStart))
            {
                return number <= BladeSections(heelAt) ? new RailShape { Hidden = true } : null;
            }
            SwitchZone.FindPair(first, FindClientSection, out Section partnerFirst, out _, out _, out _);

            SwitchSide own = GetSwitchSide(first, firstSwitchAtStart, partnerFirst);
            if (number <= BladeSections(heelAt, own)) return new RailShape { Hidden = true };
            if (own.Throat >= 0 && number >= own.HideFrom && number <= own.HideTo) return new RailShape { Hidden = true };
            return null;
        }

        /// <summary>
        /// Ставить деталі стрілки, якщо секція це перша секція однієї з двох її колій: гостряк, вусовик і осердя.
        /// Усі три йдуть по лінії внутрішньої рейки колії, тому збираються з її перерізів.
        /// </summary>
        private void AddBlade(Vec3i chunkCoord, Section section)
        {
            if (!SwitchZone.FindPair(section, FindClientSection, out Section partner, out bool primary, out _, out int number, out bool active,
                out bool switchAtStart, out double heelAt)) return;
            if (number != 1) return;

            bool left = TowardPartner(section, partner) > 0;
            SectionPart part = left ? SectionPart.FirstRail : SectionPart.SecondRail;
            if (!section.IsInstalled(part)) return;

            string material = section.GetMaterial(part) ?? "iron";
            SwitchSide own = GetSwitchSide(section, switchAtStart, partner);

            // ── Гостряк ──
            int bladePoints = Math.Min(BladeSections(heelAt, own) * 2 + 1, own.Frames.Count);
            if (bladePoints >= 2)
            {
                // Де в кожному перерізі рамна рейка: зовнішня рейка сусідньої колії. Біля стрілки вона лежить
                // на лінії гостряка, далі відходить убік. Якщо сусідньої колії не знайдено, вважаємо,
                // що вона відходить рівномірно і біля кореня гостряк уже повний
                double[] stock = new double[bladePoints];
                for (int i = 0; i < bladePoints; i++)
                {
                    BladeFrame frame = own.Frames[i];
                    stock[i] = (double)i / (bladePoints - 1) * BladeMesh.HeadHalfWidth * 4;
                    if (own.Other != null && i < own.Other.Count)
                    {
                        BladeFrame other = own.Other[i];
                        double reach = section.TrackWidth;
                        stock[i] = (other.Center.X - other.Side.X * reach - frame.Center.X) * frame.Side.X
                            + (other.Center.Y - other.Side.Y * reach - frame.Center.Y) * frame.Side.Y
                            + (other.Center.Z - other.Side.Z * reach - frame.Center.Z) * frame.Side.Z;
                    }
                    stock[i] = Math.Max(stock[i], 0);
                }

                var blade = new LoftPart();
                LoftPoint[][] ends = null;
                void AddStation(BladeFrame frame, double position, double stockAt)
                {
                    // Відведений гостряк відходить на повний хід біля вістря і повертається на свою лінію
                    // біля кореня, який не рухається
                    double t = position / (bladePoints - 1);
                    double shift = active ? 0 : BladeThrow * (1 - t * t * (3 - 2 * t));
                    blade.Stations.Add(BladeProfile(frame, stockAt, shift, out ends));
                    if (blade.Stations.Count == 1) blade.StartCaps.AddRange(ends);
                }

                // Гостряк починається не від самої стрілки, а там, де від головки вже лишається найменша
                // ширина: ближче до стрілки від профілю є лише клаптик підошви й шийки, і його не малюємо.
                // Це місце зазвичай між двома перерізами колії, тож перший переріз гостряка ставиться між ними
                int firstFull = 0;
                while (firstFull < bladePoints && stock[firstFull] < BladeMinHead) firstFull++;
                if (firstFull > 0 && firstFull < bladePoints)
                {
                    double fraction = (BladeMinHead - stock[firstFull - 1]) / (stock[firstFull] - stock[firstFull - 1]);
                    if (fraction < 0.95) AddStation(Between(own.Frames[firstFull - 1], own.Frames[firstFull], fraction), firstFull - 1 + fraction, BladeMinHead);
                }
                for (int i = firstFull; i < bladePoints; i++) AddStation(own.Frames[i], i, stock[i]);

                if (blade.Stations.Count >= 2)
                {
                    blade.EndCaps.AddRange(ends);
                    Renderer.SetLoft(chunkCoord, section.IndexInChunk, left, "", material, new List<LoftPart> { blade });
                }
            }

            // Хрестовина одна на обидві колії: її ставить перша з них
            if (primary && own.Throat >= 0) AddFrog(chunkCoord, section, left, material, own);

            // Контррейка в кожної колії своя
            if (own.Throat >= 0) AddCheckRail(chunkCoord, section, left, own);
        }

        // На скільки кінці контррейки відігнуті від ходової рейки, блоків
        private const double CheckRailFlare = 0.04;
        // Вкладиш між шийками ходової рейки й контррейки: довжина вздовж колії і висота верху над підошвою, блоків.
        // Верх нижчий за головку рейки, щоб гребінь колеса проходив над ним
        private const double SpacerLength = 0.1;
        private const double SpacerTop = 1.5 / 16;

        /// <summary>
        /// Ставить контррейку: коротку рейку навпроти хрестовини, біля зовнішньої рейки колії, з боку її осі.
        /// Між нею і ходовою рейкою лишається жолоб для гребеня колеса. Вона притримує колесо з протилежного
        /// боку осі, поки інше проходить розрив біля вістря осердя. Кінці трохи відігнуті, щоб гребінь
        /// заходив у жолоб плавно. Між шийками двох рейок стоять вкладиші, що тримають ширину жолоба.
        /// Усе це один меш, зібраний так само, як хрестовина: повний профіль рейки в кожному перерізі,
        /// два торці і вкладиші окремими частинами.
        /// inner каже, з якого боку секції внутрішня рейка колії; контррейка стоїть біля протилежної.
        /// </summary>
        private void AddCheckRail(Vec3i chunkCoord, Section section, bool inner, SwitchSide own)
        {
            SectionPart part = inner ? SectionPart.SecondRail : SectionPart.FirstRail;
            if (!section.IsInstalled(part)) return;
            string material = section.GetMaterial(part) ?? "iron";

            int from = (own.HideFrom - 1) * 2;
            int to = Math.Min(own.Frames.Count - 1, own.HideTo * 2);
            if (to - from < 2) return;

            double half = BladeMesh.HeadHalfWidth;
            // Перерізи стоять на внутрішній рейці, бік дивиться від осі колії до неї. Зовнішня рейка лежить
            // за ширину колії в протилежний бік, контррейка ближче до осі на головку рейки і жолоб
            double gap = half * 2 + FrogFlangeway;
            double running = -section.TrackWidth;
            double offset = running + gap;

            // Вісь контррейки в кожному перерізі
            var axis = new List<Vec3d>();
            for (int q = from; q <= to; q++)
            {
                BladeFrame frame = own.Frames[q];
                double x = offset + (q == from || q == to ? CheckRailFlare : 0);
                axis.Add(new Vec3d(frame.Center.X + frame.Side.X * x, frame.Center.Y + frame.Side.Y * x, frame.Center.Z + frame.Side.Z * x));
            }

            var rail = new LoftPart();
            for (int i = 0; i < axis.Count; i++)
            {
                BladeFrame frame = own.Frames[from + i];
                // Через відгин лінія контррейки не йде точно вздовж колії: напрямок веде вона сама
                Vec3d before = axis[Math.Max(i - 1, 0)], after = axis[Math.Min(i + 1, axis.Count - 1)];
                Vec3d along = new Vec3d(after.X - before.X, after.Y - before.Y, after.Z - before.Z);
                along = along.Length() < 1e-9 ? frame.Along : along.Normalize();

                BladeFrame at = new BladeFrame { Center = axis[i], Side = frame.Side, Up = frame.Up };
                rail.Stations.Add(RailProfile(at, 0, along));

                // Торці: повний переріз рейки трьома прямокутниками, підошва, шийка і головка
                if (i == 0 || i == axis.Count - 1)
                {
                    List<LoftPoint[]> caps = i == 0 ? rail.StartCaps : rail.EndCaps;
                    caps.Add(Box(at, -RailFootHalf, RailFootHalf, 0, RailFootTop));
                    caps.Add(Box(at, -RailWebHalf, RailWebHalf, RailFootTop, FrogBodyTop));
                    caps.Add(Box(at, -half, half, FrogBodyTop, FrogRailTop));
                }
            }
            var parts = new List<LoftPart> { rail };

            // Вкладиші: посередині кожної секції, тобто між шпалами, крім відігнутих кінців
            for (int q = from + 1; q < to; q += 2)
            {
                BladeFrame frame = own.Frames[q];
                var spacer = new LoftPart();
                for (int end = -1; end <= 1; end += 2)
                {
                    double shift = end * SpacerLength / 2;
                    BladeFrame at = new BladeFrame
                    {
                        Center = new Vec3d(frame.Center.X + frame.Along.X * shift, frame.Center.Y + frame.Along.Y * shift, frame.Center.Z + frame.Along.Z * shift),
                        Side = frame.Side, Up = frame.Up
                    };
                    // Від шийки ходової рейки до шийки контррейки, над їхніми підошвами
                    LoftPoint[] box = Box(at, running + RailWebHalf, offset - RailWebHalf, RailFootTop, SpacerTop);
                    spacer.Stations.Add(new LoftStation { Along = frame.Along, Points = new[] { box[0], box[3], box[2], box[1] } });
                    (end < 0 ? spacer.StartCaps : spacer.EndCaps).Add(box);
                }
                parts.Add(spacer);
            }

            Renderer.SetLoft(chunkCoord, section.IndexInChunk, !inner, "CR", material, parts);
        }

        // Найменша ширина головки гостряка, блоків: з такої він починається біля вістря
        private const double BladeMinHead = 0.01;

        // За якого зміщення гостряка підпора під головкою сягає низу шийки, блоків. За меншого вона коротша
        private const double SupportFullOffset = 0.05;

        /// <summary>
        /// Контур гостряка в одному перерізі. Гостряк це звичайна рейка, оброблена так, як обробляють справжні:
        /// 1. З боку рамної рейки головка й підошва підрізані по формі рамної рейки, але не далі, ніж до
        ///    площини власної шийки: шийка лишається цілою, і з цього боку найвужчий гостряк це рівна стінка.
        /// 2. Де рамна рейка ближче, ніж дозволяє ця стінка, гостряк стоїть зі зміщенням: відсунутий від
        ///    своєї лінії до осі колії рівно настільки, щоб стінкою прилягати до підошви рамної рейки.
        ///    Підошва в рейки найширша, тому вона й визначає зміщення; головка гостряка при цьому
        ///    підрізана рівно по головці рамної рейки і прилягає до неї.
        /// 3. Через зміщення головка гостряка виступає за лінію, по якій котиться гребінь колеса. Усе, що
        ///    виступає, зрізане, ніби його зняли самі колеса: робоча грань головки стоїть на лінії рейки.
        ///    Тож біля вістря головка вузька, а шийка й підошва під нею ширші за неї з боку колії.
        /// stock це де вісь рамної рейки в цьому перерізі, блоків від лінії гостряка в бік Side.
        /// thrown це на скільки блоків гостряк відведений від рамної рейки.
        /// ends це торець у цьому перерізі: підошва, шийка й головка.
        /// </summary>
        private static LoftStation BladeProfile(BladeFrame frame, double stock, double thrown, out LoftPoint[][] ends)
        {
            double half = BladeMesh.HeadHalfWidth;
            const double floor = FrogBodyTop, top = FrogRailTop;

            // Робоча грань головки рамної рейки, від лінії гостряка
            double face = stock - half;
            // Зміщення: стінка шийки не може зайти за край підошви рамної рейки
            double offset = Math.Max(0, RailWebHalf - (stock - RailFootHalf));

            // Далі все у власних координатах гостряка, від його осі
            // Головка: з боку рамної рейки до її грані, але не вужче за шийку; з боку колії до лінії рейки
            double headRight = GameMath.Clamp(face + offset, RailWebHalf, half);
            double headLeft = Math.Min(-half + offset, headRight);
            // Підошва: до підошви рамної рейки, але не вужче за шийку
            double footRight = GameMath.Clamp(stock - RailFootHalf + offset, RailWebHalf, RailFootHalf);

            // Біля вістря головка вузька і через зміщення стоїть не над шийкою, а збоку від неї, з боку
            // рамної рейки. Щоб вона не висіла в повітрі, під нею підпора: стінка від краю головки похило
            // спускається до низу шийки. Місце для неї є: це простір над підошвою рамної рейки, під її
            // головкою. Що менше зміщення, то коротша підпора; без зміщення це звичайний низ головки
            double support = floor - (floor - RailFootTop) * GameMath.Clamp(offset / SupportFullOffset, 0, 1);

            double move = offset + thrown;
            LoftPoint P(double x, double y) => Point(frame, x - move, y);

            ends = new[]
            {
                Box(frame, -RailFootHalf - move, footRight - move, 0, RailFootTop),
                Box(frame, -RailWebHalf - move, RailWebHalf - move, RailFootTop, floor),
                Box(frame, headLeft - move, headRight - move, floor, top),
                new[] { P(RailWebHalf, support), P(headRight, floor), P(RailWebHalf, floor), P(RailWebHalf, floor) }
            };
            return new LoftStation
            {
                Along = frame.Along,
                Points = new[]
                {
                    // Бік колії: підошва, шийка, головка від лінії рейки
                    P(-RailFootHalf, 0), P(-RailFootHalf, RailFootTop), P(-RailWebHalf, RailFootTop), P(-RailWebHalf, floor),
                    P(headLeft, floor), P(headLeft, top),
                    // Верх головки і бік рамної рейки
                    P(headRight, top), P(headRight, floor), P(RailWebHalf, support), P(RailWebHalf, RailFootTop),
                    P(footRight, RailFootTop), P(footRight, 0)
                }
            };
        }

        // Переріз рейки між двома сусідніми: part від 0 (перший) до 1 (другий)
        private static BladeFrame Between(BladeFrame a, BladeFrame b, double part)
        {
            Vec3d Mixed(Vec3d p, Vec3d q) => new Vec3d(p.X + (q.X - p.X) * part, p.Y + (q.Y - p.Y) * part, p.Z + (q.Z - p.Z) * part);
            return new BladeFrame { Center = Mixed(a.Center, b.Center), Side = Mixed(a.Side, b.Side).Normalize(), Up = Mixed(a.Up, b.Up).Normalize(), Along = Mixed(a.Along, b.Along).Normalize() };
        }

        // Прямокутник у перерізі: кути по колу, починаючи з лівого нижнього, далі правий нижній
        private static LoftPoint[] Box(BladeFrame at, double left, double right, double bottom, double top)
        {
            return new[] { Point(at, left, bottom), Point(at, right, bottom), Point(at, right, top), Point(at, left, top) };
        }

        // Хрестовина лита, один суцільний блок. Висоти його частин над підошвою рейки, блоків: тіло доходить
        // до низу головки рейки, поверхні кочення до її верху. Числа з моделі рейки (shapes/item/rail.json)
        private const double FrogBodyTop = 1.8 / 16;
        private const double FrogRailTop = 2.5 / 16;

        // Профіль звичайної рейки, блоків (shapes/item/rail.json): пів ширини підошви, її висота, пів товщини шийки
        private const double RailFootHalf = 1.0 / 16;
        private const double RailFootTop = 0.3 / 16;
        private const double RailWebHalf = 0.2 / 16;

        /// <summary>
        /// Збирає хрестовину: одну литу деталь. У кожному перерізі задано її контур, і меш це поверхня,
        /// натягнута на ці контури: усередині немає жодної грані.
        /// Контур зліва направо: зовнішній бік лівого вусовика, такий самий, як у звичайної рейки (підошва,
        /// шийка, головка); верх вусовика; жолоб із дном на рівні низу головки; осердя, яке далі розходиться
        /// на два промені; другий жолоб; правий вусовик і його зовнішній бік. Тож назовні хрестовина ніде
        /// не виступає за контур рейки, а між рейками суцільна.
        /// Перед вістрям осердя немає: його точки зведені в одну на дні жолоба, і така грань не малюється.
        /// Від перерізу без осердя до першого перерізу з ним воно піднімається схилом, як зістружене вістря.
        /// Усі перерізи тіла спільні для двох колій: посередині між двома рейками, упоперек бісектриси їхніх
        /// напрямків. Тому торці тіла пласкі й замкнені. Але рейки двох колій закінчуються кожна у своїй
        /// площині, упоперек себе, і з пласким торцем не збігаються. Тому крайній переріз тіла відсунутий
        /// углиб настільки, щоб жодна з двох рейок у нього не заходила, а від нього до стику з рейкою колії
        /// добудовано хвостик: повний профіль рейки, усі його точки. Стінка торця закриває проміжок між
        /// хвостиками.
        /// </summary>
        private void AddFrog(Vec3i chunkCoord, Section section, bool left, string material, SwitchSide own)
        {
            double half = BladeMesh.HeadHalfWidth;
            // Відстань між серединами рейок, за якої між їхніми головками лишається жолоб
            double clearance = half * 2 + FrogFlangeway;
            const double floor = FrogBodyTop, top = FrogRailTop;

            int count = own.Across.Length;
            int from = (own.HideFrom - 1) * 2;
            int to = Math.Min(count - 1, own.HideTo * 2);
            if (to - from < 1) return;

            // Тіло вузла
            var body = new LoftPart();
            List<LoftStation> stations = body.Stations;
            List<LoftPoint[]> startCaps = body.StartCaps, endCaps = body.EndCaps;
            // Хвостики: два на початку вузла, від рейок колій до вусовиків, і два в кінці, від променів осердя
            var parts = new List<LoftPart> { body };

            for (int q = from; q <= to; q++)
            {
                // Перерізи внутрішніх рейок двох колій у цьому місці: своєї і сусідньої. Сусідня повернута боком
                // у той самий бік, що й своя
                BladeFrame mine = own.Frames[q], theirs = Mirrored(own.Other[q]);
                bool first = q == from, last = q == to;

                // Спільний переріз вузла
                Vec3d center = new Vec3d((mine.Center.X + theirs.Center.X) / 2, (mine.Center.Y + theirs.Center.Y) / 2, (mine.Center.Z + theirs.Center.Z) / 2);
                Vec3d along = new Vec3d(mine.Along.X + theirs.Along.X, mine.Along.Y + theirs.Along.Y, mine.Along.Z + theirs.Along.Z);
                if (along.Length() < 1e-6) along = mine.Along.Clone();
                along.Normalize();

                Vec3d side = mine.Side.Clone();
                side.Sub(along.Clone().Mul(side.Dot(along)));
                if (side.Length() < 1e-6) continue;
                side.Normalize();
                Vec3d up = side.Cross(along);
                if (up.Y < 0) up.Mul(-1);

                // Крайній переріз відсувається вглиб вузла, за найдальший кут підошви обох рейок
                if (first || last)
                {
                    double shift = 0;
                    foreach (BladeFrame rail in new[] { mine, theirs })
                    {
                        for (int corner = -1; corner <= 1; corner += 2)
                        {
                            double reach = (rail.Center.X + rail.Side.X * RailFootHalf * corner - center.X) * along.X
                                + (rail.Center.Y + rail.Side.Y * RailFootHalf * corner - center.Y) * along.Y
                                + (rail.Center.Z + rail.Side.Z * RailFootHalf * corner - center.Z) * along.Z;
                            shift = first ? Math.Max(shift, reach) : Math.Min(shift, reach);
                        }
                    }
                    shift = GameMath.Clamp(shift, -0.2, 0.2);
                    center = new Vec3d(center.X + along.X * shift, center.Y + along.Y * shift, center.Z + along.Z * shift);
                }
                BladeFrame station = new BladeFrame { Center = center, Side = side, Up = up, Along = along };

                // Де дві рейки перетинають цей переріз. До перетину рейок своя з додатного боку, сусідня
                // з від'ємного, після перетину навпаки: знак виходить сам
                double mineAt = Across(mine, station), theirsAt = Across(theirs, station);
                double across = theirsAt - mineAt;

                // На якій відстані від сусідньої рейки тримається вусовик. Далеко від перетину це просто його
                // рейка на своїй лінії; ближче за два жолоби відстань плавно перестає зменшуватися
                // і від перетину далі дорівнює жолобу
                double keep = clearance;
                if (across < 0)
                {
                    double near = Math.Min(-across / (2 * clearance), 1);
                    keep = near < 1 ? clearance * (1 + near * near) : -across;
                }
                // На початку вузла вусовики це самі рейки колій
                if (first) keep = Math.Abs(across);

                // Лівий вусовик на початку вузла це рейка сусідньої колії, далі він іде поруч зі своєю.
                // Правий навпаки
                double wingLeft = mineAt - keep;
                double wingRight = theirsAt + keep;

                // Осердя: від робочої грані своєї рейки до робочої грані сусідньої. Поки рейки не розійшлися
                // на ширину головки, воно суцільне, далі це два промені з дном між ними
                double noseA, noseB, noseC, noseD, noseTop = floor;
                if (last)
                {
                    // У кінці вузла промені це самі рейки колій
                    noseTop = top;
                    noseA = mineAt - half;
                    noseB = mineAt + half;
                    noseC = theirsAt - half;
                    noseD = theirsAt + half;
                    if (noseB > noseC) noseB = noseC = (noseB + noseC) / 2;
                }
                else if (q >= own.Tip)
                {
                    noseTop = top;
                    noseA = mineAt - half;
                    noseB = Math.Min(mineAt + half, 0);
                    noseC = Math.Max(theirsAt - half, 0);
                    noseD = theirsAt + half;
                    noseA = Math.Min(Math.Max(noseA, wingLeft + half), noseB);
                    noseD = Math.Max(Math.Min(noseD, wingRight - half), noseC);
                }
                else
                {
                    noseA = noseB = noseC = noseD = GameMath.Clamp(0, wingLeft + half, wingRight - half);
                }
                // Між променями дно, лише коли вони справді розійшлися
                double between = noseC - noseB > 1e-6 ? floor : noseTop;

                LoftPoint P(double x, double y) => Point(station, x, y);

                stations.Add(new LoftStation
                {
                    Along = along,
                    Points = new[]
                    {
                        // Зовнішній бік лівого вусовика: профіль рейки
                        P(wingLeft - RailFootHalf, 0), P(wingLeft - RailFootHalf, RailFootTop), P(wingLeft - RailWebHalf, RailFootTop),
                        P(wingLeft - RailWebHalf, floor), P(wingLeft - half, floor), P(wingLeft - half, top),
                        // Його верх і стінка жолоба
                        P(wingLeft + half, top), P(wingLeft + half, floor),
                        // Осердя: лівий промінь, дно між променями, правий промінь
                        P(noseA, floor), P(noseA, noseTop), P(noseB, noseTop), P(noseB, between),
                        P(noseC, between), P(noseC, noseTop), P(noseD, noseTop), P(noseD, floor),
                        // Правий вусовик
                        P(wingRight - half, floor), P(wingRight - half, top),
                        P(wingRight + half, top), P(wingRight + half, floor), P(wingRight + RailWebHalf, floor),
                        P(wingRight + RailWebHalf, RailFootTop), P(wingRight + RailFootHalf, RailFootTop), P(wingRight + RailFootHalf, 0)
                    }
                });

                if (first)
                {
                    // Торець: стінка між шийками двох вусовиків. Самі вусовики продовжуються хвостиками до рейок
                    startCaps.Add(new[] { P(wingLeft + RailWebHalf, 0), P(wingRight - RailWebHalf, 0), P(wingRight - RailWebHalf, floor), P(wingLeft + RailWebHalf, floor) });
                    parts.Add(Tail( RailProfile(theirs, 0, theirs.Along), RailProfile(station, wingLeft, theirs.Along)));
                    parts.Add(Tail( RailProfile(mine, 0, mine.Along), RailProfile(station, wingRight, mine.Along)));
                }
                if (last)
                {
                    // Торець: вусовики тут обриваються, видно весь їхній переріз, і стінка між ними.
                    // Промені осердя продовжуються хвостиками до рейок
                    endCaps.Add(new[] { P(wingLeft - RailFootHalf, 0), P(wingLeft - RailWebHalf, 0), P(wingLeft - RailWebHalf, RailFootTop), P(wingLeft - RailFootHalf, RailFootTop) });
                    endCaps.Add(new[] { P(wingLeft - RailWebHalf, 0), P(wingRight + RailWebHalf, 0), P(wingRight + RailWebHalf, floor), P(wingLeft - RailWebHalf, floor) });
                    endCaps.Add(new[] { P(wingLeft - half, floor), P(wingLeft + half, floor), P(wingLeft + half, top), P(wingLeft - half, top) });
                    endCaps.Add(new[] { P(wingRight - half, floor), P(wingRight + half, floor), P(wingRight + half, top), P(wingRight - half, top) });
                    endCaps.Add(new[] { P(wingRight + RailWebHalf, 0), P(wingRight + RailFootHalf, 0), P(wingRight + RailFootHalf, RailFootTop), P(wingRight + RailWebHalf, RailFootTop) });
                    parts.Add(Tail( RailProfile(station, mineAt, mine.Along), RailProfile(mine, 0, mine.Along)));
                    parts.Add(Tail( RailProfile(station, theirsAt, theirs.Along), RailProfile(theirs, 0, theirs.Along)));
                }
            }

            // Тіло й хвостики це один меш
            Renderer.SetLoft(chunkCoord, section.IndexInChunk, left, "F", material, parts);
        }

        private static LoftPart Tail(LoftStation from, LoftStation to)
        {
            var part = new LoftPart();
            part.Stations.Add(from);
            part.Stations.Add(to);
            return part;
        }

        private static LoftPoint Point(BladeFrame at, double x, double y)
        {
            return new LoftPoint { Center = at.Center, Side = at.Side, Up = at.Up, X = x, Y = y };
        }

        // Де лінія рейки перетинає площину перерізу вузла: відстань від його середини вздовж його боку.
        // Рейка біля перерізу вважається прямою
        private static double Across(BladeFrame rail, BladeFrame station)
        {
            double dx = station.Center.X - rail.Center.X, dy = station.Center.Y - rail.Center.Y, dz = station.Center.Z - rail.Center.Z;
            double facing = rail.Along.Dot(station.Along);
            double t = Math.Abs(facing) > 1e-6 ? (dx * station.Along.X + dy * station.Along.Y + dz * station.Along.Z) / facing : 0;
            return (rail.Along.X * t - dx) * station.Side.X + (rail.Along.Y * t - dy) * station.Side.Y + (rail.Along.Z * t - dz) * station.Side.Z;
        }

        // Повний профіль звичайної рейки в перерізі at, з віссю за x блоків від його середини: усі дванадцять точок
        private static LoftStation RailProfile(BladeFrame at, double x, Vec3d along)
        {
            double half = BladeMesh.HeadHalfWidth;
            // Початок відліку ставимо на вісь рейки: лінію хвостика між двома перерізами веде крива
            // від початку відліку одного до початку відліку іншого, і вона має йти вздовж рейки
            BladeFrame axis = new BladeFrame
            {
                Center = new Vec3d(at.Center.X + at.Side.X * x, at.Center.Y + at.Side.Y * x, at.Center.Z + at.Side.Z * x),
                Side = at.Side, Up = at.Up
            };
            return new LoftStation
            {
                Along = along,
                Points = new[]
                {
                    Point(axis, -RailFootHalf, 0), Point(axis, -RailFootHalf, RailFootTop), Point(axis, -RailWebHalf, RailFootTop),
                    Point(axis, -RailWebHalf, FrogBodyTop), Point(axis, -half, FrogBodyTop), Point(axis, -half, FrogRailTop),
                    Point(axis, half, FrogRailTop), Point(axis, half, FrogBodyTop), Point(axis, RailWebHalf, FrogBodyTop),
                    Point(axis, RailWebHalf, RailFootTop), Point(axis, RailFootHalf, RailFootTop), Point(axis, RailFootHalf, 0)
                }
            };
        }

        // Переріз сусідньої рейки, повернутий боком у той самий бік, що й спільні перерізи вузла. Її власний бік
        // дивиться назустріч, і якби лишити його, поверхня між сусідніми перерізами вивернулася б навиворіт
        private static BladeFrame Mirrored(BladeFrame frame)
        {
            return new BladeFrame { Center = frame.Center, Side = frame.Side.Clone().Mul(-1), Up = frame.Up, Along = frame.Along };
        }

        private static Vec3d RailPoint(Vec3d axis, Vec3f normal, double offset)
        {
            return new Vec3d(axis.X + normal.X * offset, axis.Y + normal.Y * offset, axis.Z + normal.Z * offset);
        }

        private static Vec3d Middle(Section section)
        {
            Vec3d start = section.FullStartPosition, end = section.FullEndPosition;
            return new Vec3d((start.X + end.X) / 2, (start.Y + end.Y) / 2, (start.Z + end.Z) / 2);
        }

        public void RemoveChunk(Vec3i chunkCoord)
        {
            bool known = clientSections.Remove(chunkCoord);
            clientBoxes.Remove(chunkCoord);
            selectionRenderer?.MarkChunkDirty(chunkCoord);
            // Колія в чанку зникла: сусіди могли малювати з нею спільні шпали
            if (known) QueueNeighbourRender(chunkCoord);
            Renderer?.RemoveChunkParts(chunkCoord);
            Renderer?.RemoveRailChunk(chunkCoord);
        }

        private void OnChunkDirty(Vec3i chunkCoord, IWorldChunk chunk, EnumChunkDirtyReason reason)
        {
            // При MarkedDirty (ламання блоків тощо) не чіпаємо рейки —
            // наші зміни приходять окремим RailDataPacket
            if (reason == EnumChunkDirtyReason.MarkedDirty) return;

            DataInChunk railData = chunk.GetModdata<DataInChunk>(DataInChunk.ModdataKey, null);
            if (railData == null) return;

            SetChunkData(chunkCoord, railData);
        }

        private void OnChunkUnloaded(IClientChunk chunk)
        {
            DataInChunk railData = chunk.GetModdata<DataInChunk>(DataInChunk.ModdataKey, null);
            if (railData == null) return;

            // Координати чанку беремо з першої секції
            foreach (var kvp in railData.RailWaySections)
            {
                RemoveChunk(kvp.Value.ChunkAddres);
                break;
            }
        }

        private void DisposeRenderers()
        {
            Renderer?.Dispose();
            Renderer = null;
            selectionRenderer?.Dispose();
            selectionRenderer = null;
            routePlanner?.Dispose();
            routePlanner = null;
            interaction?.Dispose();
            clientBoxes.Clear();
        }

        // ── Сервер ───────────────────────────────────────────────────────────

        private void OnSectionAction(IServerPlayer fromPlayer, SectionActionPacket packet)
        {
            string uid = fromPlayer.PlayerUID;
            EnumSectionAction action = (EnumSectionAction)packet.Action;

            if (action == EnumSectionAction.Select)
            {
                serverSelections[uid] = packet;
                return;
            }
            if (action == EnumSectionAction.Deselect)
            {
                serverSelections.Remove(uid);
                return;
            }

            SectionSelection sel = Resolve(fromPlayer, packet, out string rejectReason);
            if (sel == null)
            {
                sapi.Logger.Notification("[RailWorld] Section action {0} from {1} rejected: {2}", action, fromPlayer.PlayerName, rejectReason);
                return;
            }

            IWorldAccessor world = sapi.World;
            float secondsUsed = serverUsingSince.TryGetValue(uid, out long since) ? (sapi.World.ElapsedMilliseconds - since) / 1000f : 0;

            switch (action)
            {
                case EnumSectionAction.InteractStart:
                    if (!world.Claims.TryAccess(fromPlayer, sel.Position, EnumBlockAccessFlags.Use)) return;
                    if (Handler.OnSectionInteractStart(world, fromPlayer, sel)) serverUsingSince[uid] = world.ElapsedMilliseconds;
                    break;

                case EnumSectionAction.InteractStep:
                    Handler.OnSectionInteractStep(secondsUsed, world, fromPlayer, sel);
                    break;

                case EnumSectionAction.InteractStop:
                    Handler.OnSectionInteractStop(secondsUsed, world, fromPlayer, sel);
                    serverUsingSince.Remove(uid);
                    break;

                case EnumSectionAction.InteractCancel:
                    Handler.OnSectionInteractCancel(secondsUsed, world, fromPlayer, sel, (EnumItemUseCancelReason)packet.CancelReason);
                    serverUsingSince.Remove(uid);
                    break;

                case EnumSectionAction.Break:
                    if (!world.Claims.TryAccess(fromPlayer, sel.Position, EnumBlockAccessFlags.BuildOrBreak))
                    {
                        sapi.Logger.Notification("[RailWorld] Section break from {0} rejected: no build access at {1}", fromPlayer.PlayerName, sel.Position);
                        return;
                    }
                    Handler.OnSectionBroken(world, sel, fromPlayer);
                    break;
            }
        }

        private TextCommandResult OnRailInfoCommand(TextCommandCallingArgs args)
        {
            SectionSelection sel = GetSelection(args.Caller.Player);
            if (sel == null) return TextCommandResult.Error("Look at a rail section first");

            RailDataSession session = new RailDataSession(sapi.World);
            DataInChunk data = session.Get(sel.Chunk);
            if (data == null || !data.RailWaySections.TryGetValue(sel.Index, out Section section))
                return TextCommandResult.Error("Section not found");

            // Заразом доз'єднує колію, прокладену до появи зв'язків
            string start = DescribeEnd(session, section, true);
            string end = DescribeEnd(session, section, false);
            if (session.HasChanges) session.SaveAll();

            return TextCommandResult.Success(string.Format(
                "Section {0} in chunk {1}. Sleeper: {2}, rails: {3}/{4}. Start -> {5}. End -> {6}.",
                section.IndexInChunk, section.ChunkAddres,
                section.SleeperInstalled, section.FirstRailInstalled, section.SecondRailInstalled, start, end));
        }

        private static string DescribeEnd(RailDataSession session, Section section, bool atStart)
        {
            SectionStep next = SectionLinker.GetNext(session, section, atStart);
            int count = section.GetLinks(atStart).Count;
            if (next == null) return count == 0 ? "nothing" : "unloaded chunk";

            return string.Format("section {0} in chunk {1} ({2}){3}",
                next.Section.IndexInChunk, next.Section.ChunkAddres, next.EnterAtStart ? "its start" : "its end",
                count > 1 ? ", " + count + " links" : "");
        }

        // Перевіряє, що деталь із пакета справді існує і гравець до неї дотягується
        private SectionSelection Resolve(IPlayer player, SectionActionPacket packet, out string rejectReason)
        {
            rejectReason = null;
            Vec3i chunk = new Vec3i(packet.ChunkX, packet.ChunkY, packet.ChunkZ);
            DataInChunk data = DataInChunk.Get(sapi.World, chunk);
            if (data == null || !data.RailWaySections.TryGetValue(packet.Index, out Section section))
            {
                rejectReason = "no section " + packet.Index + " in chunk " + chunk;
                return null;
            }

            SectionPart part = (SectionPart)packet.Part;
            if (!Enum.IsDefined(typeof(SectionPart), packet.Part))
            {
                rejectReason = "unknown part " + packet.Part;
                return null;
            }

            Vec3d hit = new Vec3d(packet.HitX, packet.HitY, packet.HitZ);
            Vec3d eye = player.Entity.Pos.XYZ.Add(player.Entity.LocalEyePos);
            double distance = eye.DistanceTo(hit);
            if (distance > player.WorldData.PickingRange + ReachTolerance)
            {
                rejectReason = "out of reach, distance " + distance.ToString("0.00") + ", picking range " + player.WorldData.PickingRange;
                return null;
            }

            return new SectionSelection
            {
                Chunk = chunk,
                Index = packet.Index,
                Part = part,
                Section = section,
                HitPosition = hit,
                Distance = distance
            };
        }

        public override void Dispose()
        {
            base.Dispose();
            DisposeRenderers();
        }
    }
}
