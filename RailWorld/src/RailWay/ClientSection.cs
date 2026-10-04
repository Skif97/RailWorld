using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace RailWorld.src.RailWay
{
    /// <summary>
    /// Клієнтське представлення секції рейок.
    /// Містить попередньо обчислені дані для рендеру.
    /// </summary>
    public class ClientSection
    {
        public Section ServerSection { get; }
        public Vec3d GlobalPosition { get; }
        public float Yaw { get; }

        public ClientSection(Section serverSection)
        {
            ServerSection = serverSection;
            GlobalPosition = serverSection.GetGlobalPos();
            Yaw = (float)System.Math.Atan2(
                serverSection.CenterTangent.X,
                serverSection.CenterTangent.Z);
        }
    }
}
