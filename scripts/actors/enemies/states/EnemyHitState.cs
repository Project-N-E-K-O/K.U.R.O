using Godot;

namespace Kuros.Actors.Enemies.States
{
    public partial class EnemyHitState : EnemyState
    {
        private const float STUN_DURATION = 0.3f;
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

