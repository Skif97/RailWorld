using HarmonyLib;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
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

    /// <summary>
    /// Сервер повідомляє клієнтам, що стрілку переведено. Revision це номер зміни даних чанка після цього.
    /// </summary>
    [ProtoContract]
    public class SwitchTogglePacket
    {
        [ProtoMember(1)] public int ChunkX;
        [ProtoMember(2)] public int ChunkY;
        [ProtoMember(3)] public int ChunkZ;
        [ProtoMember(4)] public int Index;
        [ProtoMember(5)] public bool AtStart;
        [ProtoMember(6)] public int Position;
        [ProtoMember(7)] public int Revision;
    }

    public class RailWaySystem : ModSystem
    {
        private const string ChannelName = "railworld-section";

        // На скільки блоків далі за дальність взаємодії сервер ще приймає дії клієнта
        private const double ReachTolerance = 1.5;
        // На скільки блоків точка влучання може лежати за боксом деталі: на похибку чисел
        private const double HitTolerance = 0.1;

        private ICoreAPI api;
        private ICoreClientAPI capi;
        private ICoreServerAPI sapi;

        private IClientNetworkChannel clientChannel;
        private IServerNetworkChannel serverChannel;

        public RailWayRendererSystem Renderer { get; private set; }

        private RailSelectionRenderer selectionRenderer;
        private RoutePlanner routePlanner;
        private SectionInteractionClient interaction;

        /// <summary>
        /// Поведінка секцій при взаємодії. Щоб змінити її, підставте сюди свій нащадок SectionHandler.
        /// </summary>
        public SectionHandler Handler { get; set; } = new SectionHandler();

        // Тільки на клієнті: що він знає про колію, як її малює і які стрілки зараз переводяться
        private ClientTrack clientTrack;
        private SwitchAnimator switchAnimator;
        private TrackChunkRenderer chunkRenderer;

        /// <summary>Журнал побудов колії, тільки на сервері: з нього команда /railundo відкочує останні побудови.</summary>
        public BuildJournal Journal { get; private set; }

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
            // Правила прокладання світу: клієнт отримав їх від сервера разом із налаштуваннями світу
            TrackRules.Load(api.World.Config, false);

            // Дані колії лежать у статичному сховищі й могли лишитися від попереднього світу
            DataInChunk.ClearClientCache();

            clientChannel = api.Network
                .RegisterChannel(ChannelName)
                .RegisterMessageType<SectionActionPacket>()
                .RegisterMessageType<SwitchTogglePacket>()
                .SetMessageHandler<SwitchTogglePacket>(OnSwitchToggled);

            api.Event.BlockTexturesLoaded += OnLoaded;

            api.Event.LeaveWorld += DisposeRenderers;

            // Оновлюємо рендер коли чанк змінився
            api.Event.ChunkDirty += OnChunkDirty;

            clientTrack = new ClientTrack();
            switchAnimator = new SwitchAnimator(api);

            // Плавне переведення стрілок: кожні 15 мс гри, поки котрась рухається. На паузі такти не йдуть
            api.Event.RegisterGameTickListener(dt => chunkRenderer?.AnimateSwitches(), 15);

            // Латки гри живуть, поки гравець у світі: при виході їх знімає DisposeRenderers
            harmony = new Harmony(HarmonyId);
            MyChunkEvents.Init(harmony, api.Logger);
            MyChunkEvents.ChunkUnloaded += OnChunkUnloaded;

            interaction = new SectionInteractionClient(api, this, harmony);

            // Команда клієнтська: лише клієнт знає, на що гравець дивився. Поки відкритий чат, під прицілом
            // нічого немає, тому беремо секцію, на яку він дивився останньою. Відповідає сервер: дані в нього
            api.ChatCommands.Create("railinfo")
                .WithDescription("Shows the rail section you looked at last and what the server knows about it")
                .HandleWith(OnRailInfoCommand);

            // Налагодження: де межі чанка гравця і які секції записані в його даних
            api.ChatCommands.Create("railchunk")
                .WithDescription("Shows the chunk you stand in as a translucent box and the rail sections stored in it in purple")
                .HandleWith(args => TextCommandResult.Success(selectionRenderer?.ToggleChunkView() == true
                    ? "Чанк і його секції показано" : "Показ чанка вимкнено"));

            // Камера пасажира нахиляється разом із вагонеткою
            TrolleyCameraPatch.Init(harmony, api);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            this.sapi = api;
            TrackRules.Load(api.World.Config, true);

            // Кеш статичний і міг лишитися від попереднього світу
            DataInChunk.ClearServerCache();
            SwitchRegistry.Reset();
            CrossingRegistry.Reset();
            Journal = new BuildJournal();

            api.ChatCommands.Create("railundo")
                .WithDescription("Undoes your last track builds, newest first. Creative mode only")
                .RequiresPrivilege(Privilege.chat)
                .RequiresPlayer()
                .WithArgs(api.ChatCommands.Parsers.OptionalInt("count", 1))
                .HandleWith(OnRailUndoCommand);

            // Записи про стрілки й перетини рахуються, лише коли щось змінюється в колії. Коли змінюються самі
            // правила, наявні записи лишаються старими, доки їх не перерахувати
            api.ChatCommands.Create("railrecheck")
                .WithDescription("Recalculates switch and crossing records of the track around you, after the rules have changed")
                .RequiresPrivilege(Privilege.chat)
                .RequiresPlayer()
                .WithArgs(api.ChatCommands.Parsers.OptionalInt("radius", 2))
                .HandleWith(OnRailRecheckCommand);

            // Записи стрілок і перетинів, пораховані неповністю, іншою версією розрахунку або з посиланням на
            // секцію, якої вже немає
            api.Event.ChunkColumnLoaded += OnChunkColumnLoaded;

            serverChannel = api.Network
                .RegisterChannel(ChannelName)
                .RegisterMessageType<SectionActionPacket>()
                .RegisterMessageType<SwitchTogglePacket>()
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
            chunkRenderer = new TrackChunkRenderer(capi, Renderer, clientTrack, switchAnimator);
            selectionRenderer = new RailSelectionRenderer(capi, this);
            routePlanner = new RoutePlanner(capi, this);
        }

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
        public IEnumerable<Vec3i> ClientChunkCoords => clientTrack.ChunkCoords;

        /// <summary>
        /// Секція, на яку вказує посилання, з того, що клієнт зараз знає про колію. null, якщо її чанку немає.
        /// </summary>
        public Section FindClientSection(SectionLink link)
        {
            return clientTrack.FindSection(link);
        }

        /// <summary>
        /// Міняється щоразу, коли клієнт дізнається про зміну колії в якомусь чанку, і коли переводять стрілку.
        /// Хто запам'ятав щось, пораховане з колії, за цим числом бачить, що запам'ятоване застаріло.
        /// </summary>
        public int ClientRevision { get; private set; }

        public List<SectionBox> GetClientBoxes(Vec3i chunkCoord)
        {
            return clientTrack.GetBoxes(chunkCoord);
        }

        /// <summary>Деталь секції під прицілом з того, що знає клієнт. dir має бути нормалізований.</summary>
        public SectionSelection PickClient(Vec3d origin, Vec3d dir, double range, System.Func<SectionBox, bool> filter)
        {
            return clientTrack.Pick(origin, dir, range, filter);
        }

        /// <summary>
        /// Замінює все, що клієнт знає про колію в чанку: бокси виділення, шпали і рейки.
        /// Повертає false, якщо дані відкинуто, бо клієнт уже має новіші.
        /// </summary>
        public bool SetChunkData(Vec3i chunkCoord, DataInChunk data)
        {
            // Колії в чанку більше немає: прибираємо все. Якщо ж дані просто замінюються новими, нічого наперед
            // не викидаємо: перебудова сама прибере зайве, а незмінні деталі стрілок лишить як є
            if (data == null)
            {
                RemoveChunk(chunkCoord);
                return true;
            }

            // Чанк гра розпаковує у фоні, а пакет зі зміною приходить одразу: дані чанка, відправленого
            // до зміни, можуть дійти після неї. Той самий номер теж не приймаємо: сервер збільшує його з кожним
            // збереженням (DataInChunk.Save), тож це ті самі дані, що вже є, лише вдруге (нашим пакетом і разом із
            // чанком, який гра пересилає після MarkModified). Так само OnSwitchToggled відкидає пакет із номером, який уже є
            DataInChunk known = DataInChunk.GetClient(chunkCoord);
            if (known != null && data.Revision <= known.Revision) return false;

            Vec3i coord = chunkCoord.Clone();
            clientTrack.Set(coord, data, Handler);
            ClientRevision++;
            selectionRenderer?.MarkChunkDirty(coord);

            chunkRenderer?.Rebuild(coord, data.RailWaySections);
            chunkRenderer?.QueueNeighbours(coord);
            return true;
        }

        /// <summary>Сервер: повідомляє клієнтам, у яких є чанк стрілки, що її переведено.</summary>
        public void SendSwitchToggled(Section trunk, bool atStart, int revision)
        {
            Vec3i chunk = trunk.ChunkAddres;
            IServerPlayer[] players = RailWorld.PlayersWithChunk(chunk);
            if (serverChannel == null || players.Length == 0) return;
            serverChannel.SendPacket(new SwitchTogglePacket
            {
                ChunkX = chunk.X,
                ChunkY = chunk.Y,
                ChunkZ = chunk.Z,
                Index = trunk.IndexInChunk,
                AtStart = atStart,
                Position = trunk.GetSwitchPosition(atStart),
                Revision = revision
            }, players);
        }

        // Клієнт: стрілку переведено. Решта чанка не змінилася, тому нічого не перебудовується: важелю кажуть
        // нове положення, і далі його веде гра, а гостряки веде TrackChunkRenderer.AnimateSwitches
        private void OnSwitchToggled(SwitchTogglePacket packet)
        {
            Vec3i chunk = new Vec3i(packet.ChunkX, packet.ChunkY, packet.ChunkZ);
            DataInChunk data = DataInChunk.GetClient(chunk);
            // Чанка в клієнта немає: положення стрілки приїде разом із ним. Або клієнт уже має новіші дані
            if (data == null || packet.Revision <= data.Revision) return;
            if (!data.RailWaySections.TryGetValue(packet.Index, out Section trunk)) return;

            trunk.SetSwitchPosition(packet.AtStart, packet.Position);
            data.Revision = packet.Revision;
            ClientRevision++;
            if (!trunk.HasSwitch(packet.AtStart)) return;
            switchAnimator.Track(chunk, trunk, packet.AtStart);
            chunkRenderer?.SwitchToggled(chunk, trunk, packet.AtStart);
        }

        public void RemoveChunk(Vec3i chunkCoord)
        {
            bool known = clientTrack.Remove(chunkCoord);
            ClientRevision++;
            switchAnimator.Forget(chunkCoord);
            chunkRenderer?.Forget(chunkCoord);
            selectionRenderer?.MarkChunkDirty(chunkCoord);
            // Колія в чанку зникла: сусіди могли малювати з нею спільні шпали
            if (known) chunkRenderer?.QueueNeighbours(chunkCoord);
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

            // Чанк привіз старіші дані, ніж уже прийшли пакетом: у чанку мають лежати ті самі, що в клієнта
            if (!SetChunkData(chunkCoord, railData)) chunk.SetModdata(DataInChunk.ModdataKey, DataInChunk.GetClient(chunkCoord));
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

        private const string HarmonyId = "com.railworld.myMod";
        private Harmony harmony;

        private void DisposeRenderers()
        {
            // Викликається двічі, при виході зі світу і при вивантаженні мода: друге нічого вже не знаходить
            MyChunkEvents.ChunkUnloaded -= OnChunkUnloaded;
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            TrolleyCameraPatch.Release();

            chunkRenderer?.Dispose();
            chunkRenderer = null;
            Renderer?.Dispose();
            Renderer = null;
            selectionRenderer?.Dispose();
            selectionRenderer = null;
            routePlanner?.Dispose();
            routePlanner = null;
            interaction?.Dispose();
            clientTrack?.Clear();
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

        // Відкочує останні побудови гравця. Повернуті блоки нічого не коштують, тому лише в креативі:
        // у виживанні так можна було б розмножувати блоки, зламані при побудові
        private TextCommandResult OnRailUndoCommand(TextCommandCallingArgs args)
        {
            IPlayer player = args.Caller.Player;
            if (player.WorldData.CurrentGameMode != EnumGameMode.Creative) return TextCommandResult.Error("Відкат побудови працює лише в креативі");

            int wanted = GameMath.Clamp((int)args[0], 1, BuildJournal.MaxRecords);
            int undone = 0, sections = 0, blocks = 0;
            string refusal = null;
            while (undone < wanted && Journal.UndoLast(sapi.World, player, out int removed, out int restored, out refusal))
            {
                undone++;
                sections += removed;
                blocks += restored;
            }
            if (undone == 0) return TextCommandResult.Error(refusal == null ? "Немає побудов, які можна відкотити" : "Не відкочено: " + refusal);

            return TextCommandResult.Success(string.Format("Відкочено побудов: {0}. Секцій прибрано: {1}, блоків повернуто: {2}. Ще можна відкотити: {3}",
                undone, sections, blocks, Journal.Count(player.PlayerUID)) + (refusal == null ? "" : ". Далі не відкочено: " + refusal));
        }

        // Перераховує записи стрілок і перетинів у завантажених чанках навколо гравця, radius у чанках
        private TextCommandResult OnRailRecheckCommand(TextCommandCallingArgs args)
        {
            int radius = GameMath.Clamp((int)args[0], 0, 8);
            EntityPos pos = args.Caller.Player.Entity.Pos;
            int centerX = (int)pos.X / GlobalConstants.ChunkSize, centerY = (int)pos.Y / GlobalConstants.ChunkSize, centerZ = (int)pos.Z / GlobalConstants.ChunkSize;

            var session = new RailDataSession(sapi.World);
            int chunks = 0;
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                for (int z = centerZ - radius; z <= centerZ + radius; z++)
                {
                    for (int y = centerY - 1; y <= centerY + 1; y++)
                    {
                        var chunk = new Vec3i(x, y, z);
                        if (session.Get(chunk) == null) continue;
                        session.MarkDirty(chunk);
                        chunks++;
                    }
                }
            }
            if (chunks == 0) return TextCommandResult.Error("Поруч немає колії");

            session.SaveAll();
            return TextCommandResult.Success(string.Format("Перераховано записи стрілок і перетинів у чанках з колією: {0}", chunks));
        }

        // Щойно завантажена колонка чанків: дописує її стрілки й перетини, яких ще немає в записах, і дораховує
        // ті в сусідніх чанках, чиї колії сюди заходять
        private void OnChunkColumnLoaded(Vec2i column, IWorldChunk[] chunks)
        {
            // Колонка без колії нікого не цікавить: стрілка чекає лише на чанк, куди заходить її гілка.
            // Дані не розбираємо, дивимося тільки, чи вони є
            bool hasTrack = false;
            for (int y = 0; y < chunks.Length && !hasTrack; y++)
            {
                hasTrack = chunks[y]?.GetModdata(DataInChunk.ModdataKey) != null;
            }
            if (!hasTrack) return;

            // Гра повідомляє про колонку раніше, ніж додає її чанки до завантажених
            // (ServerSystemSupplyChunks.mainThreadLoadChunkColumn), тож світ їх іще не віддає. Рахуємо, щойно віддасть
            int chunkX = column.X, chunkZ = column.Y, height = chunks.Length;
            sapi.Event.EnqueueMainThreadTask(() => CompleteSwitches(chunkX, chunkZ, height), "railworld-switches");
        }

        private void CompleteSwitches(int chunkX, int chunkZ, int height)
        {
            var session = new RailDataSession(sapi.World);
            // Спершу прибираємо те, що веде в нікуди. У цій колонці все: її посилання на завантажених сусідів тепер
            // можна перевірити. У сусідніх лише посилання сюди: решту вони перевірили, коли завантажувалися самі, а
            // посилання сюди тоді лишили, бо цієї колонки ще не було. Зв'язки ведуть лише в сусідні чанки
            System.Func<Vec3i, bool> intoColumn = chunk => chunk.X == chunkX && chunk.Z == chunkZ;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    bool own = dx == 0 && dz == 0;
                    for (int y = 0; y < height; y++) SectionLinker.PruneLost(session, new Vec3i(chunkX + dx, y, chunkZ + dz), own ? null : intoColumn);
                }
            // Кінці колії, які чекали на цей чанк. Досить кінців цієї колонки: зустрічний кінець у сусідній
            // колонці теж вільний, і зв'язок ставиться в обидва боки
            for (int y = 0; y < height; y++) SectionLinker.LinkOpenEnds(session, new Vec3i(chunkX, y, chunkZ));
            for (int y = 0; y < height; y++) SwitchRegistry.Complete(session, new Vec3i(chunkX, y, chunkZ));
            SwitchRegistry.CompletePendingNear(session, chunkX, chunkZ);
            for (int y = 0; y < height; y++) CrossingRegistry.Complete(session, new Vec3i(chunkX, y, chunkZ));
            CrossingRegistry.CompletePendingNear(session, chunkX, chunkZ);
            if (session.HasChanges) session.SaveAll();
        }

        // Що сервер знає про секцію: її деталі, сусідів і стрілки
        private string DescribeSection(Vec3i chunk, int index)
        {
            RailDataSession session = new RailDataSession(sapi.World);
            DataInChunk data = session.Get(chunk);
            if (data == null || !data.RailWaySections.TryGetValue(index, out Section section))
                return "Section " + index + " not found in chunk " + chunk;

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
            SectionStep next = SectionLinker.Next(section, atStart, session.GetSection);
            int count = section.GetLinks(atStart).Count;
            if (next == null) return count == 0 ? "nothing" : "unloaded chunk";

            return string.Format("section {0} in chunk {1} ({2}){3}",
                next.Section.IndexInChunk, next.Section.ChunkAddres, next.EnterAtStart ? "its start" : "its end",
                count > 1 ? ", " + count + " links" : "");
        }

        // Перевіряє, що деталь із пакета справді існує і гравець до неї дотягується
        // Чи лежить точка в боксі виділення цієї деталі секції: тих самих боксів, у які цілиться клієнт
        // (Handler.GetSelectionBoxes)
        private bool OnPart(Vec3i chunk, int index, Section section, SectionPart part, Vec3d hit)
        {
            var boxes = new List<SectionBox>();
            Handler.GetSelectionBoxes(boxes, chunk, index, section);
            return boxes.Exists(box => box.Part == part && box.Contains(hit, HitTolerance));
        }

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

            // Точку влучання присилає клієнт. Як і гра з блоком (ServerSystemBlockSimulation), сервер вірить їй,
            // лише якщо вона лежить на самій деталі, і міряє відстань до неї: інакше клієнт прислав би точку біля
            // себе й дістав секцію будь-де. Гравцеві з правом гри на необмежену досяжність це не потрібно.
            // На налаштування сервера AntiAbuse не зважаємо: гра вмикає свою перевірку лише з ним, а воно за
            // замовчуванням вимкнене
            if (!player.HasPrivilege(Privilege.pickingrange))
            {
                if (!OnPart(chunk, packet.Index, section, part, hit))
                {
                    rejectReason = "hit point is not on the " + part + " of the section";
                    return null;
                }
                if (distance > player.WorldData.PickingRange + ReachTolerance)
                {
                    rejectReason = "out of reach, distance " + distance.ToString("0.00") + ", picking range " + player.WorldData.PickingRange;
                    return null;
                }
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
