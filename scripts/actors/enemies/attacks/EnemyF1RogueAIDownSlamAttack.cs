using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
	/// <summary>
	/// rogueAI 近身下砸攻击（普通/Pro 共用）：给动画控制器暴露 Warmup/Active 相位与循环圈数，
	/// 并按 part2 控制器的**通用血量阈值**做普通/Pro 二选一（同一时刻只有一个能进选招池/起手）。
	/// 范围检测、伤害、击退全部继承 <see cref="EnemyF1RogueAITurretMeleeAttack"/>（机枪台/炮台同款）。
	/// </summary>
	public partial class EnemyF1RogueAIDownSlamAttack : EnemyF1RogueAITurretMeleeAttack
	{
		/// <summary>Active 段 up_to_down 的循环圈数（1 = 原版；Pro 配 3）。</summary>
		[Export(PropertyHint.Range, "1,10,1")] public int StrikeLoopCount { get; set; } = 1;
		/// <summary>true = 仅 Pro 解锁后可用；false = 仅 Pro 解锁前可用。</summary>
		[Export] public bool RequiresProUpgrade { get; set; }

		public bool IsWarmup => CurrentPhase == EnemyAttackTemplate.AttackPhase.Warmup;
		public bool IsActive => CurrentPhase == EnemyAttackTemplate.AttackPhase.Active;

		/// <summary>选招资格同样受血量门限制，避免另一个变体被随机选中后起不了手。</summary>
		public override bool IsPlayerInDetectionRange()
			=> IsUpgradeStateAllowed() && base.IsPlayerInDetectionRange();

		/// <summary>起手门：普通/Pro 变体按通用血量阈值二选一。</summary>
		public override bool CanStart()
			=> IsUpgradeStateAllowed() && base.CanStart();

		private bool IsUpgradeStateAllowed()
		{
			var controller = Enemy?.StateMachine?
				.GetNodeOrNull<EnemyF1RogueAIPart2AttackController>("Attack/AttackController");
			bool upgraded = controller != null && controller.IsProUnlocked;
			return RequiresProUpgrade == upgraded;
		}
	}
}
