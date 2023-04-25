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

namespace RailWorld
{
	public class ItemTrolley : Item
	{

		public override string GetHeldTpUseAnimation(ItemSlot activeHotbarSlot, Entity byEntity)
		{
			return null;
		}

		public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
		{
			if (blockSel == null)
			{
				return;
			}
			IPlayer player = byEntity.World.PlayerByUid((byEntity as EntityPlayer).PlayerUID);
			if (!byEntity.World.Claims.TryAccess(player, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
			{
				return;
			}
			if (!(byEntity is EntityPlayer) || player.WorldData.CurrentGameMode != EnumGameMode.Creative)
			{
				slot.TakeOut(1);
				slot.MarkDirty();
			}
			//AssetLocation assetLocation = new AssetLocation(this.Code.Domain, base.CodeEndWithoutParts(1));
			AssetLocation assetLocation = new AssetLocation(this.Code.Domain, "trolley");
			EntityProperties entityType = byEntity.World.GetEntityType(assetLocation);
			if (entityType == null)
			{
				byEntity.World.Logger.Error("ItemCreature: No such entity - {0}", new object[]
				{
					assetLocation
				});
				if (this.api.World.Side == EnumAppSide.Client)
				{
					(this.api as ICoreClientAPI).TriggerIngameError(this, "nosuchentity", string.Format("No such entity loaded - '{0}'.", assetLocation));
				}
				return;
			}
			Entity entity = byEntity.World.ClassRegistry.CreateEntity(entityType);
			if (entity != null)
			{
				BlockPos pos = blockSel.Position.Copy();
                if (byEntity.World.BlockAccessor.GetBlock(pos).Id == byEntity.World.GetBlock(new AssetLocation("railworld", "rail")).Id)
                {
                    BlockEntityRail bentity = byEntity.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityRail;
                    if (bentity != null)
                    {
						List<RailSection> railSections = bentity.GetRailSections();

                        if (railSections.Count != 0) 
						{
							int railslot = (int)((railSections.Count - 1f) / 2f);
							entity.ServerPos.X = railSections[railslot].centerСenterPos.X;
                            entity.ServerPos.Y = railSections[railslot].centerСenterPos.Y;
                            entity.ServerPos.Z = railSections[railslot].centerСenterPos.Z;

                            //entity.ServerPos.Yaw = railSections[railslot].centerYaw;
                            //entity.ServerPos.Pitch = railSections[railslot].centerPitch;
                            //entity.ServerPos.Roll = railSections[railslot].centerRoll;

                            entity.ServerPos.Yaw = railSections[railslot].centerYaw;
                            entity.ServerPos.Pitch = railSections[railslot].centerPitch;
                            entity.ServerPos.Roll = railSections[railslot].centerRoll;

                        }
                    }
                }
				else 
				{
                    entity.ServerPos.X = blockSel.Position.X + (blockSel.DidOffset ? 0 : blockSel.Face.Normali.X) + 0.5f;
                    entity.ServerPos.Y = blockSel.Position.Y + (blockSel.DidOffset ? 0 : blockSel.Face.Normali.Y);
                    entity.ServerPos.Z = blockSel.Position.Z + (blockSel.DidOffset ? 0 : blockSel.Face.Normali.Z) + 0.5f;
					entity.ServerPos.Yaw = byEntity.BodyYaw;
                }
				
				entity.Pos.SetFrom(entity.ServerPos);
				entity.PositionBeforeFalling.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
				entity.Attributes.SetString("origin", "playerplaced");
				//JsonObject attributes = this.Attributes;
				//if (attributes != null && attributes.IsTrue("setGuardedEntityAttribute"))
				//{
				//	entity.WatchedAttributes.SetLong("guardedEntityId", byEntity.EntityId);
				//	EntityPlayer entityPlayer = byEntity as EntityPlayer;
				//	if (entityPlayer != null)
				//	{
				//		entity.WatchedAttributes.SetString("guardedPlayerUid", entityPlayer.PlayerUID);
				//	}
				//}
				byEntity.World.SpawnEntity(entity);
				handHandling = EnumHandHandling.PreventDefaultAction;
			}
		}

		public override string GetHeldTpIdleAnimation(ItemSlot activeHotbarSlot, Entity byEntity, EnumHand hand)
		{
			EntityProperties entityType = byEntity.World.GetEntityType(new AssetLocation(this.Code.Domain, base.CodeEndWithoutParts(1)));
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
