using Godot;

namespace Kuros.Environments;

/// <summary>
/// 显式挂在地面效果下。首次物理帧（生成位置设置完成后）只尝试绑定一次，
/// 之后保持平台附着，不因平台边缘或区域信号顺序反复脱离。
/// </summary>
[GlobalClass]
public partial class PlatformEffectFollower : Node
{
    /// <summary>相对于本组件的效果根节点路径；默认父节点。整体移动视觉与伤害区域。</summary>
    [Export] public NodePath TargetPath { get; set; } = new("..");
    /// <summary>直接拖入用于平台检测的 Marker2D；未设置或已释放时使用效果根世界坐标。</summary>
    [Export] public Marker2D? GroundAnchor { get; set; }
    /// <summary>查询地面锚点时叠加的世界空间偏移，不移动效果本身。</summary>
    [Export] public Vector2 GroundOffset { get; set; }
    /// <summary>关闭时不自动绑定，可在落地并设置位置后显式调用 TryBindToPlatformBelow。</summary>
    [Export] public bool AutoBindOnFirstPhysicsFrame { get; set; } = true;
    /// <summary>平台释放或失去承托资格时销毁效果根；默认仅解除绑定并保留世界位置。</summary>
    [Export] public bool DestroyOnPlatformLost { get; set; }

    public FallMovingPlatform? BoundPlatform { get; private set; }
    private Node2D? _target;
    private bool _attempted;

    public override void _Ready()
    {
        _target = GetNodeOrNull<Node2D>(TargetPath);
        // 先绑定，再由平台（-100）更新位移；不在 Ready 读取尚未设置的生成坐标。
        ProcessPhysicsPriority = -200;
        if (_target == null)
        { GD.PushWarning($"[PlatformEffectFollower] {GetPath()}: TargetPath 未指向 Node2D。"); SetPhysicsProcess(false); }
        else SetPhysicsProcess(AutoBindOnFirstPhysicsFrame);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_attempted)
        {
            TryBindToPlatformBelow();
            return;
        }
        if (BoundPlatform != null && (!IsInstanceValid(BoundPlatform) || !BoundPlatform.CanAttachEffects))
            HandlePlatformLost();
    }

    /// <summary>生成坐标已设置或落地后调用。未命中则保留世界位置，不等待路过的平台。</summary>
    public bool TryBindToPlatformBelow()
    {
        Detach();
        _attempted = true;
        _target = GetNodeOrNull<Node2D>(TargetPath);
        if (_target == null || !_target.IsInsideTree() || _target.IsQueuedForDeletion()) return false;
        Vector2 foot = (IsInstanceValid(GroundAnchor) && !GroundAnchor!.IsQueuedForDeletion()
            ? GroundAnchor.GlobalPosition : _target.GlobalPosition) + GroundOffset;
        var selected = PlatformCarryPhysics.FindPlatformAt(_target.GetWorld2D(), foot);
        if (selected == null) return false;
        BoundPlatform = selected;
        selected.WorldDisplaced += OnPlatformDisplaced;
        selected.TreeExiting += HandlePlatformLost;
        SetPhysicsProcess(true);
        return true;
    }

    private void OnPlatformDisplaced(Vector2 motion)
    {
        if (!IsInstanceValid(_target) || _target!.IsQueuedForDeletion() || !_target.CanProcess()) return;
        if (!IsInstanceValid(BoundPlatform) || !BoundPlatform!.CanAttachEffects) { HandlePlatformLost(); return; }
        // 已经在平台变换子树下的普通节点天然随动，不再叠加一次；TopLevel 会截断继承。
        bool inheritsMotion = false;
        for (Node? node = _target; node != null; node = node.GetParent())
        {
            if (node == BoundPlatform) { inheritsMotion = true; break; }
            if (node is Node2D node2D && node2D.TopLevel) break;
        }
        if (!inheritsMotion && PlatformCarryPhysics.TryClaim(_target)) _target.GlobalPosition += motion;
    }

    /// <summary>解除附着，不改变父节点、朝向、缩放、位置、持续时间或攻击来源。</summary>
    public void Detach()
    {
        if (IsInstanceValid(BoundPlatform))
        {
            BoundPlatform!.WorldDisplaced -= OnPlatformDisplaced;
            BoundPlatform.TreeExiting -= HandlePlatformLost;
        }
        BoundPlatform = null;
        _attempted = true;
        SetPhysicsProcess(false);
    }

    private void HandlePlatformLost()
    {
        Detach();
        if (DestroyOnPlatformLost && IsInstanceValid(_target) && !_target!.IsQueuedForDeletion()) _target.QueueFree();
    }

    public override void _ExitTree() => Detach();
}
