using Godot;

namespace Kuros.Actors.Enemies.States;

/// <summary>
/// 炮台族（Cannon / Machinegun）受击：眩晕来源仅播 hit；其他来源完整播放 hit 后接恢复动画。
/// 一整轮 Hit 连击沿用首次来源，不能因 Hit 重入或眩晕到期改变恢复方式。
/// </summary>
public partial class EnemyF1RogueAITurretHitState : EnemyHitState
{
    [Export(PropertyHint.Range, "0.01,5,0.01")]
    public float FullHitDuration { get; set; } = 0.3333333f;

    [Export(PropertyHint.Range, "0.01,5,0.01")]
    public float StunRecoveryDuration { get; set; } = 0.6666667f;

    public string SourceState { get; private set; } = string.Empty;
    public bool EnteredFromStun => SourceState is "JarAssistStun" or "Frozen" or "CooldownFrozen";
    public bool UseStunRecovery => !EnteredFromStun;

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
