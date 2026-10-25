using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace RailWorld.src
{
    public interface IItemCustomMesh
    {
        public MeshData GetMeshData(ICoreClientAPI capi, ItemStack stack);
 
    }
}
