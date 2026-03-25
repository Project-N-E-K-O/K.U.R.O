using Godot;
using System.Collections.Generic;
using Kuros.Actors.Heroes.Attacks;

namespace Kuros.Actors.Heroes.States
{
	public partial class PlayerAttackState : PlayerState
	{

		public float AttackAnimationSpeed = 1.2f;
		private float _originalSpeedScale = 1.0f;
		
		private readonly List<PlayerAttackTemplate> _attackTemplates = new();
		private PlayerAttackTemplate? _activeTemplate;

		protected override void _ReadyState()
			{
			base._ReadyState();

			foreach (Node child in GetChildren())
			{
				if (child is PlayerAttackTemplate template)
			{
					template.Initialize(Player);
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
			Player.Velocity = Vector2.Zero;
			AlignFacingToInput();
			
			// Save original speed scale before modifying
			if (Actor.AnimPlayer != null)
			{
				_originalSpeedScale = Actor.AnimPlayer.SpeedScale;
				// Set animation playback speed only for attack animation
				Actor.AnimPlayer.SpeedScale = AttackAnimationSpeed;
			}

			if (!TryStartTemplateAttack())
			{
				ChangeState("Idle");
			}
		}

		public override void Exit()
		{
			_activeTemplate?.Cancel(clearCooldown: true);
			_activeTemplate = null;
			
			// Restore original animation speed when leaving attack state
			if (Actor.AnimPlayer != null)
			{
				Actor.AnimPlayer.SpeedScale = _originalSpeedScale;
			}
		}

		public override void PhysicsUpdate(double delta)
		{
			if (_activeTemplate == null)
			{
				ChangeState("Idle");
				return;
			}

			_activeTemplate.Tick(delta);

			Player.MoveAndSlide();
			Player.ClampPositionToScreen();

			if (!_activeTemplate.IsRunning)
			{
				ChangeState("Idle");
			}
		}
		
		private bool TryStartTemplateAttack()
		{
			string requestedState = Player.ConsumeAttackRequestSource();
			if (string.IsNullOrEmpty(requestedState))
			{
				requestedState = Player.LastMovementStateName;
			}

			foreach (var template in _attackTemplates)
			{
				template.SetTriggerSourceState(requestedState);
				if (!template.HasWeaponRequirement || !template.IsWeaponRequirementSatisfied())
				{
					continue;
				}

				if (template.TryStart(checkInput: false))
				{
					_activeTemplate = template;
					return true;
				}
			}

			foreach (var template in _attackTemplates)
			{
				template.SetTriggerSourceState(requestedState);
				if (template.HasWeaponRequirement)
				{
					continue;
				}

				if (template.TryStart(checkInput: false))
				{
					_activeTemplate = template;
					return true;
			}
			}

			return false;
		}

		private void AlignFacingToInput()
		{
			Vector2 input = GetMovementInput();
			if (Mathf.Abs(input.X) > 0.01f)
			{
				Player.FlipFacing(input.X > 0f);
			}
		}
	}
}
