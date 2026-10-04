using Vintagestory.API.MathTools;

namespace RailWorld
{
    public class RWPartData
    {
        public Vec3f rotation;
        public int currentStage;
        public Vec4f light;

        public RWPartData(Vec3f rotation)
        {
            this.rotation = rotation;
        }
    }
}
