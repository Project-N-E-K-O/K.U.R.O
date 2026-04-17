using Godot;
using System;
using Kuros.Actors.Heroes;

namespace Kuros.Actors.Heroes.States
{
	/// <summary>
	/// 持握可投掷物品的待机状态
	/// 在此状态下可以投掷物品，移动则进入 RunHolding 状态
	/// </summary>
	public partial class PlayerIdleHoldingState : PlayerState
	{
		public float IdleHoldingAnimationSpeed = 1.0f;
		private float _originalSpeedScale = 1.0f;
		private PlayerItemInteractionComponent? _interaction;
		
		protected override void _ReadyState()
		{
			base._ReadyState();
			_interaction = Player.GetNodeOrNull<PlayerItemInteractionComponent>("ItemInteraction");
		}
		
		public override void Enter()
		{
			Player.NotifyMovementState(Name);
			//GD.Print($"[PlayerIdleHoldingState] 进入持握状态");
			
			// 播放持握动画
			if (Player is MainCharacter mainChar)
			{
				PlayAnimation(mainChar.IdleHoldingAnimationName, true, IdleHoldingAnimationSpeed);
			}
			else
			{
				if (Actor.AnimPlayer != null)
				{
					_originalSpeedScale = Actor.AnimPlayer.SpeedScale;
					PlayAnimation("animations/Idle", true, IdleHoldingAnimationSpeed);
				}
			}
			Actor.Velocity = Vector2.Zero;
		}
		
		public override void Exit()
		{
			if (Actor.AnimPlayer != null)
			{
				Actor.AnimPlayer.SpeedScale = _originalSpeedScale;
			}
		}

		public override void PhysicsUpdate(double delta)
		{
			if (HandleDialogueGating(delta)) return;

			// 检查是否还有可投掷物品
			var selectedStack = Player.InventoryComponent?.GetSelectedQuickBarStack();
			if (selectedStack == null || selectedStack.IsEmpty || !selectedStack.Item.IsThrowable)
			{
				//GD.Print($"[PlayerIdleHoldingState] 物品不可投掷或已消失，返回 Idle");
				ChangeState("Idle");
				return;
			}
			
			// 检查投掷按键
			if (Input.IsActionJustPressed("throw"))
			{
				//GD.Print($"[PlayerIdleHoldingState] 投掷按键被按下");
				ChangeState("Throw");
				return;
			}
			
			// 检查移动输入
			Vector2 input = GetMovementInput();
			if (input != Vector2.Zero)
			{
				//GD.Print($"[PlayerIdleHoldingState] 检测到移动输入，转换到 RunHolding");
				if (IsActionPressed("run"))
				{
					ChangeState("RunHolding");
				}
				else
				{
					ChangeState("RunHolding");
				}
				return;
			}

			// 检查攻击按键
			if (IsActionJustPressed("attack") && Actor.AttackTimer <= 0)
			{
              	Player.RequestAttackFromState(Name);
                ChangeState("Throw");
                return;
			}
			
			// 应用摩擦力
			Actor.Velocity = Actor.Velocity.MoveToward(Vector2.Zero, Actor.Speed * 2 * (float)delta);
			Actor.MoveAndSlide();
			Actor.ClampPositionToScreen();
		}
	}
}

