using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace RailWorld
{
    /// <summary>
    /// Світло, яким освітлено деталь колії: світло блока, де вона лежить. Рейки, шпали й деталі стрілок беруть
    /// його однаково, тож деталі поруч мають одне світло.
    /// </summary>
    internal static class TrackLight
    {
        // Світло береться на чверть блока вище за точку деталі
        private const double Lift = 0.25;

        /// <summary>Світло в точці: rgb це світло від джерел, a сонячне.</summary>
        public static Vec4f Of(IBlockAccessor blocks, Vec3d pos)
        {
            return blocks.GetLightRGBs((int)Math.Floor(pos.X), (int)Math.Floor(pos.Y + Lift), (int)Math.Floor(pos.Z));
        }

        /// <summary>Те саме світло одним числом: щоб перевірити, чи воно змінилося, не створюючи об'єкта.</summary>
        public static int KeyOf(IBlockAccessor blocks, Vec3d pos)
        {
            return blocks.GetLightRGBsAsInt((int)Math.Floor(pos.X), (int)Math.Floor(pos.Y + Lift), (int)Math.Floor(pos.Z));
        }
    }
}
