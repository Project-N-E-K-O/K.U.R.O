using Godot;
using Kuros.Actors.Enemies;

namespace Kuros.Actors.Enemies.States
{
	/// <summary>
	/// rogueAI part2 的 up → down 过渡状态：本体血量跌破阈值时强制切入，动画控制器按状态名播
	/// up_to_down（恰好一次）；期间锁移动/出招并开超甲。到时先让本体进入 down 形态，再回
	/// <see cref="NextStateName"/>。过渡期间只允许退出到 Dying/Dead——攻击控制器强制切 Attack 会被拒绝。
	/// </summary>
	public partial class EnemyF1RogueAIPart2DownTransitionState : EnemyState
	{
		/// <summary>过渡时长（秒）：与骨架 up_to_down 的 0.8s 对齐。</summary>
		[Export(PropertyHint.Range, "0.1,5,0.05")] public float DurationSeconds { get; set; } = 0.8f;
		/// <summary>过渡结束后回到的状态。</summary>
		[Export] public string NextStateName { get; set; } = "Idle";
		/// <summary>过渡期间忽略受击硬直（免伤之外的第二道保险）。</summary>
		[Export] public bool EnableSuperArmor { get; set; } = true;

		private float _timer;
		private bool? _previousIgnoreHitStateOnDamage;

		public override void Enter()
		{
			Enemy.Velocity = Vector2.Zero;
			_timer = Mathf.Max(DurationSeconds, 0.01f);
			if (EnableSuperArmor)
			{
				_previousIgnoreHitStateOnDamage = Enemy.IgnoreHitStateOnDamage;
				Enemy.IgnoreHitStateOnDamage = true;
			}
		}

		public override void Exit()
		{
			Enemy.Velocity = Vector2.Zero;
			if (_previousIgnoreHitStateOnDamage.HasValue)
			{
				Enemy.IgnoreHitStateOnDamage = _previousIgnoreHitStateOnDamage.Value;
				_previousIgnoreHitStateOnDamage = null;
			}
		}

		public override bool CanExitTo(string nextStateName)
			=> _timer <= 0f || nextStateName == "Dying" || nextStateName == "Dead";

		public override void PhysicsUpdate(double delta)
		{
			Enemy.Velocity = Vector2.Zero;

			_timer -= (float)delta;
			if (_timer > 0f) return;

			if (Enemy is EnemyF1RogueAIPart2 part2)
				part2.EnterDownForm();

			var machine = Enemy.StateMachine;
			if (machine == null) return;
			if (!string.IsNullOrEmpty(NextStateName) && machine.HasState(NextStateName))
				ChangeState(NextStateName);
			else
				ChangeState("Idle");
		}
	}
}
