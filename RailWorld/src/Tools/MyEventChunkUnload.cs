using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace RailWorld
{
    public class MyChunkEvents
    {
        public delegate void ChunkUnloadedEventHandler(IClientChunk chunk);
        public static event ChunkUnloadedEventHandler ChunkUnloaded;

        public static void Init(Harmony harmony)
        {
            var originalMethod = typeof(SystemUnloadChunks).GetMethod(
                "UnloadChunk",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (originalMethod == null) return;

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
