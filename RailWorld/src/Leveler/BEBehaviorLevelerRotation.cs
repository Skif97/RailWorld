using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace RailWorld
{
    /// <summary>
    /// Кут триноги. Збереження кута у ванільної BEBehaviorRotatablePlaceable, але блокова поведінка RotateablePlaceable
    /// шукає в блок-сутності IRotatablePlaceable, а ванільна поведінка його не оголошує; манекен гри додає інтерфейс
    /// підкласом (BEBehaviorMannequin), тут те саме. Рамки тринога не повертає (див. нижче), а кут змінюють і після
    /// постановки, кліками (Rotate).
    /// </summary>
    public class BEBehaviorLevelerRotation : BEBehaviorRotatablePlaceable, IRotatablePlaceable, IMultiBlockColSelBoxes
    {
        /// <summary>Крок повороту кліком, той самий, що крок постановки.</summary>
        public const float Step = GameMath.PIHALF / 2;

        public BEBehaviorLevelerRotation(BlockEntity blockentity) : base(blockentity)
        {
        }

        /// <summary>Повертає на крок за годинниковою стрілкою (sign 1) чи проти неї (sign -1).</summary>
        public void Rotate(int sign)
        {
            MeshAngleRad = GameMath.Mod(MeshAngleRad + sign * Step, GameMath.TWOPI);
            Blockentity.MarkDirty(true);
        }

        // Рамки не повертаються: це квадрат посередині блока, а повернутий квадрат гра описує вирівняною рамкою, тож
        // при кожному куті, не кратному 90°, рамка ставала б більшою. Для верхньої клітинки рамки зсунуті вниз на
        // її висоту, як у ванільної поведінки
        Cuboidf[] IRotatablePlaceable.GetSelectionBoxes() => Boxes(Block.SelectionBoxes, 0);
        Cuboidf[] IRotatablePlaceable.GetCollisionBoxes() => Boxes(Block.CollisionBoxes, 0);
        Cuboidf[] IMultiBlockColSelBoxes.MBGetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset) => Boxes(Block.SelectionBoxes, -offset.Y);
        Cuboidf[] IMultiBlockColSelBoxes.MBGetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset) => Boxes(Block.CollisionBoxes, -offset.Y);

        private static Cuboidf[] Boxes(Cuboidf[] boxes, int level)
        {
            return level == 0 ? boxes : boxes.Select(box => box.OffsetCopy(0, -level, 0)).ToArray();
        }
    }
}
