using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace RailWorld
{
    /// <summary>
    /// Подія «клієнт вивантажив чанк»: гра її не дає, тож вона береться з її закритого методу.
    /// Латку ставить і знімає RailWaySystem разом з рештою своїх латок.
    /// </summary>
    public class MyChunkEvents
    {
        public delegate void ChunkUnloadedEventHandler(IClientChunk chunk);
        public static event ChunkUnloadedEventHandler ChunkUnloaded;

        public static void Init(Harmony harmony, ILogger logger)
        {
            var originalMethod = typeof(SystemUnloadChunks).GetMethod(
                "UnloadChunk",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (originalMethod == null)
            {
                logger.Error("[RailWorld] у грі немає методу SystemUnloadChunks.UnloadChunk: колія вивантажених чанків не прибиратиметься з пам'яті");
                return;
            }

            var postfixMethod = typeof(MyChunkEvents).GetMethod(
                "UnloadChunkPostfix",
                BindingFlags.NonPublic | BindingFlags.Static);

            harmony.Patch(originalMethod, postfix: new HarmonyMethod(postfixMethod));
        }

        private static void UnloadChunkPostfix(ClientChunk clientchunk)
        {
            ChunkUnloaded?.Invoke(clientchunk);
        }
    }
}
