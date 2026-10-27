using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace RailWorld
{
    /// <summary>
    /// Подія «клієнт вивантажив чанк»: гра її не дає, тож вона береться з її закритого методу. Координати чанків,
    /// які вивантажуються, гра знає лише з пакета сервера (SystemUnloadChunks.HandleChunkUnload), тож латка стоїть
    /// на ньому і віддає координати кожного чанка з пакета.
    /// Латку ставить і знімає RailWaySystem разом з рештою своїх латок.
    /// </summary>
    public class MyChunkEvents
    {
        public delegate void ChunkUnloadedEventHandler(Vec3i chunkCoord);
        public static event ChunkUnloadedEventHandler ChunkUnloaded;

        public static void Init(Harmony harmony, ILogger logger)
        {
            var originalMethod = typeof(SystemUnloadChunks).GetMethod(
                "HandleChunkUnload",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (originalMethod == null)
            {
                logger.Error("[RailWorld] у грі немає методу SystemUnloadChunks.HandleChunkUnload: колія вивантажених чанків не прибиратиметься з пам'яті");
                return;
            }

            var postfixMethod = typeof(MyChunkEvents).GetMethod(
                "HandleChunkUnloadPostfix",
                BindingFlags.NonPublic | BindingFlags.Static);

            harmony.Patch(originalMethod, postfix: new HarmonyMethod(postfixMethod));
        }

        private static void HandleChunkUnloadPostfix(Packet_Server packet)
        {
            Packet_UnloadServerChunk unload = packet?.UnloadChunk;
            if (unload == null || ChunkUnloaded == null) return;

            int count = unload.GetXCount();
            for (int i = 0; i < count; i++)
            {
                ChunkUnloaded(new Vec3i(unload.X[i], unload.Y[i], unload.Z[i]));
            }
        }
    }
}
