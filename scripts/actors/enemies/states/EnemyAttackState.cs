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
			if (!HasPlayer)
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
			if (Enemy.AttackTimer > 0f)
			{
				Enemy.Velocity = Vector2.Zero;
				Enemy.MoveAndSlide();
				return;
			}

			if (Enemy.IsPlayerWithinDetectionRange())
			{
				if (Enemy.IsPlayerInAttackRange())
				{
					ChangeState("Attack");
				}
				else
				{
					ChangeState("Walk");
				}
			}
			else
			{
				ChangeState("Idle");
			}
		}
	}
}
