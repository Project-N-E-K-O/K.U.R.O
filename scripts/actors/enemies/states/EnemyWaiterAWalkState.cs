using Godot;

namespace Kuros.Actors.Enemies.States
{
    /// <summary>
    /// WaiterA 专用 Walk 状态：在标准追踪行为基础上，
    /// 检测到范围内有低血量友方时立刻切入 Assist 状态。
    /// </summary>
    public partial class EnemyWaiterAWalkState : EnemyWalkState
    {
        public override void PhysicsUpdate(double delta)
        {
            if (Enemy.StateMachine?.HasState("Assist") == true && ShouldEnterAssistState())
            {
                ChangeState("Assist");
                GD.Print("WaiterA Assist:从 Walk 状态切换到 Assist 状态.");
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
