using Kuros.Systems.FSM;

namespace Kuros.Actors.Enemies.States;

/// <summary>强制地形中断；死亡立即结算，独立视觉替身不依赖敌人节点继续存活。</summary>
public partial class EnemyFallState : EnemyState, IPriorityInterruptState
{
    private EnemyFallController Controller => Enemy.GetNode<EnemyFallController>("FallController");
    public override bool CanEnterFrom(string? previous) => Controller.HasPendingFall && !Enemy.IsDeadOrDying && !Enemy.IsDead;
    public bool CanInterrupt(string? previous) => CanEnterFrom(previous);
    public override bool CanExitTo(string next) => next is "Dying" or "Dead";
    public override void Enter() => Controller.BeginFall();
}
