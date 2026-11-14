using Godot;

namespace Kuros.Actors.Enemies.States;

/// <summary>
/// 炮台族（Cannon / Machinegun）受击：眩晕来源仅播 hit；其他来源完整播放 hit 后接恢复动画。
/// 非致死 Hit 连击沿用首次来源；致死时优先完整播放一次 hit，再直接进入死亡，跳过 stun_2。
/// </summary>
public partial class EnemyF1RogueAITurretHitState : EnemyHitState
{
    [Export(PropertyHint.Range, "0.01,5,0.01")]
    public float FullHitDuration { get; set; } = 0.3333333f;

    [Export(PropertyHint.Range, "0.01,5,0.01")]
    public float StunRecoveryDuration { get; set; } = 0.6666667f;

    public string SourceState { get; private set; } = string.Empty;
    public bool EnteredFromStun => SourceState is "JarAssistStun" or "Frozen" or "CooldownFrozen";
    public bool IsLethalHit => Enemy != null && (Enemy.CurrentHealth <= 0 || Enemy.IsDyingDeferred);
    public bool UseStunRecovery => !EnteredFromStun && !IsLethalHit;
    private bool _lethalHitStarted;
    private float _lethalHitRemaining;

    public override void Enter()
    {
        _lethalHitStarted = false;
        base.Enter();
    }

    public override void PhysicsUpdate(double delta)
    {
        if (!IsLethalHit) { base.PhysicsUpdate(delta); return; }
        // 致死连击即使被重入保护抑制、或发生在 stun_2 恢复中，也单独播完一次 hit 再死亡。
        // 炮台拒绝外部击退，无需等待通用 Hit 的位移/起身恢复时间轴。
        if (!_lethalHitStarted)
        {
            _lethalHitStarted = true;
            _lethalHitRemaining = Mathf.Max(FullHitDuration, 0.01f);
        }
        Enemy.Velocity = Vector2.Zero;
        _lethalHitRemaining -= (float)delta;
        if (_lethalHitRemaining <= 0f) Enemy.TryEnterDeferredDeath();
    }

    public override bool CanEnterFrom(string? currentStateName)
    {
        if (!base.CanEnterFrom(currentStateName)) return false;
        // 此时尚未切换 CurrentState，能准确获得来源；Hit -> Hit 不覆盖。
        if (currentStateName != "Hit") SourceState = currentStateName ?? string.Empty;
        return true;
    }

    protected override float ResolveImpactDuration()
        => UseStunRecovery ? Mathf.Max(FullHitDuration, 0.01f) : base.ResolveImpactDuration();

    protected override float ResolveRecoverDuration()
        => UseStunRecovery ? Mathf.Max(StunRecoveryDuration, 0.01f) : base.ResolveRecoverDuration();
}
