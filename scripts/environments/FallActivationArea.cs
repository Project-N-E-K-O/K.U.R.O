using Godot;
namespace Kuros.Environments;

/// <summary>黄色范围。默认独占物理层18；无需监听 BodyEntered。</summary>
[GlobalClass]
public partial class FallActivationArea : Area2D
{
    public const uint Layer = 1u << 17;
    public FallRegion? Region => FallRegion.FindRegion(this);
    public override void _Ready() { CollisionLayer = Layer; CollisionMask = 0; Monitoring = false; }
}
