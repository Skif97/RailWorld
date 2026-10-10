using HarmonyLib;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
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
        Break = 6,
        // Команда .railinfo: розказати, що сервер знає про секцію
        Info = 7
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

            // Плавне переведення стрілок: щокадру, поки котрась рухається
            api.Event.RegisterGameTickListener(OnSwitchAnimationTick, 15);

            // Harmony патч для події вивантаження чанку
            Harmony harmony = new Harmony("com.railworld.myMod");
            MyChunkEvents.Init(harmony);
            MyChunkEvents.ChunkUnloaded += OnChunkUnloaded;

            interaction = new SectionInteractionClient(api, this, harmony);

            // Команда клієнтська: лише клієнт знає, на що гравець дивився. Поки відкритий чат, під прицілом
            // нічого немає, тому беремо секцію, на яку він дивився останньою. Відповідає сервер: дані в нього
            api.ChatCommands.Create("railinfo")
                .WithDescription("Shows the rail section you looked at last and what the server knows about it")
                .HandleWith(OnRailInfoCommand);

            // Камера пасажира нахиляється разом із вагонеткою
            TrolleyCameraPatch.Init(harmony, api);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            this.sapi = api;

            // Кеш статичний і міг лишитися від попереднього світу
            DataInChunk.ClearServerCache();

            // Записи про стрілки для світів, збережених до їх появи, і для стрілок, порахованих неповністю
            api.Event.ChunkColumnLoaded += OnChunkColumnLoaded;

            api.Network
                .RegisterChannel(ChannelName)
                .RegisterMessageType<SectionActionPacket>()
                .SetMessageHandler<SectionActionPacket>(OnSectionAction);

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

        private TextCommandResult OnRailInfoCommand(TextCommandCallingArgs args)
        {
            SectionSelection sel = interaction?.LastLooked;
            if (sel == null) return TextCommandResult.Error("Look at a rail section first");

            SendSectionAction(EnumSectionAction.Info, sel);
            return TextCommandResult.Success();
        }

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

        /// <summary>Секції колії, що зберігаються в чанку, якими їх знає клієнт; null, якщо колії там немає.</summary>
        public IEnumerable<Section> ClientSectionsIn(Vec3i chunkCoord)
        {
            return clientSections.TryGetValue(chunkCoord, out IDictionary<int, Section> sections) ? sections.Values : null;
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
            // Колії в чанку більше немає: прибираємо все. Якщо ж дані просто замінюються новими, нічого наперед
            // не викидаємо: перебудова сама прибере зайве, а незмінні деталі стрілок лишить як є
            if (data == null)
            {
                RemoveChunk(chunkCoord);
                return;
            }

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
        // ── Переведення стрілки ──
        // Сервер перемикає стрілку миттєво, і клієнт дізнається про це з нових даних чанка. Рух гостряків
        // і важеля це лише вигляд: клієнт помічає, що положення стрілки змінилося, і далі сам плавно
        // переводить деталі зі старого положення в нове.

        // Скільки триває рух на екрані. Трохи довше за час, коли вагонетка на стрілці сходить із колії
        private const int SwitchAnimationMs = Section.SwitchTimeMs + 200;

        // Стрілка, яку клієнт уже бачив: у якому вона положенні і коли воно востаннє змінилося (час клієнта)
        private class SwitchState
        {
            public Vec3i Chunk;
            public int Index;
            public bool AtStart;
            public int Position;
            // Де стрілка була в мить останньої зміни: 0 це перша гілка, 1 друга, між ними проміжні положення
            public double From;
            public long ChangedAtMs = long.MinValue / 2;
            public bool Moving;
        }

        private Dictionary<string, SwitchState> switchStates = new Dictionary<string, SwitchState>();

        // Стрілку визначає кінець секції, з якого колія розходиться
        private static string SwitchKey(Vec3i chunk, int index, bool atStart)
        {
            return $"{chunk.X}_{chunk.Y}_{chunk.Z}_{index}_{(atStart ? "S" : "E")}";
        }

        // Запам'ятовує положення стрілки. Якщо воно не те, що бачили минулого разу, з цієї миті починається рух.
        // Стрілка, яку бачимо вперше, одразу стоїть на місці
        private void TrackSwitch(Vec3i chunkCoord, Section section, bool atStart)
        {
            string key = SwitchKey(chunkCoord, section.IndexInChunk, atStart);
            int position = section.GetSwitchPosition(atStart);

            if (!switchStates.TryGetValue(key, out SwitchState state))
            {
                switchStates[key] = new SwitchState { Chunk = chunkCoord.Clone(), Index = section.IndexInChunk, AtStart = atStart, Position = position, From = position };
                return;
            }
            if (state.Position == position) return;

            // Рух починається з того місця, де стрілка зараз: якщо її перевели назад, не дочекавшись кінця,
            // вона розвертається з півдороги, а не перестрибує в крайнє положення
            state.From = SwitchValue(state);
            state.Position = position;
            state.ChangedAtMs = capi.ElapsedMilliseconds;
            state.Moving = true;
        }

        // Де стрілка зараз: 0 це перша гілка, 1 друга. Рух плавно розганяється і плавно зупиняється;
        // неповний шлях займає відповідно менше часу
        private double SwitchValue(SwitchState state)
        {
            double distance = Math.Abs(state.Position - state.From);
            if (distance < 1e-6) return state.Position;

            double passed = GameMath.Clamp((capi.ElapsedMilliseconds - state.ChangedAtMs) / (SwitchAnimationMs * distance), 0, 1);
            return state.From + (state.Position - state.From) * passed * passed * (3 - 2 * passed);
        }

        // Наскільки стрілка вже стала в теперішнє положення, від 0 до 1
        private double SwitchProgress(string key)
        {
            if (!switchStates.TryGetValue(key, out SwitchState state)) return 1;
            double value = SwitchValue(state);
            return state.Position == 1 ? value : 1 - value;
        }

        // Важіль стрілки. Стоїть прямо, а нахилом убік показує, котра з двох гілок увімкнена; під час
        // переведення перекидається з одного боку на інший
        private void AddLever(Vec3i chunkCoord, Section section, bool atStart)
        {
            Vec3d start = section.FullStartPosition;
            Vec3d end = section.FullEndPosition;
            double dx = end.X - start.X, dy = end.Y - start.Y, dz = end.Z - start.Z;
            float yaw = (float)Math.Atan2(dx, dz);
            float pitch = (float)Math.Atan2(dy, Math.Sqrt(dx * dx + dz * dz));

            float target = section.GetSwitchPosition(atStart) == 0 ? LeverLean : -LeverLean;
            double moved = SwitchProgress(SwitchKey(chunkCoord, section.IndexInChunk, atStart));
            float lean = (float)(target * (2 * moved - 1));
            Renderer.AddLever(chunkCoord, SectionPicker.LeverPosition(section, atStart), new Vec3f(pitch, yaw, lean));
        }

        // Щокадру, поки котрась стрілка рухається: ставить її важіль і гостряки в нове проміжне положення.
        // Решту чанка не чіпає; хрестовина й контррейки не змінюються і лишаються з готовим мешем
        private void OnSwitchAnimationTick(float dt)
        {
            if (Renderer == null) return;

            foreach (SwitchState state in switchStates.Values)
            {
                if (!state.Moving) continue;
                // Останній раз деталі ставляться вже в кінцевому положенні
                if (capi.ElapsedMilliseconds - state.ChangedAtMs >= SwitchAnimationMs) state.Moving = false;

                if (!clientSections.TryGetValue(state.Chunk, out IDictionary<int, Section> sections)
                    || !sections.TryGetValue(state.Index, out Section trunk) || !trunk.HasSwitch(state.AtStart)) continue;

                // Розрахунки стрілок дійсні лише на один прохід
                switchSides.Clear();
                switchComplete.Clear();
                AddLever(state.Chunk, trunk, state.AtStart);
                foreach (SectionLink link in trunk.GetLinks(state.AtStart))
                {
                    Section branch = FindClientSection(link);
                    if (branch != null) AddBlade(link.Chunk, branch);
                }
            }
        }

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
            switchComplete.Clear();

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

                    TrackSwitch(chunkCoord, section, atStart);
                    AddLever(chunkCoord, section, atStart);
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

        // Хід вістря: на скільки блоків відведений гостряк відходить від рамної рейки
        private const double BladeThrow = 0.12;





        // Розраховані колії стрілок, за першою секцією колії. Живе одну перебудову чанка
        private Dictionary<Section, BranchGeometry> switchSides = new Dictionary<Section, BranchGeometry>();
        // Чи стрілка зібрана повністю, за її ключем. Живе стільки ж, скільки switchSides
        private Dictionary<string, bool> switchComplete = new Dictionary<string, bool>();

        /// <summary>
        /// Чи стрілку, до якої гілка first приєднана кінцем switchAtStart, зібрано повністю: обидві рейки
        /// на всіх секціях обох гілок, поки гілки лежать одна на одній і до кінця хрестовини.
        /// Недобудована стрілка малюється як звичайні колії.
        /// </summary>
        private bool SwitchComplete(Section first, bool switchAtStart)
        {
            List<SectionLink> back = first.GetLinks(switchAtStart);
            if (back.Count != 1) return false;
            Section trunk = FindClientSection(back[0]);
            if (trunk == null) return false;

            string key = SwitchKey(back[0].Chunk, back[0].Index, back[0].AtStart);
            if (switchComplete.TryGetValue(key, out bool known)) return known;
            switchComplete[key] = true;

            bool complete = SwitchZone.IsComplete(trunk, back[0].AtStart, FindClientSection);
            List<SectionLink> links = trunk.GetLinks(back[0].AtStart);
            for (int k = 0; complete && k < links.Count; k++)
            {
                // Хрестовина на крутій стрілці сягає далі, ніж гілки лежать одна на одній
                Section branch = FindClientSection(links[k]);
                Section other = FindClientSection(links[1 - k]);
                if (branch == null || other == null) continue;

                BranchGeometry side = GetBranchGeometry(branch, links[k].AtStart, other);
                if (side.Throat < 0) continue;

                Section current = branch;
                bool entered = links[k].AtStart;
                for (int n = 1; n <= side.HideTo && current != null; n++)
                {
                    if (!current.FirstRailInstalled || !current.SecondRailInstalled) complete = false;
                    List<SectionLink> ahead = current.GetLinks(!entered);
                    if (ahead.Count != 1) break;
                    entered = ahead[0].AtStart;
                    current = FindClientSection(ahead[0]);
                }
            }

            switchComplete[key] = complete;
            return complete;
        }


        /// <summary>
        /// Розраховує внутрішню рейку колії стрілки і місце хрестовини на ній. first це перша секція колії.
        /// </summary>

        private BranchGeometry GetBranchGeometry(Section first, bool switchAtStart, Section partnerFirst)
        {
            if (switchSides.TryGetValue(first, out BranchGeometry known)) return known;

            // Друга гілка має справді виходити з тієї самої стрілки
            bool partnerSwitchAtStart = true;
            bool paired = partnerFirst != null && Sibling(first, switchAtStart, out Section sibling, out partnerSwitchAtStart) && sibling == partnerFirst;

            BranchGeometry result = SwitchGeometry.Branch(first, switchAtStart, partnerFirst, paired ? partnerSwitchAtStart : (bool?)null, FindClientSection);
            switchSides[first] = result;
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
            // Стрілка може бути з будь-якого боку секції, і з обох одразу, якщо стрілки стоять густо:
            // перевіряємо кожен бік окремо
            for (int end = 0; end < 2; end++)
            {
                bool switchAtStart = end == 0;
                // Хрестовина на крутій стрілці сягає далі, ніж спільні шпали, тому пара шукається на будь-якій відстані
                if (!SwitchZone.FindPairToward(section, switchAtStart, FindClientSection, out Section partner, out _, out int number,
                    out _, out double heelAt, true) || partner == null) continue;
                if (left != SwitchGeometry.TowardPartner(section, partner) > 0) continue;

                if (!FindFirst(section, switchAtStart, number, out Section first, out bool firstSwitchAtStart)) continue;
                // Недобудована стрілка: звичайні рейки
                if (!SwitchComplete(first, firstSwitchAtStart)) continue;
                if (!Sibling(first, firstSwitchAtStart, out Section partnerFirst, out _)) continue;

                BranchGeometry own = GetBranchGeometry(first, firstSwitchAtStart, partnerFirst);
                if (number <= SwitchGeometry.BladeSections(heelAt, own)) return new RailShape { Hidden = true };
                if (own.Throat >= 0 && number >= own.HideFrom && number <= own.HideTo) return new RailShape { Hidden = true };
            }
            return null;
        }

        // Друга гілка тієї самої стрілки: first це перша секція гілки, приєднана до стрілки кінцем switchAtStart.
        // siblingAtStart каже, котрим кінцем друга гілка приєднана до стрілки
        private bool Sibling(Section first, bool switchAtStart, out Section sibling, out bool siblingAtStart)
        {
            sibling = null;
            siblingAtStart = true;

            List<SectionLink> back = first.GetLinks(switchAtStart);
            if (back.Count != 1) return false;
            Section trunk = FindClientSection(back[0]);
            if (trunk == null) return false;

            foreach (SectionLink link in trunk.GetLinks(back[0].AtStart))
            {
                if (link.PointsTo(first)) continue;
                sibling = FindClientSection(link);
                siblingAtStart = link.AtStart;
                return sibling != null;
            }
            return false;
        }

        /// <summary>
        /// Ставить деталі стрілки, якщо секція це перша секція однієї з двох її колій: гостряк, вусовик і осердя.
        /// Усі три йдуть по лінії внутрішньої рейки колії, тому збираються з її перерізів.
        /// </summary>
        private void AddBlade(Vec3i chunkCoord, Section section)
        {
            // Секція має бути першою на гілці стрілки; з котрого боку від неї стрілка, перевіряємо окремо
            Section partner = null;
            bool primary = false, active = false, switchAtStart = true, found = false;
            double heelAt = 0;
            for (int end = 0; end < 2 && !found; end++)
            {
                switchAtStart = end == 0;
                found = SwitchZone.FindPairToward(section, switchAtStart, FindClientSection, out partner, out primary, out int number,
                    out active, out heelAt, false) && number == 1 && partner != null;
            }
            if (!found) return;
            // Недобудована стрілка малюється звичайними рейками, без деталей стрілки
            if (!SwitchComplete(section, switchAtStart)) return;

            bool left = SwitchGeometry.TowardPartner(section, partner) > 0;
            SectionPart part = left ? SectionPart.FirstRail : SectionPart.SecondRail;
            if (!section.IsInstalled(part)) return;

            string material = section.GetMaterial(part) ?? "iron";
            BranchGeometry own = GetBranchGeometry(section, switchAtStart, partner);

            // Наскільки гостряк відведений від рамної рейки: 0 притиснутий, 1 відведений на повний хід.
            // Під час переведення стрілки він між цими положеннями
            double open = active ? 0 : 1;
            List<SectionLink> back = section.GetLinks(switchAtStart);
            if (back.Count == 1)
            {
                double moved = SwitchProgress(SwitchKey(back[0].Chunk, back[0].Index, back[0].AtStart));
                open = active ? 1 - moved : moved;
            }

            // ── Гостряк ──
            int bladePoints = Math.Min(SwitchGeometry.BladeSections(heelAt, own) * 2 + 1, own.Frames.Count);
            if (bladePoints >= 2)
            {
                // Де в кожному перерізі рамна рейка: зовнішня рейка сусідньої колії. Біля стрілки вона лежить
                // на лінії гостряка, далі відходить убік. Якщо сусідньої колії не знайдено, вважаємо,
                // що вона відходить рівномірно і біля кореня гостряк уже повний
                double[] stock = new double[bladePoints];
                for (int i = 0; i < bladePoints; i++)
                {
                    RailFrame frame = own.Frames[i];
                    stock[i] = (double)i / (bladePoints - 1) * BladeMesh.HeadHalfWidth * 4;
                    if (own.Other != null && i < own.Other.Count)
                    {
                        RailFrame other = own.Other[i];
                        double reach = section.TrackWidth;
                        stock[i] = (other.Center.X - other.Side.X * reach - frame.Center.X) * frame.Side.X
                            + (other.Center.Y - other.Side.Y * reach - frame.Center.Y) * frame.Side.Y
                            + (other.Center.Z - other.Side.Z * reach - frame.Center.Z) * frame.Side.Z;
                    }
                    stock[i] = Math.Max(stock[i], 0);
                }

                var blade = new LoftPart();
                LoftPoint[][] ends = null;
                // Ставить переріз гостряка за position перерізів колії від стрілки; position може бути дробом
                void AddStation(double position, bool withHead)
                {
                    int before = Math.Min((int)Math.Floor(position), bladePoints - 2);
                    double fraction = position - before;
                    RailFrame frame = fraction < 1e-6 ? own.Frames[before]
                        : fraction > 1 - 1e-6 ? own.Frames[before + 1] : Between(own.Frames[before], own.Frames[before + 1], fraction);
                    double stockAt = stock[before] + (stock[before + 1] - stock[before]) * fraction;

                    // Відведений гостряк відходить на повний хід біля вістря і повертається на свою лінію
                    // біля кореня, який не рухається
                    double t = position / (bladePoints - 1);
                    double shift = open * BladeThrow * (1 - t * t * (3 - 2 * t));
                    blade.Stations.Add(BladeProfile(frame, stockAt, shift, withHead, out ends));
                    if (blade.Stations.Count == 1) blade.StartCaps.AddRange(ends);
                }

                // Де рамна рейка відійшла на stockNeeded блоків, у перерізах колії від стрілки, дробом
                double Reaches(double stockNeeded, out int index)
                {
                    index = 0;
                    while (index < bladePoints && stock[index] < stockNeeded) index++;
                    if (index == 0 || index >= bladePoints) return index;
                    return index - 1 + (stockNeeded - stock[index - 1]) / (stock[index] - stock[index - 1]);
                }

                // Тіло гостряка, підошва з шийкою, починається на секцію ближче до стрілки, ніж місце, де
                // головка набирає ширину BladeMinHead. Далі за сам стик стрілки воно не йде: там рейка
                // ще одна на обидві колії
                double begin = Math.Max(0, Reaches(BladeMinHead, out int firstFull) - BladeLead);
                // І не пізніше, ніж за пів секції до тяги: тяга тримається за тіло обох гостряків
                begin = Math.Min(begin, TieBarAt - 1);
                // Головка починається пізніше, сходинкою, там, де від неї лишається ширина BladeHeadFrom:
                // вужча була б лезом. До цього місця гостряк це лише тіло, врівень з низом головки
                double headFrom = Math.Max(begin, Reaches(BladeHeadFrom, out _));

                if (headFrom < bladePoints - 1)
                {
                    if (headFrom - begin > 0.05)
                    {
                        AddStation(begin, false);
                        for (int i = (int)Math.Floor(begin) + 1; i < headFrom - 0.05; i++)
                        {
                            if (i - begin > 0.05) AddStation(i, false);
                        }
                        // Два перерізи в одному місці, без головки і з нею: між ними торець головки, сходинка
                        AddStation(headFrom, false);
                    }
                    AddStation(headFrom, true);
                    for (int i = (int)Math.Floor(headFrom) + 1; i < bladePoints; i++)
                    {
                        if (i - headFrom > 0.05) AddStation(i, true);
                    }
                }

                if (blade.Stations.Count >= 2)
                {
                    blade.EndCaps.AddRange(ends);
                    var parts = new List<LoftPart> { blade };

                    // Тяга між двома гостряками: планка, що тримає їх на сталій відстані, тому вони й рухаються
                    // разом. Одна на стрілку, її ставить перша гілка. Лежить між шпалами, біля вістря
                    if (primary && own.Other != null)
                    {
                        for (int bar = 0; bar < TieBars; bar++)
                        {
                            LoftPart tie = TieBar(section, own, bladePoints, stock, firstFull, open, bar);
                            if (tie != null) parts.Add(tie);
                        }
                    }

                    Renderer.SetLoft(chunkCoord, section.IndexInChunk, left, "", material, parts);
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
        private void AddCheckRail(Vec3i chunkCoord, Section section, bool inner, BranchGeometry own)
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
            double gap = half * 2 + SwitchGeometry.FrogFlangeway;
            double running = -section.TrackWidth;
            double offset = running + gap;

            // Вісь контррейки в кожному перерізі
            var axis = new List<Vec3d>();
            for (int q = from; q <= to; q++)
            {
                RailFrame frame = own.Frames[q];
                double x = offset + (q == from || q == to ? CheckRailFlare : 0);
                axis.Add(new Vec3d(frame.Center.X + frame.Side.X * x, frame.Center.Y + frame.Side.Y * x, frame.Center.Z + frame.Side.Z * x));
            }

            var rail = new LoftPart();
            for (int i = 0; i < axis.Count; i++)
            {
                RailFrame frame = own.Frames[from + i];
                // Через відгин лінія контррейки не йде точно вздовж колії: напрямок веде вона сама
                Vec3d before = axis[Math.Max(i - 1, 0)], after = axis[Math.Min(i + 1, axis.Count - 1)];
                Vec3d along = new Vec3d(after.X - before.X, after.Y - before.Y, after.Z - before.Z);
                along = along.Length() < 1e-9 ? frame.Along : along.Normalize();

                RailFrame at = new RailFrame { Center = axis[i], Side = frame.Side, Up = frame.Up };
                rail.Stations.Add(RailContour(at, 0, along));

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
                RailFrame frame = own.Frames[q];
                var spacer = new LoftPart();
                for (int end = -1; end <= 1; end += 2)
                {
                    double shift = end * SpacerLength / 2;
                    RailFrame at = new RailFrame
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

        // Тяга між гостряками: ширина вздовж колії, низ і верх над підошвою рейки, блоків
        private const int TieBars = 1;
        // На якому перерізі колії від стрілки стоїть тяга: 2 це кінець першої секції
        private const int TieBarAt = 2;
        private const double TieBarWidth = 0.09;
        private const double TieBarBottom = 0.5 / 16;
        private const double TieBarTop = 1.0 / 16;

        /// <summary>
        /// Тяга між гостряками: планка впоперек колії від шийки одного гостряка до шийки другого.
        /// Стоїть на стику першої і другої секцій стрілки, тобто між шпалами, навпроти важеля; number це
        /// котра вона за рахунком, наступні стоять через секцію. Кінці її
        /// рахуються з того, де кожен гостряк стоїть саме зараз, тож під час переведення вона їде з ними.
        /// stock і firstFull це вже пораховане для свого гостряка: де рамна рейка і з якого перерізу він є.
        /// </summary>
        private LoftPart TieBar(Section section, BranchGeometry own, int bladePoints, double[] stock, int firstFull, double open, int number)
        {
            int count = Math.Min(bladePoints, own.Other.Count);
            double width = section.TrackWidth;

            // Де рамна рейка для сусіднього гостряка: зовнішня рейка своєї колії
            double OtherStock(int i)
            {
                RailFrame mine = own.Frames[i], theirs = own.Other[i];
                return Math.Max(0, (mine.Center.X - mine.Side.X * width - theirs.Center.X) * theirs.Side.X
                    + (mine.Center.Y - mine.Side.Y * width - theirs.Center.Y) * theirs.Side.Y
                    + (mine.Center.Z - mine.Side.Z * width - theirs.Center.Z) * theirs.Side.Z);
            }

            // Перша тяга стоїть рівно на стику першої і другої секцій стрілки, навпроти важеля;
            // наступні через секцію одна від одної
            int at = TieBarAt + number * 2;
            if (at > count - 1) return null;

            // Де стоїть стінка шийки гостряка з боку колії: зміщення біля вістря плюс відведення
            double t = (double)at / (bladePoints - 1);
            double throwAt = BladeThrow * (1 - t * t * (3 - 2 * t));
            Vec3d WebFace(RailFrame frame, double stockAt, double opened)
            {
                double move = Math.Max(0, RailWebHalf - (stockAt - RailFootHalf)) + opened * throwAt;
                // Трохи всередину шийки, щоб між тягою і гостряком не було щілини
                double x = -(RailWebHalf * 0.5 + move);
                return new Vec3d(frame.Center.X + frame.Side.X * x, frame.Center.Y + frame.Side.Y * x, frame.Center.Z + frame.Side.Z * x);
            }

            RailFrame near = own.Frames[at];
            Vec3d from = WebFace(near, stock[at], open);
            Vec3d to = WebFace(own.Other[at], OtherStock(at), 1 - open);
            Vec3d along = to.SubCopy(from);
            if (along.Length() < 0.05) return null;
            along.Normalize();

            var bar = new LoftPart();
            foreach (Vec3d end in new[] { from, to })
            {
                // Переріз планки: упоперек неї це напрямок колії
                RailFrame frame = new RailFrame { Center = end, Side = near.Along, Up = near.Up };
                LoftPoint[] box = Box(frame, -TieBarWidth / 2, TieBarWidth / 2, TieBarBottom, TieBarTop);
                bar.Stations.Add(new LoftStation { Along = along, Points = new[] { box[0], box[3], box[2], box[1] } });
                (bar.Stations.Count == 1 ? bar.StartCaps : bar.EndCaps).Add(box);
            }
            return bar;
        }

        // Ширина головки гостряка, блоків, від якої рахується його початок, і на скільки перерізів колії
        // ближче до стрілки він починається насправді. Два перерізи це одна секція
        private const double BladeMinHead = 0.01;
        private const double BladeLead = 2;
        // Найменша ширина головки гостряка, блоків: з такої вона починається, сходинкою
        private const double BladeHeadFrom = 0.005;

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
        /// withHead: чи є в цьому перерізі головка. Біля самого вістря її немає, є лише тіло.
        /// ends це торець у цьому перерізі: підошва, шийка й головка.
        /// </summary>
        private static LoftStation BladeProfile(RailFrame frame, double stock, double thrown, bool withHead, out LoftPoint[][] ends)
        {
            double half = BladeMesh.HeadHalfWidth;
            const double floor = FrogBodyTop;
            // Без головки верх гостряка на рівні її низу
            double top = withHead ? FrogRailTop : floor;

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
        private static RailFrame Between(RailFrame a, RailFrame b, double part)
        {
            Vec3d Mixed(Vec3d p, Vec3d q) => new Vec3d(p.X + (q.X - p.X) * part, p.Y + (q.Y - p.Y) * part, p.Z + (q.Z - p.Z) * part);
            return new RailFrame { Center = Mixed(a.Center, b.Center), Side = Mixed(a.Side, b.Side).Normalize(), Up = Mixed(a.Up, b.Up).Normalize(), Along = Mixed(a.Along, b.Along).Normalize() };
        }

        // Прямокутник у перерізі: кути по колу, починаючи з лівого нижнього, далі правий нижній
        private static LoftPoint[] Box(RailFrame at, double left, double right, double bottom, double top)
        {
            return new[] { Point(at, left, bottom), Point(at, right, bottom), Point(at, right, top), Point(at, left, top) };
        }

        // Хрестовина лита, один суцільний блок. Висоти його частин над підошвою рейки, блоків: тіло доходить
        // до низу головки рейки, поверхні кочення до її верху. Числа з моделі рейки (shapes/item/rail.json)
        private const double FrogBodyTop = RailProfile.HeadBottom;
        private const double FrogRailTop = RailProfile.Top;

        // Профіль звичайної рейки, блоків (shapes/item/rail.json): пів ширини підошви, її висота, пів товщини шийки
        private const double RailFootHalf = RailProfile.FootHalfWidth;
        private const double RailFootTop = RailProfile.FootTop;
        private const double RailWebHalf = RailProfile.WebHalfWidth;

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
        private void AddFrog(Vec3i chunkCoord, Section section, bool left, string material, BranchGeometry own)
        {
            double half = BladeMesh.HeadHalfWidth;
            // Відстань між серединами рейок, за якої між їхніми головками лишається жолоб
            double clearance = half * 2 + SwitchGeometry.FrogFlangeway;
            const double floor = FrogBodyTop, top = FrogRailTop;

            int count = own.Across.Length;
            int from = (own.HideFrom - 1) * 2;
            int to = Math.Min(count - 1, own.HideTo * 2);
            if (to - from < 1) return;

            // Тіло вузла
            // З якого перерізу осердя розходиться на два промені
            int splitFrom = to;
            for (int q = from; q <= to; q++)
            {
                if (own.Across[q] <= half * 2) continue;
                splitFrom = q;
                break;
            }

            var body = new LoftPart();
            List<LoftStation> stations = body.Stations;
            List<LoftPoint[]> startCaps = body.StartCaps, endCaps = body.EndCaps;
            // Хвостики: два на початку вузла, від рейок колій до вусовиків, і два в кінці, від променів осердя
            var parts = new List<LoftPart> { body };

            for (int q = from; q <= to; q++)
            {
                // Перерізи внутрішніх рейок двох колій у цьому місці: своєї і сусідньої. Сусідня повернута боком
                // у той самий бік, що й своя
                RailFrame mine = own.Frames[q], theirs = Mirrored(own.Other[q]);
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
                    foreach (RailFrame rail in new[] { mine, theirs })
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
                RailFrame station = new RailFrame { Center = center, Side = side, Up = up, Along = along };

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
                // Осердя ділиться на два промені рівно посередині між двома рейками. Це не завжди середина
                // перерізу: точки двох колій з однаковим номером не стоять одна навпроти одної, і в перерізі
                // рейки лежать не дзеркально. Якби ділити по середині перерізу, один промінь відокремився б
                // раніше за другий, і ямка між ними вийшла б кривобокою
                double middle = (mineAt + theirsAt) / 2;
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
                    noseB = Math.Min(mineAt + half, middle);
                    noseC = Math.Max(theirsAt - half, middle);
                    noseD = theirsAt + half;
                    noseA = Math.Min(Math.Max(noseA, wingLeft + half), noseB);
                    noseD = Math.Max(Math.Min(noseD, wingRight - half), noseC);
                }
                else
                {
                    noseA = noseB = noseC = noseD = GameMath.Clamp(middle, wingLeft + half, wingRight - half);
                    // Вістря осердя на повну висоту головки від самого початку: у перерізі перед першим
                    // широким воно вже стоїть, нульової ширини. Тож осердя це гострий клин, а не схил від дна
                    if (q >= own.Tip - 1) noseTop = top;
                }
                // Між променями дно, лише коли вони справді розійшлися
                // Дно теж одразу на своєму рівні, від перерізу перед розходженням: стінки ямки між
                // променями прямовисні, а не схил від верху головки
                double between = q >= splitFrom - 1 || noseC - noseB > 1e-6 ? floor : noseTop;

                LoftPoint P(double x, double y) => Point(station, x, y);

                // Контур перерізу з осердям висотою noseHeight і дном між променями на висоті gapHeight
                LoftStation Contour(double noseHeight, double gapHeight)
                {
                    return new LoftStation
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
                            P(noseA, floor), P(noseA, noseHeight), P(noseB, noseHeight), P(noseB, gapHeight),
                            P(noseC, gapHeight), P(noseC, noseHeight), P(noseD, noseHeight), P(noseD, floor),
                            // Правий вусовик
                            P(wingRight - half, floor), P(wingRight - half, top),
                            P(wingRight + half, top), P(wingRight + half, floor), P(wingRight + RailWebHalf, floor),
                            P(wingRight + RailWebHalf, RailFootTop), P(wingRight + RailFootHalf, RailFootTop), P(wingRight + RailFootHalf, 0)
                        }
                    };
                }

                // Осердя і ямка між його променями починаються прямовисно. Щоб так вийшло, у місці, де
                // вони починаються, стоять два перерізи поспіль: один ще без них, другий уже з ними, нульової
                // ширини. Між такими перерізами поверхні немає, тож не виходить і грані нульової товщини,
                // що тяглася б від вістря назад до дна жолоба
                if (!last && q == own.Tip - 1) stations.Add(Contour(floor, floor));
                if (!last && q == splitFrom - 1 && noseTop > floor && between < noseTop) stations.Add(Contour(noseTop, noseTop));
                stations.Add(Contour(noseTop, between));

                if (first)
                {
                    // Торець: стінка між шийками двох вусовиків. Самі вусовики продовжуються хвостиками до рейок
                    startCaps.Add(new[] { P(wingLeft + RailWebHalf, 0), P(wingRight - RailWebHalf, 0), P(wingRight - RailWebHalf, floor), P(wingLeft + RailWebHalf, floor) });
                    parts.Add(Tail( RailContour(theirs, 0, theirs.Along), RailContour(station, wingLeft, theirs.Along)));
                    parts.Add(Tail( RailContour(mine, 0, mine.Along), RailContour(station, wingRight, mine.Along)));
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
                    parts.Add(Tail( RailContour(station, mineAt, mine.Along), RailContour(mine, 0, mine.Along)));
                    parts.Add(Tail( RailContour(station, theirsAt, theirs.Along), RailContour(theirs, 0, theirs.Along)));
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

        private static LoftPoint Point(RailFrame at, double x, double y)
        {
            return new LoftPoint { Center = at.Center, Side = at.Side, Up = at.Up, X = x, Y = y };
        }

        // Де лінія рейки перетинає площину перерізу вузла: відстань від його середини вздовж його боку.
        // Рейка біля перерізу вважається прямою
        private static double Across(RailFrame rail, RailFrame station)
        {
            double dx = station.Center.X - rail.Center.X, dy = station.Center.Y - rail.Center.Y, dz = station.Center.Z - rail.Center.Z;
            double facing = rail.Along.Dot(station.Along);
            double t = Math.Abs(facing) > 1e-6 ? (dx * station.Along.X + dy * station.Along.Y + dz * station.Along.Z) / facing : 0;
            return (rail.Along.X * t - dx) * station.Side.X + (rail.Along.Y * t - dy) * station.Side.Y + (rail.Along.Z * t - dz) * station.Side.Z;
        }

        // Повний профіль звичайної рейки в перерізі at, з віссю за x блоків від його середини: усі дванадцять точок
        private static LoftStation RailContour(RailFrame at, double x, Vec3d along)
        {
            double half = BladeMesh.HeadHalfWidth;
            // Початок відліку ставимо на вісь рейки: лінію хвостика між двома перерізами веде крива
            // від початку відліку одного до початку відліку іншого, і вона має йти вздовж рейки
            RailFrame axis = new RailFrame
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
        private static RailFrame Mirrored(RailFrame frame)
        {
            return new RailFrame { Center = frame.Center, Side = frame.Side.Clone().Mul(-1), Up = frame.Up, Along = frame.Along };
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
            foreach (string key in switchStates.Where(pair => pair.Value.Chunk.Equals(chunkCoord)).Select(pair => pair.Key).ToList()) switchStates.Remove(key);
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
            if (action == EnumSectionAction.Info)
            {
                // Лише читання, тому дотягуватися до секції не обов'язково
                fromPlayer.SendMessage(Vintagestory.API.Config.GlobalConstants.CurrentChatGroup, DescribeSection(new Vec3i(packet.ChunkX, packet.ChunkY, packet.ChunkZ), packet.Index), EnumChatType.CommandSuccess);
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

        // Щойно завантажена колонка чанків: дописує її стрілки, яких ще немає в записах, і дораховує стрілки
        // сусідніх чанків, чиї гілки сюди заходять
        private void OnChunkColumnLoaded(Vec2i column, IWorldChunk[] chunks)
        {
            RailDataSession session = null;
            for (int y = 0; y < chunks.Length; y++)
            {
                Vec3i chunk = new Vec3i(column.X, y, column.Y);
                DataInChunk data = DataInChunk.Get(sapi.World, chunk);
                if (data == null || data.RailWaySections.Count == 0) continue;

                if (session == null) session = new RailDataSession(sapi.World);
                SwitchRegistry.Complete(session, chunk);
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (dx != 0 || dy != 0 || dz != 0) SwitchRegistry.Complete(session, new Vec3i(chunk.X + dx, chunk.Y + dy, chunk.Z + dz));
                        }
                    }
                }
            }
            if (session != null && session.HasChanges) session.SaveAll();
        }

        // Що сервер знає про секцію: її деталі, сусідів і стрілки
        private string DescribeSection(Vec3i chunk, int index)
        {
            RailDataSession session = new RailDataSession(sapi.World);
            DataInChunk data = session.Get(chunk);
            if (data == null || !data.RailWaySections.TryGetValue(index, out Section section))
                return "Section " + index + " not found in chunk " + chunk;

            // Заразом доз'єднує колію, прокладену до появи зв'язків
            string start = DescribeEnd(session, section, true);
            string end = DescribeEnd(session, section, false);
            string switches = DescribeSwitches(session, data, section);
            if (session.HasChanges) session.SaveAll();

            return string.Format(
                // Чат гри розбирає текст як розмітку: кутових дужок у ньому бути не може
                "Section {0} in chunk {1}. Sleeper: {2}, rails: {3}/{4}. Start: {5}. End: {6}.{7}",
                section.IndexInChunk, section.ChunkAddres,
                section.SleeperInstalled, section.FirstRailInstalled, section.SecondRailInstalled, start, end, switches);
        }

        // Що сервер записав про стрілки, до яких секція має стосунок
        private static string DescribeSwitches(RailDataSession session, DataInChunk data, Section section)
        {
            string text = "";
            for (int end = 0; end < 2; end++)
            {
                bool atStart = end == 0;
                string name = atStart ? "start" : "end";

                RailSwitch own = section.HasSwitch(atStart) ? SwitchRegistry.Get(session, section.ChunkAddres, section.IndexInChunk, atStart) : null;
                if (own != null && own.Branches.Count == 2)
                {
                    SwitchBranch a = own.Branches[0], b = own.Branches[1];
                    text += string.Format(" Switch at {0}: complete {1}, resolved {2}, position {3}; blade {4}, frog {5}-{6}, shared sleepers {7}, zone {8}, members {9}/{10}.",
                        name, own.Complete, own.Resolved, section.GetSwitchPosition(atStart), a.BladeSections, a.FrogFrom, a.FrogTo,
                        a.PairSections, a.ZoneSections, a.Members.Count, b.Members.Count);
                }

                SwitchRef member = section.GetSwitchRef(atStart);
                if (member != null)
                {
                    text += string.Format(" From its {0} it is section {1} of branch {2} of switch {3} in chunk {4}/{5}/{6}.",
                        name, member.Number, member.Branch, member.Key, member.ChunkX, member.ChunkY, member.ChunkZ);
                }
            }
            return text;
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
