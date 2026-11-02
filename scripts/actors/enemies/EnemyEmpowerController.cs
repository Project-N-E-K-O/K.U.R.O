using Godot;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies;

/// <summary>限时强化与独立视觉。普通/Pro 选择读取 IsEmpowered，Hit 不会暂停或重置计时。</summary>
[GlobalClass]
public partial class EnemyEmpowerController : Node
{
    [Export(PropertyHint.Range, "0.1,120,0.1,or_greater")] public float Duration { get; set; } = 12f;
    [Export] public bool RefreshOnReapply { get; set; } = true;
    [Export] public bool ClearOnJarAssistStun { get; set; } = true;
    /// <summary>强化期间攻击间隔倍率；0.5 表示间隔减半。</summary>
    [Export(PropertyHint.Range, "0,3,0.05,or_greater")]
    public float AttackIntervalMultiplier { get; set; } = 0.5f;
    /// <summary>本体及机械轴移动速度倍率；不修改滑槽另一轴的独立速度。</summary>
    [Export(PropertyHint.Range, "0.1,5,0.05,or_greater")]
    public float MovementSpeedMultiplier { get; set; } = 1.5f;
    [Export] public PackedScene? EffectScene { get; set; }
    /// <summary>相对敌人根节点。留空跟随敌人；特效保持世界朝向。</summary>
    [Export] public NodePath EffectAnchorPath { get; set; } = new();
    [Export] public Vector2 EffectOffset { get; set; } = Vector2.Zero;
    [Export] public Vector2 EffectScale { get; set; } = Vector2.One;
    [Export] public int EffectZOffset { get; set; } = -1;
    /// <summary>0 = 只生成一次（循环特效）；正数 = 每隔 N 秒替换一次，适配一次性素材。</summary>
    [Export(PropertyHint.Range, "0,10,0.1,or_greater")] public float EffectRepeatInterval { get; set; }

    public bool IsEmpowered { get; private set; }
    public double RemainingSeconds { get; private set; }
    private SampleEnemy? _enemy;
    private Node2D? _visual;
    private double _repeatElapsed;
    private EnemyF1RogueAITurretAttackController? _attackController;
    private RailKeepDistanceBehavior? _keepDistance;
    private float _originalAttackInterval;
    private float _originalSpeed;
    private float _originalSpacingSpeed;
    private bool _modifiersApplied;

    public override void _Ready()
    {
        _enemy = GetParent() as SampleEnemy;
        SetProcess(false);
    }

    public void Activate()
    {
        if (_enemy == null || _enemy.IsDeadOrDying) return;
        if (IsEmpowered)
        {
            if (RefreshOnReapply) RemainingSeconds = Mathf.Max(Duration, 0.1f);
            return;
        }
        IsEmpowered = true;
        RemainingSeconds = Mathf.Max(Duration, 0.1f);
        _repeatElapsed = 0;
        ApplyModifiers();
        SpawnVisual();
        SetProcess(true);
    }

    public void Clear()
    {
        RestoreModifiers();
        IsEmpowered = false;
        RemainingSeconds = 0;
        if (_visual != null && IsInstanceValid(_visual)) _visual.QueueFree();
        _visual = null;
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        if (!IsEmpowered) return;
        if (_enemy == null || !IsInstanceValid(_enemy) || _enemy.IsDeadOrDying)
        {
            Clear();
            return;
        }
        RemainingSeconds -= delta;
        if (RemainingSeconds <= 0) { Clear(); return; }
        _repeatElapsed += delta;
        if (EffectRepeatInterval > 0 && _repeatElapsed >= EffectRepeatInterval)
        {
            _repeatElapsed = 0;
            SpawnVisual();
        }
        UpdateVisual();
    }

    private void SpawnVisual()
    {
        if (_visual != null && IsInstanceValid(_visual)) _visual.QueueFree();
        _visual = null;
        if (EffectScene == null || _enemy == null) return;
        var instance = EffectScene.Instantiate();
        if (instance is not Node2D visual)
        {
            instance.Free();
            GD.PushWarning($"{Name}: EffectScene 必须以 Node2D 为根。");
            return;
        }
        _visual = visual;
        _enemy.AddChild(visual);
        visual.TopLevel = true;
        visual.ZAsRelative = false;
        UpdateVisual();
    }

    private void ApplyModifiers()
    {
        if (_modifiersApplied || _enemy == null) return;
        _modifiersApplied = true;
        _originalSpeed = _enemy.Speed;
        _enemy.Speed = _originalSpeed * Mathf.Max(MovementSpeedMultiplier, 0.1f);
        _attackController = _enemy.GetNodeOrNull<EnemyF1RogueAITurretAttackController>("StateMachine/Attack/AttackController");
        if (_attackController != null)
        {
            _originalAttackInterval = _attackController.MinInterAttackDelay;
            _attackController.SetMinInterAttackDelay(_originalAttackInterval * Mathf.Max(AttackIntervalMultiplier, 0f));
        }
        // 沿轨组件 Speed > 0 时覆盖本体 Speed，需同时处理这一实际生效的上限。
        foreach (var child in _enemy.GetChildren())
        {
            if (child is not RailKeepDistanceBehavior spacing) continue;
            _keepDistance = spacing;
            _originalSpacingSpeed = spacing.Speed;
            if (spacing.Speed > 0f) spacing.Speed *= Mathf.Max(MovementSpeedMultiplier, 0.1f);
            break;
        }
    }

    private void RestoreModifiers()
    {
        if (!_modifiersApplied) return;
        _modifiersApplied = false;
        if (_enemy != null && IsInstanceValid(_enemy)) _enemy.Speed = _originalSpeed;
        if (_keepDistance != null && IsInstanceValid(_keepDistance)) _keepDistance.Speed = _originalSpacingSpeed;
        if (_attackController != null && IsInstanceValid(_attackController))
            _attackController.SetMinInterAttackDelay(_originalAttackInterval);
        _keepDistance = null;
        _attackController = null;
    }

    private void UpdateVisual()
    {
        if (_enemy == null || _visual == null || !IsInstanceValid(_visual)) return;
        var anchor = EffectAnchorPath.IsEmpty ? null : _enemy.GetNodeOrNull<Node2D>(EffectAnchorPath);
        _visual.GlobalPosition = (anchor ?? _enemy).GlobalPosition + EffectOffset;
        _visual.GlobalRotation = 0;
        _visual.GlobalScale = EffectScale;
        _visual.ZIndex = Mathf.Clamp(_enemy.ZIndex + EffectZOffset, -4096, 4096);
    }

    public override void _ExitTree() => Clear();
}
