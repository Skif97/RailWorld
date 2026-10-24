using System;
using System.Collections.Generic;
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
            var (x, y, z) = BlockOf(pos);
            return blocks.GetLightRGBs(x, y, z);
        }

        /// <summary>Те саме світло одним числом: щоб перевірити, чи воно змінилося, не створюючи об'єкта.</summary>
        public static int KeyOf(IBlockAccessor blocks, Vec3d pos)
        {
            var (x, y, z) = BlockOf(pos);
            return blocks.GetLightRGBsAsInt(x, y, z);
        }

        /// <summary>Блок, світло якого бере деталь у точці pos.</summary>
        public static (int X, int Y, int Z) BlockOf(Vec3d pos)
        {
            return ((int)Math.Floor(pos.X), (int)Math.Floor(pos.Y + Lift), (int)Math.Floor(pos.Z));
        }
    }

    /// <summary>
    /// Світло блоків на один такт перевірки освітлення. У блоці колії лежить зо півтора десятка деталей різних
    /// рендерів, і кожна питає світло того самого блока: тут кожен блок питається в гри лише раз за такт, а
    /// решта деталей беруть готове. Живе один такт, тож світло, що змінилося, наступний такт побачить одразу.
    /// </summary>
    internal class LightProbe
    {
        private readonly IBlockAccessor blocks;
        private readonly Dictionary<(int, int, int), int> keys = new Dictionary<(int, int, int), int>();
        private readonly Dictionary<(int, int, int), Vec4f> lights = new Dictionary<(int, int, int), Vec4f>();

        public LightProbe(IBlockAccessor blocks)
        {
            this.blocks = blocks;
        }

        /// <summary>Забуває запитане: новий такт, світло могло змінитися.</summary>
        public void Clear()
        {
            keys.Clear();
            lights.Clear();
        }

        /// <summary>Світло деталі в точці pos одним числом, як TrackLight.KeyOf.</summary>
        public int KeyOf(Vec3d pos)
        {
            var block = TrackLight.BlockOf(pos);
            if (!keys.TryGetValue(block, out int key)) keys[block] = key = blocks.GetLightRGBsAsInt(block.X, block.Y, block.Z);
            return key;
        }

        /// <summary>Світло деталі в точці pos, як TrackLight.Of. Той самий об'єкт для всіх деталей блока: його не міняють.</summary>
        public Vec4f Of(Vec3d pos)
        {
            var block = TrackLight.BlockOf(pos);
            if (!lights.TryGetValue(block, out Vec4f light)) lights[block] = light = blocks.GetLightRGBs(block.X, block.Y, block.Z);
            return light;
        }
    }
}
