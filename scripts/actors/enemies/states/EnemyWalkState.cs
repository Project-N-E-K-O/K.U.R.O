using Godot;

namespace Kuros.Actors.Enemies.States
{
    public partial class EnemyWalkState : EnemyState
    {
        public override void Enter()
        {
            Enemy.AnimPlayer?.Play("animations/Walk");
        }

        public override void PhysicsUpdate(double delta)
        {
            if (!HasPlayer)
            {
                ChangeState("Idle");
                return;
            }

            if (!Enemy.IsPlayerWithinDetectionRange())
            {
                ChangeState("Idle");
                return;
            }

            if (Enemy.IsPlayerInAttackRange() && Enemy.AttackTimer <= 0)
            {
                ChangeState("Attack");
                return;
            }

            Vector2 direction = Enemy.GetDirectionToPlayer();
            Vector2 velocity = Enemy.Velocity;
            velocity = direction * Enemy.Speed;

            Enemy.Velocity = velocity;

            if (direction.X != 0)
            {
                Enemy.FlipFacing(direction.X > 0);
            }

            Enemy.MoveAndSlide();
            Enemy.ClampPositionToScreen();
        }
    }
}

