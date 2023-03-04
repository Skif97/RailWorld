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

namespace TrainWorld
{
	public class BlockEntityRail : BlockEntity
	{

        List<RailSection> railSections = new List<RailSection>();
		Block ownBlock;
		MeshData totalmesh = new MeshData(4, 3);
        Random rnd = new Random();
        // Implementation of IBlockShapeSupplier

        public override void Initialize(ICoreAPI api)
		{
			ownBlock = api.World.BlockAccessor.GetBlock(Pos) as BlockRail;
			base.Initialize(api);
			if (api.Side == EnumAppSide.Client )
			{

				this.loadOrCreateMesh();
			}
		}

		public float GetHeightSections(int sec=0) 
		{
			return (float)this.railSections[sec].centerCenterOffset.Y;

        }

		public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
		{
    
            mesher.AddMeshData(this.totalmesh, 1);
			return true;

			// We return so that the default block cube mesh is also added

		}

		private void loadOrCreateMesh()
		{

			//totalmesh = new MeshData(4, 3);
			totalmesh.Clear();
            ICoreClientAPI coreClientAPI = this.Api as ICoreClientAPI;

			MeshData rail = ObjectCacheUtil.GetOrCreate<MeshData>(coreClientAPI, "trainworldrailmesh", delegate
			{
				Shape shapeRail = Shape.TryGet(this.Api, new AssetLocation("trainworld", "shapes/block/rail.json"));
				MeshData mesh;
				coreClientAPI.Tesselator.TesselateShape(ownBlock, shapeRail, out mesh, null, null, null);
				return mesh;
			});
			MeshData test = ObjectCacheUtil.GetOrCreate<MeshData>(coreClientAPI, "trainworldtestmesh", delegate
			{
				Shape shapeRail = Shape.TryGet(this.Api, new AssetLocation("trainworld", "shapes/block/test.json"));
				MeshData mesh;
				coreClientAPI.Tesselator.TesselateShape(ownBlock, shapeRail, out mesh, null, null, null);
				return mesh;
			});
			MeshData sleeper = ObjectCacheUtil.GetOrCreate<MeshData>(coreClientAPI, "trainworldsleepermesh", delegate
			{
				Shape shapeRail = Shape.TryGet(coreClientAPI, new AssetLocation("trainworld", "shapes/block/sleeper.json"));
				MeshData mesh;
				coreClientAPI.Tesselator.TesselateShape(ownBlock, shapeRail, out mesh, null, null, null);
				return mesh;
			});


			for (int slot = 0; railSections.Count > slot && railSections[slot] != null; slot++)
			{


                //float[] rotX = GameMath.SectionsToRotateX(railSection);
                //float[] rotZ = GameMath.SectionsToRotateX(railSection);
                //totalmesh = test.Clone();
                double rad = ModMath.TangentToPitch(railSections[slot].centerCenterTangent);
				double rad2 = ModMath.TangentToPitch(railSections[slot].leftCenterTangent);
                double rad3 = ModMath.TangentToPitch(railSections[slot].rightCenterTangent);


				//       totalmesh.AddMeshData(sleeper.Clone()
				//.Scale(new Vec3f(0f, 0f, 0f), 1f, 1f, 1.35f + (float)rnd.NextDouble() / 5f)
				//.Rotate(new Vec3f(0f, 0f, 0f), 0f, (float)ModMath.TangentToYaw(railSections[slot].centerCenterTangent) + ((float)rnd.NextDouble()/10.4652f)-0.0477773956f, 0f)
				//.RotateAroundAxis(railSections[slot].centerCenterNormal, rad)
				//.Translate(railSections[slot].centerCenterOffset.ToVec3f())
				//.Translate(new Vec3f((float)rnd.NextDouble() / 16f, 0f, (float)rnd.NextDouble() / 16f))
				//);

				Vec3f sleeperOffset = railSections[slot].centerCenterOffset.ToVec3f();

				//sleeperOffset.Y += 0.09375f;

				totalmesh.AddMeshData(sleeper.Clone()
                                                 .Scale(new Vec3f(0f, 0f, 0f), 1f, 1f, 1.35f )
                                                 .Rotate(new Vec3f(0f, 0f, 0f), 0f, (float)ModMath.TangentToYaw(railSections[slot].centerCenterTangent), 0f)
                                                 .RotateAroundAxis(railSections[slot].centerCenterNormal, rad)
                                                 .Translate(sleeperOffset)
												 .Translate(new Vec3f(0f, -0.078125f, 0f))
												 );


                totalmesh.AddMeshData(rail.Clone().Scale(new Vec3f(0f, 0f, 0f), (float)railSections[slot].leftlenght, 1f, 1f).Rotate(new Vec3f(0f, 0f, 0f), 0f, (float)ModMath.TangentToYaw(railSections[slot].leftCenterTangent ), 0f).RotateAroundAxis(railSections[slot].leftCenterNormal , rad2).Translate(railSections[slot].leftCenterOffset.ToVec3f() ).Translate(new Vec3f(0f, -0.078125f, 0f)));
				totalmesh.AddMeshData(rail.Clone().Scale(new Vec3f(0f, 0f, 0f), (float)railSections[slot].rightlenght, 1f, 1f).Rotate(new Vec3f(0f, 0f, 0f), 0f, (float)ModMath.TangentToYaw(railSections[slot].rightCenterTangent), 0f).RotateAroundAxis(railSections[slot].rightCenterNormal, rad3).Translate(railSections[slot].rightCenterOffset.ToVec3f()).Translate(new Vec3f(0f, -0.078125f, 0f)));

			}

		}

		public void AddSection(ItemStack byItemStack = null) 
		{
			if (byItemStack != null)
			{
				railSections.Add(new RailSection(byItemStack));
                if (Api != null && Api.Side == EnumAppSide.Client)
                {
                    this.loadOrCreateMesh();
                    base.MarkDirty(true, null);
                }
            }
		}


		public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
			if (byItemStack != null)
			{
				railSections.Add(new RailSection(byItemStack));

                //Block.CollisionBoxes[0].Y2 = (float)railSections[0].centerCenterOffset.Y + 0.09375f;
                //Block.SelectionBoxes[0].Y2 = (float)railSections[0].centerCenterOffset.Y + 0.09375f;
                ICoreAPI api = this.Api;
                if (api != null && api.Side == EnumAppSide.Client)
                {
                    this.loadOrCreateMesh();
                    base.MarkDirty(true, null);
                }
            }
			base.OnBlockPlaced(byItemStack);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
			for (int slot = 0; tree.TryGetDouble(string.Format("{0}.position.X", slot)) != null; slot++) 
			{
				railSections.Add(new RailSection(tree, slot));
			}

			
			ICoreAPI api = this.Api;
			if (api != null && api.Side == EnumAppSide.Client)
			{
				this.loadOrCreateMesh();
				base.MarkDirty(true, null);
			}
			base.FromTreeAttributes(tree, worldForResolving);
		}

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
        	base.ToTreeAttributes(tree);
        	if (base.Block != null)
        	{
        		tree.SetString("forBlockCode", base.Block.Code.ToShortString());
        	}

			for(int slot = 0; railSections.Count > slot; slot++) 
			{
				railSections[slot].ToTreeAttribute(tree, slot);
			}

			
		}



    }
}
