using Godot;
using Kuros.Core;
using Kuros.Environments;
using Kuros.Fx;
using System.Collections.Generic;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies;

/// <summary>地面敌人的深坑判定与表现；只需红区和承托区，无玩家安全历史或回岸逻辑。</summary>
[GlobalClass]
public partial class EnemyFallController : FallController
{
    /// <summary>当前状态名在此列表时免坠落；状态结束后留在坑内也会重新判定。</summary>
    [Export] public string[] ImmuneStates { get; set; } = System.Array.Empty<string>();
    [Export(PropertyHint.Layers2DPhysics)] public uint LootObstacleMask { get; set; } = 3;
    public bool HasPendingFall => _pending != null;
    private SampleEnemy _enemy = null!;
    private FallRegion? _pending;
    private bool _warnedMissingState;
    private readonly List<EnemyAttackTemplate> _attackModules = new();

    public override void _Ready()
    {
        _enemy = GetParent<SampleEnemy>();
        RefreshAttackModules();
        InitializeDetection(_enemy);
    }
    /// <summary>场景加载时缓存攻击模组；若运行时动态添加模组，在添加完成后再次调用。</summary>
    public void RefreshAttackModules()
    {
        _attackModules.Clear();
        CacheAttackModules(_enemy);
    }

    private void CacheAttackModules(Node node)
    {
        if (node is EnemyAttackTemplate attack) _attackModules.Add(attack);
        foreach (Node child in node.GetChildren()) CacheAttackModules(child);
    }

    private bool HasAttackFallImmunity()
    {
        var state = _enemy.StateMachine?.CurrentState;
        if (state == null) return false;
        foreach (var attack in _attackModules)
            if (IsInstanceValid(attack) && !attack.IsQueuedForDeletion() && attack.IsInsideTree()
                && state.IsAncestorOf(attack) && attack.IsRunning && attack.ProvidesFallImmunity) return true;
        return false;
    }

    protected override bool CanCheckFall() => base.CanCheckFall() && !_enemy.IsDeadOrDying && !_enemy.IsDead;
    protected override bool IsFallImmune()
    {
        if ((_enemy.ActiveImmunities & ImmunityFlags.Fall) != 0) return true;
        string? state = _enemy.StateMachine?.CurrentState?.Name;
        return System.Array.IndexOf(ImmuneStates, state) >= 0 || HasAttackFallImmunity();
    }

    protected override void TryStartFall(FallRegion region)
    {
        if (_enemy.StateMachine == null || !_enemy.StateMachine.HasState("Fall"))
        {
            if (!_warnedMissingState) GD.PushWarning($"[EnemyFall] {_enemy.Name}: 启用了坠落但未配置 Fall 状态。");
            _warnedMissingState = true;
            return;
        }
        // 结清旧合并伤害；若已正常死亡，不再额外结算坠落死亡。
        _enemy.PrepareForTerrainFall();
        if (_enemy.IsDeadOrDying || _enemy.IsDead) return;
        _pending = region;
        if (!_enemy.StateMachine.ChangeState("Fall")) _pending = null;
    }

    public void BeginFall()
    {
        if (_pending == null || IsFalling) return;
        IsFalling = true;
        Vector2? lootPosition = FindLootPosition(_pending);
        _enemy.Visible = false;
        _enemy.Velocity = Vector2.Zero;
        FallPhysics.DisableCollisions(_enemy);
        SpawnVisual(Mathf.Max(0.1f, _pending.FallDuration));
        // Attack.Exit 已取消攻击；禁用子树，避免本帧残余导航/动画回调继续运行。
        _enemy.ProcessMode = ProcessModeEnum.Disabled;
        _enemy.FinalizeFallDeath(lootPosition);
    }

    private void SpawnVisual(float duration)
        => FallPhysics.SpawnVisual(this, FallEffectScene, FallAnimationName,
            _enemy.GlobalPosition + FallEffectOffset, duration, _enemy.FacingRight);
    private Vector2? FindLootPosition(FallRegion region)
    {
        Vector2? best = null;
        var space = _enemy.GetWorld2D().DirectSpaceState;
        foreach (var path in region.FallbackMarkerPaths)
        {
            if (region.GetNodeOrNull<Node2D>(path) is not { } marker) continue;
            Vector2 point = marker.GlobalPosition;
            var unsafeArea = space.IntersectShape(new PhysicsShapeQueryParameters2D
            {
                Shape = new CircleShape2D { Radius = Mathf.Max(1, region.SafeDistance) },
                Transform = new Transform2D(0, point), CollisionMask = FallHazardArea.Layer | FallSupportArea.Layer,
                CollideWithAreas = true, CollideWithBodies = false
            }, 1);
            if (unsafeArea.Count > 0) continue;
            if (space.IntersectPoint(new PhysicsPointQueryParameters2D
            {
                Position = point, CollisionMask = LootObstacleMask, CollideWithBodies = true, CollideWithAreas = false,
                Exclude = new() { _enemy.GetRid() }
            }, 1).Count > 0) continue;
            if (!best.HasValue || point.DistanceSquaredTo(_enemy.GlobalPosition) < best.Value.DistanceSquaredTo(_enemy.GlobalPosition)) best = point;
        }
        if (!best.HasValue && _enemy.LootTable != null)
            GD.PushWarning($"[EnemyFall] {region.Name}: 无有效固定掉落点，本次不生成深坑内掉落物。");
        return best;
    }
}
