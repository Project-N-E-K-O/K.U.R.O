using Godot;
using System.Collections.Generic;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies.States
{
    public partial class EnemyAttackState : EnemyState
    {
        private readonly List<EnemyAttackTemplate> _attackTemplates = new();
        private EnemyAttackTemplate? _activeTemplate;

        protected override void _ReadyState()
        {
            base._ReadyState();

            foreach (Node child in GetChildren())
            {
                if (child is EnemyAttackTemplate template)
                {
                    template.Initialize(Enemy);
                    _attackTemplates.Add(template);
                }
            }
        }

        public override void Enter()
        {
            Enemy.Velocity = Vector2.Zero;
			TryStartTemplateAttack();
        }

        public override void Exit()
        {
            _activeTemplate?.Cancel(clearCooldown: true);
            _activeTemplate = null;
        }

        public override void PhysicsUpdate(double delta)
        {
            // 使用 IsPlayerWithinDetectionRange 检查玩家，这会刷新玩家引用
            // 如果玩家不存在或不在范围内，切换到 Idle
            if (!Enemy.IsPlayerWithinDetectionRange() && !Enemy.IsPlayerInAttackRange())
            {
                ChangeState("Idle");
                return;
            }

			if (!ProcessTemplateAttack(delta))
            {
            ChangeToNextState();
			}
        }

        private bool TryStartTemplateAttack()
        {
            if (_attackTemplates.Count == 0) return false;

            _activeTemplate = SelectTemplate();
            if (_activeTemplate == null) return false;

            if (_activeTemplate.TryStart())
            {
                return true;
            }

            _activeTemplate = null;
            return false;
        }

        private EnemyAttackTemplate? SelectTemplate()
        {
            foreach (var template in _attackTemplates)
            {
                if (template.CanStart())
                {
                    return template;
                }
            }

            return null;
        }

        private bool ProcessTemplateAttack(double delta)
        {
			var template = _activeTemplate;
			if (template == null) return false;

            Enemy.MoveAndSlide();
            Enemy.ClampPositionToScreen();

			template.Tick(delta);
			if (template.IsRunning)
            {
                return true;
            }

            _activeTemplate = null;

            if (TryStartTemplateAttack())
            {
                return true;
            }

			if (Enemy.AttackTimer > 0f)
			{
				Enemy.Velocity = Vector2.Zero;
				Enemy.MoveAndSlide();
				return true;
			}

            ChangeToNextState();
            return true;
        }

        private void ChangeToNextState()
        {
            bool playerDetected = Enemy.IsPlayerWithinDetectionRange();
            bool playerInAttackRange = Enemy.IsPlayerInAttackRange();

            if (Enemy.AttackTimer > 0f)
            {
                if (playerDetected)
                {
                    ChangeState("Walk");
                }
                else
                {
                    ChangeState("Idle");
                }
                return;
            }

            if (!playerDetected)
            {
                ChangeState("Idle");
                return;
            }

            if (playerInAttackRange)
                {
                    ChangeState("Attack");
                }
                else
                {
                    ChangeState("Walk");
            }
        }
    }
}
