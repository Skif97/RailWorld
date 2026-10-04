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
        // Предметом для прокладання колії блоки не ламаються. Для секції blockSel дорівнює null,
        // тому видалення секцій цим самим предметом працює далі
        public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
        {
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
                string sleeperMaterial = mystack.Attributes.GetString("sleeperMaterial", "oak");
                string railMaterial    = mystack.Attributes.GetString("railMaterial", "iron");

                CubicBezierCurve3d controlPoints;
                Vec3d pos = blockSel.Position.ToVec3d();
                // ModMath очікує yaw, для якого напрямок = (cos, 0, -sin). З 1.20 Pos.Yaw гравця
                // повернутий на 90°, тому рахуємо кут з реального вектора погляду
                Vec3f view = byPlayer.Entity.Pos.GetViewVector();
                double yaw = GameMath.Mod((float)Math.Atan2(-view.Z, view.X), GameMath.TWOPI);

                if (railMode == "Turn90")
                    controlPoints = ModMath.CotrolPointSercherForArc(pos, yaw, railLengRad, Math.PI / 2, left, 0.8f, railClimDes);
                else if (railMode == "Turn45")
                    controlPoints = ModMath.CotrolPointSercherForArc(pos, yaw, railLengRad, Math.PI / 4, left, 0.8f, railClimDes);
                else if (railMode == "Straight")
                    controlPoints = ModMath.CotrolPointSercherForStraight(pos, yaw, railLengRad, 0.8f, railClimDes);
                else
                    controlPoints = ModMath.CotrolPointSercherForStraight(pos, yaw, 1f, 0.8f, railClimDes);

                List<PointOnBezierCurve> pointList = controlPoints.CutIntoEqualPieces(0.25f);
                ModMath.ApplyCant(pointList);
                double trackWidth = 0.78f;

                RailDataSession session = new RailDataSession(world);
                List<Section> stretch = new List<Section>();

                for (int i = 0; i < pointList.Count - 2; i += 2)
                {
                    var section = new Section(
                        pointList[i], pointList[i + 1], pointList[i + 2], (float)trackWidth);
                    // Деталь, для якої в меню вибрано «не будувати», лишається порожнім місцем
                    if (sleeperMaterial != RailWorld.DontBuild) section.InstallationSleeper(sleeperMaterial);
                    if (railMaterial != RailWorld.DontBuild) section.InstallationRails(railMaterial);

                    // Чанк може бути не завантажений, тоді секцію записати нікуди
                    DataInChunk data = session.Get(section.ChunkAddres, create: true);
                    if (data == null) continue;

                    // Якщо тут уже лежить така сама секція, другу поверх неї не кладемо
                    if (SectionLinker.HasSameSection(data, section)) continue;

                    data.Add(section);
                    session.MarkDirty(section.ChunkAddres);
                    stretch.Add(section);
                }

                SectionLinker.LinkStretch(session, stretch);
                session.SaveAll();
            }

            return true;
        }
    }
}
