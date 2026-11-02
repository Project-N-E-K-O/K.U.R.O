using System.Collections.Generic;
using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
	/// <summary>
	/// rogueAI 二阶段本体的攻击控制器（Enemy_F1_rogueAI_part2 专属）：把父类的大招武装判据从
	/// 关卡进度换回**血量阈值**——
	///   · 判据 = <c>CurrentHealth / MaxHealth ≤ 阈值</c>（血量从高往低跌落，与其它 boss 的
	///     UltimateHealthThresholds 语义一致）；<see cref="HealthThresholds"/> 是血量阈值列表
	///     （升序配置、内部降序消费）。
	///   · **通用血量阈值**：<see cref="HealthUpgradeThreshold"/> 是 part2 所有"Pro 升级"的唯一来源——
	///     大招 Pro 与近战下砸 Pro 共用；基类不再导出"进度"字段（part1 用自己的 ProgressThresholds）。
	///     <see cref="HealthRatio"/> / <see cref="IsProUnlocked"/> 公开给攻击模板查询。
	///   · 背景：二阶段本体的"血量"由部件驱动（EnemyF1RogueAIPart2），是真实进度量；而关卡进度在
	///     phase-2 恒 ≥1，父类的进度判据会让全部大招出场即连放。part1 不受影响（仍用父类进度判据）。
	///   · 指针/消费/打断退还/重触发间隔/清陈旧队列/Pro 切换等机械**全部继承父类**，只换判据源。
	///   · 近身下砸：普通/Pro 两个节点都登记进普攻名单（大招武装时一并清零/还原）；可起手时绕过 10s 呼吸窗。
	/// </summary>
	[GlobalClass]
	public partial class EnemyF1RogueAIPart2AttackController : EnemyF1RogueAIAttackController
	{
		/// <summary>近身下砸攻击名（必须与节点名一致）。</summary>
		[Export] public string ProximityAttackName { get; set; } = "RogueAIDownSlam";
		/// <summary>近身下砸 Pro 攻击名（血量跌破 <see cref="HealthUpgradeThreshold"/> 后替换普通版）。</summary>
		[Export] public string ProximityProAttackName { get; set; } = "RogueAIDownSlamPro";
		/// <summary>血量阈值数组（0~1，升序配置、内部降序消费）：每跨过一个允许放一次大招；
		/// 长度 = 本场最多几发。</summary>
		[Export] public float[] HealthThresholds { get; set; } = { 0.25f, 0.5f, 0.75f };
		/// <summary>通用 Pro 升级血量阈值（0~1）：血量比值 ≤ 本值时，所有 Pro 版本解锁
		/// （大招 Pro、下砸 Pro 共用）。</summary>
		[Export(PropertyHint.Range, "0,1,0.01")] public float HealthUpgradeThreshold { get; set; } = 0.5f;

		protected override float[] TriggerThresholds => HealthThresholds;
		protected override float UpgradeThreshold => HealthUpgradeThreshold;

		/// <summary>当前血量比值（0~1）；本体缺失 / MaxHealth ≤ 0 时视为 1（不武装，安全侧）。</summary>
		public float HealthRatio
			=> Enemy != null && GodotObject.IsInstanceValid(Enemy) && Enemy.MaxHealth > 0
				? Mathf.Clamp((float)Enemy.CurrentHealth / Enemy.MaxHealth, 0f, 1f)
				: 1f;

		/// <summary>通用 Pro 升级门：血量比值 ≤ <see cref="HealthUpgradeThreshold"/>。攻击模板用它做变体二选一。</summary>
		public bool IsProUnlocked => HealthRatio <= HealthUpgradeThreshold;

		/// <summary>阈值缓存改**降序**：血量从高往低跌落，先跨过最大的比值（如 0.75），指针沿降序列表前进
		/// （父类进度语义是升序——进度只升、血量只降，同形异向）。</summary>
		protected override void RefreshThresholdCache()
		{
			var list = new List<float>();
			if (TriggerThresholds != null)
			{
				foreach (float t in TriggerThresholds)
				{
					float clamped = Mathf.Clamp(t, 0f, 1f);
					if (!list.Contains(clamped)) list.Add(clamped);
				}
			}
			list.Sort((a, b) => b.CompareTo(a));
			_sortedThresholds = list.ToArray();
		}

		/// <summary>"血量已跌破阈值但还没放"：直接轮询当前血量比值，天然幂等（本体一次跌多件也不会漏）。</summary>
		protected override bool ShouldTriggerUltimate()
		{
			if (_sortedThresholds.Length == 0) return false;
			if (ConsumedUltimates >= _sortedThresholds.Length) return false;
			return HealthRatio <= _sortedThresholds[ConsumedUltimates];
		}

		/// <summary>通用血量阈值 → 原版大招换成 Pro（与下砸 Pro 同源）。</summary>
		protected override bool ShouldUpgradeUltimate() => IsProUnlocked;

		protected override IEnumerable<string> EnumerateMeleeNames()
		{
			foreach (string name in base.EnumerateMeleeNames())
				yield return name;

			if (!string.IsNullOrEmpty(ProximityAttackName))
				yield return ProximityAttackName;
			if (!string.IsNullOrEmpty(ProximityProAttackName))
				yield return ProximityProAttackName;
		}

		/// <summary>近身下砸（普通或 Pro）此刻就能起手时绕过 MinInterAttackDelay 呼吸窗；
		/// 大招武装的绕过仍由父类负责。</summary>
		protected override bool ShouldBypassInterAttackDelay()
		{
			if (base.ShouldBypassInterAttackDelay()) return true;
			return IsStartable(ProximityAttackName) || IsStartable(ProximityProAttackName);
		}

		private bool IsStartable(string attackName)
		{
			if (string.IsNullOrEmpty(attackName)) return false;
			var attack = GetNodeOrNull<EnemyAttackTemplate>(attackName);
			return attack != null && attack.CanStart();
		}
	}
}
