using Godot;

namespace Kuros.Environments;

/// <summary>公共检测入口：黄区激活/休眠、出生/传送补查、脚底查询。结算由具体对象控制器负责。</summary>
public abstract partial class FallController : Node
{
    [Export] public bool FallEnabled { get; set; }
    [Export] public NodePath GroundProbePath { get; set; } = new("GroundProbe");
    [Export] public PackedScene? FallEffectScene { get; set; }
    [Export] public string FallAnimationName { get; set; } = "fall";
    /// <summary>替身相对对象根节点的世界偏移，不影响脚底检测或回退点。</summary>
    [Export] public Vector2 FallEffectOffset { get; set; }
    public bool IsFalling { get; protected set; }
    protected Node2D Subject { get; private set; } = null!;
    private GroundContactSensor? _sensor;
    protected virtual bool IncludeActivation => false;
    protected virtual Vector2 GetGroundPosition() => PlatformCarryPhysics.GroundPosition(Subject, GroundProbePath);
    protected virtual bool CanCheckFall() => FallEnabled && !IsFalling && !Subject.IsQueuedForDeletion();
    protected virtual bool IsFallImmune() => false;
    protected virtual void OnGroundChecked(FallPhysics.GroundResult result, float delta) { }
    protected virtual void OnActivationChanged(bool active) { }
    protected abstract void TryStartFall(FallRegion region);

    protected void InitializeDetection(Node2D subject, bool useAreaProbe = false)
    {
        Subject = subject;
        ProcessPhysicsPriority = 100;
        SetPhysicsProcess(false);
        _sensor = new GroundContactSensor { Name = "GroundSensor" };
        _sensor.Configure(subject, GetGroundPosition, active =>
        { SetPhysicsProcess(active); OnActivationChanged(active); }, useAreaProbe, IncludeActivation);
        AddChild(_sensor);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!CanCheckFall()) return;
        var result = _sensor!.SampleNow();
        // 安全记录先于免疫判断：Dash/攻击免疫期间仍可记录固定岸边。
        OnGroundChecked(result, (float)delta);
        if (result.Hazard != null && !result.Supported && !IsFallImmune()) TryStartFall(result.Hazard);
    }

    /// <summary>外部传送/重生后补查；普通移动通过黄色区域信号激活。</summary>
    public void RefreshDetectionAfterTeleport()
    {
        if (IsInstanceValid(_sensor) && _sensor!.IsInsideTree()) _sensor.Refresh();
    }
}
