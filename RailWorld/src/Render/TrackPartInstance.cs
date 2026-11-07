using Vintagestory.API.MathTools;

namespace RailWorld
{
    internal class TrackPartInstance : InstancedPart
    {
        // У скільки разів модель розтягнута вздовж своєї осі X. Шпала й брус стрілки це та сама модель різної
        // довжини; решта деталей малюється як є
        public float Stretch;
        // У скільки разів модель товща (вісь Y, униз від верху) і ширша (вісь Z): шпала широкої колії
        public float Thickness, Breadth;

        public TrackPartInstance(Vec3d position, Vec3f rotation, float stretch, float thickness = 1f, float breadth = 1f)
        {
            Position = position;
            Rotation = rotation;
            Stretch = stretch;
            Thickness = thickness;
            Breadth = breadth;
        }
    }
}
