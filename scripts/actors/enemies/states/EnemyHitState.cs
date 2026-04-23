using Godot;
using Kuros.Core.Effects;

namespace Kuros.Actors.Enemies.States
{
    public partial class EnemyHitState : EnemyState
    {
        private const float STUN_DURATION = 0.2f;
        private float _stunTimer;

        public override void Enter()
        {
            _stunTimer = STUN_DURATION;
            Enemy.Velocity = Vector2.Zero;
            Enemy.AnimPlayer?.Play("animations/hit");
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

