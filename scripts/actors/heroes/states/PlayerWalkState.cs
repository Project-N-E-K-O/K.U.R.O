using Godot;
using System;

namespace Kuros.Actors.Heroes.States
{
	public partial class PlayerWalkState : PlayerState
	{
		public override void Enter()
		{
			Player.NotifyMovementState(Name);
			if (Actor.AnimPlayer != null)
			{
				Actor.AnimPlayer.Play("animations/Walk");
				// Force loop mode just in case resource isn't set
				var anim = Actor.AnimPlayer.GetAnimation("animations/Walk");
				if (anim != null) anim.LoopMode = Animation.LoopModeEnum.Linear;
			}
		}

		public override void PhysicsUpdate(double delta)
		{
			// 如果对话正在进行，不处理玩家输入（但保留ESC和Space键给对话系统）
			if (!ShouldProcessPlayerInput())
			{
				// 对话中时，停止移动并切换到Idle状态
				Actor.Velocity = Actor.Velocity.MoveToward(Vector2.Zero, Actor.Speed * 2 * (float)delta);
				Actor.MoveAndSlide();
				if (Actor.Velocity.Length() < 1.0f)
				{
					ChangeState("Idle");
				}
				return;
			}
			
			if (Input.IsActionJustPressed("attack") && Actor.AttackTimer <= 0)
			{
			Player.RequestAttackFromState(Name);
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
