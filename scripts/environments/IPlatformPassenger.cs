using Godot;

namespace Kuros.Environments;

/// <summary>有内部物理/投掷生命周期的物件自行决定承载资格并同步位置。</summary>
public interface IPlatformPassenger
{
    bool CanRidePlatform { get; }
    Vector2 PlatformGroundPosition { get; }
    void ApplyPlatformDisplacement(Vector2 displacement, uint obstacleMask);
}
