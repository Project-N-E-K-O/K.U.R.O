using Godot;
using System.Collections.Generic;
namespace Kuros.Environments;

/// <summary>黄色外围标识：由所有对象自动创建的脚底探针监听，激活附近的精确坠落检测。</summary>
[GlobalClass]
public partial class FallActivationArea : Area2D
{
    public const uint Layer = 1u << 17;
    public FallRegion? Region => FallRegion.FindRegion(this);
    internal static readonly HashSet<FallActivationArea> LiveAreas = new();
    public override void _Ready()
    {
        CollisionLayer = Layer;
        CollisionMask = 0;
        Monitoring = false;
        Monitorable = true;
        LiveAreas.Add(this);
        FallDetectionGate.AreaAdded(this);
    }
    public override void _ExitTree()
    {
        LiveAreas.Remove(this);
        FallDetectionGate.AreaRemoved(this);
    }
}
