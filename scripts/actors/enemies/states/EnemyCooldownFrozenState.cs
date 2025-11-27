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

			_timer -= (float)delta;
			if (_timer <= 0f && Enemy?.StateMachine != null)
			{
				Enemy.StateMachine.ChangeState("Idle");
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

