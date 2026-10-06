using RailWorld.src;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace RailWorld
{
    internal class SleeperRenderer
    {
        private ICoreClientAPI capi;
        private MeshData itemMesh;
        private MeshRef meshref;
        public int TextureId { get; private set; }
        private CustomMeshDataPartFloat matrixAndLightFloats;
        private Dictionary<Vec3d, SleeperInstance> positions = new Dictionary<Vec3d, SleeperInstance>();

        protected float[] tmpMat = Mat4f.Create();
        protected double[] quat = Quaterniond.Create();
        protected Vec3f tmp = new Vec3f();

        public SleeperRenderer(ICoreClientAPI capi, ItemStack itemStack)
        {
            this.capi = capi;

            if (itemStack?.Item == null) return;

            // Отримуємо меш через IItemCustomMesh або стандартний шлях
            if (itemStack.Item is IItemCustomMesh customMesh)
            {
                itemMesh = customMesh.GetMeshData(capi, itemStack);
            }
            else
            {
                // Fallback: tesselate стандартно
                itemMesh = new MeshData(4, 3);
                capi.Tesselator.TesselateItem(itemStack.Item, out itemMesh);
            }

            if (itemMesh == null) return;

            // Копія, щоб instanced CustomFloats не потрапили в кешований меш предмета
            itemMesh = itemMesh.Clone();
            TextureId = itemMesh.TextureIds != null && itemMesh.TextureIds.Length > 0
                ? itemMesh.TextureIds[0]
                : capi.BlockTextureAtlas.Positions[0].atlasTextureId;

            // Налаштовуємо instanced custom floats: 4 (light rgba) + 16 (matrix) = 20 floats на інстанс
            int maxInstances = 10000;
            int floatsPerInstance = 20; // 4 rgba + 16 matrix
            int bufferSize = maxInstances * floatsPerInstance;

            matrixAndLightFloats = new CustomMeshDataPartFloat(bufferSize)
            {
                Instanced = true,
                InterleaveOffsets = new int[] { 0, 16, 32, 48, 64 },
                InterleaveSizes = new int[] { 4, 4, 4, 4, 4 },
                InterleaveStride = 80,
                StaticDraw = false
            };
            matrixAndLightFloats.Values = new float[bufferSize];
            matrixAndLightFloats.SetAllocationSize(bufferSize);
            itemMesh.CustomFloats = matrixAndLightFloats;
            meshref = capi.Render.UploadMesh(itemMesh);
        }

        public void AddRailWayPart(Vec3d position, SleeperInstance data)
        {
            data.light = GetLight(position);
            positions[position] = data;
        }

        private Vec4f GetLight(Vec3d pos)
        {
            return capi.World.BlockAccessor.GetLightRGBs((int)Math.Floor(pos.X), (int)Math.Floor(pos.Y + 0.25), (int)Math.Floor(pos.Z));
        }

        public void UpdateLights()
        {
            foreach (var kvp in positions)
                kvp.Value.light = GetLight(kvp.Key);
        }

        public void RemoveRailWayPart(Vec3d position)
        {
            positions.Remove(position);
        }

        // Прибирає шпали секцій, що зберігаються в цьому чанку. Саме за чанком секції, а не за місцем шпали:
        // шпала на самій межі може стояти вже в сусідньому чанку, і якби прибирати за координатами,
        // оновлення сусіда стирало б її, а повернути було б нікому
        public void RemoveChunk(Vec3i chunkCoord)
        {
            var toRemove = positions.Where(p => p.Value.chunk.Equals(chunkCoord)).Select(p => p.Key).ToList();
            foreach (var key in toRemove)
                positions.Remove(key);
        }

        // Скільки інстансів лежить у буфері після останнього PrepareFrame
        private int preparedCount;

        /// <summary>
        /// Раз на кадр: рахує матриці всіх шпал відносно камери і заливає їх у відеокарту.
        /// </summary>
        public void PrepareFrame()
        {
            preparedCount = 0;
            if (meshref == null || positions.Count == 0) return;

            int count = positions.Count;
            int floatCount = count * 20;

            // Збільшуємо буфер якщо не вистачає
            if (matrixAndLightFloats.Values == null || matrixAndLightFloats.Values.Length < floatCount)
            {
                matrixAndLightFloats.Values = new float[floatCount + 200];
                matrixAndLightFloats.SetAllocationSize(matrixAndLightFloats.Values.Length);
            }

            UpdateCustomFloatBuffer();

            matrixAndLightFloats.Count = floatCount;
            itemMesh.CustomFloats = matrixAndLightFloats;
            capi.Render.UpdateMesh(meshref, itemMesh);
            preparedCount = count;
        }

        /// <summary>
        /// Малює підготовлені шпали поточним шейдером. Викликається і для основного проходу, і для тіней.
        /// </summary>
        public void Draw()
        {
            if (meshref == null || preparedCount == 0) return;
            capi.Render.RenderMeshInstanced(meshref, preparedCount);
        }

        private void UpdateCustomFloatBuffer()
        {
            Vec3d camPos = capi.World.Player.Entity.CameraPos;
            int i = 0;
            foreach (var kvp in positions)
            {
                Vec3d pos = kvp.Key;
                SleeperInstance data = kvp.Value;

                tmp.Set(
                    (float)(pos.X - camPos.X),
                    (float)(pos.Y - camPos.Y),
                    (float)(pos.Z - camPos.Z)
                );

                UpdateLightAndTransformMatrix(i, tmp, data);
                i++;
            }
        }

        private void UpdateLightAndTransformMatrix(int index, Vec3f distToCamera, SleeperInstance data)
        {
            Mat4f.Identity(tmpMat);
            Mat4f.Translate(tmpMat, tmpMat, distToCamera.X, distToCamera.Y, distToCamera.Z);
            // rotation: X = ухил, Y = поворот, Z = крен (нахил полотна)
            Mat4f.RotateY(tmpMat, tmpMat, data.rotation.Y);
            Mat4f.RotateX(tmpMat, tmpMat, -data.rotation.X);
            Mat4f.RotateZ(tmpMat, tmpMat, -data.rotation.Z);
            Mat4f.Scale(tmpMat, tmpMat, new float[] { data.length, 1f, 1f });
            // Меш шпали займає блок 0..1, обертаємо навколо його центру
            Mat4f.Translate(tmpMat, tmpMat, -0.5f, 0f, -0.5f);

            int j = index * 20;
            float[] values = matrixAndLightFloats.Values;
            // Світло блока, де лежить шпала: rgb = світло від джерел, a = сонячне
            values[j]     = data.light.R;
            values[j + 1] = data.light.G;
            values[j + 2] = data.light.B;
            values[j + 3] = data.light.A;
            // Matrix (16 floats)
            for (int k = 0; k < 16; k++)
                values[j + 4 + k] = tmpMat[k];
        }

        public void Dispose()
        {
            meshref?.Dispose();
        }
    }
}
