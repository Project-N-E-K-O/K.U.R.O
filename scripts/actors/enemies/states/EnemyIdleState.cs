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

            // 先检查玩家是否在攻击范围内（这会刷新玩家引用）
            if (Enemy.IsPlayerInAttackRange() && Enemy.AttackTimer <= 0)
            {
                ChangeState("Attack");
                return;
            }

            // 检查玩家是否在检测范围内（这也会刷新玩家引用）
            if (Enemy.IsPlayerWithinDetectionRange())
            {
                ChangeState("Walk");
            }
        }
    }
}

