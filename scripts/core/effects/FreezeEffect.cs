using Godot;
using Kuros.Actors.Enemies.Attacks;
using Kuros.Systems.FSM;

namespace Kuros.Core.Effects
{
    /// <summary>
    /// 冻结效果：施加后强制进入 Frozen 状态，移除时恢复为指定状态。
    /// </summary>
    public partial class FreezeEffect : ActorEffect
    {
        [Export] public string FrozenStateName = "Frozen";
        [Export] public string FallbackStateName = "Idle";

        [Export] public bool ResumePreviousState = true;

        private string _previousState = string.Empty;
        private EnemyAttackController? _cachedAttackController;

        protected override void OnApply()
        {
            base.OnApply();

            if (Actor.StateMachine == null) return;

            _previousState = Actor.StateMachine.CurrentState?.Name ?? FallbackStateName;
            //Actor.StateMachine.ChangeState(FrozenStateName);

            Actor.Velocity = Vector2.Zero;
            Actor.MoveAndSlide();
            if (Actor.AttackTimer < Duration)
            {
                Actor.AttackTimer = Duration;
            }

            // 只在移除时强制刷新攻击，避免重复抽取
        }

        public override void OnRemoved()
        {
            bool skipStateRecovery = Actor.IsDeathSequenceActive || Actor.IsDead;

            if (!skipStateRecovery && Actor.StateMachine != null)
            {
                var current = Actor.StateMachine.CurrentState?.Name;
                // 只在自然到期（仍处于 Frozen 状态）时才恢复状态并清除冷却。
                // 若已被命中等外部行为提前打断，则跳过，避免延迟触发多余的攻击循环。
                if (current == FrozenStateName)
                {
                    string targetState = FallbackStateName;
                    if (ResumePreviousState && !string.IsNullOrEmpty(_previousState) && _previousState != FrozenStateName)
                    {
                        targetState = _previousState;
                    }

                    Actor.AttackTimer = 0f;
                    Actor.StateMachine.ChangeState(targetState);
                    TryForceQueueNextAttack("FreezeRemoved");
                }
            }

            base.OnRemoved();
        }

        private void TryForceQueueNextAttack(string reason)
        {
            var controller = GetAttackController();
            controller?.ForceQueueNextAttack(reason);
        }

        private EnemyAttackController? GetAttackController()
        {
            if (_cachedAttackController != null && GodotObject.IsInstanceValid(_cachedAttackController))
            {
                return _cachedAttackController;
            }

            if (Actor == null) return null;

            var controller = Actor.GetNodeOrNull<EnemyAttackController>("StateMachine/Attack/AttackController");
            controller ??= Actor.GetNodeOrNull<EnemyAttackController>("AttackController");

            _cachedAttackController = controller;
            return _cachedAttackController;
        }
    }
}

