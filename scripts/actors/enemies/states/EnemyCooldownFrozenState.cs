using Godot;

namespace Kuros.Actors.Enemies.States
{
    /// <summary>
    /// 用于敌人攻击后摇或特殊冻结的次级冻结状态。
    /// </summary>
    public partial class EnemyCooldownFrozenState : EnemyState
    {
		[Export(PropertyHint.Range, "0.1,10,0.1")]
		public float Duration = 1.0f;

		[Export]
		public string AnimationName = "animations/idle";

		private float _timer;

		public override void Enter()
		{
			_timer = Duration;
			Enemy.Velocity = Vector2.Zero;

			if (Enemy.AnimPlayer != null && !string.IsNullOrEmpty(AnimationName))
			{
				if (Enemy.AnimPlayer.HasAnimation(PrimaryAnimation()))
				{
					Enemy.AnimPlayer.Play(PrimaryAnimation());
				}
			}
		}

		public override void PhysicsUpdate(double delta)
		{
			if (Enemy == null || !GodotObject.IsInstanceValid(Enemy))
			{
				return;
			}

			Enemy.Velocity = Vector2.Zero;
			Enemy.MoveAndSlide();

			if (_timer > 0f)
			{
				_timer -= (float)delta;
				if (_timer <= 0f && Enemy?.StateMachine != null)
				{
					// 根据玩家位置决定下一个状态，而不是总是切换到 Idle
					if (Enemy.IsPlayerWithinDetectionRange())
					{
						Enemy.StateMachine.ChangeState("Walk");
					}
					else
					{
						Enemy.StateMachine.ChangeState("Idle");
					}
				}
			}
		}

		private string PrimaryAnimation()
		{
			if (!string.IsNullOrEmpty(AnimationName))
			{
				return AnimationName;
			}

			return "animations/idle";
		}
    }
}

