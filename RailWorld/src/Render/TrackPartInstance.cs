using Vintagestory.API.MathTools;

namespace RailWorld
{
    internal class TrackPartInstance : InstancedPart
    {
        // У скільки разів модель розтягнута вздовж своєї осі X. Шпала й брус стрілки це та сама модель різної
        // довжини; решта деталей малюється як є
        public float Stretch;

        public TrackPartInstance(Vec3d position, Vec3f rotation, float stretch)
        {
            Position = position;
            Rotation = rotation;
            Stretch = stretch;
        }
    }
}
