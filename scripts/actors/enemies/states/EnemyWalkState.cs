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
            // IsPlayerWithinDetectionRange 会刷新玩家引用，同时检查玩家是否在范围内
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

            // 若有外部移动组件（EnemyChaseMovement）负责速度与 MoveAndSlide，此处仅处理状态转换
            if (Enemy.HasMeta("__movement_component_registered"))
                return;

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

