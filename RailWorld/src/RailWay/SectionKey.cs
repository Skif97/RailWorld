using System;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Яка це секція у світі: чанк і номер у ньому. Нею впізнають секцію в множинах і словниках, і нею ж
    /// впізнають перетин: його запис лежить у чанку перетнутої секції основної колії під її номером.
    /// </summary>
    public readonly struct SectionKey : IEquatable<SectionKey>
    {
        public readonly int ChunkX, ChunkY, ChunkZ;
        public readonly int Index;

        public SectionKey(int chunkX, int chunkY, int chunkZ, int index)
        {
            ChunkX = chunkX;
            ChunkY = chunkY;
            ChunkZ = chunkZ;
            Index = index;
        }

        public SectionKey(Vec3i chunk, int index) : this(chunk.X, chunk.Y, chunk.Z, index) { }

        public static SectionKey Of(Section section)
        {
            return new SectionKey(section.ChunkAddres, section.IndexInChunk);
        }

        /// <summary>Секція, на яку веде посилання.</summary>
        public static SectionKey Of(SectionLink link)
        {
            return new SectionKey(link.ChunkX, link.ChunkY, link.ChunkZ, link.Index);
        }

        public bool Equals(SectionKey other)
        {
            return Index == other.Index && ChunkX == other.ChunkX && ChunkY == other.ChunkY && ChunkZ == other.ChunkZ;
        }

        public override bool Equals(object obj)
        {
            return obj is SectionKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ChunkX;
                hash = hash * 397 ^ ChunkY;
                hash = hash * 397 ^ ChunkZ;
                return hash * 397 ^ Index;
            }
        }

        public static bool operator ==(SectionKey a, SectionKey b) => a.Equals(b);
        public static bool operator !=(SectionKey a, SectionKey b) => !a.Equals(b);

        public override string ToString()
        {
            return ChunkX + "_" + ChunkY + "_" + ChunkZ + "_" + Index;
        }
    }

    /// <summary>
    /// Кінець секції у світі. Ним впізнають стрілку: колія розходиться з кінця спільної секції, і запис стрілки
    /// лежить у її чанку під ключем RailSwitch.KeyOf цього кінця.
    /// </summary>
    public readonly struct SectionEndKey : IEquatable<SectionEndKey>
    {
        public readonly SectionKey Section;
        public readonly bool AtStart;

        public SectionEndKey(SectionKey section, bool atStart)
        {
            Section = section;
            AtStart = atStart;
        }

        public SectionEndKey(Vec3i chunk, int index, bool atStart) : this(new SectionKey(chunk, index), atStart) { }

        /// <summary>Стрілка, чий запис лежить у чанку chunk під ключем RailSwitch.Key.</summary>
        public static SectionEndKey OfSwitch(Vec3i chunk, int switchKey)
        {
            return new SectionEndKey(chunk, switchKey / 2, switchKey % 2 == 0);
        }

        public bool Equals(SectionEndKey other)
        {
            return AtStart == other.AtStart && Section.Equals(other.Section);
        }

        public override bool Equals(object obj)
        {
            return obj is SectionEndKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Section.GetHashCode() * 2 + (AtStart ? 0 : 1);
        }

        public static bool operator ==(SectionEndKey a, SectionEndKey b) => a.Equals(b);
        public static bool operator !=(SectionEndKey a, SectionEndKey b) => !a.Equals(b);

        public override string ToString()
        {
            return Section + (AtStart ? "_S" : "_E");
        }
    }
}
