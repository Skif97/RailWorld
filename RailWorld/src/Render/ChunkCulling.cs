using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace RailWorld
{
    /// <summary>
    /// Чи видно чанк у проході, який зараз малюється, так само, як гра вирішує це для своїх чанків
    /// (ModelDataPoolLocation.IsVisible): в основному проході він має бути в полі зору й ближче за дальність
    /// видимості, у проході тіней у коробці тіней. Відсікач гри перед кожним проходом тіней сам перераховується під
    /// світло (SystemRenderShadowMap.PrepareForShadowRendering, порядок 0), тож тут він уже той, що треба. Між
    /// кадрами (у тактах) він тримає камеру гравця: останнім гра рахує його для основного проходу (ClientMain,
    /// перед Opaque).
    /// Чанк береться кубом: деталі секції біля межі трохи вилазять за неї, а сфера куба сягає на 28 блоків від
    /// його середини, тож покриває і їх.
    /// </summary>
    internal static class ChunkCulling
    {
        public static bool Visible(ICoreClientAPI capi, int chunkX, int chunkY, int chunkZ, bool shadow)
        {
            int size = GlobalConstants.ChunkSize;
            Sphere sphere = Sphere.BoundingSphereForCube(chunkX * size, chunkY * size, chunkZ * size, size);
            FrustumCulling culler = capi.Render.DefaultFrustumCuller;
            // 1 це звичайний рівень деталізації чанка: у полі зору й ближче за дальність видимості
            return shadow ? culler.InFrustumShadowPass(sphere) : culler.InFrustumAndRange(sphere, true, 1);
        }
    }
}
