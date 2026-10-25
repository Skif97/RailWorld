using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using RailWorld.src;

namespace RailWorld
{
    /// <summary>
    /// Деталь колії, що малюється інстансом: де стоїть, як повернута і яке на ній світло. Що потрібно понад
    /// це (розтяг, частина текстури), додає підклас.
    /// </summary>
    internal class InstancedPart
    {
        // Де деталь стоїть у світі
        public Vec3d Position;
        // X ухил, Y поворот, Z крен (нахил полотна), радіани
        public Vec3f Rotation;
        // Світло блока, де стоїть деталь: rgb це світло від джерел, a сонячне
        public Vec4f Light;
        // Те саме світло одним числом: за ним видно, чи воно змінилося
        public int LightKey;
    }

    /// <summary>
    /// Те, що система рендеру колії робить з кожним рендером деталей однаково, якого б типу не були деталі.
    /// </summary>
    internal interface IInstancedParts : IDisposable
    {
        int TextureId { get; }
        void ClearChunk(Vec3i chunkCoord);
        void RemoveChunk(Vec3i chunkCoord);
        int UpdateLights(int budget, LightProbe probe);
        void PrepareFrame();
        void Draw(IShaderProgram prog, Vec3d cameraPos, bool shadow);
    }

    /// <summary>
    /// Малює однакові деталі колії однією моделлю через instanced rendering.
    /// Деталі зібрані пачками по чанках своїх секцій, так само як гра малює чанки: матриця кожної деталі
    /// задана відносно кута її чанка і лежить у відеокарті незмінною, а щокадру шейдерові передається
    /// лише одне зміщення на пачку, кут чанка відносно камери. Перебудова чанка зачіпає тільки його пачку.
    /// Кожна деталь має ключ: деталь з тим самим ключем у тому самому чанку замінює попередню.
    /// Сам по собі ставить модель як є: повернутою і серединою (0.5, 0, 0.5) у точку деталі. Підклас
    /// перевизначає Transform, якщо модель треба розтягнути чи поставити інакше, і ExtraFloats з WriteExtra,
    /// якщо шейдерові потрібні ще числа на інстанс.
    /// </summary>
    internal class InstancedPartRenderer<TKey, TPart> : IInstancedParts where TPart : InstancedPart
    {
        // Дані інстанса в буфері, по порядку: світло (location 4 у railinstanced.vsh), матриця (location 5-8),
        // далі числа підкласу (location 9). Location 10 зайнятий даними вершин рейки, тож чисел підкласу не
        // більше чотирьох
        private const int LightFloats = 4;
        private const int MatrixFloats = 16;
        private const int MaxExtraFloats = 4;

        protected readonly ICoreClientAPI capi;
        // При оновленні у відеопам'ять дописуються лише дані інстансів: у цьому меші крім них нічого немає
        private MeshData updateMesh = new MeshData(false);

        /// <summary>Модель, яку малюють інстанси. null, якщо її немає: тоді рендер нічого не малює.</summary>
        protected MeshData Mesh { get; private set; }
        public int TextureId { get; private set; }

        private class Batch
        {
            public Vec3i Chunk;
            public Dictionary<TKey, TPart> Parts = new Dictionary<TKey, TPart>();
            // Ті самі деталі в тому порядку, в якому вони лежать у буфері
            public List<TPart> Drawn = new List<TPart>();
            public CustomMeshDataPartFloat Floats;
            public MeshRef Mesh;
            // У пачці щось змінилося, і вона чекає в dirty на перебудову
            public bool Dirty;
        }

        private Dictionary<Vec3i, Batch> batches = new Dictionary<Vec3i, Batch>();
        // Пачки по порядку, для обходу світла по колу
        private List<Batch> order = new List<Batch>();
        // Пачки, у яких щось змінилося: їх і лише їх PrepareFrame збирає заново
        private readonly List<Batch> dirty = new List<Batch>();

        private void MarkDirty(Batch batch)
        {
            if (batch.Dirty) return;
            batch.Dirty = true;
            dirty.Add(batch);
        }
        private int lightCursor;

        private float[] tmpMat = Mat4f.Create();
        private Vec3f origin = new Vec3f();

        public InstancedPartRenderer(ICoreClientAPI capi, MeshData mesh)
        {
            this.capi = capi;
            if (mesh == null) return;

            // Копія, щоб дані інстансів не потрапили в кешований меш предмета
            Mesh = mesh.Clone();
            TextureId = Mesh.TextureIds != null && Mesh.TextureIds.Length > 0
                ? Mesh.TextureIds[0]
                : capi.BlockTextureAtlas.Positions[0].atlasTextureId;
        }

        /// <summary>
        /// Малює частину готового меша, текстури якої лежать на сторінці атласу textureId (MeshData.SplitByTextureId):
        /// у такої частини перелік сторінок лишається від цілого меша, тож свою сторінку їй кажуть.
        /// </summary>
        public InstancedPartRenderer(ICoreClientAPI capi, MeshData mesh, int textureId) : this(capi, mesh)
        {
            TextureId = textureId;
        }

        /// <summary>Меш предмета зі стека, яким його малює гра. null, якщо предмета немає.</summary>
        public static MeshData ItemMesh(ICoreClientAPI capi, ItemStack itemStack)
        {
            if (itemStack?.Item == null) return null;

            if (itemStack.Item is IItemCustomMesh customMesh) return customMesh.GetMeshData(capi, itemStack);
            capi.Tesselator.TesselateItem(itemStack.Item, out MeshData mesh);
            return mesh;
        }

        /// <summary>Найменший буфер пачки, в інстансах: щоб чанк із парою деталей не перезавантажував меш на кожну нову.</summary>
        protected virtual int MinCapacity => 64;

        /// <summary>Скільки чисел на інстанс підклас додає після світла й матриці, не більше чотирьох.</summary>
        protected virtual int ExtraFloats => 0;

        /// <summary>Пише числа підкласу деталі part у values, починаючи з at.</summary>
        protected virtual void WriteExtra(TPart part, float[] values, int at)
        {
        }

        /// <summary>
        /// Записує в matrix, як модель стає на місце деталі, без самого місця: його додає рендер, відносно
        /// кута чанка. Звичайно це поворот навколо середини моделі (0.5, 0, 0.5).
        /// </summary>
        protected virtual void Transform(TPart part, float[] matrix)
        {
            SetRotation(matrix, part.Rotation);
            Mat4f.Translate(matrix, matrix, -0.5f, 0f, -0.5f);
        }

        /// <summary>Записує в matrix поворот деталі: спершу поворот, тоді ухил, тоді крен.</summary>
        protected static void SetRotation(float[] matrix, Vec3f rotation)
        {
            Mat4f.Identity(matrix);
            Mat4f.RotateY(matrix, matrix, rotation.Y);
            Mat4f.RotateX(matrix, matrix, -rotation.X);
            Mat4f.RotateZ(matrix, matrix, -rotation.Z);
        }

        /// <summary>
        /// Ставить деталь у пачку чанка chunkCoord: того, у даних якого лежить її секція, а не обов'язково того,
        /// де стоїть сама деталь. Деталь з тим самим ключем у цьому чанку замінюється.
        /// </summary>
        public void Add(Vec3i chunkCoord, TKey key, TPart part)
        {
            if (Mesh == null || part == null) return;

            if (!batches.TryGetValue(chunkCoord, out Batch batch))
            {
                batch = new Batch { Chunk = chunkCoord.Clone() };
                batches[batch.Chunk] = batch;
                order.Add(batch);
            }

            part.LightKey = TrackLight.KeyOf(capi.World.BlockAccessor, part.Position);
            part.Light = TrackLight.Of(capi.World.BlockAccessor, part.Position);
            batch.Parts[key] = part;
            MarkDirty(batch);
        }

        /// <summary>Прибирає деталь з ключем key із пачки чанка chunkCoord.</summary>
        public void Remove(Vec3i chunkCoord, TKey key)
        {
            if (batches.TryGetValue(chunkCoord, out Batch batch) && batch.Parts.Remove(key)) MarkDirty(batch);
        }

        // Спорожнює пачку чанка перед тим, як його колію покладуть у неї знову. Буфер у відеокарті лишається:
        // якщо деталей не побільшає, нові ляжуть у той самий
        public void ClearChunk(Vec3i chunkCoord)
        {
            if (!batches.TryGetValue(chunkCoord, out Batch batch)) return;

            batch.Parts.Clear();
            MarkDirty(batch);
        }

        // Прибирає деталі секцій, що зберігаються в цьому чанку, разом з буфером
        public void RemoveChunk(Vec3i chunkCoord)
        {
            if (!batches.TryGetValue(chunkCoord, out Batch batch)) return;

            batch.Mesh?.Dispose();
            batches.Remove(chunkCoord);
            order.Remove(batch);
            if (batch.Dirty) dirty.Remove(batch);
        }

        /// <summary>
        /// Перевіряє, чи змінилося світло деталей, не більш як у budget деталей за раз, по колу: гра не повідомляє
        /// моди про зміну освітлення, тому його доводиться опитувати. Деталі, чиє світло змінилося, у буфері
        /// переписуються лише чотири числа світла. Повертає, скільки деталей перевірено.
        /// </summary>
        public int UpdateLights(int budget, LightProbe probe)
        {
            int checkedCount = 0;
            int floats = FloatsPerInstance;
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
                    TPart part = batch.Drawn[i];
                    int key = probe.KeyOf(part.Position);
                    if (key == part.LightKey) continue;

                    part.LightKey = key;
                    part.Light = probe.Of(part.Position);
                    WriteLight(part, values, i * floats);
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
            if (dirty.Count == 0) return;
            foreach (Batch batch in dirty) Rebuild(batch);
            dirty.Clear();
        }

        private int FloatsPerInstance => LightFloats + MatrixFloats + ExtraFloats;

        // Рахує матриці деталей пачки відносно кута її чанка і кладе їх у відеокарту
        private void Rebuild(Batch batch)
        {
            batch.Dirty = false;
            batch.Drawn.Clear();
            int count = batch.Parts.Count;
            if (count == 0) return;

            // Буфер у відеокарті має розмір, заданий при завантаженні меша, і сам не росте: якщо деталей стало
            // більше, меш завантажується заново з більшим буфером, із запасом удвічі
            int floats = FloatsPerInstance;
            bool upload = batch.Mesh == null || batch.Floats.Values.Length < count * floats;
            if (upload) batch.Floats = NewFloats(Math.Max(count * 2, MinCapacity));

            float[] values = batch.Floats.Values;
            double originX = batch.Chunk.X * GlobalConstants.ChunkSize, originY = batch.Chunk.Y * GlobalConstants.ChunkSize, originZ = batch.Chunk.Z * GlobalConstants.ChunkSize;

            foreach (TPart part in batch.Parts.Values)
            {
                Transform(part, tmpMat);
                // Місце деталі відносно кута чанка, разом зі зсувом, який дала сама матриця
                tmpMat[12] += (float)(part.Position.X - originX);
                tmpMat[13] += (float)(part.Position.Y - originY);
                tmpMat[14] += (float)(part.Position.Z - originZ);

                int j = batch.Drawn.Count * floats;
                WriteLight(part, values, j);
                for (int k = 0; k < MatrixFloats; k++) values[j + LightFloats + k] = tmpMat[k];
                WriteExtra(part, values, j + LightFloats + MatrixFloats);

                batch.Drawn.Add(part);
            }

            batch.Floats.Count = count * floats;
            if (upload)
            {
                batch.Mesh?.Dispose();
                Mesh.CustomFloats = batch.Floats;
                batch.Mesh = capi.Render.UploadMesh(Mesh);
            }
            else
            {
                updateMesh.CustomFloats = batch.Floats;
                capi.Render.UpdateMesh(batch.Mesh, updateMesh);
            }
        }

        private static void WriteLight(TPart part, float[] values, int at)
        {
            values[at] = part.Light.R;
            values[at + 1] = part.Light.G;
            values[at + 2] = part.Light.B;
            values[at + 3] = part.Light.A;
        }

        // Буфер даних інстансів на capacity деталей. Шейдер бере дані шматками до чотирьох чисел: світло, чотири
        // стовпці матриці і, якщо є, числа підкласу
        private CustomMeshDataPartFloat NewFloats(int capacity)
        {
            int extra = ExtraFloats;
            if (extra > MaxExtraFloats) throw new InvalidOperationException(GetType().Name + ": більше " + MaxExtraFloats + " чисел підкласу на інстанс шейдер не приймає");

            int groups = (LightFloats + MatrixFloats) / 4 + (extra > 0 ? 1 : 0);
            var offsets = new int[groups];
            var sizes = new int[groups];
            for (int g = 0; g < groups; g++)
            {
                offsets[g] = g * 4 * sizeof(float);
                sizes[g] = 4;
            }
            if (extra > 0) sizes[groups - 1] = extra;

            int size = capacity * FloatsPerInstance;
            var floats = new CustomMeshDataPartFloat(size)
            {
                Instanced = true,
                InterleaveOffsets = offsets,
                InterleaveSizes = sizes,
                InterleaveStride = FloatsPerInstance * sizeof(float),
                StaticDraw = false
            };
            floats.SetAllocationSize(size);
            return floats;
        }

        /// <summary>
        /// Малює деталі поточним шейдером, пачку за пачкою. Перед кожною передає шейдерові origin: кут чанка
        /// пачки відносно камери. Викликається і для основного проходу, і для тіней (shadow); пачки, чанка яких
        /// у цьому проході не видно, не малюються (ChunkCulling).
        /// </summary>
        public void Draw(IShaderProgram prog, Vec3d cameraPos, bool shadow)
        {
            foreach (Batch batch in order)
            {
                if (batch.Mesh == null || batch.Drawn.Count == 0) continue;
                if (!ChunkCulling.Visible(capi, batch.Chunk.X, batch.Chunk.Y, batch.Chunk.Z, shadow)) continue;

                origin.Set((float)(batch.Chunk.X * GlobalConstants.ChunkSize - cameraPos.X), (float)(batch.Chunk.Y * GlobalConstants.ChunkSize - cameraPos.Y), (float)(batch.Chunk.Z * GlobalConstants.ChunkSize - cameraPos.Z));
                prog.Uniform("origin", origin);
                capi.Render.RenderMeshInstanced(batch.Mesh, batch.Drawn.Count);
            }
        }

        public void Dispose()
        {
            foreach (Batch batch in order) batch.Mesh?.Dispose();
            batches.Clear();
            order.Clear();
            dirty.Clear();
        }
    }
}
