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
            selectionRenderer?.MarkChunkDirty(coord);

            if (Renderer == null) return;

            var railSections = new List<Section>();

            foreach (var kvp in data.RailWaySections)
            {
                Section section = kvp.Value;

                if (section.SleeperInstalled)
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

                if (section.FirstRailInstalled || section.SecondRailInstalled)
                    railSections.Add(section);
            }

            if (railSections.Count > 0)
                Renderer.RebuildRails(chunkCoord, railSections);
        }

        public void RemoveChunk(Vec3i chunkCoord)
        {
            clientBoxes.Remove(chunkCoord);
            selectionRenderer?.MarkChunkDirty(chunkCoord);
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
