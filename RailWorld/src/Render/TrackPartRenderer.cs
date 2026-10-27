using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace RailWorld
{
    /// <summary>
    /// Малює однакові деталі колії: шпали одного дерева або металеві деталі однієї моделі (підкладки, костилі,
    /// болти, стикові накладки, нерухомі важелі). Деталі в пачці лежать за своїм місцем: за ним деталь знаходять і
    /// прибирають, і деталь, поставлена вдруге на те саме місце, замінює першу, а не подвоюється. Так робить важіль
    /// стрілки: рушаючи, прибирає свою нерухому модель за місцем (SwitchLeverRenderer.Unrest), а зупинившись, ставить
    /// знову; і перебудова чанка ставить її ще раз, коли вона вже стоїть.
    /// </summary>
    internal class TrackPartRenderer : InstancedPartRenderer<Vec3d, TrackPartInstance>
    {
        private float[] scale = new float[] { 1f, 1f, 1f };

        public TrackPartRenderer(ICoreClientAPI capi, LightProbe lights, ItemStack itemStack) : this(capi, lights, ItemMesh(capi, itemStack))
        {
        }

        /// <summary>Малює готовий меш: деталь, у якої немає свого предмета.</summary>
        public TrackPartRenderer(ICoreClientAPI capi, LightProbe lights, MeshData mesh) : base(capi, lights, mesh)
        {
            MeasureLength();
        }

        /// <summary>Малює частину готового меша, текстури якої лежать на сторінці атласу textureId.</summary>
        public TrackPartRenderer(ICoreClientAPI capi, LightProbe lights, MeshData mesh, int textureId) : base(capi, lights, mesh, textureId)
        {
            MeasureLength();
        }

        /// <summary>Довжина моделі вздовж її осі X, блоків: довжина шпали, якою її намальовано.</summary>
        public float ModelLength { get; private set; } = 1f;

        private void MeasureLength()
        {
            if (Mesh == null) return;

            float left = float.MaxValue, right = float.MinValue;
            for (int v = 0; v < Mesh.VerticesCount; v++)
            {
                left = Math.Min(left, Mesh.xyz[v * 3]);
                right = Math.Max(right, Mesh.xyz[v * 3]);
            }
            if (right > left) ModelLength = right - left;
        }

        /// <summary>
        /// Ставить деталь на місце position у пачку чанка chunkCoord: того, у даних якого лежить її секція.
        /// </summary>
        public void Add(Vec3i chunkCoord, Vec3d position, Vec3f rotation, float stretch)
        {
            Add(chunkCoord, position, new TrackPartInstance(position, rotation, stretch));
        }

        // Шпала розтягується вздовж своєї осі X навколо середини моделі
        protected override void Transform(TrackPartInstance part, float[] matrix)
        {
            SetRotation(matrix, part.Rotation);
            scale[0] = part.Stretch;
            Mat4f.Scale(matrix, matrix, scale);
            Mat4f.Translate(matrix, matrix, -0.5f, 0f, -0.5f);
        }
    }
}
