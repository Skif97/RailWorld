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
    /// Підтверджений маршрут по точках: положення і напрямок колії в кожній точці.
    /// Клієнт надсилає його лише після перегляду; до того розмітка живе тільки в нього.
    /// </summary>
    [ProtoContract]
    public class RoutePacket
    {
        [ProtoMember(1)] public double[] X;
        [ProtoMember(2)] public double[] Y;
        [ProtoMember(3)] public double[] Z;
        [ProtoMember(4)] public double[] TangentX;
        [ProtoMember(5)] public double[] TangentY;
        [ProtoMember(6)] public double[] TangentZ;
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
                .SetMessageHandler<RailDataPacket>(OnRailDataReceived);
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
        /// Клієнт: відправляє підтверджений маршрут на побудову.
        /// </summary>
        public static void SendRoute(List<RoutePoint> route)
        {
            int count = route.Count;
            RoutePacket packet = new RoutePacket
            {
                X = new double[count], Y = new double[count], Z = new double[count],
                TangentX = new double[count], TangentY = new double[count], TangentZ = new double[count]
            };

            for (int i = 0; i < count; i++)
            {
                packet.X[i] = route[i].Position.X;
                packet.Y[i] = route[i].Position.Y;
                packet.Z[i] = route[i].Position.Z;
                packet.TangentX[i] = route[i].Tangent.X;
                packet.TangentY[i] = route[i].Tangent.Y;
                packet.TangentZ[i] = route[i].Tangent.Z;
            }

            _clientChannel?.SendPacket(packet);
        }

        // ── Сервер: будує маршрут по точках ──────────────────────────────────
        private void OnRoutePacketReceived(IServerPlayer fromPlayer, RoutePacket packet)
        {
            ItemStack tool = fromPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack;
            if (tool?.Attributes == null || tool.ItemAttributes?.IsTrue("AllowGuiDialogRailMenu") != true) return;
            if (tool.Attributes.GetString("railMode") != RoutePlanner.ModeCode) return;

            int count = packet.X?.Length ?? 0;
            if (count < 2 || count > MaxRoutePoints) return;
            if (packet.Y?.Length != count || packet.Z?.Length != count) return;
            if (packet.TangentX?.Length != count || packet.TangentY?.Length != count || packet.TangentZ?.Length != count) return;

            var route = new List<RoutePoint>();
            for (int i = 0; i < count; i++)
            {
                Vec3d tangent = new Vec3d(packet.TangentX[i], packet.TangentY[i], packet.TangentZ[i]);
                if (tangent.Length() < 1e-6) return;

                Vec3d position = new Vec3d(packet.X[i], packet.Y[i], packet.Z[i]);
                if (i > 0 && position.DistanceTo(route[i - 1].Position) > MaxRouteSpan) return;

                route.Add(new RoutePoint { Position = position, Tangent = tangent.Normalize() });
            }

            List<PointOnBezierCurve> points = RouteCurve.BuildPoints(route);
            if (points.Count < 3) return;

            TrackBuilder.Build(_sapi.World, fromPlayer, tool, points);
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
            renderSystem?.SetChunkData(new Vec3i(packet.ChunkX, packet.ChunkY, packet.ChunkZ), data);
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
