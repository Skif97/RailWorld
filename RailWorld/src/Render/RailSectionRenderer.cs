using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Config;
using RailWorld.src;
using RailWorld.src.RailWay;

namespace RailWorld
{
    internal class RailInstanceData
    {
        public Vec3d Position;
        public float Yaw;
        public float Pitch;
        public float Roll;
        public Vec4f Light;
        // Те саме світло одним числом: за ним видно, чи воно змінилося
        public int LightKey;
        public float ScaleZ;
    }

    /// <summary>
    /// Рендерить рейки одного матеріалу через instanced rendering.
    /// Рейка секції малюється двома прямими шматками, від початку секції до середини і від середини до кінця:
    /// так вона ближче до кривої, і злам на стиках удвічі менший. Один інстанс на шматок.
    /// Меш рейки масштабується по Z матрицею трансформації.
    /// Шматки зібрані пачками по чанках своїх секцій, так само як гра малює чанки: матриця кожного задана
    /// відносно кута його чанка і лежить у відеокарті незмінною, а щокадру шейдерові передається лише одне
    /// зміщення на пачку, кут чанка відносно камери.
    /// </summary>
    internal class RailSectionRenderer
    {
        // На інстанс: світло (4 числа) і матриця (16 чисел)
        private const int FloatsPerInstance = 20;
        // Найменший буфер пачки, в інстансах: щоб чанк із парою рейок не перезавантажував меш на кожну нову
        private const int MinCapacity = 128;

        private ICoreClientAPI capi;
        private MeshData itemMesh;
        // При оновленні у відеопам'ять дописуються лише дані інстансів: у цьому меші крім них нічого немає
        private MeshData updateMesh = new MeshData(false);
        public int TextureId { get; private set; }

        // Шматки рейок одного чанка: чанк перебудовується цілком, і прибрати його рейки це викинути пачку
        private class Batch
        {
            public Vec3i Chunk;
            public List<RailInstanceData> Pieces = new List<RailInstanceData>();
            public CustomMeshDataPartFloat Floats;
            public MeshRef Mesh;
            // Скільки шматків лежить у буфері
            public int Drawn;
            public bool Dirty = true;
        }

        private Dictionary<Vec3i, Batch> batches = new Dictionary<Vec3i, Batch>();
        // Пачки по порядку, для обходу світла по колу
        private List<Batch> order = new List<Batch>();
        private int lightCursor;
        private Vec3f origin = new Vec3f();

        // Габарити перерізу рейки, беруться з меша: висота над підошвою і півширина від осі
        private double railHeight;
        // Довжина моделі рейки, блоків: шматок, яким рейку намальовано
        private double railLength;
        private double railHalfWidth;

        private float[] tmpMat = Mat4f.Create();

        /// <summary>
        /// Меш рейки як є, без даних інстансів: з нього збираються гостряки стрілок.
        /// </summary>
        public MeshData TemplateMesh { get; private set; }

        /// <summary>
        /// Скільки варіантів рейки з тією самою моделлю, але різним місцем текстури вздовж неї. Модель рейки це
        /// шматок у чверть блока, і вздовж він бере чверть текстури; якби всі шматки брали ту саму чверть,
        /// малюнок повторювався б на кожному. Сусідні шматки беруть варіанти по черзі.
        /// </summary>
        public const int Variants = 4;

        /// <param name="variant">Котра за рахунком частина текстури вздовж рейки лягає на цей варіант, з нуля.</param>
        public RailSectionRenderer(ICoreClientAPI capi, ItemStack itemStack, int variant = 0)
        {
            this.capi = capi;

            if (itemStack?.Item == null) return;

            if (itemStack.Item is IItemCustomMesh customMesh)
                itemMesh = customMesh.GetMeshData(capi, itemStack);
            else
                capi.Tesselator.TesselateItem(itemStack.Item, out itemMesh);

            if (itemMesh == null) return;

            // Копія, щоб instanced CustomFloats не потрапили в кешований меш предмета
            itemMesh = itemMesh.Clone();

            TemplateMesh = itemMesh.Clone();
            ShiftTexture(itemMesh, variant);

            TextureId = itemMesh.TextureIds != null && itemMesh.TextureIds.Length > 0
                ? itemMesh.TextureIds[0]
                : capi.BlockTextureAtlas.Positions[0].atlasTextureId;

            for (int v = 0; v < itemMesh.VerticesCount; v++)
            {
                railHalfWidth = Math.Max(railHalfWidth, Math.Abs(itemMesh.xyz[v * 3] - 0.5));
                railHeight    = Math.Max(railHeight, itemMesh.xyz[v * 3 + 1]);
                railLength    = Math.Max(railLength, itemMesh.xyz[v * 3 + 2]);
            }
        }

        // Зсуває текстуру вздовж рейки на variant її частин. Яка вісь текстури йде вздовж рейки, у кожної грані
        // своя: на боках одна, зверху інша, тож це видно лише з самої грані. Торці не чіпаються. Зсув не може
        // вивести грань за межі текстури: в атласі поруч лежить чужа
        private static void ShiftTexture(MeshData mesh, int variant)
        {
            if (variant <= 0 || mesh.VerticesCount == 0) return;

            // Межі текстури в атласі невідомі, відомі лише координати, які модель з неї бере. Початок текстури
            // це найменші з них, розмір найбільші, округлені вгору до цілого числа частин
            float[] low = { float.MaxValue, float.MaxValue }, high = { float.MinValue, float.MinValue };
            for (int v = 0; v < mesh.VerticesCount; v++)
            {
                for (int axis = 0; axis < 2; axis++)
                {
                    low[axis] = Math.Min(low[axis], mesh.Uv[v * 2 + axis]);
                    high[axis] = Math.Max(high[axis], mesh.Uv[v * 2 + axis]);
                }
            }

            // Грань це чотири вершини підряд
            for (int face = 0; face + 3 < mesh.VerticesCount; face += 4)
            {
                float zLow = float.MaxValue, zHigh = float.MinValue;
                for (int n = 0; n < 4; n++)
                {
                    float z = mesh.xyz[(face + n) * 3 + 2];
                    zLow = Math.Min(zLow, z);
                    zHigh = Math.Max(zHigh, z);
                }
                // Торець: уздовж рейки він не тягнеться
                if (zHigh - zLow < 1e-5) continue;

                // Вісь текстури, що міняється разом з місцем уздовж рейки
                float zMiddle = (zLow + zHigh) / 2;
                int along = -1;
                float span = 0, start = 0;
                for (int axis = 0; axis < 2; axis++)
                {
                    float atStart = 0, atEnd = 0, least = float.MaxValue;
                    int startCount = 0, endCount = 0;
                    for (int n = 0; n < 4; n++)
                    {
                        float uv = mesh.Uv[(face + n) * 2 + axis];
                        least = Math.Min(least, uv);
                        if (mesh.xyz[(face + n) * 3 + 2] < zMiddle) { atStart += uv; startCount++; }
                        else { atEnd += uv; endCount++; }
                    }
                    if (startCount == 0 || endCount == 0) continue;

                    float change = Math.Abs(atEnd / endCount - atStart / startCount);
                    if (change <= span) continue;
                    span = change;
                    start = least;
                    along = axis;
                }
                if (along < 0 || span < 1e-7) continue;

                // Скільки таких частин уміщається в текстуру від місця цієї грані до її кінця
                int total = (int)Math.Ceiling((high[along] - low[along]) / span - 1e-3);
                int room = total - (int)Math.Round((start - low[along]) / span);
                if (room <= 1) continue;

                float shift = variant % room * span;
                for (int n = 0; n < 4; n++) mesh.Uv[(face + n) * 2 + along] += shift;
            }
        }

        /// <summary>Ставить один з двох шматків рейки секції: piece 0 від початку до середини, 1 від середини до кінця.</summary>
        public void AddRailPiece(Vec3i chunkCoord, Section section, bool left, int piece)
        {
            float offset = section.TrackWidth / 2f * (left ? 1f : -1f);
            Vec3d center = section.GetGlobalPos();

            if (piece == 0) AddPiece(chunkCoord, BuildInstance(section.FullStartPosition, section.StartNormal, section.StartTangent, center, section.CenterNormal, section.CenterTangent, offset));
            else AddPiece(chunkCoord, BuildInstance(center, section.CenterNormal, section.CenterTangent, section.FullEndPosition, section.EndNormal, section.EndTangent, offset));
        }

        private void AddPiece(Vec3i chunkCoord, RailInstanceData piece)
        {
            // Шматок нульової довжини: середина секції збіглася з її краєм
            if (piece == null || itemMesh == null) return;

            if (!batches.TryGetValue(chunkCoord, out Batch batch))
            {
                batch = new Batch { Chunk = chunkCoord.Clone() };
                batches[batch.Chunk] = batch;
                order.Add(batch);
            }
            batch.Pieces.Add(piece);
            batch.Dirty = true;
        }

        private float[] scale = new float[] { 1f, 1f, 1f };

        // Спорожнює пачку чанка перед тим, як його рейки покладуть у неї знову. Буфер у відеокарті лишається:
        // якщо шматків не побільшає, нові ляжуть у той самий
        public void ClearChunk(Vec3i chunkCoord)
        {
            if (!batches.TryGetValue(chunkCoord, out Batch batch)) return;

            batch.Pieces.Clear();
            batch.Dirty = true;
        }

        public void RemoveChunk(Vec3i chunkCoord)
        {
            if (!batches.TryGetValue(chunkCoord, out Batch batch)) return;

            batch.Mesh?.Dispose();
            batches.Remove(chunkCoord);
            order.Remove(batch);
        }

        // Шматок рейки між двома точками осі колії. offset це зсув рейки вбік від осі: пів ширини колії
        // в один чи другий бік
        private RailInstanceData BuildInstance(Vec3d from, Vec3f fromNormal, Vec3f fromTangent, Vec3d to, Vec3f toNormal, Vec3f toTangent, float offset)
        {
            // Кінці шматка зсуваємо кожен по своїй нормалі і тягнемо його по хорді між ними,
            // тоді кінець цього шматка збігається з початком наступного і сходинок не буде
            // Нормаль може бути нахилена (нахил полотна), тоді зовнішня рейка піднімається, а внутрішня опускається
            Vec3d pos = new Vec3d(
                from.X + fromNormal.X * offset,
                from.Y + fromNormal.Y * offset,
                from.Z + fromNormal.Z * offset);

            double dx = to.X + toNormal.X * offset - pos.X;
            double dy = to.Y + toNormal.Y * offset - pos.Y;
            double dz = to.Z + toNormal.Z * offset - pos.Z;

            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (length < 1e-4) return null;

            double yaw    = Math.Atan2(dx, dz);
            double pitch  = Math.Atan2(dy, Math.Sqrt(dx * dx + dz * dz));

            // Сусідні рейки сходяться на спільній площині стику, перпендикулярній до дотичної кривої.
            // Подовжуємо рейку рівно настільки, щоб її найдальший кут дістав до цієї площини
            double startExt = JointExtension(fromTangent, yaw, pitch, atStart: true);
            double endExt   = JointExtension(toTangent, yaw, pitch, atStart: false);

            double back = startExt / length;
            pos.Add(-dx * back, -dy * back, -dz * back);

            // Шматок жорсткий, тому крен беремо середній між його початком і кінцем
            double roll = (Math.Asin(GameMath.Clamp(fromNormal.Y, -1, 1)) + Math.Asin(GameMath.Clamp(toNormal.Y, -1, 1))) / 2;

            return new RailInstanceData
            {
                Position = pos,
                Yaw      = (float)yaw,
                Pitch    = (float)pitch,
                Roll     = (float)roll,
                Light    = GetLight(pos),
                LightKey = LightKey(pos),
                ScaleZ   = (float)(length + startExt + endExt)
            };
        }

        private double JointExtension(Vec3f tangent, double yaw, double pitch, bool atStart)
        {
            double tangentYaw   = Math.Atan2(tangent.X, tangent.Z);
            double tangentPitch = Math.Atan2(tangent.Y, Math.Sqrt(tangent.X * tangent.X + tangent.Z * tangent.Z));

            // Рейка обертається навколо підошви, тому щілина зверху буває лише на опуклому зламі ухилу
            double pitchDiff = atStart ? tangentPitch - pitch : pitch - tangentPitch;
            // По горизонталі вісь обертання посередині, тому зовнішній бік розходиться при будь-якому знаку
            double yawDiff = Math.Abs(GameMath.AngleRadDistance((float)yaw, (float)tangentYaw));

            return railHeight * Math.Tan(Math.Max(0, pitchDiff)) + railHalfWidth * Math.Tan(yawDiff);
        }

        // Світло блока, де лежить шматок: rgb це світло від джерел, a сонячне
        private Vec4f GetLight(Vec3d pos)
        {
            return TrackLight.Of(capi.World.BlockAccessor, pos);
        }

        // Те саме світло одним числом: щоб перевірити, чи воно змінилося, не створюючи об'єкта
        private int LightKey(Vec3d pos)
        {
            return TrackLight.KeyOf(capi.World.BlockAccessor, pos);
        }

        /// <summary>
        /// Перевіряє, чи змінилося світло шматків, не більш як у budget шматків за раз, по колу: гра не повідомляє
        /// моди про зміну освітлення, тому його доводиться опитувати. Шматкові, чиє світло змінилося, у буфері
        /// переписуються лише чотири числа світла. Повертає, скільки шматків перевірено.
        /// </summary>
        public int UpdateLights(int budget)
        {
            int checkedCount = 0;
            for (int n = 0; n < order.Count && checkedCount < budget; n++)
            {
                if (lightCursor >= order.Count) lightCursor = 0;
                Batch batch = order[lightCursor++];
                checkedCount += batch.Drawn;
                // Пачку однаково буде зібрано заново
                if (batch.Dirty || batch.Mesh == null) continue;

                bool changed = false;
                float[] values = batch.Floats.Values;
                for (int i = 0; i < batch.Drawn; i++)
                {
                    RailInstanceData piece = batch.Pieces[i];
                    int key = LightKey(piece.Position);
                    if (key == piece.LightKey) continue;

                    piece.LightKey = key;
                    piece.Light = GetLight(piece.Position);
                    int j = i * FloatsPerInstance;
                    values[j] = piece.Light.R;
                    values[j + 1] = piece.Light.G;
                    values[j + 2] = piece.Light.B;
                    values[j + 3] = piece.Light.A;
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

        // Рахує матриці шматків пачки відносно кута її чанка і кладе їх у відеокарту
        private void Rebuild(Batch batch)
        {
            batch.Dirty = false;
            batch.Drawn = 0;
            int count = batch.Pieces.Count;
            if (count == 0) return;

            // Буфер у відеокарті має розмір, заданий при завантаженні меша, і сам не росте: якщо шматків стало
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
            double originX = batch.Chunk.X * GlobalConstants.ChunkSize, originY = batch.Chunk.Y * GlobalConstants.ChunkSize, originZ = batch.Chunk.Z * GlobalConstants.ChunkSize;

            for (int n = 0; n < count; n++)
            {
                RailInstanceData d = batch.Pieces[n];

                Mat4f.Identity(tmpMat);
                Mat4f.RotateY(tmpMat, tmpMat, d.Yaw);
                Mat4f.RotateX(tmpMat, tmpMat, -d.Pitch);
                Mat4f.RotateZ(tmpMat, tmpMat, -d.Roll);
                // Модель рейки це шматок звичайної довжини; шматок секції розтягує її рівно настільки,
                // наскільки він довший чи коротший
                scale[2] = railLength > 0 ? (float)(d.ScaleZ / railLength) : d.ScaleZ;
                Mat4f.Scale(tmpMat, tmpMat, scale);
                // Рейка в меші лежить по X = 0.5, зсуваємо її на вісь
                Mat4f.Translate(tmpMat, tmpMat, -0.5f, 0f, 0f);

                // Положення шматка відносно кута чанка, разом зі зсувом, який дала сама матриця
                tmpMat[12] += (float)(d.Position.X - originX);
                tmpMat[13] += (float)(d.Position.Y - originY);
                tmpMat[14] += (float)(d.Position.Z - originZ);

                int j = n * FloatsPerInstance;
                values[j] = d.Light.R; values[j + 1] = d.Light.G; values[j + 2] = d.Light.B; values[j + 3] = d.Light.A;
                for (int k = 0; k < 16; k++) values[j + 4 + k] = tmpMat[k];
            }

            batch.Drawn = count;
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
        /// Малює рейки поточним шейдером, пачку за пачкою. Перед кожною передає шейдерові origin: кут чанка
        /// пачки відносно камери. Викликається і для основного проходу, і для тіней.
        /// </summary>
        public void Draw(IShaderProgram prog, Vec3d cameraPos)
        {
            foreach (Batch batch in order)
            {
                if (batch.Mesh == null || batch.Drawn == 0) continue;

                origin.Set((float)(batch.Chunk.X * GlobalConstants.ChunkSize - cameraPos.X), (float)(batch.Chunk.Y * GlobalConstants.ChunkSize - cameraPos.Y), (float)(batch.Chunk.Z * GlobalConstants.ChunkSize - cameraPos.Z));
                prog.Uniform("origin", origin);
                capi.Render.RenderMeshInstanced(batch.Mesh, batch.Drawn);
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
