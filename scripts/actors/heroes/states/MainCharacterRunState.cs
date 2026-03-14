using Godot;
using System;

namespace Kuros.Actors.Heroes.States
{
	/// <summary>
	/// MainCharacter 的跑步状态，使用 Spine 动画
	/// </summary>
	public partial class MainCharacterRunState : MainCharacterState
	{
		public float RunAnimationSpeed { get; set; } = 2.0f;
		
		public override void Enter()
		{
			MainCharacter.NotifyMovementState(Name);
			
			// 播放 Spine 跑步动画（使用 MainCharacter 的动画名称）
			PlayAnimation(MainCharacter.RunAnimationName, true, RunAnimationSpeed);
		}

		public override void PhysicsUpdate(double delta)
		{
			if (HandleDialogueGating(delta)) return;
			
			if (Input.IsActionJustPressed("attack") && Actor.AttackTimer <= 0)
			{
				MainCharacter.RequestAttackFromState(Name);
				ChangeState("Attack");
				return;
			}
			
			// Stop running if shift is released
			if (!Input.IsActionPressed("run"))
			{
				ChangeState("Walk");
				return;
			}
			
			Vector2 input = GetMovementInput();
			
			if (input == Vector2.Zero)
			{
				ChangeState("Idle");
				return;
			}
			
			// Run Logic (使用 MainCharacter 的 RunSpeedMultiplier)
			Vector2 velocity = Actor.Velocity;
			float runSpeed = Actor.Speed * MainCharacter.RunSpeedMultiplier;
			velocity.X = input.X * runSpeed;
			velocity.Y = input.Y * runSpeed;
			
			Actor.Velocity = velocity;
			
			if (input.X != 0)
			{
				Actor.FlipFacing(input.X > 0);
			}
			
			Actor.MoveAndSlide();
			Actor.ClampPositionToScreen();
		}
	}
}
