using Godot;

namespace Kuros.Actors.Enemies.States
{
    public partial class EnemyIdleState : EnemyState
    {
        public override void Enter()
        {
            Enemy.Velocity = Vector2.Zero;
            Enemy.AnimPlayer?.Play("animations/idle");
        }

        public override void PhysicsUpdate(double delta)
        {
            // Damp velocity to ensure enemy settles quickly.
            Enemy.Velocity = Enemy.Velocity.MoveToward(Vector2.Zero, Enemy.Speed * 2.0f * (float)delta);
            Enemy.MoveAndSlide();

            // 使用 CanStartAttack() 以兼容新的攻击CD系统（包含 _interAttackDelay 检查）
            if (Enemy.CanStartAttack())
            {
                ChangeState("Attack");
                return;
            }

            bool playerDetected = Enemy.IsPlayerWithinDetectionRange();
            bool playerInAttackRange = Enemy.IsPlayerInAttackRange();

            // 玩家在检测范围但不在攻击范围内：追击
            // 玩家在攻击范围（CD中）或不在检测范围：原地等待CD
            if (playerDetected && !playerInAttackRange)
            {
                ChangeState("Walk");
            }
        }
    }
}

