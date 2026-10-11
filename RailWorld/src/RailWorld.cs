using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using System;
using Vintagestory.API.Common.Entities;
using ProtoBuf;
using RailWorld.src.Items;
using RailWorld.src.RailWay;

namespace RailWorld
{
    [ProtoContract]
    public struct RailMenuPacket
    {
        [ProtoMember(1)] public string railMode;
        [ProtoMember(2)] public int railLengRad;
        [ProtoMember(3)] public int railClimDes;
        [ProtoMember(4)] public string railDirection;
        // Матеріал шпал і рейок нової колії або RailWorld.DontBuild
        [ProtoMember(5)] public string sleeperMaterial;
        [ProtoMember(6)] public string railMaterial;
        // Порода гравію для підсипки або RailWorld.DontBuild
        [ProtoMember(7)] public string ballastMaterial;
        // Ламати блоки на шляху колії замість того, щоб лишати секцію заблокованою
        [ProtoMember(8)] public bool replaceBlocks;
    }

    /// <summary>
    /// Пакет що надсилається з сервера на клієнт після того як секції збережені в чанк.
    /// Містить координати чанку і серіалізовані дані секцій.
    /// </summary>
    [ProtoContract]
    public class RailDataPacket
    {
        [ProtoMember(1)] public int ChunkX;
        [ProtoMember(2)] public int ChunkY;
        [ProtoMember(3)] public int ChunkZ;
        [ProtoMember(4)] public byte[] Data; // серіалізований DataInChunk
    }

    /// <summary>
    /// Маршрут, який гравець підтвердив: точки розмітки, як він їх поставив. Маршрут із них складає сервер.
    /// Точка на блоці це блок і кут погляду; точка на колії це секція, її кінець і бік, у який іде маршрут.
    /// </summary>
    [ProtoContract]
    public class RoutePacket
    {
        // Номер маршруту в клієнта: з ним сервер надсилає відповідь
        [ProtoMember(1)] public int Id;
        [ProtoMember(2)] public int[] BlockX;
        [ProtoMember(3)] public int[] BlockY;
        [ProtoMember(4)] public int[] BlockZ;
        [ProtoMember(5)] public double[] Yaw;
        [ProtoMember(6)] public bool[] Exact;
        // Секція, на кінці якої стоїть точка. Номер -1 для точки на блоці
        [ProtoMember(7)] public int[] JointChunkX;
        [ProtoMember(8)] public int[] JointChunkY;
        [ProtoMember(9)] public int[] JointChunkZ;
        [ProtoMember(10)] public int[] JointIndex;
        [ProtoMember(11)] public bool[] JointAtStart;
        [ProtoMember(12)] public int[] Sign;
    }

    /// <summary>
    /// Відповідь сервера на маршрут: побудовано чи ні, і якщо ні, то чому. Доти клієнт тримає перегляд.
    /// </summary>
    [ProtoContract]
    public class RouteResultPacket
    {
        [ProtoMember(1)] public int Id;
        [ProtoMember(2)] public bool Built;
        [ProtoMember(3)] public string Reason;
    }

    public class RailWorld : ModSystem
    {
        /// <summary>Значення матеріалу, яке означає «цю деталь не ставити».</summary>
        public const string DontBuild = "none";
        static internal ICoreClientAPI _capi;
        static internal ICoreServerAPI _sapi;
        static IServerNetworkChannel _serverChannel;
        static IClientNetworkChannel _clientChannel;
        GuiDialog _dialog;

        public override bool ShouldLoad(EnumAppSide forSide) => true;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.RegisterBlockClass("BlockRail", typeof(BlockRail));
            api.RegisterBlockClass("BlockTrackBed", typeof(BlockTrackBed));
            api.RegisterBlockEntityClass("BlockEntityTrackBed", typeof(BlockEntityTrackBed));
            api.RegisterBlockEntityBehaviorClass("TrackBedSnowCover", typeof(BEBehaviorTrackBedSnowCover));
            api.RegisterItemClass("ItemTrolley", typeof(ItemTrolley));
            api.RegisterEntity("EntityTrolley", typeof(EntityTrolley));
            api.RegisterItemClass("ItemSleeper", typeof(ItemSleeper));
            api.RegisterItemClass("ItemRail", typeof(ItemRail));
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            _capi = api;

            api.Input.RegisterHotKey(
                "openrailmenu",
                Lang.Get("Train World: Open rail menu"),
                GlKeys.F,
                HotkeyType.GUIOrOtherControls);

            api.Input.SetHotKeyHandler("openrailmenu", ToggleGuiDialogRailMenu);

            _clientChannel = api.Network
                .RegisterChannel("TWchannel")
                .RegisterMessageType<RailMenuPacket>()
                .RegisterMessageType<RailDataPacket>()
                .RegisterMessageType<RoutePacket>()
                .RegisterMessageType<RouteResultPacket>()
                .SetMessageHandler<RailDataPacket>(OnRailDataReceived)
                .SetMessageHandler<RouteResultPacket>(packet => RoutePlanner.Instance?.OnServerAnswer(packet.Id, packet.Built, packet.Reason));
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            _sapi = api;
            base.StartServerSide(api);

            _serverChannel = api.Network
                .RegisterChannel("TWchannel")
                .RegisterMessageType<RailMenuPacket>()
                .RegisterMessageType<RailDataPacket>()
                .RegisterMessageType<RoutePacket>()
                .RegisterMessageType<RouteResultPacket>()
                .SetMessageHandler<RailMenuPacket>(OnRailMenuPacketReceived)
                .SetMessageHandler<RoutePacket>(OnRoutePacketReceived);
        }

        // ── Сервер: отримали налаштування рейки від клієнта ──────────────────
        private void OnRailMenuPacketReceived(IServerPlayer fromPlayer, RailMenuPacket packet)
        {
            ItemStack mystack = _sapi.World.PlayerByUid(fromPlayer.PlayerUID)
                .InventoryManager.ActiveHotbarSlot.Itemstack;

            if (mystack != null && mystack.Attributes != null
                && mystack.ItemAttributes.IsTrue("AllowGuiDialogRailMenu"))
            {
                mystack.Attributes.SetString("railMode", packet.railMode);
                mystack.Attributes.SetInt("railLengRad", packet.railLengRad);
                mystack.Attributes.SetInt("railClimDes", packet.railClimDes);
                mystack.Attributes.SetString("railDirection", packet.railDirection);
                mystack.Attributes.SetString("sleeperMaterial", packet.sleeperMaterial ?? "oak");
                mystack.Attributes.SetString("railMaterial", packet.railMaterial ?? "iron");
                mystack.Attributes.SetString("ballastMaterial", packet.ballastMaterial ?? DontBuild);
                mystack.Attributes.SetBool("replaceBlocks", packet.replaceBlocks);
                _sapi.World.PlayerByUid(fromPlayer.PlayerUID)
                    .InventoryManager.ActiveHotbarSlot.MarkDirty();
            }
        }

        // Скільки точок і яку відстань між сусідніми сервер приймає в одному маршруті
        private const int MaxRoutePoints = 64;
        private const double MaxRouteSpan = 400;

        /// <summary>
        /// Клієнт: відправляє підтверджений маршрут на побудову. id це номер, з яким сервер відповість.
        /// </summary>
        public static void SendRoute(List<RouteMarker> markers, int id)
        {
            int count = markers.Count;
            RoutePacket packet = new RoutePacket
            {
                Id = id,
                BlockX = new int[count], BlockY = new int[count], BlockZ = new int[count],
                Yaw = new double[count], Exact = new bool[count],
                JointChunkX = new int[count], JointChunkY = new int[count], JointChunkZ = new int[count],
                JointIndex = new int[count], JointAtStart = new bool[count], Sign = new int[count]
            };

            for (int i = 0; i < count; i++)
            {
                RouteMarker marker = markers[i];
                packet.BlockX[i] = marker.Block.X;
                packet.BlockY[i] = marker.Block.Y;
                packet.BlockZ[i] = marker.Block.Z;
                packet.Yaw[i] = marker.Yaw;
                packet.Exact[i] = marker.Exact;

                packet.JointIndex[i] = marker.Joint == null ? -1 : marker.Joint.IndexInChunk;
                if (marker.Joint == null) continue;

                packet.JointChunkX[i] = marker.Joint.ChunkAddres.X;
                packet.JointChunkY[i] = marker.Joint.ChunkAddres.Y;
                packet.JointChunkZ[i] = marker.Joint.ChunkAddres.Z;
                packet.JointAtStart[i] = marker.JointAtStart;
                packet.Sign[i] = marker.Sign;
            }

            _clientChannel?.SendPacket(packet);
        }

        // ── Сервер: будує маршрут по точках ──────────────────────────────────
        private void OnRoutePacketReceived(IServerPlayer fromPlayer, RoutePacket packet)
        {
            // Клієнт тримає перегляд маршруту, доки не дізнається, чим скінчилося: відповідаємо завжди
            string refusal = BuildRoute(fromPlayer, packet);
            _serverChannel.SendPacket(new RouteResultPacket { Id = packet.Id, Built = refusal == null, Reason = refusal }, fromPlayer);
        }

        // Складає маршрут із точок гравця і кладе колію. Клієнтові віримо лише в тому, що поставив гравець:
        // який блок, куди дивився, котрий стик колії. Усе інше береться зі своїх даних.
        // Повертає причину відмови або null, якщо колію покладено
        private string BuildRoute(IServerPlayer fromPlayer, RoutePacket packet)
        {
            ItemStack tool = fromPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack;
            if (tool?.Attributes == null || tool.ItemAttributes?.IsTrue("AllowGuiDialogRailMenu") != true) return "предмет для прокладання колії не в руці";

            int count = packet.BlockX?.Length ?? 0;
            if (count < 2 || count > MaxRoutePoints) return "у маршруті має бути від 2 до " + MaxRoutePoints + " точок";
            if (packet.BlockY?.Length != count || packet.BlockZ?.Length != count || packet.Yaw?.Length != count || packet.Exact?.Length != count
                || packet.JointChunkX?.Length != count || packet.JointChunkY?.Length != count || packet.JointChunkZ?.Length != count
                || packet.JointIndex?.Length != count || packet.JointAtStart?.Length != count || packet.Sign?.Length != count) return "пошкоджений запит";

            RailDataSession lookup = new RailDataSession(_sapi.World);
            var markers = new List<RouteMarker>();
            for (int i = 0; i < count; i++)
            {
                RouteMarker marker;
                if (packet.JointIndex[i] >= 0)
                {
                    var link = new SectionLink { ChunkX = packet.JointChunkX[i], ChunkY = packet.JointChunkY[i], ChunkZ = packet.JointChunkZ[i], Index = packet.JointIndex[i] };
                    Section joint = lookup.GetSection(link);
                    if (joint == null) return "колії в точці " + (i + 1) + " уже немає";

                    bool atStart = packet.JointAtStart[i];
                    marker = RouteMarker.OnJoint(joint, atStart, packet.Sign[i] < 0 ? -1 : 1, i == 0, lookup.GetSection);

                    // Тут маршрут дасть нову стрілку. Клієнт це вже перевірив, але його дані могли застаріти
                    if (marker.Track != null && SwitchZone.JointInBody(joint, atStart, lookup.GetSection)) return "надто близько до іншої стрілки";
                }
                else
                {
                    double yaw = packet.Yaw[i];
                    if (double.IsNaN(yaw) || double.IsInfinity(yaw)) return "пошкоджений запит";
                    marker = RouteMarker.OnBlock(new BlockPos(packet.BlockX[i], packet.BlockY[i], packet.BlockZ[i], 0), yaw);
                    marker.Exact = packet.Exact[i];
                }

                if (i > 0 && marker.Position.DistanceTo(markers[i - 1].Position) > MaxRouteSpan) return "точки надто далеко одна від одної";
                markers.Add(marker);
            }

            List<RoutePoint> route = RouteBuilder.Build(markers);
            if (route == null) return "з цих точок маршрут не складається";

            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(route, lookup.GetSection, out string conflict,
                chunk => lookup.Get(chunk)?.RailWaySections.Values);

            // Клієнт уже перевірив маршрут у себе, але колія з того часу могла змінитися. Маршрут,
            // який порушує жорсткі умови, не будуємо зовсім: половина маршруту гірша за жоден
            string problem = conflict ?? RoutePlanner.FindProblem(points);
            if (problem != null) return problem;
            if (points.Count < 3) return "маршрут надто короткий";

            return TrackBuilder.Build(_sapi.World, fromPlayer, tool, points);
        }

        /// <summary>
        /// Викликається з BlockRail після збереження секцій в чанк.
        /// Надсилає оновлені дані чанку всім гравцям поблизу.
        /// </summary>
        public static void SendChunkDataToClients(Vec3i chunkCoord, DataInChunk data)
        {
            if (_serverChannel == null || _sapi == null) return;

            byte[] serialized = Vintagestory.API.Util.SerializerUtil.Serialize(data);

            RailDataPacket packet = new RailDataPacket
            {
                ChunkX = chunkCoord.X,
                ChunkY = chunkCoord.Y,
                ChunkZ = chunkCoord.Z,
                Data = serialized
            };

            // Надсилаємо всім гравцям (можна звузити до nearby пізніше)
            _serverChannel.BroadcastPacket(packet);
        }

        // ── Клієнт: отримали дані рейок з сервера ────────────────────────────
        private void OnRailDataReceived(RailDataPacket packet)
        {
            if (_capi == null) return;

            DataInChunk data = Vintagestory.API.Util.SerializerUtil.Deserialize<DataInChunk>(packet.Data);
            if (data == null) return;

            RailWaySystem renderSystem = _capi.ModLoader.GetModSystem<RailWaySystem>();
            if (renderSystem == null || !renderSystem.SetChunkData(new Vec3i(packet.ChunkX, packet.ChunkY, packet.ChunkZ), data)) return;

            // Як гра робить з укріпленнями блоків (ModSystemBlockReinforcement): прислані дані лягають і в чанк
            _capi.World.BlockAccessor.GetChunk(packet.ChunkX, packet.ChunkY, packet.ChunkZ)?.SetModdata(DataInChunk.ModdataKey, packet.Data);
        }

        private bool ToggleGuiDialogRailMenu(KeyCombination keyCombination)
        {
            ItemStack mystack = _capi.World.Player.InventoryManager.ActiveHotbarSlot.Itemstack;

            if (mystack != null && mystack.ItemAttributes != null
                && mystack.ItemAttributes.IsTrue("AllowGuiDialogRailMenu"))
            {
                if (_dialog is null) _dialog = new GuiDialogRailMenu(_capi);
                if (!_dialog.IsOpened()) return _dialog.TryOpen();
                if (!_dialog.TryClose()) return true;
                _dialog.Dispose();
                _dialog = null;
            }
            return true;
        }
    }
}
