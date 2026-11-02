using Godot;
namespace Kuros.Environments;

/// <summary>红色危险范围，默认独占物理层19。</summary>
[GlobalClass]
public partial class FallHazardArea : Area2D
{
    public const uint Layer = 1u << 18;
    public FallRegion? Region => FallRegion.FindRegion(this);
    public override void _Ready() { CollisionLayer = Layer; CollisionMask = 0; Monitoring = false; }
}
