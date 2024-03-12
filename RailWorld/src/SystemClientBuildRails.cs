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
using Vintagestory.Common;
using Vintagestory.GameContent;
using System.Collections.Immutable;
using System.Numerics;
using Vintagestory.Client;
using Microsoft.VisualBasic;
using static System.Net.Mime.MediaTypeNames;

namespace RailWorld
{

    public class SystemClientBuildRails : IRenderer, IDisposable, ITexPositionSource
    {
        TextureAtlasPosition texPosition;
        MeshData SelectionCube;
        ShaderProgramChunkopaque prog;
        MeshRef meshRef;
        Dictionary<string, int> textures;
        float size = 2f;
        int tex;
        ICoreClientAPI capi;
        protected Matrixf ModelMat = new Matrixf();
        public SystemClientBuildRails(ICoreClientAPI capi)
        {
            this.capi = capi;
            Initialize();
            this.capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque);

        }
        public double RenderOrder
        {
            get
            {
                return 0.37f;
            }
        }

        public int RenderRange
        {
            get
            {
                return 100;
            }
        }

        public Size2i AtlasSize => (capi as ICoreClientAPI).BlockTextureAtlas.Size;

        public TextureAtlasPosition this[string textureCode] => texPosition;

        public void Initialize()
        {
            if (capi.Side == EnumAppSide.Client)
            {
                var loc = new AssetLocation("railworld", "textures/block/iron.png");
                tex = capi.Render.GetOrLoadTexture(loc);
                textures = new Dictionary<string, int>();
                textures.Add("iron", tex);

                MeshData rail = ObjectCacheUtil.GetOrCreate<MeshData>(capi, "trainworldrailmesh", delegate
                {
                    Shape shapeRail = Shape.TryGet(capi, new AssetLocation("railworld", "shapes/block/rail.json"));
                    texPosition = capi.BlockTextureAtlas.Positions[0];
                    TextureAtlasPosition etetr = new TextureAtlasPosition();
                    etetr.get;
                    MeshData mesh;
                    capi.Tesselator.TesselateShape("customshape", shapeRail, out mesh, this);
                    capi.Tesselator.TesselateShape()


                    return mesh;
                    
                });

                SelectionCube = rail.Clone();
                //SelectionCube = new MeshData(24, 36, false, false, true, false);

                //SelectionCube = ModelCubeUtilExt.GetCube();

                //int color = ColorUtil.ToRgba(150, (int)(GuiStyle.ActiveButtonTextColor[2] * 255.0), 
                //                                 (int)(GuiStyle.ActiveButtonTextColor[1] * 255.0), 
                //  
                // Vec3f centerPos = new Vec3f(0f, 0f, 0f);
                // Vec3f cubeSize = new Vec3f(size, size, size);
                // float[] shadings = CubeMeshUtil.DefaultBlockSideShadingsByFacing;
                //  for (int k = 0; k < 6; k++)
                //   {
                //     BlockFacing face = BlockFacing.ALLFACES[k];
                //BlockFacing NORTH = new BlockFacing("north", 1, 0, 2, 1, new Vec3i(0, 0, -1), new Vec3f(0.5f, 0.5f, 0f), EnumAxis.Z, new Cuboidf(0f, 0f, 0f, 1f, 1f, 0f));

                //       ModelCubeUtilExt.AddFaceSkipTex(SelectionCube, face, centerPos, cubeSize, color, shadings[face.Index]);
                // SelectionCube.AddVertex(array[0], array[1], array[2], ColorUtil.ColorMultiply3(color, brightness))
                //    }
                //SelectionCube.Translate(new Vec3f(-1f, -1f, -1f));
                //SelectionCube.Translate(new Vec3f(0f, 3f, 0f));
                meshRef = capi.Render.UploadMesh(SelectionCube.Clone());

                prog = ShaderPrograms.Chunkopaque;

            }
        }


        void IRenderer.OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            Vec3d playerPos = capi.World.Player.Entity.CameraPos;
            EntityPos bpos = capi.World.Player.Entity.Pos.Copy();
            // IStandardShaderProgram prog = capi.Render.shader

            //SelectionCube.Translate(new Vec3f((float)-playerPos.X, (float)-playerPos.Y, (float)-playerPos.Z));
            // capi.Render.Upd (meshRef, SelectionCube);

            capi.Render.GlPushMatrix();
            capi.Render.GlLoadMatrix(capi.Render.CameraMatrixOrigin);

            prog.Use();
            if (meshRef != null)
            {
                // prog.Tex2D = capi.BlockTextureAtlas.AtlasTextures[tex].TextureId;
                prog.textureLocations.

                //prog.NightVisonStrength = 5;
                if (capi.World.Player.CurrentBlockSelection != null)
                {
                    bpos.SetPos(capi.World.Player.CurrentBlockSelection.Position.ToVec3d());
                }

                capi.Render.GlTranslate(bpos.X + 0.5, bpos.Y + 1, bpos.Z + 0.5);
                //capi.Render.GlTranslate((double)((float)((double) - playerPos.X)), (double)((float)((double) - playerPos.Y)), (double)((float)((double) - playerPos.Z)));
                prog.ProjectionMatrix = capi.Render.CurrentProjectionMatrix;
                //prog.ModelMatrix 
                //prog.ViewMatrix = capi.Render.CameraMatrixOriginf;

                prog.ModelViewMatrix = capi.Render.CurrentModelviewMatrix;
                //prog.BindTexture2D
                //prog.RgbaAmbientIn = capi.Render.AmbientColor;
                // prog.RgbaFogIn = capi.Render.FogColor;
                // prog.FogMinIn = capi.Render.FogMin;
                // prog.FogDensityIn = capi.Render.FogDensity;
                //prog.rgb .RgbaTint = ColorUtil.WhiteArgbVec;

                capi.Render.RenderMesh(meshRef);
                capi.Render.GlPopMatrix();
            }

            prog.Stop();


        }

        //void IRenderer.OnRenderFrame(float deltaTime, EnumRenderStage stage)
        //{
        //    if (capi.World.Player != null)
        //    {
        //        if (capi.World.Player.CurrentBlockSelection != null)
        //        {
        //            Block bl = capi.World.Player.CurrentBlockSelection.Block as BlockRail;
        //            if (bl !=null) 
        //            {
        //                BlockEntity ble = capi.World.Player.CurrentBlockSelection.Block.GetBlockEntity<BlockEntityRail>(capi.World.Player.CurrentBlockSelection);
        //                if (ble != null) 
        //                {
        //                    Vec3d from = capi.World.Player.Entity.Pos.XYZ.Clone().Add(capi.World.Player.Entity.LocalEyePos);
        //                    Vec3d to = capi.World.Player.Entity.Pos.GetViewVector().ToVec3d();
        //                    Ray ray = new Ray();
        //                    ray.dir = to;
        //                    ray.origin = from;
        //                    OBBIntersectionTest intersectionTest = new OBBIntersectionTest();
        //                    intersectionTest.LoadRayAndPos(ray);

        //                    Vec3d offset = capi.World.Player.CurrentBlockSelection.Position.ToVec3d();
        //                    Vec3d playerPos = capi.World.Player.Entity.CameraPos;

        //                    for (int i = 0; ((BlockEntityRail)ble).GetRailSections().Count > i; i++)
        //                    {
        //                        if (((BlockEntityRail)ble).GetRailSections().Count > 0)
        //                        {
        //                            SelectionCube = new MeshData(24, 36, false, false, true, false);

        //                            RailSectionClient rs = ((BlockEntityRail)ble).GetRailSection(i);

        //                            int color = ColorUtil.ToRgba(150, (int)(GuiStyle.ActiveButtonTextColor[2] * 255.0),
        //                                             (int)(GuiStyle.ActiveButtonTextColor[1] * 255.0),
        //                                             (int)(GuiStyle.ActiveButtonTextColor[0] * 255.0));
        //                            float[] shadings = CubeMeshUtil.DefaultBlockSideShadingsByFacing;
        //                            color = ColorUtil.ColorMultiply3(color, shadings[0]);
        //                            //0, 1
        //                            SelectionCube.AddVertexSkipTex((float)rs.leftStartOffset.X, (float)rs.leftStartOffset.Y - 0.15f, (float)rs.leftStartOffset.Z, color);
        //                            SelectionCube.AddVertexSkipTex((float)rs.leftStartOffset.X, (float)rs.leftStartOffset.Y + 0.08f, (float)rs.leftStartOffset.Z, color);
        //                            //2, 3
        //                            SelectionCube.AddVertexSkipTex((float)rs.leftEndOffset.X, (float)rs.leftEndOffset.Y - 0.15f, (float)rs.leftEndOffset.Z, color);
        //                            SelectionCube.AddVertexSkipTex((float)rs.leftEndOffset.X, (float)rs.leftEndOffset.Y + 0.08f, (float)rs.leftEndOffset.Z, color);
        //                            //4, 5
        //                            SelectionCube.AddVertexSkipTex((float)rs.rightEndOffset.X, (float)rs.rightEndOffset.Y - 0.15f, (float)rs.rightEndOffset.Z, color);
        //                            SelectionCube.AddVertexSkipTex((float)rs.rightEndOffset.X, (float)rs.rightEndOffset.Y + 0.08f, (float)rs.rightEndOffset.Z, color);
        //                            //6, 7
        //                            SelectionCube.AddVertexSkipTex((float)rs.rightStartOffset.X, (float)rs.rightStartOffset.Y - 0.15f, (float)rs.rightStartOffset.Z, color);
        //                            SelectionCube.AddVertexSkipTex((float)rs.rightStartOffset.X, (float)rs.rightStartOffset.Y + 0.08f, (float)rs.rightStartOffset.Z, color);



        //                            SelectionCube.AddIndex(0); //лево
        //                            SelectionCube.AddIndex(3);
        //                            SelectionCube.AddIndex(1);

        //                            SelectionCube.AddIndex(0);
        //                            SelectionCube.AddIndex(3);
        //                            SelectionCube.AddIndex(2);

        //                            SelectionCube.AddIndex(4); //право
        //                            SelectionCube.AddIndex(7);
        //                            SelectionCube.AddIndex(5);

        //                            SelectionCube.AddIndex(4);
        //                            SelectionCube.AddIndex(7);
        //                            SelectionCube.AddIndex(6);

        //                            SelectionCube.AddIndex(2); //зад 
        //                            SelectionCube.AddIndex(5);
        //                            SelectionCube.AddIndex(3);

        //                            SelectionCube.AddIndex(2);
        //                            SelectionCube.AddIndex(5);
        //                            SelectionCube.AddIndex(4);

        //                            SelectionCube.AddIndex(6); //перед 
        //                            SelectionCube.AddIndex(1);
        //                            SelectionCube.AddIndex(7);

        //                            SelectionCube.AddIndex(6);
        //                            SelectionCube.AddIndex(1);
        //                            SelectionCube.AddIndex(0);

        //                            SelectionCube.AddIndex(2); //низ 
        //                            SelectionCube.AddIndex(6);
        //                            SelectionCube.AddIndex(0);

        //                            SelectionCube.AddIndex(2);
        //                            SelectionCube.AddIndex(6);
        //                            SelectionCube.AddIndex(4);

        //                            SelectionCube.AddIndex(3); //верх 
        //                            SelectionCube.AddIndex(7);
        //                            SelectionCube.AddIndex(1);

        //                            SelectionCube.AddIndex(3);
        //                            SelectionCube.AddIndex(7);
        //                            SelectionCube.AddIndex(5);

        //                        }
        //                        MeshData SelectionCube2 = SelectionCube.Clone();
        //                        SelectionCube.Translate(new Vec3f(((float)(offset.X)), ((float)(offset.Y)), ((float)(offset.Z))));


        //                        if (true)
        //                        {
        //                            SelectionCube.Translate(new Vec3f((float)-playerPos.X, (float)-playerPos.Y, (float)-playerPos.Z));
        //                            capi.Render.UpdateMesh(meshRef, SelectionCube);
        //                            ShaderProgramBlockhighlights prog = ShaderPrograms.Blockhighlights;
        //                            prog.Use();
        //                            if (meshRef != null)
        //                            {

        //                                capi.Render.GlPushMatrix();

        //                                capi.Render.GlLoadMatrix(capi.Render.CameraMatrixOrigin);
        //                                //prog.NightVisonStrength = 5;
        //                                //BlockPos bpos = capi.World.Player.CurrentBlockSelection.Position;
        //                                //capi.Render.GlTranslate(bpos.X, bpos.Y, bpos.Z);
        //                               // capi.Render.GlTranslate((double)((float)((double) - playerPos.X)), (double)((float)((double) - playerPos.Y)), (double)((float)((double) - playerPos.Z)));
        //                                prog.ProjectionMatrix = capi.Render.CurrentProjectionMatrix;
        //                                prog.ModelViewMatrix = capi.Render.CurrentModelviewMatrix;
        //                                capi.Render.RenderMesh(meshRef);
        //                                capi.Render.GlPopMatrix();
        //                            }

        //                            prog.Stop();
        //                        }
        //                        if (true)
        //                        {
        //                            if (SelectionCube != null)
        //                            {
        //                                capi.Render.UpdateMesh(meshRef, intersectionTest.TransformMesh(SelectionCube2));
        //                            }
        //                            //capi.Render.UpdateMesh(meshRef, intersectionTest.TransformMesh(SelectionCube2));
        //                            ShaderProgramBlockhighlights prog = ShaderPrograms.Blockhighlights;
        //                            prog.Use();
        //                            if (meshRef != null)
        //                            {

        //                                capi.Render.GlPushMatrix();
        //                                capi.Render.GlLoadMatrix(Mat4d.Create());

        //                                //capi.Render.GlLoadMatrix(capi.Render.CameraMatrixOrigin);
        //                                //prog.NightVisonStrength = 5;
        //                                //BlockPos bpos = capi.World.Player.CurrentBlockSelection.Position;
        //                                //capi.Render.GlTranslate(bpos.X, bpos.Y, bpos.Z);
        //                                // capi.Render.GlTranslate((double)((float)((double) - playerPos.X)), (double)((float)((double) - playerPos.Y)), (double)((float)((double) - playerPos.Z)));
        //                                prog.ProjectionMatrix = capi.Render.CurrentProjectionMatrix;
        //                                prog.ModelViewMatrix = capi.Render.CurrentModelviewMatrix;
        //                                capi.Render.RenderMesh(meshRef);
        //                                capi.Render.GlPopMatrix();
        //                            }

        //                            prog.Stop();
        //                        }

        //                    }


        //                }
        //            }

        //        }

        //    }

        //}





        public void Dispose()
        {

            capi.Render.DeleteMesh(meshRef);
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        }
    }
}
