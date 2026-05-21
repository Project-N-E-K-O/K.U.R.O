using Godot;

namespace Kuros.Actors.Enemies.States
{
    /// <summary>
    /// WaiterA 专用 Attack 状态：在每轮攻击结束决定下一个状态时，
    /// 优先检查 Assist 条件，确保 Assist 优先级高于继续攻击。
    /// </summary>
    public partial class EnemyWaiterAAttackState : EnemyAttackState
    {
        /// <summary>
        /// 每个攻击动画结束后、尝试链接下一个攻击模板之前检查 Assist。
        /// 这是优先级拦截的关键点：只要 Assist 条件满足就立刻切换，不等所有模板 CD 耗尽。
        /// </summary>
        protected override bool TryStartTemplateAttack()
        {
            if (ShouldEnterAssistState())
            {
                ChangeState("Assist");
                GD.Print("WaiterA Assist：从 Attack 状态切换到 Assist 状态.");
                return true; // 返回 true 阻止 ProcessTemplateAttack 继续调用 ChangeToNextState
            }

            return base.TryStartTemplateAttack();
        }

        protected override void ChangeToNextState()
        {
            // 兜底：所有模板都在 CD 时仍检查 Assist
            if (ShouldEnterAssistState())
            {
                ChangeState("Assist");
                GD.Print("WaiterA Assist：从 Attack 状态切换到 Assist 状态.");
                return;
            }

            base.ChangeToNextState();
        }

        private bool ShouldEnterAssistState()
        {
            if (Enemy.StateMachine?.HasState("Assist") != true) return false;
            var assistState = Enemy.StateMachine?.GetNodeOrNull<EnemyAssistState>("Assist");
            return assistState?.ShouldTriggerAssist() == true;
        }
    }
}
