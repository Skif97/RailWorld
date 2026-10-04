using System;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using OpenTK.Graphics.OpenGL;

namespace RailWorld
{
    /// <summary>
    /// Допоміжний клас для instanced рендеру MultiTextureMeshRef через VAO напряму.
    /// </summary>
    public static class MyRender
    {
        public static void RenderMultiTextureMeshInstanced(MultiTextureMeshRef mmr, int quantity = 1)
        {
            if (mmr?.meshrefs == null) return;

            for (int m = 0; m < mmr.meshrefs.Length; m++)
            {
                VAO vao = mmr.meshrefs[m] as VAO;
                if (vao == null) continue;

                RuntimeStats.drawCallsCount++;
                GL.BindVertexArray(vao.VaoId);
                for (int i = 0; i < vao.vaoSlotNumber; i++)
                    GL.EnableVertexAttribArray(i);

                GL.BindBuffer(BufferTarget.ElementArrayBuffer, vao.vboIdIndex);
                GL.DrawElementsInstanced(
                    vao.drawMode,
                    vao.IndicesCount,
                    DrawElementsType.UnsignedInt,
                    IntPtr.Zero,
                    quantity);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);

                for (int j = 0; j < vao.vaoSlotNumber; j++)
                    GL.DisableVertexAttribArray(j);

                GL.BindVertexArray(0);
            }
        }
    }
}
