using Godot;
using Kuros.Fx;

namespace Kuros.Environments;

/// <summary>
/// 物品、武器和地面特效的统一接入：物品按脚下承载，特效固定附着，共用地面检测。
/// 跟随与坠落默认开启；物品生命周期自动限制资格，普通特效飞行时需设置 Grounded=false。
/// </summary>
[GlobalClass]
public partial class GroundObjectController : Node, IPlatformPassenger
{
    [Signal] public delegate void FallStartedEventHandler();
    /// <summary>相对于本组件的效果根节点路径；默认父节点。整体移动视觉与伤害区域。</summary>
    [Export] public NodePath TargetPath { get; set; } = new("..");
    /// <summary>直接拖入用于平台检测的 Marker2D；未设置或已释放时使用效果根世界坐标。</summary>
    [Export] public Marker2D? GroundAnchor { get; set; }
    /// <summary>查询地面锚点时叠加的世界空间偏移，不移动效果本身。</summary>
    [Export] public Vector2 GroundOffset { get; set; }
    [Export] public bool FollowPlatformEnabled { get; set; } = true;
    /// <summary>关闭时不自动绑定，可在落地并设置位置后显式调用 TryBindToPlatformBelow。</summary>
    [Export] public bool AutoBindOnFirstPhysicsFrame { get; set; } = true;
    /// <summary>平台释放或失去承托资格时销毁效果根；默认仅解除绑定并保留世界位置。</summary>
    [Export] public bool DestroyOnPlatformLost { get; set; }
    /// <summary>仅对地面特效开启。无绿色承托且锚点在红区时，停用并释放 Target。</summary>
    [ExportGroup("Fall")]
    [Export] public bool FallEnabled { get; set; } = true;
    /// <summary>飞行阶段可关闭；落地后开启并调用 RefreshGroundDetection，避免空中经过深坑被销毁。</summary>
    [Export] public bool Grounded { get; set; } = true;
    /// <summary>可选独立替身；为空直接释放本体，不播放动画。</summary>
    [Export] public PackedScene? FallEffectScene { get; set; }
    [Export] public string FallAnimationName { get; set; } = "fall";
    /// <summary>相对于地面检测点的世界偏移，不继承目标缩放或旋转。</summary>
    [Export] public Vector2 FallEffectOffset { get; set; }

    public FallMovingPlatform? BoundPlatform { get; private set; }
    public bool IsFalling { get; private set; }
    private Node2D? _target;
    private GroundContactSensor _sensor = null!;
    private bool _attempted;
    private bool HasTarget => IsInstanceValid(_target) && _target!.IsInsideTree() && !_target.IsQueuedForDeletion();
    public Node2D PlatformTarget => _target!;
    public Vector2 PlatformGroundPosition => (IsInstanceValid(GroundAnchor) && GroundAnchor!.IsInsideTree()
        && !GroundAnchor.IsQueuedForDeletion() ? GroundAnchor.GlobalPosition
        : _target is IGroundFallReceiver receiver ? receiver.FallGroundPosition
        : _target is IPlatformPassenger passenger ? passenger.PlatformGroundPosition : _target!.GlobalPosition) + GroundOffset;
    public bool CanRidePlatform => HasTarget && !IsFalling && Grounded && FollowPlatformEnabled && _target!.CanProcess()
        && (_target is not IPlatformPassenger passenger || passenger.CanRidePlatform);

    public static void AttachTo(Node2D target)
    {
        foreach (Node child in target.GetChildren())
            if (child is GroundObjectController) return;
        target.AddChild(new GroundObjectController { Name = "GroundObjectController" });
    }

    public override void _Ready()
    {
        _target = GetNodeOrNull<Node2D>(TargetPath);
        ProcessPhysicsPriority = 100;
        if (_target == null)
        { GD.PushWarning($"[GroundObject] {GetPath()}: TargetPath 未指向 Node2D。"); SetPhysicsProcess(false); return; }
        _sensor = new GroundContactSensor { Name = "GroundSensor" };
        _sensor.Configure(_target, () => PlatformGroundPosition, _ => UpdateProcessing(), useProbe: true);
        AddChild(_sensor);
        UpdateProcessing();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!HasTarget || IsFalling) { SetPhysicsProcess(false); return; }
        if (!Grounded || !FollowPlatformEnabled)
        { if (BoundPlatform != null) Detach(); }
        if (!_attempted)
        {
            _attempted = true;
            if (AutoBindOnFirstPhysicsFrame) TryBindToPlatformBelow();
        }
        if (BoundPlatform != null && (!IsInstanceValid(BoundPlatform) || !BoundPlatform.CanAttachEffects)) HandlePlatformLost();
        if (!HasTarget) return;
        // 物品落地资格来自自身生命周期；地面滑行不等于飞行。
        if (FallEnabled && Grounded && _sensor.Active && (_target is not IGroundFallReceiver receiver || receiver.CanFall))
        {
            var result = _sensor.SampleNow();
            if (!result.Supported && result.Hazard != null) BeginFall(result.Hazard);
        }
        UpdateProcessing();
    }

    private void UpdateProcessing() => SetPhysicsProcess(HasTarget && !IsFalling
        && (!_attempted && AutoBindOnFirstPhysicsFrame || BoundPlatform != null
            || FallEnabled && IsInstanceValid(_sensor) && _sensor.Active));

    /// <summary>落地、传送、运行时启用功能后调用；物品按脚下承载，特效可显式重新附着。</summary>
    public void RefreshGroundDetection(bool bindPlatform = true)
    {
        if (!HasTarget || IsFalling) return;
        _sensor.Refresh();
        if (bindPlatform) TryBindToPlatformBelow();
        UpdateProcessing();
    }

    public bool TryBindToPlatformBelow()
    {
        Detach(); _attempted = true;
        if (!CanRidePlatform) return false;
        var selected = _sensor.SampleNow().Platform;
        if (selected == null) return false;
        // 物品每帧检查实际站立，继续由平台 world_items 路径携带，不能同时固定附着。
        if (_target is IPlatformPassenger) return true;
        BoundPlatform = selected;
        selected.RegisterAttachedPassenger(this);
        selected.TreeExiting += HandlePlatformLost;
        UpdateProcessing();
        return true;
    }

    public void ApplyPlatformDisplacement(Vector2 motion, uint obstacleMask)
    {
        if (!CanRidePlatform) return;
        if (_target is IPlatformPassenger passenger) { passenger.ApplyPlatformDisplacement(motion, obstacleMask); return; }
        for (Node? node = _target; node != null; node = node.GetParent())
        {
            if (node == BoundPlatform) return; // 已继承父平台位移。
            if (node is Node2D node2D && node2D.TopLevel) break;
        }
        _target!.GlobalPosition += motion;
    }

    public void Detach()
    {
        if (IsInstanceValid(BoundPlatform))
        { BoundPlatform!.UnregisterAttachedPassenger(this); BoundPlatform.TreeExiting -= HandlePlatformLost; }
        BoundPlatform = null; _attempted = true;
        UpdateProcessing();
    }
    private void HandlePlatformLost()
    {
        Detach();
        if (DestroyOnPlatformLost && HasTarget) _target!.QueueFree();
        else if (IsInstanceValid(_sensor) && _sensor.IsInsideTree()) _sensor.Refresh();
    }
    private void BeginFall(FallRegion region)
    {
        IsFalling = true;
        Vector2 position = PlatformGroundPosition + FallEffectOffset;
        Detach();
        if (_target is IGroundFallReceiver receiver) receiver.PrepareForFall();
        EmitSignal(SignalName.FallStarted);
        FallPhysics.SpawnVisual(this, FallEffectScene, FallAnimationName, position, region.FallDuration);
        if (HasTarget)
        { _target!.Visible = false; FallPhysics.DisableCollisions(_target, stopProcessing: true); _target.QueueFree(); }
    }
    public override void _ExitTree() { Detach(); SetPhysicsProcess(false); }
}
