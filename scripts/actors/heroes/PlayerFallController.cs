using Godot;
using System.Collections.Generic;
using Kuros.Core;
using Kuros.Environments;
using Kuros.Fx;
using Kuros.Actors.Heroes.States;

namespace Kuros.Actors.Heroes;

/// <summary>按脚底查询区域，低频保存固定地面安全点，管理一次完整坠落。</summary>
[GlobalClass]
public partial class PlayerFallController : FallController
{
    [Export(PropertyHint.Range, "0.01,1,0.01")] public float SampleInterval { get; set; } = 0.1f;
    [Export] public string[] ImmuneStates { get; set; } = { "Dash" };
    [Export(PropertyHint.Layers2DPhysics)] public uint ObstacleMask { get; set; } = 3;
    public bool HasPendingFall => _pending != null;
    public bool Completing { get; private set; }
    public Vector2? LastSafePosition => _tracking != null && _safe.TryGetValue(_tracking, out var point) ? point : null;

    private MainCharacter _player = null!;
    private readonly Dictionary<FallRegion, Vector2> _safe = new();
    private readonly List<(CollisionObject2D Node, uint Layer, uint Mask)> _collisions = new();
    private FallRegion? _tracking;
    private FallRegion? _pending;
    private Node2D? _visual;
    private float _sampleTimer, _remaining;
    private Vector2 _returnPoint, _entryPoint;
    private bool _wasVisible;
    private bool _settlingFallDamage;
    private bool _hiddenByLethalFall;
    private bool _visibilityBeforeLethalFall;
    private float _damageRatio;
    private bool _warned;
    private float _duration;
    public PlayerFallController() { FallEnabled = true; }
    protected override bool IncludeActivation => true;
    public override void _Ready()
    {
        _player = GetParent<MainCharacter>();
        InitializeDetection(_player);
    }
    protected override bool CanCheckFall() => base.CanCheckFall() && !_player.IsDead && !_player.IsDeathSequenceActive;
    protected override void OnActivationChanged(bool active)
    { if (!active) { _tracking = null; _sampleTimer = 0; } }
    private Vector2 FootOffset => _player.GetNodeOrNull<Node2D>(GroundProbePath)?.GlobalPosition - _player.GlobalPosition ?? Vector2.Zero;
    private PhysicsDirectSpaceState2D Space => _player.GetWorld2D().DirectSpaceState;

    private Godot.Collections.Array<Godot.Collections.Dictionary> At(Vector2 foot, uint mask)
        => FallPhysics.At(_player.GetWorld2D(), foot, mask);

    private bool HasSupport(Vector2 foot)
    {
        foreach (var hit in At(foot, FallSupportArea.Layer))
            if (hit["collider"].AsGodotObject() is FallSupportArea support && PlatformCarryPhysics.IsSupportEnabled(support)) return true;
        return false;
    }

    protected override void OnGroundChecked(FallPhysics.GroundResult result, float delta)
    {
        var activation = result.Activation;
        bool supported = result.Supported;
        bool entered = activation != _tracking;
        _tracking = activation;
        if (entered)
            foreach (var oldRegion in new List<FallRegion>(_safe.Keys))
                if (!IsInstanceValid(oldRegion) || !oldRegion.IsInsideTree()) _safe.Remove(oldRegion);
        _sampleTimer -= (float)delta;
        if (activation != null && (entered || _sampleTimer <= 0))
        {
            _sampleTimer = Mathf.Max(0.01f, SampleInterval);
            if (!supported && IsSafe(activation, _player.GlobalPosition))
                _safe[activation] = _player.GlobalPosition;
        }
    }

    protected override bool IsFallImmune()
    {
        if (_player.ActiveImmunities.HasFlag(ImmunityFlags.Fall)) return true;
        string state = _player.StateMachine.CurrentState?.Name.ToString() ?? "";
        foreach (string immune in ImmuneStates) if (state == immune) return true;
        return _player.StateMachine.CurrentState is PlayerAttackState attack && attack.ProvidesFallImmunity;
    }
    public bool IsSafe(FallRegion region, Vector2 rootPosition)
    {
        if (!IsInstanceValid(region) || !region.IsInsideTree()) return false;
        Vector2 foot = rootPosition + FootOffset;
        bool inActivation = false;
        foreach (var hit in At(foot, FallActivationArea.Layer))
            if (hit["collider"].AsGodotObject() is FallActivationArea yellow && yellow.Region == region) inActivation = true;
        if (!inActivation || HasSupport(foot)) return false;
        using var circle = new CircleShape2D { Radius = Mathf.Max(1, region.SafeDistance) };
        var nearPit = Space.IntersectShape(new PhysicsShapeQueryParameters2D
        {
            Shape = circle, Transform = new Transform2D(0, foot), CollisionMask = FallHazardArea.Layer,
            CollideWithAreas = true, CollideWithBodies = false
        }, 1);
        if (nearPit.Count != 0) return false;
        // 使用玩家实际身体形状，保留旋转、缩放；不查询 AttackArea / HitArea。
        foreach (Node child in _player.GetChildren())
        {
            if (child is not CollisionShape2D body || body.Disabled || body.Shape == null) continue;
            Transform2D transform = body.GlobalTransform;
            transform.Origin += rootPosition - _player.GlobalPosition;
            if (Space.IntersectShape(new PhysicsShapeQueryParameters2D
            {
                Shape = body.Shape, Transform = transform, CollisionMask = ObstacleMask,
                CollideWithAreas = false, CollideWithBodies = true,
                Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() }
            }, 1).Count > 0) return false;
        }
        return true;
    }

    private bool ResolveReturn(FallRegion region, out Vector2 point)
    {
        bool hasRecord = _safe.TryGetValue(region, out point);
        Vector2 fallbackReference = hasRecord ? point : _entryPoint;
        if (hasRecord && IsSafe(region, point)) return true;
        bool found = false;
        float distance = float.PositiveInfinity;
        foreach (var path in region.FallbackMarkerPaths)
        {
            var marker = region.GetNodeOrNull<Node2D>(path);
            if (marker == null || !IsSafe(region, marker.GlobalPosition)) continue;
            float candidateDistance = marker.GlobalPosition.DistanceSquaredTo(fallbackReference);
            if (candidateDistance >= distance) continue;
            distance = candidateDistance; point = marker.GlobalPosition; found = true;
        }
        return found;
    }

    protected override void TryStartFall(FallRegion region)
    {
        _entryPoint = _player.GlobalPosition;
        if (!ResolveReturn(region, out _returnPoint))
        {
            if (!_warned) GD.PushWarning($"[Fall] {region.GetPath()}: 无有效安全点或兜底点，拒绝隐藏玩家；请配置 FallbackMarkerPaths。");
            _warned = true;
            return;
        }
        _warned = false;
        _player.PrepareForTerrainFall();
        if (_player.IsDead || _player.IsDeathSequenceActive) return;
        _pending = region;
        if (!_player.StateMachine.ChangeState("Fall")) _pending = null;
    }

    public void BeginFall()
    {
        if (_pending == null) return;
        IsFalling = true; Completing = false; _settlingFallDamage = false;
        _duration = Mathf.Max(0.1f, _pending.FallDuration);
        _remaining = _duration; _damageRatio = Mathf.Clamp(_pending.DamageRatio, 0, 1);
        _wasVisible = _player.Visible;
        _player.Visible = false; _player.Velocity = Vector2.Zero;
        _player.ConsumePendingHitKnockback();
        _player.ClearFallInputBuffers();
        CacheCollisions(_player);
        SpawnVisual();
    }

    private void CacheCollisions(Node node)
    {
        if (node is CollisionObject2D obj)
        {
            _collisions.Add((obj, obj.CollisionLayer, obj.CollisionMask));
            obj.CollisionLayer = 0; obj.CollisionMask = 0;
        }
        foreach (Node child in node.GetChildren()) CacheCollisions(child);
    }

    private void SpawnVisual()
    {
        if (FallEffectScene == null) GD.PushWarning("[Fall] 未配置动画替身，恢复流程仍会运行。");
        _visual = FallPhysics.SpawnVisual(this, FallEffectScene, FallAnimationName,
            _entryPoint + FallEffectOffset, _duration, _player.FacingRight, autoCleanup: false);
    }
    public void TickFall(float delta)
    {
        if (!IsFalling || Completing) return;
        _player.GlobalPosition = _entryPoint; // 旧抓取或位移回调不能拖动隐藏本体。
        _remaining -= delta;
        if (_remaining > 0) return;
        if (_pending == null || !IsInstanceValid(_pending) || !_pending.IsInsideTree())
        {
            GD.PushWarning("[Fall] 来源区域已卸载，取消坠落并恢复控制。");
            Completing = true; _player.StateMachine.ChangeState("Idle"); return;
        }
        if (!ResolveReturn(_pending, out _returnPoint))
        {
            if (!_warned) GD.PushWarning("[Fall] 所有回退点暂时失效；保持坠落保护，每0.1秒重试，请检查场景配置。");
            _warned = true; _remaining = 0.1f; return;
        }
        _player.GlobalPosition = _returnPoint; _player.Velocity = Vector2.Zero;
        Completing = true;
        // 扣血可能同步进入 Dying 并调用 EndFall，整个结算期间保持本体隐藏。
        _settlingFallDamage = true;
        _player.ApplyFallDamage(_damageRatio);
        _settlingFallDamage = false;
        if (IsFalling && !_player.IsDead && !_player.IsDeathSequenceActive) _player.StateMachine.ChangeState("Idle");
    }

    public void EndFall()
    {
        if (!IsFalling) return;
        bool lethalFall = _settlingFallDamage
            && (_player.CurrentHealth <= 0 || _player.IsDead || _player.IsDeathSequenceActive);
        if (lethalFall)
        {
            _hiddenByLethalFall = true;
            _visibilityBeforeLethalFall = _wasVisible;
        }
        _player.Visible = _wasVisible && !lethalFall;
        foreach (var entry in _collisions)
            if (IsInstanceValid(entry.Node)) { entry.Node.CollisionLayer = entry.Layer; entry.Node.CollisionMask = entry.Mask; }
        _collisions.Clear();
        if (IsInstanceValid(_visual)) _visual!.QueueFree();
        _visual = null; _pending = null; IsFalling = false; Completing = false;
        _settlingFallDamage = false;
        _player.ConsumePendingHitKnockback();
        _player.Velocity = Vector2.Zero; _player.ClearFallInputBuffers();
        RefreshDetectionAfterTeleport();
    }

    /// <summary>
    /// 统一复活流程确认成功、恢复生命与死亡标记并退出死亡状态后调用。
    /// 仅恢复坠落致死保留的可见性；不负责复活、切换状态或恢复生命。
    /// </summary>
    public void RestoreVisibilityAfterRevive()
    {
        if (!_hiddenByLethalFall || IsFalling || !IsInstanceValid(_player)) return;
        _hiddenByLethalFall = false;
        _player.Visible = _visibilityBeforeLethalFall;
    }

    /// <summary>外部传送/重生前调用。取消当前流程，不扣血，不解锁其他系统。</summary>
    public void ResetSafeHistory()
    {
        RefreshDetectionAfterTeleport();
        _safe.Clear(); _tracking = null; _sampleTimer = 0;
        if (IsFalling) { Completing = true; _player.StateMachine.ChangeState("Idle"); }
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(_player)) EndFall();
        _safe.Clear();
    }
}
