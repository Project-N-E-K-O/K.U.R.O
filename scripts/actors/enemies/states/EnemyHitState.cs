using Godot;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Actors.Enemies.States
{
    public partial class EnemyHitState : EnemyState
    {
        private const float STUN_DURATION = 0.2f;
        private float _stunTimer;
        private float _savedFrozenRemainingTime = 0f;

        public override void Enter()
        {
            _stunTimer = STUN_DURATION;
            Enemy.Velocity = Vector2.Zero;
            Enemy.AnimPlayer?.Play("animations/hit");

            // 检查是否从Frozen状态进入，并保存剩余时长
            if (Enemy.FrozenStateRemainingTime > 0f)
            {
                _savedFrozenRemainingTime = Enemy.FrozenStateRemainingTime;
                // 立即清空标志，防止后续重复使用
                Enemy.FrozenStateRemainingTime = 0f;
            }
        }

        public override void PhysicsUpdate(double delta)
        {
            _stunTimer -= (float)delta;

            Enemy.Velocity = Enemy.Velocity.MoveToward(Vector2.Zero, Enemy.Speed * (float)delta);
            Enemy.MoveAndSlide();

            if (_stunTimer > 0) return;

            // 若仍有活跃的 FreezeEffect，Hit 结束后转到该效果配置的目标状态
            var freezeEffect = Enemy.EffectController?.GetEffect<FreezeEffect>();
            if (freezeEffect != null)
            {
                ChangeState(freezeEffect.FrozenStateName);
                return;
            }

            // 若之前是从Frozen进入，且Frozen仍有剩余时长，则恢复Frozen
            if (_savedFrozenRemainingTime > 0f)
            {
                // 恢复到Frozen状态，由Frozen.Enter()从Enemy.FrozenStateRemainingTime恢复时长
                Enemy.FrozenStateRemainingTime = _savedFrozenRemainingTime;
                ChangeState("Frozen");
                _savedFrozenRemainingTime = 0f;
                return;
            }

            if (Enemy.IsPlayerWithinDetectionRange())
            {
                ChangeState("Walk");
            }
            else
            {
                ChangeState("Idle");
            }
        }
    }
}


