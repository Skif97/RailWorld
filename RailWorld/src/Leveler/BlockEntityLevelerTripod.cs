using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace RailWorld
{
    /// <summary>
    /// Поставлена тринога або нівелір (тринога з приладом): пам'ятає, з чого зроблена, і малює себе з цими
    /// матеріалами, повернутою на кут постановки. Кут рахує й зберігає ванільна поведінка RotatablePlaceable, текстури
    /// підставляє ванільне джерело TypedTextureSource, як у шафи. Тринога і нівелір мають одну блок-сутність: коли на
    /// триногу ставлять прилад, блок лише підміняється, і матеріали з кутом лишаються.
    /// </summary>
    public class BlockEntityLevelerTripod : BlockEntity
    {
        private ITreeAttribute materials = new TreeAttribute();
        private MeshData mesh;

        /// <summary>Стек цього блока з матеріалами: так тринога чи нівелір падає, коли його ламають.</summary>
        public ItemStack ToStack()
        {
            var stack = new ItemStack(Block);
            stack.Attributes = TripodMaterials.Copy(materials);
            return stack;
        }

        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);
            materials = TripodMaterials.Copy(byItemStack?.Attributes);
        }

        // Тринога стала нівеліром чи навпаки: інша форма
        public override void OnExchanged(Block block)
        {
            base.OnExchanged(block);
            mesh = null;
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            materials = TripodMaterials.Copy(tree.GetTreeAttribute("materials"));
            mesh = null;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree["materials"] = materials;
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
        {
            if (mesh == null && Api is ICoreClientAPI capi && Block is BlockLevelerTripod tripod)
            {
                mesh = tripod.GenMesh(capi, tesselator, Block.Shape.Base, ToStack());
            }
            if (mesh == null) return false;
            // Кут постановки плюс зсув моделі з json (offsetDeg), як у ванільного манекена: за нульового кута до
            // гравця обернений бік +Z, а окуляр приладу в моделі дивиться в -X
            float angle = (GetBehavior<IRotatablePlaceable>()?.MeshAngleRad ?? 0)
                + (Block.GetBehavior<BlockBehaviorRotateablePlaceable>()?.OffsetRad ?? 0);
            mesher.AddMeshData(mesh.Clone().Rotate(0, angle, 0));
            return true;
        }
    }
}
