using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
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
        public float ScaleZ;
        // Ширина шматка відносно звичайної рейки: гостряк стрілки до вістря звужується
        public float ScaleX = 1f;
    }

    /// <summary>
    /// Чим рейка секції відрізняється від звичайної: гостряк стрілки лежить не на своєму місці, а зсунутий
    /// до осі колії, і до вістря звужується. Числа задані для трьох точок секції: початку, середини і кінця.
    /// </summary>
    internal class RailShape
    {
        // На скільки блоків рейка зсунута до осі колії
        public double[] Inset = new double[3];
        // Ширина відносно звичайної рейки
        public float[] Width = new float[] { 1f, 1f, 1f };
        // Рейку на цій секції не малювати зовсім: її місце займає гостряк стрілки, окрема деталь
        public bool Hidden;
    }

    /// <summary>
    /// Рендерить рейки одного матеріалу через instanced rendering.
    /// Рейка секції малюється двома прямими шматками, від початку секції до середини і від середини до кінця:
    /// так вона ближче до кривої, і злам на стиках удвічі менший. Один інстанс на шматок.
    /// Меш рейки масштабується по Z матрицею трансформації.
    /// </summary>
    internal class RailSectionRenderer
    {
        private ICoreClientAPI capi;
        private MeshData itemMesh;
        // Щокадру у відеопам'ять дописуються лише дані інстансів: у цьому меші крім них нічого немає
        private MeshData updateMesh = new MeshData(false);
        private MeshRef meshref;
        public int TextureId { get; private set; }
        private CustomMeshDataPartFloat matrixAndLightFloats;

        private Dictionary<string, RailInstanceData> instances = new Dictionary<string, RailInstanceData>();

        // Габарити перерізу рейки, беруться з меша: висота над підошвою і півширина від осі
        private double railHeight;
        private double railHalfWidth;

        private float[] tmpMat = Mat4f.Create();
        private Vec3f tmp = new Vec3f();

        /// <summary>
        /// Меш рейки як є, без даних інстансів: з нього збираються гостряки стрілок.
        /// </summary>
        public MeshData TemplateMesh { get; private set; }

        public RailSectionRenderer(ICoreClientAPI capi, ItemStack itemStack)
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

            TextureId = itemMesh.TextureIds != null && itemMesh.TextureIds.Length > 0
                ? itemMesh.TextureIds[0]
                : capi.BlockTextureAtlas.Positions[0].atlasTextureId;

            for (int v = 0; v < itemMesh.VerticesCount; v++)
            {
                railHalfWidth = Math.Max(railHalfWidth, Math.Abs(itemMesh.xyz[v * 3] - 0.5));
                railHeight    = Math.Max(railHeight, itemMesh.xyz[v * 3 + 1]);
            }

            int bufferSize = 20000 * 20;
            matrixAndLightFloats = new CustomMeshDataPartFloat(bufferSize)
            {
                Instanced = true,
                InterleaveOffsets = new int[] { 0, 16, 32, 48, 64 },
                InterleaveSizes   = new int[] { 4, 4, 4, 4, 4 },
                InterleaveStride  = 80,
                StaticDraw        = false
            };
            matrixAndLightFloats.Values = new float[bufferSize];
            matrixAndLightFloats.SetAllocationSize(bufferSize);
            itemMesh.CustomFloats = matrixAndLightFloats;
            meshref = capi.Render.UploadMesh(itemMesh);
        }

        public void AddRail(Vec3i chunkCoord, int index, Section section, bool left, RailShape shape = null)
        {
            string key = $"{chunkCoord.X}_{chunkCoord.Y}_{chunkCoord.Z}_{index}_{(left ? "L" : "R")}";
            float sign = left ? 1f : -1f;
            float offset = section.TrackWidth / 2f * sign;
            Vec3d start = section.FullStartPosition;
            Vec3d center = section.GetGlobalPos();
            Vec3d end = section.FullEndPosition;

            // Зсув убік у кожній із трьох точок секції. У звичайної рейки він скрізь однаковий
            float[] offsets = { offset, offset, offset };
            float[] widths = { 1f, 1f };
            if (shape != null)
            {
                for (int k = 0; k < 3; k++) offsets[k] = (float)(offset - shape.Inset[k] * sign);
                widths[0] = (shape.Width[0] + shape.Width[1]) / 2;
                widths[1] = (shape.Width[1] + shape.Width[2]) / 2;
            }

            AddPiece(key + "0", BuildInstance(start, section.StartNormal, section.StartTangent, center, section.CenterNormal, section.CenterTangent, offsets[0], offsets[1], widths[0]));
            AddPiece(key + "1", BuildInstance(center, section.CenterNormal, section.CenterTangent, end, section.EndNormal, section.EndTangent, offsets[1], offsets[2], widths[1]));
        }

        private void AddPiece(string key, RailInstanceData piece)
        {
            // Шматок нульової довжини: середина секції збіглася з її краєм
            if (piece == null) instances.Remove(key);
            else instances[key] = piece;
            layoutDirty = true;
        }

        // Матриця шматка складається з повороту з розтягом, які не міняються, і положення відносно камери,
        // яке міняється щокадру. Перше рахується один раз, коли набір шматків чи їхнє світло змінилися,
        // і лежить у буфері; щокадру переписуються лише три числа положення на шматок.
        // layoutDirty каже, що нерухому частину треба порахувати заново
        private bool layoutDirty = true;
        // Положення кожного шматка у світі, разом зі зсувом, який дає сама матриця: по три числа на шматок,
        // у тому самому порядку, що й у буфері
        private double[] basePositions = new double[0];
        private float[] scale = new float[] { 1f, 1f, 1f };

        public void RemoveChunk(Vec3i chunkCoord)
        {
            string prefix = $"{chunkCoord.X}_{chunkCoord.Y}_{chunkCoord.Z}_";
            foreach (var key in instances.Keys.Where(k => k.StartsWith(prefix)).ToList())
            {
                instances.Remove(key);
                layoutDirty = true;
            }
        }

        // Шматок рейки між двома точками осі колії. fromOffset і toOffset це зсув рейки вбік від осі на кінцях
        // шматка, width його ширина відносно звичайної рейки
        private RailInstanceData BuildInstance(Vec3d from, Vec3f fromNormal, Vec3f fromTangent, Vec3d to, Vec3f toNormal, Vec3f toTangent,
            float fromOffset, float toOffset, float width)
        {
            // Кінці шматка зсуваємо кожен по своїй нормалі і тягнемо його по хорді між ними,
            // тоді кінець цього шматка збігається з початком наступного і сходинок не буде
            // Нормаль може бути нахилена (нахил полотна), тоді зовнішня рейка піднімається, а внутрішня опускається
            Vec3d pos = new Vec3d(
                from.X + fromNormal.X * fromOffset,
                from.Y + fromNormal.Y * fromOffset,
                from.Z + fromNormal.Z * fromOffset);

            double dx = to.X + toNormal.X * toOffset - pos.X;
            double dy = to.Y + toNormal.Y * toOffset - pos.Y;
            double dz = to.Z + toNormal.Z * toOffset - pos.Z;

            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (length < 1e-4) return null;

            double yaw    = Math.Atan2(dx, dz);
            double pitch  = Math.Atan2(dy, Math.Sqrt(dx * dx + dz * dz));

            // Сусідні рейки сходяться на спільній площині стику, перпендикулярній до дотичної кривої.
            // Подовжуємо рейку рівно настільки, щоб її найдальший кут дістав до цієї площини
            double startExt = JointExtension(fromTangent, yaw, pitch, atStart: true);
            double endExt   = JointExtension(toTangent, yaw, pitch, atStart: false);

            if (length > 0)
            {
                double k = startExt / length;
                pos.Add(-dx * k, -dy * k, -dz * k);
            }

            // Шматок жорсткий, тому крен беремо середній між його початком і кінцем
            double roll = (Math.Asin(GameMath.Clamp(fromNormal.Y, -1, 1)) + Math.Asin(GameMath.Clamp(toNormal.Y, -1, 1))) / 2;

            return new RailInstanceData
            {
                Position = pos,
                Yaw      = (float)yaw,
                Pitch    = (float)pitch,
                Roll     = (float)roll,
                Light    = GetLight(pos),
                ScaleZ   = (float)(length + startExt + endExt),
                ScaleX   = width
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

        private Vec4f GetLight(Vec3d pos)
        {
            return capi.World.BlockAccessor.GetLightRGBs((int)Math.Floor(pos.X), (int)Math.Floor(pos.Y + 0.25), (int)Math.Floor(pos.Z));
        }

        public void UpdateLights()
        {
            foreach (RailInstanceData d in instances.Values)
                d.Light = GetLight(d.Position);
            layoutDirty = true;
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
        /// Раз на кадр: рахує матриці всіх рейок відносно камери і заливає їх у відеокарту.
        /// </summary>
        public void PrepareFrame()
        {
            preparedCount = 0;
            if (meshref == null || instances.Count == 0) return;

            int count      = instances.Count;
            int floatCount = count * 20;

            if (matrixAndLightFloats.Values == null || matrixAndLightFloats.Values.Length < floatCount) Grow(count);
            if (meshref == null) return;

            float[] values = matrixAndLightFloats.Values;

            if (layoutDirty)
            {
                // Нерухома частина матриць і світло всіх шматків
                if (basePositions.Length < count * 3) basePositions = new double[count * 3 * 2];

                int n = 0;
                foreach (RailInstanceData d in instances.Values)
                {
                    Mat4f.Identity(tmpMat);
                    Mat4f.RotateY(tmpMat, tmpMat, d.Yaw);
                    Mat4f.RotateX(tmpMat, tmpMat, -d.Pitch);
                    Mat4f.RotateZ(tmpMat, tmpMat, -d.Roll);
                    scale[0] = d.ScaleX;
                    scale[2] = d.ScaleZ;
                    Mat4f.Scale(tmpMat, tmpMat, scale);
                    // Рейка в меші лежить по X = 0.5, зсуваємо її на вісь
                    Mat4f.Translate(tmpMat, tmpMat, -0.5f, 0f, 0f);

                    int j = n * 20;
                    values[j] = d.Light.R; values[j+1] = d.Light.G; values[j+2] = d.Light.B; values[j+3] = d.Light.A;
                    for (int k = 0; k < 16; k++) values[j + 4 + k] = tmpMat[k];

                    // Зсув, який дала сама матриця, додається до положення шматка
                    basePositions[n * 3] = d.Position.X + tmpMat[12];
                    basePositions[n * 3 + 1] = d.Position.Y + tmpMat[13];
                    basePositions[n * 3 + 2] = d.Position.Z + tmpMat[14];
                    n++;
                }
                layoutDirty = false;
            }

            // Щокадру міняється лише положення відносно камери: три числа в кожній матриці
            Vec3d camPos = capi.World.Player.Entity.CameraPos;
            for (int i = 0; i < count; i++)
            {
                int j = i * 20 + 16;
                int b = i * 3;
                values[j] = (float)(basePositions[b] - camPos.X);
                values[j + 1] = (float)(basePositions[b + 1] - camPos.Y);
                values[j + 2] = (float)(basePositions[b + 2] - camPos.Z);
            }

            matrixAndLightFloats.Count = floatCount;
            itemMesh.CustomFloats = matrixAndLightFloats;
            updateMesh.CustomFloats = matrixAndLightFloats;
            capi.Render.UpdateMesh(meshref, updateMesh);
            preparedCount = count;
        }

        /// <summary>
        /// Малює підготовлені рейки поточним шейдером. Викликається і для основного проходу, і для тіней.
        /// </summary>
        public void Draw()
        {
            if (meshref == null || preparedCount == 0) return;
            capi.Render.RenderMeshInstanced(meshref, preparedCount);
        }

        public void Dispose()
        {
            meshref?.Dispose();
        }
    }
}
