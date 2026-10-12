using RailWorld.src;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace RailWorld
{
    /// <summary>
    /// Малює шпали одного матеріалу (і важелі стрілок: це той самий рендер з іншою моделлю).
    /// Шпали зібрані пачками по чанках своїх секцій, так само як гра малює чанки: матриця кожної шпали
    /// задана відносно кута її чанка і лежить у відеокарті незмінною, а щокадру шейдерові передається
    /// лише одне зміщення на пачку, кут чанка відносно камери. Перебудова чанка зачіпає тільки його пачку.
    /// </summary>
    internal class SleeperRenderer
    {
        // На інстанс: світло (4 числа) і матриця (16 чисел)
        private const int FloatsPerInstance = 20;
        // Найменший буфер пачки, в інстансах: щоб чанк із парою шпал не перезавантажував меш на кожну нову
        private const int MinCapacity = 64;

        private ICoreClientAPI capi;
        private MeshData itemMesh;
        // При оновленні у відеопам'ять дописуються лише дані інстансів: у цьому меші крім них нічого немає
        private MeshData updateMesh = new MeshData(false);
        public int TextureId { get; private set; }

        private class Batch
        {
            public Vec3i Chunk;
            public Dictionary<Vec3d, SleeperInstance> Parts = new Dictionary<Vec3d, SleeperInstance>();
            // Ті самі шпали в тому порядку, в якому вони лежать у буфері
            public List<SleeperInstance> Drawn = new List<SleeperInstance>();
            public CustomMeshDataPartFloat Floats;
            public MeshRef Mesh;
            public bool Dirty = true;
        }

        private Dictionary<Vec3i, Batch> batches = new Dictionary<Vec3i, Batch>();
        // Пачки по порядку, для обходу світла по колу
        private List<Batch> order = new List<Batch>();
        private int lightCursor;

        private float[] tmpMat = Mat4f.Create();
        private float[] scale = new float[] { 1f, 1f, 1f };
        private Vec3f origin = new Vec3f();

        public SleeperRenderer(ICoreClientAPI capi, ItemStack itemStack)
        {
            this.capi = capi;

            if (itemStack?.Item == null) return;

            if (itemStack.Item is IItemCustomMesh customMesh)
            {
                itemMesh = customMesh.GetMeshData(capi, itemStack);
            }
            else
            {
                capi.Tesselator.TesselateItem(itemStack.Item, out itemMesh);
            }

            UseMesh(itemMesh);
        }

        /// <summary>Малює готовий меш: деталь, у якої немає свого предмета.</summary>
        public SleeperRenderer(ICoreClientAPI capi, MeshData mesh)
        {
            this.capi = capi;
            UseMesh(mesh);
        }

        private void UseMesh(MeshData mesh)
        {
            itemMesh = mesh;
            if (itemMesh == null) return;

            // Копія, щоб дані інстансів не потрапили в кешований меш предмета
            itemMesh = itemMesh.Clone();
            TextureId = itemMesh.TextureIds != null && itemMesh.TextureIds.Length > 0
                ? itemMesh.TextureIds[0]
                : capi.BlockTextureAtlas.Positions[0].atlasTextureId;

            float left = float.MaxValue, right = float.MinValue;
            for (int v = 0; v < itemMesh.VerticesCount; v++)
            {
                left = Math.Min(left, itemMesh.xyz[v * 3]);
                right = Math.Max(right, itemMesh.xyz[v * 3]);
            }
            if (right > left) ModelLength = right - left;
        }

        /// <summary>Довжина моделі вздовж її осі X, блоків: довжина шпали, якою її намальовано.</summary>
        public float ModelLength { get; private set; } = 1f;

        /// <summary>
        /// Ставить шпалу. Шпала на тому самому місці в тому самому чанку замінюється: так важіль стрілки
        /// міняє нахил, поки стрілка переводиться.
        /// </summary>
        public void AddRailWayPart(Vec3d position, SleeperInstance data)
        {
            if (itemMesh == null) return;

            if (!batches.TryGetValue(data.chunk, out Batch batch))
            {
                batch = new Batch { Chunk = data.chunk };
                batches[data.chunk] = batch;
                order.Add(batch);
            }

            data.position = position;
            data.lightKey = LightKey(position);
            data.light = Light(position);
            batch.Parts[position] = data;
            batch.Dirty = true;
        }

        // Прибирає шпали секцій, що зберігаються в цьому чанку. Саме за чанком секції, а не за місцем шпали:
        // шпала на самій межі може стояти вже в сусідньому чанку, але належить пачці чанка своєї секції
        public void RemoveChunk(Vec3i chunkCoord)
        {
            if (!batches.TryGetValue(chunkCoord, out Batch batch)) return;

            batch.Mesh?.Dispose();
            batches.Remove(chunkCoord);
            order.Remove(batch);
        }

        // Світло блока, де лежить шпала: rgb це світло від джерел, a сонячне
        private Vec4f Light(Vec3d pos)
        {
            return capi.World.BlockAccessor.GetLightRGBs((int)Math.Floor(pos.X), (int)Math.Floor(pos.Y + 0.25), (int)Math.Floor(pos.Z));
        }

        // Те саме світло одним числом: щоб перевірити, чи воно змінилося, не створюючи об'єкта
        private int LightKey(Vec3d pos)
        {
            return capi.World.BlockAccessor.GetLightRGBsAsInt((int)Math.Floor(pos.X), (int)Math.Floor(pos.Y + 0.25), (int)Math.Floor(pos.Z));
        }

        /// <summary>
        /// Перевіряє, чи змінилося світло шпал, не більш як у budget шпал за раз, по колу: гра не повідомляє
        /// моди про зміну освітлення, тому його доводиться опитувати. Шпалі, чиє світло змінилося, у буфері
        /// переписуються лише чотири числа світла. Повертає, скільки шпал перевірено.
        /// </summary>
        public int UpdateLights(int budget)
        {
            int checkedCount = 0;
            for (int n = 0; n < order.Count && checkedCount < budget; n++)
            {
                if (lightCursor >= order.Count) lightCursor = 0;
                Batch batch = order[lightCursor++];
                checkedCount += batch.Drawn.Count;
                // Пачку однаково буде зібрано заново
                if (batch.Dirty || batch.Mesh == null) continue;

                bool changed = false;
                float[] values = batch.Floats.Values;
                for (int i = 0; i < batch.Drawn.Count; i++)
                {
                    SleeperInstance part = batch.Drawn[i];
                    int key = LightKey(part.position);
                    if (key == part.lightKey) continue;

                    part.lightKey = key;
                    part.light = Light(part.position);
                    int j = i * FloatsPerInstance;
                    values[j] = part.light.R;
                    values[j + 1] = part.light.G;
                    values[j + 2] = part.light.B;
                    values[j + 3] = part.light.A;
                    changed = true;
                }

                if (!changed) continue;
                updateMesh.CustomFloats = batch.Floats;
                capi.Render.UpdateMesh(batch.Mesh, updateMesh);
            }
            return checkedCount;
        }

        /// <summary>
        /// Раз на кадр: збирає заново пачки, в яких щось змінилося. Якщо нічого не змінилося, не робить нічого.
        /// </summary>
        public void PrepareFrame()
        {
            foreach (Batch batch in order)
            {
                if (batch.Dirty) Rebuild(batch);
            }
        }

        // Рахує матриці шпал пачки відносно кута її чанка і кладе їх у відеокарту
        private void Rebuild(Batch batch)
        {
            batch.Dirty = false;
            batch.Drawn.Clear();
            int count = batch.Parts.Count;
            if (count == 0) return;

            // Буфер у відеокарті має розмір, заданий при завантаженні меша, і сам не росте: якщо шпал стало
            // більше, меш завантажується заново з більшим буфером, із запасом удвічі
            bool upload = batch.Mesh == null || batch.Floats.Values.Length < count * FloatsPerInstance;
            if (upload)
            {
                int size = Math.Max(count * 2, MinCapacity) * FloatsPerInstance;
                batch.Floats = new CustomMeshDataPartFloat(size)
                {
                    Instanced = true,
                    InterleaveOffsets = new int[] { 0, 16, 32, 48, 64 },
                    InterleaveSizes = new int[] { 4, 4, 4, 4, 4 },
                    InterleaveStride = 80,
                    StaticDraw = false
                };
                batch.Floats.Values = new float[size];
                batch.Floats.SetAllocationSize(size);
            }

            float[] values = batch.Floats.Values;
            double originX = batch.Chunk.X * 32.0, originY = batch.Chunk.Y * 32.0, originZ = batch.Chunk.Z * 32.0;

            foreach (SleeperInstance part in batch.Parts.Values)
            {
                Mat4f.Identity(tmpMat);
                // rotation: X це ухил, Y поворот, Z крен (нахил полотна)
                Mat4f.RotateY(tmpMat, tmpMat, part.rotation.Y);
                Mat4f.RotateX(tmpMat, tmpMat, -part.rotation.X);
                Mat4f.RotateZ(tmpMat, tmpMat, -part.rotation.Z);
                scale[0] = part.stretch;
                Mat4f.Scale(tmpMat, tmpMat, scale);
                // Модель стоїть серединою в точці (0.5, 0, 0.5) блока: обертаємо й розтягуємо навколо неї
                Mat4f.Translate(tmpMat, tmpMat, -0.5f, 0f, -0.5f);

                // Положення шпали відносно кута чанка, разом зі зсувом, який дала сама матриця
                tmpMat[12] += (float)(part.position.X - originX);
                tmpMat[13] += (float)(part.position.Y - originY);
                tmpMat[14] += (float)(part.position.Z - originZ);

                int j = batch.Drawn.Count * FloatsPerInstance;
                values[j] = part.light.R;
                values[j + 1] = part.light.G;
                values[j + 2] = part.light.B;
                values[j + 3] = part.light.A;
                for (int k = 0; k < 16; k++) values[j + 4 + k] = tmpMat[k];

                batch.Drawn.Add(part);
            }

            batch.Floats.Count = count * FloatsPerInstance;
            if (upload)
            {
                batch.Mesh?.Dispose();
                itemMesh.CustomFloats = batch.Floats;
                batch.Mesh = capi.Render.UploadMesh(itemMesh);
            }
            else
            {
                updateMesh.CustomFloats = batch.Floats;
                capi.Render.UpdateMesh(batch.Mesh, updateMesh);
            }
        }

        /// <summary>
        /// Малює шпали поточним шейдером, пачку за пачкою. Перед кожною передає шейдерові origin: кут чанка
        /// пачки відносно камери. Викликається і для основного проходу, і для тіней.
        /// </summary>
        public void Draw(IShaderProgram prog, Vec3d cameraPos)
        {
            foreach (Batch batch in order)
            {
                if (batch.Mesh == null || batch.Drawn.Count == 0) continue;

                origin.Set((float)(batch.Chunk.X * 32.0 - cameraPos.X), (float)(batch.Chunk.Y * 32.0 - cameraPos.Y), (float)(batch.Chunk.Z * 32.0 - cameraPos.Z));
                prog.Uniform("origin", origin);
                capi.Render.RenderMeshInstanced(batch.Mesh, batch.Drawn.Count);
            }
        }

        public void Dispose()
        {
            foreach (Batch batch in order) batch.Mesh?.Dispose();
            batches.Clear();
            order.Clear();
        }
    }
}
