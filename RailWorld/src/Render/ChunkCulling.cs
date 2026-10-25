using System;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace RailWorld
{
    /// <summary>
    /// Де лежить те, що малюється одним мешем чи пачкою: коробка у світі, до якої додаються точки. З неї
    /// будується сфера для відсікання так само, як гра будує її для своїх чанків з меж їхнього меша
    /// (TesselatedChunk.SetBounds). Рахується, коли збирається меш, а не щокадру.
    /// </summary>
    internal struct CullBounds
    {
        public double MinX, MinY, MinZ, MaxX, MaxY, MaxZ;

        /// <summary>Порожня коробка: перша додана точка стане нею.</summary>
        public static CullBounds Empty => new CullBounds
        {
            MinX = double.MaxValue, MinY = double.MaxValue, MinZ = double.MaxValue,
            MaxX = double.MinValue, MaxY = double.MinValue, MaxZ = double.MinValue
        };

        public bool IsEmpty => MinX > MaxX;

        public void Add(double x, double y, double z)
        {
            if (x < MinX) MinX = x;
            if (y < MinY) MinY = y;
            if (z < MinZ) MinZ = z;
            if (x > MaxX) MaxX = x;
            if (y > MaxY) MaxY = y;
            if (z > MaxZ) MaxZ = z;
        }

        /// <summary>Точка з усіх боків на radius: куля, у яку вкладається бокс навколо неї.</summary>
        public void Add(double x, double y, double z, double radius)
        {
            Add(x - radius, y - radius, z - radius);
            Add(x + radius, y + radius, z + radius);
        }

        /// <summary>
        /// Коробка моделі (від min до max у її власних координатах), перенесена матрицею matrix, що лежить у масиві
        /// з місця at стовпцями, як її бере шейдер, і зсунута на (ox, oy, oz): вісім кутів коробки.
        /// </summary>
        public void Add(float[] matrix, int at, Vec3f min, Vec3f max, double ox, double oy, double oz)
        {
            for (int corner = 0; corner < 8; corner++)
            {
                float x = (corner & 1) == 0 ? min.X : max.X;
                float y = (corner & 2) == 0 ? min.Y : max.Y;
                float z = (corner & 4) == 0 ? min.Z : max.Z;
                Add(ox + matrix[at] * x + matrix[at + 4] * y + matrix[at + 8] * z + matrix[at + 12],
                    oy + matrix[at + 1] * x + matrix[at + 5] * y + matrix[at + 9] * z + matrix[at + 13],
                    oz + matrix[at + 2] * x + matrix[at + 6] * y + matrix[at + 10] * z + matrix[at + 14]);
            }
        }

        /// <summary>Коробка вершин меша в його власних координатах. false, якщо вершин немає.</summary>
        public static bool OfMesh(MeshData mesh, out Vec3f min, out Vec3f max)
        {
            return OfVertices(mesh?.xyz, mesh?.VerticesCount ?? 0, out min, out max);
        }

        /// <summary>Те саме для масиву координат вершин (по три числа на вершину).</summary>
        public static bool OfVertices(float[] xyz, int count, out Vec3f min, out Vec3f max)
        {
            min = new Vec3f(float.MaxValue, float.MaxValue, float.MaxValue);
            max = new Vec3f(float.MinValue, float.MinValue, float.MinValue);
            if (xyz == null || count <= 0) return false;

            for (int i = 0; i < count; i++)
            {
                float x = xyz[i * 3], y = xyz[i * 3 + 1], z = xyz[i * 3 + 2];
                min.X = Math.Min(min.X, x); min.Y = Math.Min(min.Y, y); min.Z = Math.Min(min.Z, z);
                max.X = Math.Max(max.X, x); max.Y = Math.Max(max.Y, y); max.Z = Math.Max(max.Z, z);
            }
            return true;
        }

        /// <summary>Сфера для відсікання, як у гри: середина коробки і її розміри.</summary>
        public Sphere Sphere => new Sphere((float)((MinX + MaxX) / 2), (float)((MinY + MaxY) / 2), (float)((MinZ + MaxZ) / 2),
            (float)(MaxX - MinX), (float)(MaxY - MinY), (float)(MaxZ - MinZ));
    }

    /// <summary>
    /// Чи видно те, що лежить у коробці bounds, у проході, який зараз малюється, так само, як гра вирішує це для
    /// своїх чанків (ModelDataPoolLocation.IsVisible): в основному проході воно має бути в полі зору й ближче за
    /// дальність видимості, у проході тіней у коробці тіней. Відсікач гри перед кожним проходом тіней сам
    /// перераховується під світло (SystemRenderShadowMap.PrepareForShadowRendering, порядок 0), тож тут він уже
    /// той, що треба. Між кадрами (у тактах) він тримає камеру гравця: останнім гра рахує його для основного
    /// проходу (ClientMain, перед Opaque).
    /// Межі беруться справжні, а не куб чанка: деталі стрілки належать чанку її спільної секції, а лежать і далеко
    /// в сусідньому, і куб чанка відсік би їх разом із ним.
    /// </summary>
    internal static class ChunkCulling
    {
        public static bool Visible(ICoreClientAPI capi, in CullBounds bounds, bool shadow)
        {
            if (bounds.IsEmpty) return false;
            Sphere sphere = bounds.Sphere;
            FrustumCulling culler = capi.Render.DefaultFrustumCuller;
            // 1 це звичайний рівень деталізації чанка: у полі зору й ближче за дальність видимості
            return shadow ? culler.InFrustumShadowPass(sphere) : culler.InFrustumAndRange(sphere, true, 1);
        }
    }
}
