using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using System;
using System.Collections.Generic;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using RailWorld.src.RailWay;

namespace RailWorld
{
    public class BlockRail : Block
    {
        private static bool IsPointsMode(ItemSlot slot)
        {
            return slot?.Itemstack?.Attributes?.GetString("railMode") == RoutePlanner.ModeCode;
        }

        // У режимі «по точках» правий клік не кладе колію, а ставить точку розмітки
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (!IsPointsMode(slot))
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }

            handling = EnumHandHandling.PreventDefault;
            // Лише перший кадр натискання: утримання кнопки нових точок не ставить
            if (api.Side == EnumAppSide.Client && firstEvent) RoutePlanner.Instance?.OnRightClick();
        }

        // Предметом для прокладання колії блоки не ламаються. Для секції blockSel дорівнює null,
        // тому видалення секцій цим самим предметом працює далі
        public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
        {
            // Поки триває розмітка маршруту, лівий клік це крок назад, а не видалення секції
            if (api.Side == EnumAppSide.Client && IsPointsMode(slot) && RoutePlanner.Instance?.OnLeftClick() == true)
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

        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer,
            BlockSelection blockSel, ItemStack byItemStack)
        {
            if (blockSel == null || byPlayer == null) return false;

            if (world.Api.Side == EnumAppSide.Server)
            {
                ItemStack mystack = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
                if (mystack == null || mystack.Attributes == null
                    || !mystack.ItemAttributes.IsTrue("AllowGuiDialogRailMenu"))
                    return false;

                string railMode      = mystack.Attributes.GetString("railMode", "SingleBlock");
                int    railLengRad   = mystack.Attributes.GetInt("railLengRad", 30);
                int    railClimDes   = mystack.Attributes.GetInt("railClimDes", 0);
                string railDirection = mystack.Attributes.GetString("railDirection", "Left");
                bool   left          = railDirection == "Left";

                // Маршрут по точках будується окремим пакетом від клієнта, не кліком
                if (railMode == RoutePlanner.ModeCode) return false;

                // Повороти зі старих режимів не крутіші за найменший радіус маршруту
                int turnRadius = Math.Max(railLengRad, (int)Math.Ceiling(RoutePlanner.MinRadius));

                CubicBezierCurve3d controlPoints;
                Vec3d pos = blockSel.Position.ToVec3d();
                // ModMath очікує yaw, для якого напрямок = (cos, 0, -sin). З 1.20 Pos.Yaw гравця
                // повернутий на 90°, тому рахуємо кут з реального вектора погляду
                Vec3f view = byPlayer.Entity.Pos.GetViewVector();
                double yaw = GameMath.Mod((float)Math.Atan2(-view.Z, view.X), GameMath.TWOPI);

                if (railMode == "Turn90")
                    controlPoints = ModMath.CotrolPointSercherForArc(pos, yaw, turnRadius, Math.PI / 2, left, 0.8f, railClimDes);
                else if (railMode == "Turn45")
                    controlPoints = ModMath.CotrolPointSercherForArc(pos, yaw, turnRadius, Math.PI / 4, left, 0.8f, railClimDes);
                else if (railMode == "Straight")
                    controlPoints = ModMath.CotrolPointSercherForStraight(pos, yaw, railLengRad, 0.8f, railClimDes);
                else
                    controlPoints = ModMath.CotrolPointSercherForStraight(pos, yaw, 1f, 0.8f, railClimDes);

                List<PointOnBezierCurve> pointList = controlPoints.CutIntoEqualPieces(0.25f);
                ModMath.ApplyCant(pointList);

                TrackBuilder.Build(world, byPlayer, mystack, pointList);
            }

            return true;
        }
    }
}
