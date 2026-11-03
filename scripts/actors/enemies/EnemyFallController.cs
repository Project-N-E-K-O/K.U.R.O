using Godot;
using Kuros.Core;
using Kuros.Environments;
using Kuros.Fx;
using System.Collections.Generic;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies;

/// <summary>地面敌人的深坑判定与表现；只需红区和承托区，无玩家安全历史或回岸逻辑。</summary>
[GlobalClass]
public partial class EnemyFallController : Node
{
    [Export] public bool FallEnabled { get; set; } = false;
    [Export] public NodePath GroundProbePath { get; set; } = new("GroundProbe");
    /// <summary>当前状态名在此列表时免坠落；状态结束后留在坑内也会重新判定。</summary>
    [Export] public string[] ImmuneStates { get; set; } = System.Array.Empty<string>();
    [Export] public PackedScene? FallEffectScene { get; set; }
    [Export] public string FallAnimationName { get; set; } = "fall";
    /// <summary>替身相对敌人踩空位置的世界空间偏移，不随敌人缩放或朝向变化。</summary>
    [Export] public Vector2 FallEffectOffset { get; set; }
    [Export(PropertyHint.Layers2DPhysics)] public uint LootObstacleMask { get; set; } = 3;
    public bool IsFalling { get; private set; }
    public bool HasPendingFall => _pending != null;
    private SampleEnemy _enemy = null!;
    private FallRegion? _pending;
    private bool _warnedMissingState;
    private readonly List<EnemyAttackTemplate> _attackModules = new();

    public override void _Ready()
    {
        _enemy = GetParent<SampleEnemy>();
        ProcessPhysicsPriority = 100;
        RefreshAttackModules();
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

    public override void _PhysicsProcess(double delta)
    {
        if (!FallEnabled || IsFalling || _enemy.IsDeadOrDying || _enemy.IsDead || _enemy.IsQueuedForDeletion()) return;
        if ((_enemy.ActiveImmunities & ImmunityFlags.Fall) != 0) return;
        string? state = _enemy.StateMachine?.CurrentState?.Name;
        if (System.Array.IndexOf(ImmuneStates, state) >= 0) return;
        if (HasAttackFallImmunity()) return;
        var hits = _enemy.GetWorld2D().DirectSpaceState.IntersectPoint(new PhysicsPointQueryParameters2D
        {
            Position = PlatformCarryPhysics.GroundPosition(_enemy, GroundProbePath),
            CollisionMask = FallHazardArea.Layer | FallSupportArea.Layer,
            CollideWithAreas = true, CollideWithBodies = false
        }, 128);
        FallRegion? region = null;
        foreach (var hit in hits)
        {
            var area = hit["collider"].AsGodotObject();
            if (area is FallSupportArea support && PlatformCarryPhysics.IsSupportEnabled(support)) return;
            if (area is FallHazardArea hazard && FallRegion.FindRegion(hazard) is { } candidate
                && (region == null || candidate.GetInstanceId() < region.GetInstanceId())) region = candidate;
        }
        if (region == null) return;
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
        DisableCollisions(_enemy);
        SpawnVisual(Mathf.Max(0.1f, _pending.FallDuration));
        // Attack.Exit 已取消攻击；禁用子树，避免本帧残余导航/动画回调继续运行。
        _enemy.ProcessMode = ProcessModeEnum.Disabled;
        _enemy.FinalizeFallDeath(lootPosition);
    }

    private static void DisableCollisions(Node node)
    {
        if (node is CollisionObject2D body) { body.CollisionLayer = 0; body.CollisionMask = 0; }
        foreach (Node child in node.GetChildren()) DisableCollisions(child);
    }

    private void SpawnVisual(float duration)
    {
        if (FallEffectScene == null) return;
        var instance = FallEffectScene.Instantiate();
        if (instance is not Node2D visual)
        {
            instance.Free();
            GD.PushWarning("[EnemyFall] 坠落替身根节点必须是 Node2D。");
            return;
        }
        if (visual is DirectionalSceneEffect effect)
        { effect.AnimationName = FallAnimationName; effect.FacingRight = _enemy.FacingRight; }
        Vector2 position = _enemy.GlobalPosition + FallEffectOffset;
        visual.TopLevel = true;
        visual.Visible = true;
        visual.Position = position;
        GetTree().CurrentScene.AddChild(visual);
        visual.GlobalPosition = position;
        var animator = visual.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
        if (animator != null && animator.HasAnimation(FallAnimationName))
        { animator.SpeedScale = (float)animator.GetAnimation(FallAnimationName).Length / duration; animator.Play(FallAnimationName); }
        // Tween 绑定替身而非敌人：敌人立即释放，替身仍播放到期；缺少动画也能回收。
        var cleanup = visual.CreateTween();
        cleanup.TweenInterval(duration);
        cleanup.TweenCallback(Callable.From(visual.QueueFree));
    }

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
