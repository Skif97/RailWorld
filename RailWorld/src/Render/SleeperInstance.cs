using Vintagestory.API.MathTools;

namespace RailWorld
{
    public class SleeperInstance
    {
        public Vec3f rotation;
        public Vec4f light;
        // Довжина шпали впоперек колії. Модель завдовжки з блок, її розтягує матриця
        public float length;
        // Чанк, у даних якого лежить секція цієї шпали. Не обов'язково той, де стоїть сама шпала
        public Vec3i chunk;

        public SleeperInstance(Vec3f rotation, float length, Vec3i chunk)
        {
            this.rotation = rotation;
            this.length = length;
            this.chunk = chunk;
        }
    }
}
