using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using RailWorld.src;
using RailWorld.src.RailWay;

namespace RailWorld
{
    internal class RailInstanceData : InstancedPart
    {
        // Довжина шматка, блоків: на стільки розтягується модель уздовж своєї осі Z
        public float ScaleZ;
        // Котра за рахунком частина текстури вздовж рейки лягає на шматок
        public int Variant;
    }

    /// <summary>Шматок рейки в пачці чанка: секція, котра з двох рейок і котрий з двох шматків.</summary>
    internal readonly struct RailPieceKey : IEquatable<RailPieceKey>
    {
        public readonly int Section;
        public readonly bool Left;
        public readonly int Piece;

        public RailPieceKey(int section, bool left, int piece)
        {
            Section = section;
            Left = left;
            Piece = piece;
        }

        public bool Equals(RailPieceKey other)
        {
            return Section == other.Section && Left == other.Left && Piece == other.Piece;
        }

        public override bool Equals(object obj)
        {
            return obj is RailPieceKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Section * 2 + (Left ? 1 : 0)) * 2 + Piece;
        }
    }

    /// <summary>
    /// Рендерить рейки одного матеріалу через instanced rendering.
    /// Рейка секції малюється двома прямими шматками, від початку секції до середини і від середини до кінця:
    /// так вона ближче до кривої, і злам на стиках удвічі менший. Один інстанс на шматок.
    /// Меш рейки масштабується по Z матрицею трансформації.
    /// </summary>
    internal class RailSectionRenderer : InstancedPartRenderer<RailPieceKey, RailInstanceData>
    {
        // Габарити перерізу рейки, беруться з меша: висота над підошвою і півширина від осі
        private double railHeight;
        // Довжина моделі рейки, блоків: шматок, яким рейку намальовано
        private double railLength;
        private double railHalfWidth;

        private float[] scale = new float[] { 1f, 1f, 1f };

        /// <summary>
        /// Меш рейки як є, без даних інстансів: з нього збираються гостряки стрілок.
        /// </summary>
        public MeshData TemplateMesh { get; private set; }

        /// <summary>
        /// На скільки частин вздовж рейки ділиться її текстура. Модель рейки це шматок у чверть блока, і вздовж
        /// він бере чверть текстури; якби всі шматки брали ту саму чверть, малюнок повторювався б на кожному.
        /// Сусідні шматки беруть частини по черзі: номер частини лежить у даних інстанса, а зсуває текстуру
        /// шейдер, тож модель на метал одна.
        /// </summary>
        public const int Variants = 4;

        public RailSectionRenderer(ICoreClientAPI capi, ItemStack itemStack) : base(capi, ItemMesh(capi, itemStack))
        {
            if (Mesh == null) return;

            TemplateMesh = Mesh.Clone();
            Mesh.CustomInts = TextureSteps(Mesh);

            for (int v = 0; v < Mesh.VerticesCount; v++)
            {
                railHalfWidth = Math.Max(railHalfWidth, Math.Abs(Mesh.xyz[v * 3] - 0.5));
                railHeight    = Math.Max(railHeight, Mesh.xyz[v * 3 + 1]);
                railLength    = Math.Max(railLength, Mesh.xyz[v * 3 + 2]);
            }
        }

        protected override int MinCapacity => 128;

        // Після світла й матриці: частина текстури вздовж рейки
        protected override int ExtraFloats => 1;

        protected override void WriteExtra(RailInstanceData piece, float[] values, int at)
        {
            values[at] = piece.Variant;
        }

        protected override void Transform(RailInstanceData piece, float[] matrix)
        {
            SetRotation(matrix, piece.Rotation);
            // Модель рейки це шматок звичайної довжини; шматок секції розтягує її рівно настільки,
            // наскільки він довший чи коротший
            scale[2] = railLength > 0 ? (float)(piece.ScaleZ / railLength) : piece.ScaleZ;
            Mat4f.Scale(matrix, matrix, scale);
            // Рейка в меші лежить по X = 0.5, зсуваємо її на вісь
            Mat4f.Translate(matrix, matrix, -0.5f, 0f, 0f);
        }

        // Для кожної вершини: уздовж якої осі текстури і на скільки зсувається одна частина текстури
        // (railinstanced.vsh). Шматок рейки завдовжки чверть блока, тож кожна його грань уздовж рейки бере першу
        // чверть текстури, і зсув на кілька таких частин, менше за Variants, лишається в межах текстури. Яка вісь
        // текстури йде вздовж рейки, у кожної грані своя: на боках одна, зверху інша, тож це видно лише з самої
        // грані. Торці не зсуваються
        private static CustomMeshDataPartInt TextureSteps(MeshData mesh)
        {
            var steps = new CustomMeshDataPartInt(mesh.VerticesCount * 2)
            {
                Instanced = false,
                InterleaveSizes = new int[] { 2 },
                InterleaveOffsets = new int[] { 0 },
                InterleaveStride = 8,
                StaticDraw = true
            };
            steps.Values = new int[mesh.VerticesCount * 2];
            steps.SetAllocationSize(mesh.VerticesCount * 2);
            steps.Count = mesh.VerticesCount * 2;

            // Грань це чотири вершини підряд
            for (int face = 0; face + 3 < mesh.VerticesCount; face += 4)
            {
                float zLow = float.MaxValue, zHigh = float.MinValue;
                for (int n = 0; n < 4; n++)
                {
                    float z = mesh.xyz[(face + n) * 3 + 2];
                    zLow = Math.Min(zLow, z);
                    zHigh = Math.Max(zHigh, z);
                }
                // Торець: уздовж рейки він не тягнеться
                if (zHigh - zLow < 1e-5) continue;

                // Вісь текстури, що міняється разом з місцем уздовж рейки, і на скільки: це і є одна частина
                float zMiddle = (zLow + zHigh) / 2;
                int along = -1;
                float span = 0;
                for (int axis = 0; axis < 2; axis++)
                {
                    float atStart = 0, atEnd = 0;
                    int startCount = 0, endCount = 0;
                    for (int n = 0; n < 4; n++)
                    {
                        float uv = mesh.Uv[(face + n) * 2 + axis];
                        if (mesh.xyz[(face + n) * 3 + 2] < zMiddle) { atStart += uv; startCount++; }
                        else { atEnd += uv; endCount++; }
                    }
                    if (startCount == 0 || endCount == 0) continue;

                    float change = Math.Abs(atEnd / endCount - atStart / startCount);
                    if (change <= span) continue;
                    span = change;
                    along = axis;
                }
                if (along < 0 || span < 1e-7) continue;

                int size = BitConverter.SingleToInt32Bits(span);
                for (int n = 0; n < 4; n++)
                {
                    steps.Values[(face + n) * 2] = size;
                    steps.Values[(face + n) * 2 + 1] = along;
                }
            }
            return steps;
        }

        /// <summary>
        /// Ставить один з двох шматків рейки секції: piece 0 від початку до середини, 1 від середини до кінця.
        /// variant каже, котра частина текстури вздовж рейки лягає на шматок, з нуля.
        /// </summary>
        public void AddRailPiece(Vec3i chunkCoord, Section section, bool left, int piece, int variant)
        {
            float offset = section.TrackWidth / 2f * (left ? 1f : -1f);
            Vec3d center = section.GetGlobalPos();

            RailInstanceData data = piece == 0
                ? BuildInstance(section.FullStartPosition, section.StartNormal, section.StartTangent, center, section.CenterNormal, section.CenterTangent, offset)
                : BuildInstance(center, section.CenterNormal, section.CenterTangent, section.FullEndPosition, section.EndNormal, section.EndTangent, offset);
            if (data == null) return;
            data.Variant = variant;
            Add(chunkCoord, new RailPieceKey(section.IndexInChunk, left, piece), data);
        }

        // Шматок рейки між двома точками осі колії. offset це зсув рейки вбік від осі: пів ширини колії
        // в один чи другий бік
        private RailInstanceData BuildInstance(Vec3d from, Vec3f fromNormal, Vec3f fromTangent, Vec3d to, Vec3f toNormal, Vec3f toTangent, float offset)
        {
            // Кінці шматка зсуваємо кожен по своїй нормалі і тягнемо його по хорді між ними,
            // тоді кінець цього шматка збігається з початком наступного і сходинок не буде
            // Нормаль може бути нахилена (нахил полотна), тоді зовнішня рейка піднімається, а внутрішня опускається
            Vec3d pos = new Vec3d(
                from.X + fromNormal.X * offset,
                from.Y + fromNormal.Y * offset,
                from.Z + fromNormal.Z * offset);

            double dx = to.X + toNormal.X * offset - pos.X;
            double dy = to.Y + toNormal.Y * offset - pos.Y;
            double dz = to.Z + toNormal.Z * offset - pos.Z;

            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (length < 1e-4) return null;

            double yaw    = Math.Atan2(dx, dz);
            double pitch  = Math.Atan2(dy, Math.Sqrt(dx * dx + dz * dz));

            // Сусідні рейки сходяться на спільній площині стику, перпендикулярній до дотичної кривої.
            // Подовжуємо рейку рівно настільки, щоб її найдальший кут дістав до цієї площини
            double startExt = JointExtension(fromTangent, yaw, pitch, atStart: true);
            double endExt   = JointExtension(toTangent, yaw, pitch, atStart: false);

            double back = startExt / length;
            pos.Add(-dx * back, -dy * back, -dz * back);

            // Шматок жорсткий, тому крен беремо середній між його початком і кінцем
            double roll = (Math.Asin(GameMath.Clamp(fromNormal.Y, -1, 1)) + Math.Asin(GameMath.Clamp(toNormal.Y, -1, 1))) / 2;

            return new RailInstanceData
            {
                Position = pos,
                Rotation = new Vec3f((float)pitch, (float)yaw, (float)roll),
                ScaleZ   = (float)(length + startExt + endExt)
            };
        }

        private double JointExtension(Vec3f tangent, double yaw, double pitch, bool atStart)
        {
            double tangentYaw   = Math.Atan2(tangent.X, tangent.Z);
            double tangentPitch = Math.Atan2(tangent.Y, Math.Sqrt(tangent.X * tangent.X + tangent.Z * tangent.Z));

            // Рейка обертається навколо підошви, тому щілина зверху буває лише на опуклому зламі ухилу
            double pitchDiff = atStart ? tangentPitch - pitch : pitch - tangentPitch;
            // По горизонталі вісь обертання посередині, тому зовнішній бік розходиться при будь-якому знаку
            double yawDiff = Math.Abs(GameMath.AngleRadDistance((float)yaw, (float)tangentYaw));

            return railHeight * Math.Tan(Math.Max(0, pitchDiff)) + railHalfWidth * Math.Tan(yawDiff);
        }
    }
}
