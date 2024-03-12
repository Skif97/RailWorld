using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using System;
using System.Collections.Generic;
using Vintagestory.API.Server;
using Vintagestory.Common;
using static System.Collections.Specialized.BitVector32;
using Vintagestory.API.Config;
using Vintagestory.Server;

namespace RailWorld
{
    public class BlockRail : Block
    {
        List<RailSectionClient> sections;

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

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            BlockEntityRail bentity = blockAccessor.GetBlockEntity(pos) as BlockEntityRail;
            if (bentity != null)
            {
                Cuboidf[] selection = new Cuboidf[1];
                selection[0] = this.SelectionBoxes[0];
                selection[0].Y2 = bentity.GetHeightSections() + +0.25f;
                return selection;

            }
            return base.GetSelectionBoxes(blockAccessor, pos);
        }



        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
        {
          
            //return base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack);

            if ((blockSel == null) || (byPlayer == null)) { return false; }
            if (world.Api.Side == EnumAppSide.Server)
            {
                string railMode;
                int railLengRad;
                int railClimDes;
                string railDirection;
                bool left;

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

                if (railDirection == "Left")
                {
                    left = true;
                }
                else
                {
                    left = false;
                }

                CubicBezierCurve3d cotrolPoints;


                if (railMode == "Turn90")
                {
                    cotrolPoints = ModMath.CotrolPointSercherForArc(blockSel.Position.ToVec3d(), byPlayer.Entity.Pos.Yaw, railLengRad, Math.PI / 2, left, 0.8f, railClimDes);
                }
                else if (railMode == "Turn45")
                {
                    cotrolPoints = ModMath.CotrolPointSercherForArc(blockSel.Position.ToVec3d(), byPlayer.Entity.Pos.Yaw, railLengRad, Math.PI / 4, left, 0.8f, railClimDes);
                }
                else if (railMode == "Straight")
                {
                    cotrolPoints = ModMath.CotrolPointSercherForStraight(blockSel.Position.ToVec3d(), byPlayer.Entity.Pos.Yaw, railLengRad, 0.8f, railClimDes);
                }
                else
                {
                    cotrolPoints = ModMath.CotrolPointSercherForStraight(blockSel.Position.ToVec3d(), byPlayer.Entity.Pos.Yaw, 1f, 0.8f, railClimDes);
                }

                sections = GenerateRailSections(cotrolPoints.CutIntoEqualPieces(0.25f), 0.78f); //размер в 2 раза меньше так как на одну секцию нужно 2 кусочка

                Dictionary<Vec3i, List<RailSectionClient>> ModDataInChunks = new Dictionary<Vec3i, List<RailSectionClient>>();
                List<RailSectionClient> railSections = null;
                for (int i = 0; i < sections.Count; i++)
                {
                    Vec3i ChunkAddres = ModMath.FindChunk(sections[i].position.X, sections[i].position.Y, sections[i].position.Z);
                    
                    if (!ModDataInChunks.ContainsKey(ChunkAddres))
                    {
                        IWorldChunk currentChunk = world.BlockAccessor.GetChunk(ChunkAddres.X, ChunkAddres.Y, ChunkAddres.Z);
                        railSections = currentChunk.GetModdata<List<RailSectionClient>>("RailSections", null);
                        ModDataInChunks.Add(ChunkAddres, railSections);
                    }
                    railSections = railSections != null ? railSections : new List<RailSectionClient>();
                    //ItemStack itemStackRailSec = sections[j].ToItemStackAttributes();
                    //BlockPos pos = new BlockPos((int)sections[j].position.X, (int)sections[j].position.Y, (int)sections[j].position.Z);


                    //if (world.BlockAccessor.GetBlock(pos).Id == world.GetBlock(new AssetLocation("railworld", "rail")).Id)
                    //{
                    //    BlockEntityRail bentity = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityRail;
                    //    if (bentity != null)
                    //    {
                    railSections.Add(sections[i]);
                    //        bentity.AddSection(itemStackRailSec);
                    //    }
                    //}
                    //else
                    //{
                    //    world.BlockAccessor.SetBlock(world.GetBlock(new AssetLocation("railworld", "rail")).Id, sections[j].position.ToBlockPos(), itemStackRailSec);
                    //}  
                    ModDataInChunks[ChunkAddres]= railSections;
                }
                foreach (var railList in ModDataInChunks)
                {
                    world.BlockAccessor.GetChunk(railList.Key.X, railList.Key.Y, railList.Key.Z).SetModdata<List<RailSectionClient>>("RailSections", railList.Value);
                }
                return true;

            }
            return true;
        }

        private int GetFreeIndex(Dictionary<int, RailSectionServer> railSectionInChunk)
        {
            int index = 0;
            while (railSectionInChunk.ContainsKey(index))
            {
                index++;
            }
            return index;
        }

        public List<RailSectionClient> GenerateRailSections(List<PointOnBezierCurve> pointsOnCurve, double trackWidth)
        {
            //List<RailSectionServer> railSectionsServer = new List<RailSectionServer>();
            List<RailSectionClient> railSections = new List<RailSectionClient>();

            for (int i = 0; i < pointsOnCurve.Count - 2; i += 2)
            {
                Vec3i ChunkAddres = ModMath.FindChunk(pointsOnCurve[i + 1].position);


                RailSectionServer railSectionsServer = new RailSectionServer(ChunkAddres, 0, (float)trackWidth, pointsOnCurve[i], pointsOnCurve[i + 1], pointsOnCurve[i + 2]);
                RailSectionClient raillSection = new RailSectionClient(api, pointsOnCurve[i], pointsOnCurve[i + 1], pointsOnCurve[i + 2], trackWidth);
                railSections.Add(raillSection);
            }
            return railSections;
        }

    }
}
