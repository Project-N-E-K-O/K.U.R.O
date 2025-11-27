using Godot;

namespace Kuros.Actors.Enemies.States
{
    public partial class EnemyIdleState : EnemyState
    {
        public override void Enter()
        {
            Enemy.Velocity = Vector2.Zero;
            Enemy.AnimPlayer?.Play("animations/Idle");
        }

        public override void PhysicsUpdate(double delta)
        {
            // Damp velocity to ensure enemy settles quickly.
            Enemy.Velocity = Enemy.Velocity.MoveToward(Vector2.Zero, Enemy.Speed * 2.0f * (float)delta);
            Enemy.MoveAndSlide();

            if (!HasPlayer) return;

            if (Enemy.IsPlayerInAttackRange() && Enemy.AttackTimer <= 0)
            {
                ChangeState("Attack");
                return;
            }

            if (Enemy.IsPlayerWithinDetectionRange())
            {
                ChangeState("Walk");
            }
        }
    }
}

