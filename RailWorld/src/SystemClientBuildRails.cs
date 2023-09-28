using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
using Vintagestory.Server;
using System.Threading.Tasks.Dataflow;
using Vintagestory.API.MathTools;
using System.Drawing;
using System.Collections.Generic;

namespace RailWorld
{

    public class SystemClientBuildRails :  IRenderer, IDisposable
    {
        MeshData SelectionCube;
        MeshRef meshRef;
        float size = 2f; 
        ICoreClientAPI capi;
        protected Matrixf ModelMat = new Matrixf();
        public SystemClientBuildRails(ICoreClientAPI capi) 
        {
            this.capi = capi;
            Initialize();
            this.capi.Event.RegisterRenderer(this, EnumRenderStage.OIT);
        }
        public double RenderOrder
        {
            get
            {
                return 0.9f;
            }
        }

        public int RenderRange
        {
            get
            {
                return 20;
            }
        }


        public void Initialize()
        {
            if (capi.Side == EnumAppSide.Client)
            {
                SelectionCube = new MeshData(24, 36, false, false, true, false);

                //SelectionCube = ModelCubeUtilExt.GetCube();

                int color = ColorUtil.ToRgba(150, (int)(GuiStyle.ActiveButtonTextColor[2] * 255.0), 
                                                 (int)(GuiStyle.ActiveButtonTextColor[1] * 255.0), 
                                                 (int)(GuiStyle.ActiveButtonTextColor[0] * 255.0));
               
                Vec3f centerPos = new Vec3f(0f, 0f, 0f);
                Vec3f cubeSize = new Vec3f(size, size, size);
                float[] shadings = CubeMeshUtil.DefaultBlockSideShadingsByFacing;
                for (int k = 0; k < 6; k++)
                {
                    BlockFacing face = BlockFacing.ALLFACES[k];
                    //BlockFacing NORTH = new BlockFacing("north", 1, 0, 2, 1, new Vec3i(0, 0, -1), new Vec3f(0.5f, 0.5f, 0f), EnumAxis.Z, new Cuboidf(0f, 0f, 0f, 1f, 1f, 0f));
                    
                    ModelCubeUtilExt.AddFace(SelectionCube, face, centerPos, cubeSize, color, shadings[face.Index]);
                   // SelectionCube.AddVertex(array[0], array[1], array[2], ColorUtil.ColorMultiply3(color, brightness))
                }
                //SelectionCube.Translate(new Vec3f(-1f, -1f, -1f));
                //SelectionCube.Translate(new Vec3f(0f, 3f, 0f));
                meshRef = capi.Render.UploadMesh(SelectionCube.Clone());
                
            }
        }


        void IRenderer.OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (capi.World.Player != null)
            {
                if (capi.World.Player.CurrentBlockSelection != null)
                {
                    Block bl = capi.World.Player.CurrentBlockSelection.Block as BlockRail;
                    if (bl !=null) 
                    {
                        BlockEntity ble = capi.World.Player.CurrentBlockSelection.Block.GetBlockEntity<BlockEntityRail>(capi.World.Player.CurrentBlockSelection);
                        if (ble != null) 
                        {
                            if (((BlockEntityRail)ble).GetRailSections().Count>0) 
                            {
                                SelectionCube = new MeshData(24, 36, false, false, true, false);

                                RailSection rs = ((BlockEntityRail)ble).GetRailSection(0);

                                int color = ColorUtil.ToRgba(150, (int)(GuiStyle.ActiveButtonTextColor[2] * 255.0),
                                                 (int)(GuiStyle.ActiveButtonTextColor[1] * 255.0),
                                                 (int)(GuiStyle.ActiveButtonTextColor[0] * 255.0));
                                float[] shadings = CubeMeshUtil.DefaultBlockSideShadingsByFacing;
                                color = ColorUtil.ColorMultiply3(color, shadings[0]);
                                //0, 1
                                SelectionCube.AddVertex((float)rs.leftStartOffset.X, (float)rs.leftStartOffset.Y - 0.15f, (float)rs.leftStartOffset.Z, color);
                                SelectionCube.AddVertex((float)rs.leftStartOffset.X, (float)rs.leftStartOffset.Y + 0.08f, (float)rs.leftStartOffset.Z, color);
                                //2, 3
                                SelectionCube.AddVertex((float)rs.leftEndOffset.X, (float)rs.leftEndOffset.Y - 0.15f, (float)rs.leftEndOffset.Z, color);
                                SelectionCube.AddVertex((float)rs.leftEndOffset.X, (float)rs.leftEndOffset.Y + 0.08f, (float)rs.leftEndOffset.Z, color);
                                //4, 5
                                SelectionCube.AddVertex((float)rs.rightEndOffset.X, (float)rs.rightEndOffset.Y - 0.15f, (float)rs.rightEndOffset.Z, color);
                                SelectionCube.AddVertex((float)rs.rightEndOffset.X, (float)rs.rightEndOffset.Y + 0.08f, (float)rs.rightEndOffset.Z, color);
                                //6, 7
                                SelectionCube.AddVertex((float)rs.rightStartOffset.X, (float)rs.rightStartOffset.Y - 0.15f, (float)rs.rightStartOffset.Z, color);
                                SelectionCube.AddVertex((float)rs.rightStartOffset.X, (float)rs.rightStartOffset.Y + 0.08f, (float)rs.rightStartOffset.Z, color);
                                


                                SelectionCube.AddIndex(0); //лево
                                SelectionCube.AddIndex(3);
                                SelectionCube.AddIndex(1);

                                SelectionCube.AddIndex(0);
                                SelectionCube.AddIndex(3);
                                SelectionCube.AddIndex(2);

                                SelectionCube.AddIndex(4); //право
                                SelectionCube.AddIndex(7);
                                SelectionCube.AddIndex(5);

                                SelectionCube.AddIndex(4);
                                SelectionCube.AddIndex(7);
                                SelectionCube.AddIndex(6);

                                SelectionCube.AddIndex(2); //зад 
                                SelectionCube.AddIndex(5);
                                SelectionCube.AddIndex(3);

                                SelectionCube.AddIndex(2);
                                SelectionCube.AddIndex(5);
                                SelectionCube.AddIndex(4);

                                SelectionCube.AddIndex(6); //перед 
                                SelectionCube.AddIndex(1);
                                SelectionCube.AddIndex(7);

                                SelectionCube.AddIndex(6);
                                SelectionCube.AddIndex(1);
                                SelectionCube.AddIndex(0);

                                SelectionCube.AddIndex(2); //низ 
                                SelectionCube.AddIndex(6);
                                SelectionCube.AddIndex(0);

                                SelectionCube.AddIndex(2);
                                SelectionCube.AddIndex(6);
                                SelectionCube.AddIndex(4);

                                SelectionCube.AddIndex(3); //верх 
                                SelectionCube.AddIndex(7);
                                SelectionCube.AddIndex(1);

                                SelectionCube.AddIndex(3);
                                SelectionCube.AddIndex(7);
                                SelectionCube.AddIndex(5);

                            }
                            //Vec3d offset = capi.World.Player.CurrentBlockSelection.FullPosition;
                            Vec3d offset = capi.World.Player.CurrentBlockSelection.Position.ToVec3d();
                            // SelectionCube.xyz[0] += 0.0011f;
                            // SelectionCube.RotateD(0.08f, -0.04f, 0.08f);
                            capi.Render.UpdateMesh(meshRef, SelectionCube);

                            ShaderProgramBlockhighlights prog = ShaderPrograms.Blockhighlights;
                            prog.Use();
                            Vec3d playerPos = capi.World.Player.Entity.CameraPos;


                            if (meshRef != null)
                            {

                                capi.Render.GlPushMatrix();
                                capi.Render.GlLoadMatrix(capi.Render.CameraMatrixOrigin);
                                prog.NightVisonStrength = 5;
                                BlockPos bpos = capi.World.Player.CurrentBlockSelection.Position;
                                //capi.Render.GlTranslate(bpos.X, bpos.Y, bpos.Z);
                                capi.Render.GlTranslate((double)((float)((double)offset.X - playerPos.X)), (double)((float)((double)offset.Y - playerPos.Y)), (double)((float)((double)offset.Z - playerPos.Z)));
                                prog.ProjectionMatrix = capi.Render.CurrentProjectionMatrix;
                                prog.ModelViewMatrix = capi.Render.CurrentModelviewMatrix;
                                capi.Render.RenderMesh(meshRef);
                                capi.Render.GlPopMatrix();
                            }

                            prog.Stop();
                        }
                    }

                }

            }

        }

    
            
        

        public void Dispose()
        {

            capi.Render.DeleteMesh(meshRef);
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        }
    }
}
