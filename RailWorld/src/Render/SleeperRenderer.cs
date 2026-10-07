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

        // Матриця шпали складається з повороту з розтягом, які не міняються, і положення відносно камери,
        // яке міняється щокадру. Перше рахується один раз, коли набір шпал чи їхнє світло змінилися,
        // і лежить у буфері; щокадру переписуються лише три числа положення на шпалу.
        // layoutDirty каже, що нерухому частину треба порахувати заново
        private bool layoutDirty = true;
        // Положення кожної шпали у світі, разом зі зсувом, який дає сама матриця: по три числа на шпалу,
        // у тому самому порядку, що й у буфері
        private double[] basePositions = new double[0];
        private float[] scale = new float[] { 1f, 1f, 1f };

        public void AddRailWayPart(Vec3d position, SleeperInstance data)
        {
            data.light = GetLight(position);
            positions[position] = data;
            layoutDirty = true;
        }

        private Vec4f GetLight(Vec3d pos)
        {
            return capi.World.BlockAccessor.GetLightRGBs((int)Math.Floor(pos.X), (int)Math.Floor(pos.Y + 0.25), (int)Math.Floor(pos.Z));
        }

        public void UpdateLights()
        {
            foreach (var kvp in positions)
                kvp.Value.light = GetLight(kvp.Key);
            layoutDirty = true;
        }

        public void RemoveRailWayPart(Vec3d position)
        {
            positions.Remove(position);
            layoutDirty = true;
        }

        // Прибирає шпали секцій, що зберігаються в цьому чанку. Саме за чанком секції, а не за місцем шпали:
        // шпала на самій межі може стояти вже в сусідньому чанку, і якби прибирати за координатами,
        // оновлення сусіда стирало б її, а повернути було б нікому
        public void RemoveChunk(Vec3i chunkCoord)
        {
            var toRemove = positions.Where(p => p.Value.chunk.Equals(chunkCoord)).Select(p => p.Key).ToList();
            foreach (var key in toRemove)
                positions.Remove(key);
            if (toRemove.Count > 0) layoutDirty = true;
        }

        // Скільки інстансів лежить у буфері після останнього PrepareFrame
        private int preparedCount;

        // Буфер матриць у відеокарті має розмір, заданий при завантаженні меша, і сам не росте.
        // Якщо інстансів стало більше, ніж у нього вміщається, оновлення мовчки відкидається відеокартою,
        // у буфері лишаються старі матриці, пораховані відносно старого положення камери, і вся колія
        // починає їздити за камерою. Тому меш завантажується заново з більшим буфером, із запасом удвічі
        private void Grow(int instancesNeeded)
        {
            int size = Math.Max(instancesNeeded * 2, 1024) * 20;
            matrixAndLightFloats = new CustomMeshDataPartFloat(size)
            {
                Instanced = true,
                InterleaveOffsets = new int[] { 0, 16, 32, 48, 64 },
                InterleaveSizes = new int[] { 4, 4, 4, 4, 4 },
                InterleaveStride = 80,
                StaticDraw = false
            };
            matrixAndLightFloats.Values = new float[size];
            matrixAndLightFloats.SetAllocationSize(size);
            itemMesh.CustomFloats = matrixAndLightFloats;

            meshref?.Dispose();
            meshref = capi.Render.UploadMesh(itemMesh);
            layoutDirty = true;
        }

        /// <summary>
        /// Раз на кадр: рахує матриці всіх шпал відносно камери і заливає їх у відеокарту.
        /// </summary>
        public void PrepareFrame()
        {
            preparedCount = 0;
            if (meshref == null || positions.Count == 0) return;

            int count = positions.Count;
            int floatCount = count * 20;

            if (matrixAndLightFloats.Values == null || matrixAndLightFloats.Values.Length < floatCount) Grow(count);
            if (meshref == null) return;

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
            if (layoutDirty)
            {
                RebuildLayout();
                layoutDirty = false;
            }

            // Щокадру міняється лише положення відносно камери: три числа в кожній матриці
            Vec3d camPos = capi.World.Player.Entity.CameraPos;
            float[] values = matrixAndLightFloats.Values;
            int count = positions.Count;

            for (int i = 0; i < count; i++)
            {
                int j = i * 20 + 16;
                int b = i * 3;
                values[j] = (float)(basePositions[b] - camPos.X);
                values[j + 1] = (float)(basePositions[b + 1] - camPos.Y);
                values[j + 2] = (float)(basePositions[b + 2] - camPos.Z);
            }
        }

        // Рахує нерухому частину матриць і світло всіх шпал і кладе їх у буфер
        private void RebuildLayout()
        {
            int count = positions.Count;
            if (basePositions.Length < count * 3) basePositions = new double[count * 3 * 2];
            float[] values = matrixAndLightFloats.Values;

            int i = 0;
            foreach (var kvp in positions)
            {
                Vec3d pos = kvp.Key;
                SleeperInstance data = kvp.Value;

                Mat4f.Identity(tmpMat);
                // rotation: X = ухил, Y = поворот, Z = крен (нахил полотна)
                Mat4f.RotateY(tmpMat, tmpMat, data.rotation.Y);
                Mat4f.RotateX(tmpMat, tmpMat, -data.rotation.X);
                Mat4f.RotateZ(tmpMat, tmpMat, -data.rotation.Z);
                scale[0] = data.length;
                Mat4f.Scale(tmpMat, tmpMat, scale);
                // Меш шпали займає блок 0..1, обертаємо навколо його центру
                Mat4f.Translate(tmpMat, tmpMat, -0.5f, 0f, -0.5f);

                int j = i * 20;
                // Світло блока, де лежить шпала: rgb = світло від джерел, a = сонячне
                values[j]     = data.light.R;
                values[j + 1] = data.light.G;
                values[j + 2] = data.light.B;
                values[j + 3] = data.light.A;
                for (int k = 0; k < 16; k++)
                    values[j + 4 + k] = tmpMat[k];

                // Зсув, який дала сама матриця (перенесення меша на вісь), додається до положення шпали
                basePositions[i * 3] = pos.X + tmpMat[12];
                basePositions[i * 3 + 1] = pos.Y + tmpMat[13];
                basePositions[i * 3 + 2] = pos.Z + tmpMat[14];
                i++;
            }
        }

        public void Dispose()
        {
            meshref?.Dispose();
        }
    }
}
