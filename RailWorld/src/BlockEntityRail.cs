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
using System.IO;

namespace RailWorld
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

		public List<RailSection> GetRailSections()
		{
			return railSections;
		}

        public RailSection GetRailSection(int num)
        {
			return railSections[num];
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
		{
    
            mesher.AddMeshData(this.totalmesh, 1);
			return true;

			// We return so that the default block cube mesh is also added

		}

		private void loadOrCreateMesh()
		{

			totalmesh = new MeshData(4, 3);
			//totalmesh.Clear();
            ICoreClientAPI coreClientAPI = this.Api as ICoreClientAPI;

			MeshData rail = ObjectCacheUtil.GetOrCreate<MeshData>(coreClientAPI, "trainworldrailmesh", delegate
			{
				Shape shapeRail = Shape.TryGet(this.Api, new AssetLocation("railworld", "shapes/block/rail.json"));
				MeshData mesh;
				coreClientAPI.Tesselator.TesselateShape(ownBlock, shapeRail, out mesh, null, null, null);
				return mesh;
			});
			MeshData test = ObjectCacheUtil.GetOrCreate<MeshData>(coreClientAPI, "trainworldtestmesh", delegate
			{
				Shape shapeRail = Shape.TryGet(this.Api, new AssetLocation("railworld", "shapes/block/test.json"));
				MeshData mesh;
				coreClientAPI.Tesselator.TesselateShape(ownBlock, shapeRail, out mesh, null, null, null);
				return mesh;
			});
			MeshData sleeper = ObjectCacheUtil.GetOrCreate<MeshData>(coreClientAPI, "trainworldsleepermesh", delegate
			{
				Shape shapeRail = Shape.TryGet(coreClientAPI, new AssetLocation("railworld", "shapes/block/sleeper.json"));
				MeshData mesh;
				coreClientAPI.Tesselator.TesselateShape(ownBlock, shapeRail, out mesh, null, null, null);
				return mesh;
			});


			for (int slot = 0; railSections.Count > slot && railSections[slot] != null; slot++)
			{
				totalmesh.AddMeshData(sleeper.Clone()
											 .MatrixTransform(railSections[slot].centerMatrix)
											 .Translate(railSections[slot].centerCenterOffset.ToVec3f())
											 .Translate(new Vec3f(0f, -0.078125f, 0f))); ;


                totalmesh.AddMeshData(rail.Clone()
                                          .MatrixTransform(railSections[slot].leftMatrix)
                                          .Translate(railSections[slot].leftCenterOffset.ToVec3f())
										  .Translate(new Vec3f(0f, -0.078125f, 0f)));

				totalmesh.AddMeshData(rail.Clone()
                                          .MatrixTransform(railSections[slot].rightMatrix)
                                          .Translate(railSections[slot].rightCenterOffset.ToVec3f())
										  .Translate(new Vec3f(0f, -0.078125f, 0f)));

			}

		}



        public void UpdateConnections(bool UpdateAround = false, List<BlockEntityRail> beRailList = null)
		{
			if (beRailList == null) 
			{
				beRailList = GetBERailAround(); 
			}
			for (int slot = 0; railSections.Count > slot; slot++)
			{
				railSections[slot].UpdateConnections(beRailList);
			}
			for (int i = 0; i < beRailList.Count && UpdateAround; i++)
			{
				beRailList[i].UpdateConnections();

			}
		}

		public List<BlockEntityRail> GetBERailAround()
		{
			List<BlockEntityRail> bentitiesAround = new List<BlockEntityRail>
			{
				this
			};

			for (int i = -1; i < 2; i++)
			{
				for (int j = -1; j < 2; j++)
				{
					for (int k = -1; k < 2; k++)
					{
						BlockPos bp = Pos;
						bp.X += i;
						bp.Y += j;
						bp.Z += k;
						BlockEntityRail bentity = Api.World.BlockAccessor.GetBlockEntity(bp) as BlockEntityRail;
						if (bentity != null && bentity.railSections.Count != 0)
						{
							bentitiesAround.Add(bentity);
						}
					}
				}
			}


			return bentitiesAround;
		}

		public void AddSection(ItemStack byItemStack = null) 
		{
			if (byItemStack != null)
			{
				railSections.Add(new RailSection(byItemStack, railSections.Count));
                UpdateConnections(true);
                MarkDirty(true, null);

                if (Api != null && Api.Side == EnumAppSide.Client)
                {
                   this.loadOrCreateMesh();
                }
            }
		}


		public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
			if (byItemStack != null)
			{
				AddSection(byItemStack);
            }
			base.OnBlockPlaced(byItemStack);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
			railSections.Clear();

            for (int slot = 0; tree.TryGetDouble(string.Format("{0}.position.X", slot)) != null; slot++) 
			{
				railSections.Add(new RailSection(tree, slot));
			}

			
			ICoreAPI api = this.Api;
            MarkDirty(true, null);
            if (api != null && api.Side == EnumAppSide.Client)
			{
				this.loadOrCreateMesh();
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
				railSections[slot].ToTreeAttribute(tree);
			}

			
		}



    }
}
