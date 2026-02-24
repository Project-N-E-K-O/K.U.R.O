using Godot;
using System.Collections.Generic;
using Kuros.Actors.Heroes.Attacks;

namespace Kuros.Actors.Heroes.States
{
	/// <summary>
	/// MainCharacter 的攻击状态，使用 Spine 动画
	/// </summary>
	public partial class MainCharacterAttackState : MainCharacterState
	{
		public float AttackAnimationSpeed { get; set; } = 1.2f;
		
		private readonly List<PlayerAttackTemplate> _attackTemplates = new();
		private PlayerAttackTemplate? _activeTemplate;

		protected override void _ReadyState()
		{
			base._ReadyState();

			foreach (Node child in GetChildren())
			{
				if (child is PlayerAttackTemplate template)
				{
					template.Initialize(MainCharacter);
					_attackTemplates.Add(template);
				}
			}

			if (_attackTemplates.Count == 0)
			{
				GD.PushWarning($"{Name}: No PlayerAttackTemplate found. Attach at least one attack to this state.");
			}
		}

		public override void Enter()
		{
			MainCharacter.Velocity = Vector2.Zero;
			
			if (!TryStartTemplateAttack())
			{
				ChangeState("Idle");
			}
		}

		public override void Exit()
		{
			_activeTemplate?.Cancel(clearCooldown: true);
			_activeTemplate = null;
		}

		public override void PhysicsUpdate(double delta)
		{
			if (_activeTemplate == null)
			{
				ChangeState("Idle");
				return;
			}

			_activeTemplate.Tick(delta);

			MainCharacter.MoveAndSlide();
			MainCharacter.ClampPositionToScreen();

			if (!_activeTemplate.IsRunning)
			{
				_activeTemplate = null;
				ChangeState("Idle");
			}
		}
		
		private bool TryStartTemplateAttack()
		{
			string requestedState = MainCharacter.ConsumeAttackRequestSource();
			if (string.IsNullOrEmpty(requestedState))
			{
				requestedState = MainCharacter.LastMovementStateName;
			}

			foreach (var template in _attackTemplates)
			{
				template.SetTriggerSourceState(requestedState);
				if (template.TryStart(checkInput: false))
				{
					_activeTemplate = template;
					return true;
				}
			}

			return false;
		}
	}
}
