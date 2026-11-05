using System;
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
    /// <summary>相对敌人根节点。留空跟随敌人；特效保持世界朝向。
    /// 锚点不是骨架的一部分，会随朝向镜像（特效默认朝左：敌人朝右时镜像 X）。</summary>
    [Export] public NodePath EffectAnchorPath { get; set; } = new();
    /// <summary>特效偏移；特效默认朝左，敌人朝右（非 d_ 动画）时自动镜像 X。</summary>
    [Export] public Vector2 EffectOffset { get; set; } = Vector2.Zero;
    /// <summary>特效缩放；X 的符号由朝向决定——特效默认朝左，敌人朝右时自动水平反转。</summary>
    [Export] public Vector2 EffectScale { get; set; } = Vector2.One;
    [Export] public int EffectZOffset { get; set; } = -1;
    /// <summary>0 = 只生成一次（循环特效）；正数 = 每隔 N 秒替换一次，适配一次性素材。</summary>
    [Export(PropertyHint.Range, "0,10,0.1,or_greater")] public float EffectRepeatInterval { get; set; }

    public bool IsEmpowered { get; private set; }
    public double RemainingSeconds { get; private set; }
    private SampleEnemy? _enemy;
    private Node2D? _visual;
    private Node? _spineHelper;
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

        // Spine 的 C# 绑定不全：借 GDScript 桥读取"当前实际播放的动画名"（判断 d_ 前缀朝向）。
        var spineScript = GD.Load<GDScript>("res://scripts/utils/SpineWrapper.gd");
        if (spineScript != null)
        {
            _spineHelper = (Node)spineScript.New();
            AddChild(_spineHelper);
        }

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

        // 约定：特效素材默认**朝左**；敌人 base（非 d_）动画朝右，d_ 动画朝左。
        // 所以敌人朝右时才是需要镜像的一侧——锚点（非骨架的一部分）、偏移与缩放一起镜像。
        bool faceRight = ResolveFaceRight();
        Vector2 offset = EffectOffset;
        if (faceRight) offset.X = -offset.X;

        Vector2 anchorPosition = _enemy.GlobalPosition;
        if (anchor != null)
        {
            Vector2 rel = anchor.GlobalPosition - _enemy.GlobalPosition;
            if (faceRight) rel.X = -rel.X;
            anchorPosition = _enemy.GlobalPosition + rel;
        }

        _visual.GlobalPosition = anchorPosition + offset;
        _visual.GlobalRotation = 0;
        _visual.GlobalScale = new Vector2(Mathf.Abs(EffectScale.X) * (faceRight ? -1f : 1f), EffectScale.Y);
        _visual.ZIndex = Mathf.Clamp(_enemy.ZIndex + EffectZOffset, -4096, 4096);
    }

    /// <summary>按 Spine 当前实际播放的动画名判断朝向：`d_` 前缀 = 朝左。
    /// 读不到 Spine 状态（无骨架/尚未播放）时回退到敌人的 FacingRight。</summary>
    private bool ResolveFaceRight()
    {
        if (_spineHelper != null && _enemy != null)
        {
            string animName = _spineHelper.Call("get_current_animation_name", _enemy).AsString();
            if (!string.IsNullOrEmpty(animName))
                return !animName.StartsWith("d_", StringComparison.Ordinal);
        }
        return _enemy?.FacingRight ?? true;
    }

    public override void _ExitTree() => Clear();
}
