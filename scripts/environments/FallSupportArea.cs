using Godot;
namespace Kuros.Environments;

/// <summary>绿色平台表面。只提供承托资格；平台移动由独立组件负责。</summary>
[GlobalClass]
public partial class FallSupportArea : Area2D
{
    public const uint Layer = 1u << 19;
    [Export] public bool SupportEnabled { get; set; } = true;
    public override void _Ready() { CollisionLayer = Layer; CollisionMask = 0; Monitoring = false; }
}
