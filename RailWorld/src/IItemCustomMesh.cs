using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace RailWorld.src
{
    public interface IItemCustomMesh
    {
        public MeshData GetMeshData(ICoreClientAPI capi, ItemStack stack);
 
    }
}
