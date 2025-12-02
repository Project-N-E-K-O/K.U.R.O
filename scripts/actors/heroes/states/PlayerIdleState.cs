using Godot;
using System;

namespace Kuros.Actors.Heroes.States
{
	public partial class PlayerIdleState : PlayerState
	{
		public override void Enter()
		{
			Player.NotifyMovementState(Name);
			
			if (Actor.AnimPlayer != null)
			{
				// Reset bones first to avoid "stuck" poses from previous animations
				if (Actor.AnimPlayer.HasAnimation("RESET"))
				{
					Actor.AnimPlayer.Play("RESET");
					Actor.AnimPlayer.Advance(0); // Apply immediately
				}
				
				Actor.AnimPlayer.Play("animations/Idle");
				var anim = Actor.AnimPlayer.GetAnimation("animations/Idle");
				if (anim != null) anim.LoopMode = Animation.LoopModeEnum.Linear;
			}
			Actor.Velocity = Vector2.Zero;
		}

		public override void PhysicsUpdate(double delta)
		{
			if (HandleDialogueGating(delta)) return;
			
			// Check for transitions
			if (Input.IsActionJustPressed("attack") && Actor.AttackTimer <= 0)
			{
				Player.RequestAttackFromState(Name);
				ChangeState("Attack");
				return;
			}
			
			Vector2 input = GetMovementInput();
			if (input != Vector2.Zero)
			{
				if (Input.IsActionPressed("run"))
				{
					ChangeState("Run");
				}
				else
				{
					ChangeState("Walk");
				}
				return;
			}

			if (Input.IsActionJustPressed("take_up"))
			{
				ChangeState("PickUp");
				return;
			}
			
			if (Input.IsActionJustPressed("take_up"))
			{
				ChangeState("PickUp");
				return;
			}
			
			// Apply friction/stop
			Actor.Velocity = Actor.Velocity.MoveToward(Vector2.Zero, Actor.Speed * 2 * (float)delta);
			Actor.MoveAndSlide();
			Actor.ClampPositionToScreen();
		}
	}
}
