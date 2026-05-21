using Godot;

namespace Kuros.Actors.Enemies.States
{
    /// <summary>
    /// WaiterA 专用 Idle 状态：在标准 Idle 逻辑之前，
    /// 优先检查是否有低血量友方需要援助，有则直接进入 Assist。
    /// </summary>
    public partial class EnemyWaiterAIdleState : EnemyIdleState
    {
        public override void PhysicsUpdate(double delta)
        {
            if (Enemy.StateMachine?.HasState("Assist") == true && ShouldEnterAssistState())
            {
                ChangeState("Assist");
                GD.Print("WaiterA Assist:从 Idle 状态切换到 Assist 状态.");
                return;
            }

            base.PhysicsUpdate(delta);
        }

        private bool ShouldEnterAssistState()
        {
            var assistState = Enemy.StateMachine?.GetNodeOrNull<EnemyAssistState>("Assist");
            return assistState?.ShouldTriggerAssist() == true;
        }
    }
}
