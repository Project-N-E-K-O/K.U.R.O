using Godot;
namespace Kuros.Environments;

/// <summary>对象自己的地面资格和坠落前清理；不把平台携带资格当作坠落资格。</summary>
public interface IGroundFallReceiver
{
    bool CanFall { get; }
    Vector2 FallGroundPosition { get; }
    void PrepareForFall();
    /// <summary>返回 true 表示对象自行管理隐藏后的生命周期，例如投掷武器等待归还。</summary>
    bool CompleteFall() => false;
}
