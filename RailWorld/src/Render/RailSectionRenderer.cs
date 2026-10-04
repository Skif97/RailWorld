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
    /// Один інстанс на рейку.
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
            instances[$"{chunkCoord.X}_{chunkCoord.Y}_{chunkCoord.Z}_{index}_{(left ? "L" : "R")}"] = BuildInstance(section, left);
        }

        public void RemoveChunk(Vec3i chunkCoord)
        {
            string prefix = $"{chunkCoord.X}_{chunkCoord.Y}_{chunkCoord.Z}_";
            foreach (var key in instances.Keys.Where(k => k.StartsWith(prefix)).ToList())
                instances.Remove(key);
        }

        private RailInstanceData BuildInstance(Section s, bool left)
        {
            float offset = s.TrackWidth / 2f * (left ? 1f : -1f);
            Vec3d fullStart = s.FullStartPosition;
            Vec3d fullEnd   = s.FullEndPosition;

            // Кінці рейки зсуваємо кожен по своїй нормалі і тягнемо рейку по хорді між ними,
            // тоді кінець цієї рейки збігається з початком наступної і сходинок не буде
            // Нормаль може бути нахилена (нахил полотна), тоді зовнішня рейка піднімається, а внутрішня опускається
            Vec3d pos = new Vec3d(
                fullStart.X + s.StartNormal.X * offset,
                fullStart.Y + s.StartNormal.Y * offset,
                fullStart.Z + s.StartNormal.Z * offset);

            double dx = fullEnd.X + s.EndNormal.X * offset - pos.X;
            double dy = fullEnd.Y + s.EndNormal.Y * offset - pos.Y;
            double dz = fullEnd.Z + s.EndNormal.Z * offset - pos.Z;

            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double yaw    = Math.Atan2(dx, dz);
            double pitch  = Math.Atan2(dy, Math.Sqrt(dx * dx + dz * dz));

            // Сусідні рейки сходяться на спільній площині стику, перпендикулярній до дотичної кривої.
            // Подовжуємо рейку рівно настільки, щоб її найдальший кут дістав до цієї площини
            double startExt = JointExtension(s.StartTangent, yaw, pitch, atStart: true);
            double endExt   = JointExtension(s.EndTangent, yaw, pitch, atStart: false);

            if (length > 0)
            {
                double k = startExt / length;
                pos.Add(-dx * k, -dy * k, -dz * k);
            }

            // Рейка жорстка, тому крен беремо середній між початком і кінцем секції
            double roll = (Math.Asin(GameMath.Clamp(s.StartNormal.Y, -1, 1)) + Math.Asin(GameMath.Clamp(s.EndNormal.Y, -1, 1))) / 2;

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

        public void OnRenderFrame(float deltaTime, IShaderProgram prog)
        {
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
            capi.Render.RenderMeshInstanced(meshref, count);
        }

        public void Dispose()
        {
            meshref?.Dispose();
        }
    }
}
