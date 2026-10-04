using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using System;
using Vintagestory.API.Common.Entities;
using System.Collections.Generic;
using RailWorld.src.RailWay;

namespace RailWorld.src.Items
{
    public class ItemTrolley : Item
    {

        public override string GetHeldTpUseAnimation(ItemSlot activeHotbarSlot, Entity byEntity)
        {
            return null;
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
        {
            EntityPlayer entityPlayer = byEntity as EntityPlayer;
            if (entityPlayer == null) return;
            IPlayer player = byEntity.World.PlayerByUid(entityPlayer.PlayerUID);
            if (player == null) return;

            // Секція колії, на яку дивиться гравець. Для неї blockSel дорівнює null
            SectionSelection railSel = api.ModLoader.GetModSystem<RailWaySystem>().GetSelection(player);
            if (railSel == null && blockSel == null) return;

            BlockPos accessPos = railSel != null ? railSel.Position : blockSel.Position;
            if (!byEntity.World.Claims.TryAccess(player, accessPos, EnumBlockAccessFlags.BuildOrBreak))
            {
                return;
            }

            AssetLocation assetLocation = new AssetLocation(Code.Domain, "trolley");
            EntityProperties entityType = byEntity.World.GetEntityType(assetLocation);
            if (entityType == null)
            {
                byEntity.World.Logger.Error("ItemCreature: No such entity - {0}", new object[]
                {
                    assetLocation
                });
                if (api.World.Side == EnumAppSide.Client)
                {
                    (api as ICoreClientAPI).TriggerIngameError(this, "nosuchentity", string.Format("No such entity loaded - '{0}'.", assetLocation));
                }
                return;
            }

            Entity entity = byEntity.World.ClassRegistry.CreateEntity(entityType);
            if (entity == null) return;

            if (railSel != null)
            {
                // Ставимо посередині секції. Далі вагонетка сама їде по зв'язках між секціями
                Section section = railSel.Section;
                double middle = section.FullStartPosition.DistanceTo(section.FullEndPosition) / 2;
                double halfHeight = (entityType.SelectionBoxSize?.Y ?? entityType.CollisionBoxSize.Y) / 2;
                EntityTrolley.ApplyRailPose(entity.Pos, section, middle, halfHeight, 1);

                entity.Attributes.SetBool("onRail", true);
                entity.Attributes.SetDouble("railS", middle);
                entity.Attributes.SetInt("railFacing", 1);
                entity.Attributes.SetInt("railChunkX", railSel.Chunk.X);
                entity.Attributes.SetInt("railChunkY", railSel.Chunk.Y);
                entity.Attributes.SetInt("railChunkZ", railSel.Chunk.Z);
                entity.Attributes.SetInt("railSectionIndex", railSel.Index);
            }
            else
            {
                entity.Pos.X = blockSel.Position.X + (blockSel.DidOffset ? 0 : blockSel.Face.Normali.X) + 0.5f;
                entity.Pos.Y = blockSel.Position.Y + (blockSel.DidOffset ? 0 : blockSel.Face.Normali.Y);
                entity.Pos.Z = blockSel.Position.Z + (blockSel.DidOffset ? 0 : blockSel.Face.Normali.Z) + 0.5f;
                entity.Pos.Yaw = byEntity.BodyYaw;
            }

            if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
            {
                slot.TakeOut(1);
                slot.MarkDirty();
            }

            entity.PositionBeforeFalling.Set(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
            entity.Attributes.SetString("origin", "playerplaced");

            byEntity.World.SpawnEntity(entity);
            handHandling = EnumHandHandling.PreventDefaultAction;
        }

        public override string GetHeldTpIdleAnimation(ItemSlot activeHotbarSlot, Entity byEntity, EnumHand hand)
        {
            EntityProperties entityType = byEntity.World.GetEntityType(new AssetLocation(Code.Domain, CodeEndWithoutParts(1)));
            if (entityType == null)
            {
                return base.GetHeldTpIdleAnimation(activeHotbarSlot, byEntity, hand);
            }
            if (Math.Max(entityType.CollisionBoxSize.X, entityType.CollisionBoxSize.Y) > 1f)
            {
                return "holdunderarm";
            }
            return "holdbothhands";
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new WorldInteraction[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "heldhelp-place",
                    MouseButton = EnumMouseButton.Right
                }
            }.Append(base.GetHeldInteractionHelp(inSlot));
        }

    }
}
