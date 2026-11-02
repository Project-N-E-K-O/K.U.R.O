using System.Collections.Generic;
using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
	/// <summary>
	/// rogueAI part1 的攻击控制器：只提供**关卡进度**判据的两个导出。基类的阈值数组/升级阈值
	/// 改为 protected virtual，由子类按判据源命名（part1 = Progress，part2 = Health），
	/// 避免另一侧的 inspector 出现"进度/血量"字样。其余选招/疲劳/大招逻辑全部继承父类。
	/// </summary>
	[GlobalClass]
	public partial class EnemyF1RogueAIPart1AttackController : EnemyF1RogueAIAttackController
	{
		/// <summary>关卡进度阈值（0~1，升序）：每跨过一个允许放一次大招；长度 = 最多几发。</summary>
		[Export] public float[] ProgressThresholds { get; set; } = { 0.5f, 1.0f };

		/// <summary>进度 ≥ 此值时原版大招被 Pro 取代。</summary>
		[Export(PropertyHint.Range, "0,1,0.01")] public float ProgressUpgradeThreshold { get; set; } = 0.75f;

		/// <summary>第三个普攻名（RogueAISlamC）：与 MeleeAttackName / MeleeAttackNameSecond 同权重随机；
		/// 大招武装时一起清零、结束后一起还原（基类 EnumerateMeleeNames 的 part1 专用扩展）。</summary>
		[Export] public string MeleeAttackNameThird { get; set; } = "";

		protected override float[] TriggerThresholds => ProgressThresholds;
		protected override float UpgradeThreshold => ProgressUpgradeThreshold;

		protected override IEnumerable<string> EnumerateMeleeNames()
		{
			foreach (string name in base.EnumerateMeleeNames())
				yield return name;
			if (!string.IsNullOrEmpty(MeleeAttackNameThird))
				yield return MeleeAttackNameThird;
		}
	}
}
