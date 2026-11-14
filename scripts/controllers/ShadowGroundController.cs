using Godot;
using Kuros.Environments;

namespace Kuros.Controllers;

/// <summary>只管理阴影的深坑遮隐，不使其所属角色坠落或接受平台位移。挂在 Shadow 下。</summary>
[GlobalClass]
public partial class ShadowGroundController : Node
{
    [Export] public bool HideOverPit { get; set; } = true;
    /// <summary>可选世界投影点；为空使用父级 Shadow 的世界原点。</summary>
    [Export] public Marker2D? GroundAnchor { get; set; }
    [Export] public Vector2 GroundOffset { get; set; }

    private Node2D _shadow = null!;
    private GroundContactSensor _sensor = null!;
    private bool _hidden;
    private float _savedAlpha;

    public override void _Ready()
    {
        SetPhysicsProcess(false);
        if (GetParent() is not Node2D shadow)
        {
            GD.PushWarning($"[ShadowGround] {GetPath()}: 必须放在 Shadow Node2D 下。");
            return;
        }
        _shadow = shadow;
        ProcessPhysicsPriority = 110; // 骨骼跟随、轨道和平台位移之后查询。
        _sensor = new GroundContactSensor { Name = "GroundSensor" };
        // 以阴影而非 actor 登记，避免替换本体的传感器或承载资格。
        _sensor.Configure(_shadow, GroundPosition, active =>
        {
            SetPhysicsProcess(active);
            if (!active) SetHidden(false);
        });
        AddChild(_sensor);
    }

    private Vector2 GroundPosition() =>
        (IsInstanceValid(GroundAnchor) && GroundAnchor!.IsInsideTree()
            ? GroundAnchor.GlobalPosition : _shadow.GlobalPosition) + GroundOffset;

    public override void _PhysicsProcess(double delta)
    {
        if (!IsInstanceValid(_shadow) || _shadow.IsQueuedForDeletion()) return;
        if (!HideOverPit) { SetHidden(false); return; }
        var ground = _sensor.SampleNow();
        SetHidden(ground.Hazard != null && !ground.Supported);
    }

    private void SetHidden(bool hidden)
    {
        if (!IsInstanceValid(_shadow)) return;
        var color = _shadow.SelfModulate;
        if (hidden)
        {
            // 只占用 Alpha；保留 RGB。隐藏期间外部写入的新透明度作为恢复值。
            if (!_hidden || !Mathf.IsZeroApprox(color.A)) _savedAlpha = color.A;
            color.A = 0;
        }
        else
        {
            if (!_hidden) return;
            if (Mathf.IsZeroApprox(color.A)) color.A = _savedAlpha;
        }
        _hidden = hidden;
        _shadow.SelfModulate = color;
    }

    /// <summary>外部传送完成后请求补查，与 actor 的地面刷新互不依赖。</summary>
    public void RefreshGroundDetection()
    {
        if (IsInstanceValid(_sensor) && _sensor.IsInsideTree()) _sensor.Refresh();
    }

    public override void _ExitTree() => SetHidden(false);
}
