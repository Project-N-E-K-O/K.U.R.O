using Godot;
using System;

namespace Kuros.Actors.Heroes.States
{
	/// <summary>
	/// MainCharacter 的待机状态，使用 Spine 动画
	/// </summary>
	public partial class MainCharacterIdleState : MainCharacterState
	{
		public float IdleAnimationSpeed { get; set; } = 1.0f;
		
		public override void Enter()
		{
			MainCharacter.NotifyMovementState(Name);
			
			// 播放 Spine 待机动画（使用 MainCharacter 的动画名称）
			PlayAnimation(MainCharacter.IdleAnimationName, true, IdleAnimationSpeed);
			
			Actor.Velocity = Vector2.Zero;
		}

		public override void PhysicsUpdate(double delta)
		{
			if (HandleDialogueGating(delta)) return;
			
			// Check for transitions
			if (Input.IsActionJustPressed("attack") && Actor.AttackTimer <= 0)
			{
				MainCharacter.RequestAttackFromState(Name);
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
			
			// Apply friction/stop
			Actor.Velocity = Actor.Velocity.MoveToward(Vector2.Zero, Actor.Speed * 2 * (float)delta);
			Actor.MoveAndSlide();
			Actor.ClampPositionToScreen();
		}
	}
}
