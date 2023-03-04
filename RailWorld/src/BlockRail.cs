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
using System.Threading;

namespace TrainWorld
{
	public class BlockRail : Block
	{
		private static int HighlightSlotId = 25;
		List<RailSection> sections;

		public override void OnLoaded(ICoreAPI api)
		{
			base.OnLoaded(api);

		}

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
			//Vec3f playerpos = capi.World.Player.Entity.Pos.AsBlockPos.ToVec3f();

			//capi.Render.RenderRectangle(playerpos.X + 2f, playerpos.Y, playerpos.Z + 2f, 1f, 1f, ColorUtil.Hex2Int("#3399FF"));
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
        }

        //private MeshData loadOrCreateMesh()
        //{
        //	MeshData totalmesh = new MeshData(4, 3);
        //	ICoreClientAPI coreClientAPI = this.api as ICoreClientAPI;
        //	if(coreClientAPI != null) 
        //	{
        //		MeshData test = ObjectCacheUtil.GetOrCreate<MeshData>(coreClientAPI, "trainworldtestmesh", delegate
        //		{
        //			Shape shapeRail = Shape.TryGet(this.api, new AssetLocation("trainworld", "shapes/block/test.json"));
        //			MeshData mesh;
        //			coreClientAPI.Tesselator.TesselateShape(ownBlock, shapeRail, out mesh, null, null, null);
        //			return mesh;
        //		});
        //		totalmesh.AddMeshData(test.Clone());
        //	}
        //	totalmesh.g
        //	return totalmesh;

        //}
        //      public override void OnBeingLookedAt(IPlayer byPlayer, BlockSelection blockSel, bool firstTick)
        //      {
        //          base.OnBeingLookedAt(byPlayer, blockSel, firstTick);

        //	ICoreClientAPI coreClientAPI = api as ICoreClientAPI;

        //          if (coreClientAPI != null) 
        //	{
        //		Vec3f playerpos = byPlayer.Entity.Pos.AsBlockPos.ToVec3f();

        //		coreClientAPI.Render.RenderRectangle(playerpos.X + 2f, playerpos.Y, playerpos.Z + 2f, 1f, 1f, ColorUtil.Hex2Int("#3399FF"));
        //	}


        //}

        public override void OnHeldIdle(ItemSlot slot, EntityAgent byEntity)
        {
            base.OnHeldIdle(slot, byEntity);
        }

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            BlockEntityRail bentity = blockAccessor.GetBlockEntity(pos) as BlockEntityRail;
            if (bentity != null)
            {
                Cuboidf[] colisions = new Cuboidf[1];
                colisions[0] = this.CollisionBoxes[0];
				colisions[0].Y2 = bentity.GetHeightSections() + 0.09375f;
				return colisions;

            }
            return base.GetCollisionBoxes(blockAccessor, pos);
        }

        public override Cuboidf[] GetSelectionBoxes (IBlockAccessor blockAccessor, BlockPos pos)
        {
            BlockEntityRail bentity = blockAccessor.GetBlockEntity(pos) as BlockEntityRail;
            if (bentity != null)
            {
                Cuboidf[] selection = new Cuboidf[1];
                selection[0] = this.SelectionBoxes[0];
                selection[0].Y2 = bentity.GetHeightSections() + + 0.25f;
                return selection;

            }
            return base.GetSelectionBoxes(blockAccessor, pos);
        }


        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
		{

			//return base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack);

			if ((blockSel == null) || (byPlayer == null)) { return false; }

			string railMode;
			int railLengRad;
			int railClimDes;
			string railDirection;
			int railDirectionInt;

			ItemStack mystack = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
			if (mystack != null && mystack.Attributes != null && mystack.ItemAttributes.IsTrue("AllowGuiDialogRailMenu"))
			{
				railMode = mystack.Attributes.GetString("railMode", "SingleBlock");
				railLengRad = mystack.Attributes.GetInt("railLengRad", 30);
				railClimDes = mystack.Attributes.GetInt("railClimDes", 0);
				railDirection = mystack.Attributes.GetString("railDirection", "Left");

			}
			else
			{
				return false;
			}

			if(railMode== "Turn90") { railDirectionInt = 0; }
			else if (railMode == "Turn45") { railDirectionInt = 2; }
            else { railDirectionInt = 0; }

			if (railDirection == "Right") { railDirectionInt++; }

			CubicBezierCurve3d cotrolPoints = ModMath.CubicBezierCotrolPointSercher4P(blockSel.Position.ToVec3d(), byPlayer.Entity.Pos.Yaw, 0.8f, railLengRad, railClimDes, railDirectionInt);
			//ModMath.ShowTheOutline(byPlayer.Entity, cotrolPoints, 2.8, HighlightSlotId);

			sections = ModMath.GenerateRailSections(cotrolPoints.CutIntoEqualPieces(1f/ (TrainWorld.sectiontPerBlock * 2)), 0.78f);

			for (int j = 0; j < sections.Count; j++)
			{
				ItemStack itemStackRailSec = sections[j].ToItemStackAttributes();
                if (world.BlockAccessor.GetBlock((int)sections[j].position.X, (int)sections[j].position.Y, (int)sections[j].position.Z).Id == world.GetBlock(new AssetLocation("trainworld", "rail")).Id) 
				{
					BlockEntityRail bentity = world.BlockAccessor.GetBlockEntity(sections[j].position.ToBlockPos()) as BlockEntityRail;
                    if (bentity != null) 
					{
						bentity.AddSection(itemStackRailSec);
					}
				}
                else 
				{
					world.BlockAccessor.SetBlock(world.GetBlock(new AssetLocation("trainworld", "rail")).Id, sections[j].position.ToBlockPos(), itemStackRailSec);
				}
				
				//world.BlockAccessor.GetChunkAtBlockPos(positions[j].position.X, positions[j].position.Y, positions[j].position.Z).MarkModified();

			}
			return true;

		}

	}
}
