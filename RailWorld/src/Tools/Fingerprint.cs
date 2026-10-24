using System;
using Vintagestory.API.MathTools;

namespace RailWorld
{
    /// <summary>
    /// Відбиток вхідних даних: якщо він той самий, що минулого разу, те, що з них пораховано, теж те саме, і
    /// рахувати заново не треба. Числа змішуються по черзі (FNV-1a), без нових об'єктів, тож відбиток можна
    /// брати щокадру.
    /// </summary>
    internal static class Fingerprint
    {
        public const long Start = unchecked((long)14695981039346656037UL);

        public static long Mix(long hash, double value)
        {
            return unchecked((hash ^ BitConverter.DoubleToInt64Bits(value)) * 1099511628211L);
        }

        public static long Mix(long hash, long value)
        {
            return unchecked((hash ^ value) * 1099511628211L);
        }

        public static long Mix(long hash, bool value)
        {
            return Mix(hash, value ? 1 : 0);
        }

        public static long Mix(long hash, Vec3d value)
        {
            return value == null ? Mix(hash, 0) : Mix(Mix(Mix(hash, value.X), value.Y), value.Z);
        }

        // Довжина спершу: тоді null і порожній рядок дають різні відбитки
        public static long Mix(long hash, string value)
        {
            hash = Mix(hash, value?.Length ?? -1);
            if (value != null) foreach (char c in value) hash = Mix(hash, c);
            return hash;
        }
    }
}
