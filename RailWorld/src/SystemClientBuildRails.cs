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
        float size = 2.5f; 
        ICoreClientAPI cAPI;
       
        public SystemClientBuildRails(ICoreClientAPI capi) 
        {
            cAPI = capi;
            Initialize();
            cAPI.Event.RegisterRenderer(this, EnumRenderStage.OIT);
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
            if (cAPI.Side == EnumAppSide.Client)
            {
                SelectionCube = new MeshData(24, 36, false, false, true, false);

                int color = ColorUtil.ToRgba(96, (int)(GuiStyle.DialogDefaultBgColor[2] * 255.0), 
                                                 (int)(GuiStyle.DialogDefaultBgColor[1] * 255.0), 
                                                 (int)(GuiStyle.DialogDefaultBgColor[0] * 255.0));
               
                Vec3f centerPos = new Vec3f(size / 2f, size / 2f, size / 2f);
                Vec3f cubeSize = new Vec3f(size, size, size);
                float[] shadings = CubeMeshUtil.DefaultBlockSideShadingsByFacing;
                for (int k = 0; k < 6; k++)
                {
                    BlockFacing face = BlockFacing.ALLFACES[k];
                    ModelCubeUtilExt.AddFace(SelectionCube, face, centerPos, cubeSize, color, shadings[face.Index]);
                }
                SelectionCube.Translate(new Vec3f(-size / 2f, -size / 2f, -size / 2f));
                meshRef = cAPI.Render.UploadMesh(SelectionCube.Clone());
                
            }
        }


        void IRenderer.OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if(cAPI.World.Player != null) 
            {
                if (cAPI.World.Player.CurrentBlockSelection != null)
                {

                    cAPI.Render.UpdateMesh(meshRef, SelectionCube.RotateD(0.08f, -0.04f, 0.08f));
                    Vec3d offset = cAPI.World.Player.CurrentBlockSelection.FullPosition;
                    //  MeshData md = SelectionCube.Clone().Translate(offset.ToVec3f());
                    // cAPI.Render.UpdateMesh(meshRef, md);

                    ShaderProgramBlockhighlights prog = ShaderPrograms.Blockhighlights;
                    prog.Use();
                    Vec3d playerPos = cAPI.World.Player.Entity.CameraPos;
                    
                   
                    if (meshRef != null)
                    {
     
                        cAPI.Render.GlPushMatrix();
                        cAPI.Render.GlLoadMatrix(cAPI.Render.CameraMatrixOrigin);

                        cAPI.Render.GlTranslate((double)((float)((double)offset.X - playerPos.X)), (double)((float)((double)offset.Y - playerPos.Y)), (double)((float)((double)offset.Z - playerPos.Z)));
                        prog.ProjectionMatrix = cAPI.Render.CurrentProjectionMatrix;
                        prog.ModelViewMatrix = cAPI.Render.CurrentModelviewMatrix;
                        cAPI.Render.RenderMesh(meshRef);
                        cAPI.Render.GlPopMatrix();
                    }
                    
                    prog.Stop();
                    
                }
                
            }
            
        }

        public void Dispose()
        {

            cAPI.Render.DeleteMesh(meshRef);
            cAPI.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        }
    }
}
