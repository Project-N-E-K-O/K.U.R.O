using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
	/// <summary>
	/// rogueAI 二阶段普攻·砸地（RogueAISlamB / RogueAISlamBPro 共用）：普通近战本体，
	/// **不做隐身**（与 part1 的同名脚本区分，二阶段的表现交给场景特效）。
	/// 按 part2 控制器的**通用血量阈值**做普通/Pro 二选一（与 RogueAIDownSlam 同一套门）：
	/// 同一时刻只有一个变体进入选招池、可以起手。
	/// </summary>
	public partial class EnemyF1RogueAIPart2SlamB : EnemySimpleMeleeAttack
	{
		/// <summary>true = 仅 Pro 解锁后可用；false = 仅 Pro 解锁前可用。</summary>
		[Export] public bool RequiresProUpgrade { get; set; }

		/// <summary>选招资格同样受血量门限制，避免另一个变体被随机选中后起不了手。</summary>
		protected override bool IsPlayerInDetectionRangeCore()
			=> IsUpgradeStateAllowed() && base.IsPlayerInDetectionRangeCore();

		/// <summary>起手门：普通/Pro 变体按通用血量阈值二选一。</summary>
		protected override bool CanStartCore()
			=> IsUpgradeStateAllowed() && base.CanStartCore();

		private bool IsUpgradeStateAllowed()
		{
			var controller = Enemy?.StateMachine?
				.GetNodeOrNull<EnemyF1RogueAIPart2AttackController>("Attack/AttackController");
			bool upgraded = controller != null && controller.IsProUnlocked;
			return RequiresProUpgrade == upgraded;
		}
	}
}
