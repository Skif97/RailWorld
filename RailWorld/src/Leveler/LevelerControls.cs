using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace RailWorld
{
    /// <summary>Що робить клік по поставленій тринозі чи нівеліру.</summary>
    public enum LevelerAction
    {
        RotateLeft,
        RotateRight,
        TakeInstrument
    }

    /// <summary>Клік по поставленій тринозі чи нівеліру: де стоїть і що зробити. Виконує сервер.</summary>
    [ProtoContract]
    public class LevelerActionPacket
    {
        [ProtoMember(1)] public int X;
        [ProtoMember(2)] public int Y;
        [ProtoMember(3)] public int Z;
        [ProtoMember(4)] public LevelerAction Action;
    }

    /// <summary>
    /// Кліки по поставленій тринозі чи нівеліру на клієнті. Гра перед тим, як ламати блок чи взаємодіяти з ним,
    /// питає подію InWorldAction; якщо клік наш, вона далі нічого не робить, ні в виживанні, ні в креативі. Що
    /// означає клік, вирішує BlockLevelerTripod.ClickAction, а виконує сервер. Поки кнопку тримають, гра повторює
    /// подію з тим самим кроком, що й ставлення блоків, тож нівелір крутиться далі.
    /// </summary>
    public class LevelerControls
    {
        private readonly ICoreClientAPI capi;
        private readonly IClientNetworkChannel channel;

        public LevelerControls(ICoreClientAPI capi, IClientNetworkChannel channel)
        {
            this.capi = capi;
            this.channel = channel;
            capi.Input.InWorldAction += OnInWorldAction;
        }

        private void OnInWorldAction(EnumEntityAction action, bool on, ref EnumHandling handled)
        {
            bool left = action == EnumEntityAction.InWorldLeftMouseDown;
            if (!on || (!left && action != EnumEntityAction.InWorldRightMouseDown)) return;

            IClientPlayer player = capi.World.Player;
            BlockSelection sel = player?.CurrentBlockSelection;
            if (sel == null) return;
            BlockPos pos = BlockLevelerTripod.ControlPos(capi.World.BlockAccessor, sel.Position);
            if (capi.World.BlockAccessor.GetBlock(pos) is not BlockLevelerTripod tripod) return;

            bool[] keys = capi.Input.KeyboardKeyStateRaw;
            bool alt = keys[(int)GlKeys.AltLeft] || keys[(int)GlKeys.AltRight];
            LevelerAction? click = tripod.ClickAction(player, left, alt);
            if (click == null) return;

            handled = EnumHandling.PreventDefault;
            channel.SendPacket(new LevelerActionPacket { X = pos.X, Y = pos.Y, Z = pos.Z, Action = click.Value });
        }
    }
}
