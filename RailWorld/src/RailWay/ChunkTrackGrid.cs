using System;
using System.Collections.Generic;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Секції колії одного чанка, розкладені по клітинках 4×4×4 блоки за серединою секції. Так гра шукає істот
    /// біля точки (EntityPartitioning): номер клітинки рахується прямо з координат, і пошук дивиться лише в ті
    /// клітинки, яких сягає запит. Клітинки зібрано по вісім у частини 8×8×8: колія займає тонкий шар чанка, і
    /// частина без жодної секції не займає пам'яті.
    /// Сітку тримає DataInChunk і міняє разом із секціями. У збереження вона не потрапляє: її складено з самих
    /// секцій, а геометрія секції після створення не міняється, тож застаріти сітці нема з чого.
    /// </summary>
    public class ChunkTrackGrid
    {
        /// <summary>Сторона клітинки, блоків.</summary>
        public const int CellSize = 4;
        private const int PartSize = 8;
        private const int PartsAcross = GlobalConstants.ChunkSize / PartSize;
        private const int CellsAcross = PartSize / CellSize;
        private const int CellsInChunk = GlobalConstants.ChunkSize / CellSize;

        private readonly List<Section>[][] parts = new List<Section>[PartsAcross * PartsAcross * PartsAcross][];

        public void Add(Section section)
        {
            Locate(Local(section), out int part, out int cell);
            List<Section>[] cells = parts[part] ?? (parts[part] = new List<Section>[CellsAcross * CellsAcross * CellsAcross]);
            (cells[cell] ?? (cells[cell] = new List<Section>())).Add(section);
        }

        /// <summary>Прибирає секцію. Повертає false, якщо її тут не було.</summary>
        public bool Remove(Section section)
        {
            Locate(Local(section), out int part, out int cell);
            List<Section> list = parts[part]?[cell];
            return list != null && list.Remove(section);
        }

        /// <summary>
        /// Додає в into усі секції з клітинок, яких торкається коробка від from до to у координатах чанка.
        /// Це клітинки цілком: точно відбирає той, хто питає.
        /// </summary>
        public void Collect(Vec3d from, Vec3d to, List<Section> into)
        {
            int fromX = CellOf(from.X), toX = CellOf(to.X);
            int fromY = CellOf(from.Y), toY = CellOf(to.Y);
            int fromZ = CellOf(from.Z), toZ = CellOf(to.Z);

            for (int y = fromY; y <= toY; y++)
            {
                for (int z = fromZ; z <= toZ; z++)
                {
                    for (int x = fromX; x <= toX; x++)
                    {
                        List<Section>[] cells = parts[PartIndex(x, y, z)];
                        List<Section> list = cells?[CellIndex(x, y, z)];
                        if (list != null) into.AddRange(list);
                    }
                }
            }
        }

        // Середина секції в координатах її чанка
        private static FastVec3d Local(Section section)
        {
            Vec3i chunk = section.ChunkAddres;
            int size = GlobalConstants.ChunkSize;
            return section.Center.Add(-chunk.X * size, -chunk.Y * size, -chunk.Z * size);
        }

        private static void Locate(FastVec3d local, out int part, out int cell)
        {
            int x = CellOf(local.X), y = CellOf(local.Y), z = CellOf(local.Z);
            part = PartIndex(x, y, z);
            cell = CellIndex(x, y, z);
        }

        // Номер клітинки вздовж осі. Чанк визначається відкиданням дробової частини (ModMath.FindChunk), тож
        // для від'ємних координат, яких у грі немає, а в тестах трапляються, середина виходить за межі чанка.
        // Такі секції лягають у крайню клітинку: пошук у ній їх знайде, бо запит обрізається так само
        private static int CellOf(double coordinate)
        {
            return GameMath.Clamp((int)Math.Floor(coordinate / CellSize), 0, CellsInChunk - 1);
        }

        private static int PartIndex(int x, int y, int z)
        {
            return ((y / CellsAcross) * PartsAcross + z / CellsAcross) * PartsAcross + x / CellsAcross;
        }

        private static int CellIndex(int x, int y, int z)
        {
            return ((y % CellsAcross) * CellsAcross + z % CellsAcross) * CellsAcross + x % CellsAcross;
        }
    }
}
