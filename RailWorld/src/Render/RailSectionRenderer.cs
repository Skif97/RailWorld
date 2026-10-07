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
        private MeshRef meshref;
        public int TextureId { get; private set; }
        private CustomMeshDataPartFloat matrixAndLightFloats;

        private Dictionary<string, RailInstanceData> instances = new Dictionary<string, RailInstanceData>();

        // Габарити перерізу рейки, беруться з меша: висота над підошвою і півширина від осі
        private double railHeight;
        private double railHalfWidth;

        private float[] tmpMat = Mat4f.Create();
        private Vec3f tmp = new Vec3f();

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

        public void AddRail(Vec3i chunkCoord, int index, Section section, bool left)
        {
            string key = $"{chunkCoord.X}_{chunkCoord.Y}_{chunkCoord.Z}_{index}_{(left ? "L" : "R")}";
            float offset = section.TrackWidth / 2f * (left ? 1f : -1f);
            Vec3d start = section.FullStartPosition;
            Vec3d center = section.GetGlobalPos();
            Vec3d end = section.FullEndPosition;

            AddPiece(key + "0", BuildInstance(start, section.StartNormal, section.StartTangent, center, section.CenterNormal, section.CenterTangent, offset));
            AddPiece(key + "1", BuildInstance(center, section.CenterNormal, section.CenterTangent, end, section.EndNormal, section.EndTangent, offset));
        }

        private void AddPiece(string key, RailInstanceData piece)
        {
            // Шматок нульової довжини: середина секції збіглася з її краєм
            if (piece == null) instances.Remove(key);
            else instances[key] = piece;
        }

        public void RemoveChunk(Vec3i chunkCoord)
        {
            string prefix = $"{chunkCoord.X}_{chunkCoord.Y}_{chunkCoord.Z}_";
            foreach (var key in instances.Keys.Where(k => k.StartsWith(prefix)).ToList())
                instances.Remove(key);
        }

        // Шматок рейки між двома точками осі колії. offset це зсув рейки вбік від осі
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

        private Vec4f GetLight(Vec3d pos)
        {
            return capi.World.BlockAccessor.GetLightRGBs((int)Math.Floor(pos.X), (int)Math.Floor(pos.Y + 0.25), (int)Math.Floor(pos.Z));
        }

        public void UpdateLights()
        {
            foreach (RailInstanceData d in instances.Values)
                d.Light = GetLight(d.Position);
        }

        // Скільки інстансів лежить у буфері після останнього PrepareFrame
        private int preparedCount;

        /// <summary>
        /// Раз на кадр: рахує матриці всіх рейок відносно камери і заливає їх у відеокарту.
        /// </summary>
        public void PrepareFrame()
        {
            preparedCount = 0;
            if (meshref == null || instances.Count == 0) return;

            int count      = instances.Count;
            int floatCount = count * 20;

            if (matrixAndLightFloats.Values == null || matrixAndLightFloats.Values.Length < floatCount)
            {
                matrixAndLightFloats.Values = new float[floatCount + 400];
                matrixAndLightFloats.SetAllocationSize(matrixAndLightFloats.Values.Length);
            }

            Vec3d camPos = capi.World.Player.Entity.CameraPos;
            float[] values = matrixAndLightFloats.Values;
            int i = 0;

            foreach (var kvp in instances)
            {
                RailInstanceData d = kvp.Value;
                tmp.Set(
                    (float)(d.Position.X - camPos.X),
                    (float)(d.Position.Y - camPos.Y),
                    (float)(d.Position.Z - camPos.Z));

                Mat4f.Identity(tmpMat);
                Mat4f.Translate(tmpMat, tmpMat, tmp.X, tmp.Y, tmp.Z);
                Mat4f.RotateY(tmpMat, tmpMat, d.Yaw);
                Mat4f.RotateX(tmpMat, tmpMat, -d.Pitch);
                Mat4f.RotateZ(tmpMat, tmpMat, -d.Roll);
                Mat4f.Scale(tmpMat, tmpMat, new float[] { 1f, 1f, d.ScaleZ });
                // Рейка в меші лежить по X = 0.5, зсуваємо її на вісь
                Mat4f.Translate(tmpMat, tmpMat, -0.5f, 0f, 0f);

                int j = i * 20;
                values[j] = d.Light.R; values[j+1] = d.Light.G; values[j+2] = d.Light.B; values[j+3] = d.Light.A;
                for (int k = 0; k < 16; k++) values[j + 4 + k] = tmpMat[k];
                i++;
            }

            matrixAndLightFloats.Count = floatCount;
            itemMesh.CustomFloats = matrixAndLightFloats;
            capi.Render.UpdateMesh(meshref, itemMesh);
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
