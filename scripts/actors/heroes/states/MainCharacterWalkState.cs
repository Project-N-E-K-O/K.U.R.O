using Godot;
using System;

namespace Kuros.Actors.Heroes.States
{
	/// <summary>
	/// MainCharacter 的行走状态，使用 Spine 动画
	/// </summary>
	public partial class MainCharacterWalkState : MainCharacterState
	{
		public float WalkAnimationSpeed { get; set; } = 1.5f;
		
		public override void Enter()
		{
			MainCharacter.NotifyMovementState(Name);
			
			// 播放 Spine 行走动画（使用 MainCharacter 的动画名称）
			PlayAnimation(MainCharacter.WalkAnimationName, true, WalkAnimationSpeed);
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
			
			// Check for run
			if (Input.IsActionPressed("run"))
			{
				ChangeState("Run");
				return;
			}
			
			Vector2 input = GetMovementInput();
			
			if (input == Vector2.Zero)
			{
				ChangeState("Idle");
				return;
			}
			
			// Movement Logic
			Vector2 velocity = Actor.Velocity;
			velocity.X = input.X * Actor.Speed;
			velocity.Y = input.Y * Actor.Speed;
			
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
