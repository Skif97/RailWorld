using Vintagestory.API.MathTools;

namespace RailWorld
{
    public class SleeperInstance
    {
        // Де шпала стоїть у світі
        public Vec3d position;
        public Vec3f rotation;
        public Vec4f light;
        // Те саме світло одним числом: за ним видно, чи воно змінилося
        public int lightKey;
        // У скільки разів модель розтягнута вздовж своєї осі X. Шпала й брус стрілки це та сама модель різної
        // довжини; решта деталей малюється як є
        public float stretch;
        // Чанк, у даних якого лежить секція цієї шпали. Не обов'язково той, де стоїть сама шпала
        public Vec3i chunk;

        public SleeperInstance(Vec3f rotation, float stretch, Vec3i chunk)
        {
            this.rotation = rotation;
            this.stretch = stretch;
            this.chunk = chunk;
        }
    }
}
