using Vintagestory.API.Common;

namespace RailWorld
{
    public class BlockRail : Block
    {
        // Правий клік колії не кладе: він ставить точку розмітки або готову форму з меню, а кладе колію
        // сервер, коли гравець підтвердить перегляд
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            handling = EnumHandHandling.PreventDefault;
            // Лише перший кадр натискання: утримання кнопки нових точок не ставить
            if (api.Side == EnumAppSide.Client && firstEvent) RoutePlanner.Instance?.OnRightClick();
        }

        // Предметом для прокладання колії блоки не ламаються. Для секції blockSel дорівнює null,
        // тому видалення секцій цим самим предметом працює далі
        public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
        {
            // Поки триває розмітка маршруту, лівий клік це крок назад, а не видалення секції
            if (api.Side == EnumAppSide.Client && RoutePlanner.Instance?.OnLeftClick() == true)
            {
                handling = EnumHandHandling.PreventDefaultAction;
                return;
            }

            if (blockSel != null)
            {
                handling = EnumHandHandling.PreventDefaultAction;
                return;
            }
            base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
        }

        // Сам предмет блоком у світ не стає
        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer,
            BlockSelection blockSel, ItemStack byItemStack)
        {
            return false;
        }
    }
}
