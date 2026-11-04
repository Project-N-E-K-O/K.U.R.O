using Godot;
using System.Collections.Generic;
namespace Kuros.Environments;

/// <summary>黄色外围：监听玩家/敌人身体进入与离开，激活附近的精确坠落检测。</summary>
[GlobalClass]
public partial class FallActivationArea : Area2D
{
    public const uint Layer = 1u << 17;
    public FallRegion? Region => FallRegion.FindRegion(this);
    internal static readonly HashSet<FallActivationArea> LiveAreas = new();
    public override void _Ready()
    {
        CollisionLayer = Layer;
        CollisionMask = 2 | 4; // 敌人与玩家身体层，不依赖其自身碰撞掩码。
        Monitoring = true;
        BodyEntered += Entered;
        BodyExited += Exited;
        LiveAreas.Add(this);
        FallDetectionGate.AreaAdded(this);
    }
    private void Entered(Node2D body) => FallDetectionGate.BodyChanged(this, body, true);
    private void Exited(Node2D body) => FallDetectionGate.BodyChanged(this, body, false);
    public override void _ExitTree()
    {
        BodyEntered -= Entered;
        BodyExited -= Exited;
        LiveAreas.Remove(this);
        FallDetectionGate.AreaRemoved(this);
    }
}
