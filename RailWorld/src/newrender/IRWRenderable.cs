using Vintagestory.API.MathTools;

namespace RailWorld
{
    /// <summary>
    /// Інтерфейс для об'єктів що можуть рендеритись системою RailWorld.
    /// </summary>
    public interface IRWPartRenderable
    {
        Vec3d Position { get; }
        float AngleRad { get; }
    }
}
