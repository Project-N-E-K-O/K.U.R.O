using Godot;
namespace Kuros.Environments;

/// <summary>对象自己的地面资格和坠落前清理；不把平台携带资格当作坠落资格。</summary>
public interface IGroundFallReceiver
{
    bool CanFall { get; }
    Vector2 FallGroundPosition { get; }
    void PrepareForFall();
}
