using System.Collections.Generic;
using Godot;
using Kuros.Environments;

namespace Kuros.Actors.Enemies.Attacks
{
	/// <summary>
	/// Enemy_F1_rogueAI 的攻击控制器：疲劳权重（基类）+ **关卡进度阈值**强制大招。
	/// 判据来源不是自身血量（本体常态免伤、血量恒定，血量阈值那套在这里永远不会跨过），
	/// 而是 <see cref="KillProgressAscendController.Progress"/>（0..1）——与其它敌人的
	/// `UltimateHealthThresholds` 同形，只把"血量降序比较"换成"进度升序比较"：
	///   · <see cref="UltimateProgressThresholds"/> 升序排列，**指针即已放次数**（_consumedUltimates）；
	///   · "已跨过但没放"= 进度 ≥ 指针所指阈值 → 大招权重拉满、普攻清零（_pendingWeightRestore 记账）；
	///   · 大招**真正起手**才前进指针（避免被别的攻击抢走这一发）；
	///   · Warmup/Active 被打断 = 没放出来 → 指针回退一格（Recovery 打断视为已释放，不退）；
	///   · 轮询式判断天然幂等：本体晚于阈值生成、一次跨多个阈值都不会漏/重
	///     （所以不需要事件订阅，也不需要 Initialize 追赶）。
	/// 无进度源（场景里没有 KillProgressAscendController，如单独测试本体）→ 不触发，只警告一次。
	/// 注意：模板**节点名必须等于 AttackName**（TrySetAttackWeight/IsAttack 都按节点名比较），
	/// 且两招用全局唯一名（IsOtherEnemyAttacking 按攻击名在全体敌人间去重）。
	/// </summary>
	public partial class EnemyF1RogueAIAttackController : EnemyFatigueAttackControllerBase
	{
		/// <summary>大招攻击名。**必须与节点名一致，且全项目唯一**——
		/// EnemyAttackController.IsOtherEnemyAttacking 按攻击名在全体敌人间去重，
		/// 用通用名（如 netAdmin 已占用的 "UltimateAttack"）会和那台敌人互相阻断出招。</summary>
		[Export] public string UltimateAttackName { get; set; } = "RogueAIOverload";
		/// <summary>强化版大招攻击名（进度升级后**取代**上面那个，不额外多一发）。
		/// **必须与节点名一致，且全项目唯一**（理由同 UltimateAttackName）。</summary>
		[Export] public string UltimateProAttackName { get; set; } = "RogueAIOverloadPro";
		/// <summary>进度 ≥ 此值时原版大招被 Pro 取代（0 = 开场即 Pro）。与阈值同源：关卡只提供进度，判据在敌人侧。</summary>
		[Export(PropertyHint.Range, "0,1,0.01")] public float UltimateUpgradeProgress { get; set; } = 0.75f;
		/// <summary>进度百分比阈值（0~1，升序检测）：进度每跨过一个允许放一次大招；
		/// **数组长度 = 这场最多几发**。与其它敌人的 `UltimateHealthThresholds` 同形，判据换成关卡进度。</summary>
		[Export] public float[] UltimateProgressThresholds { get; set; } = { 0.5f, 1.0f };
		/// <summary>两次大招之间的最小间隔（秒）：到点后若还有"已跨过未释放"的阈值就重新武装。</summary>
		[Export(PropertyHint.Range, "0,20,0.1")] public float UltimateRetriggerDelay { get; set; } = 4f;
		// 调试开关用基类 EnemyAttackController.EnableDebugLogs（此前子类又声明了一份同名导出：CS0108 + 编辑器只显示一个）

		private KillProgressAscendController? _progress;
		private float[] _sortedThresholds = System.Array.Empty<float>();
		private int _consumedUltimates;
		private bool _armed;
		private bool _ultUpgraded;
		private bool _pendingWeightRestore;
		private bool _ultimateInFlight;
		private bool _missingWarned;
		private float _ultRetriggerTimer;

		/// <summary>已放出的大招次数（= 已消费的进度阈值个数）。</summary>
		public int ConsumedUltimates => _consumedUltimates;
		/// <summary>是否还有"已跨过但没放"的阈值（调试/无头验证用）。</summary>
		public bool HasPendingUltimate => ShouldTriggerUltimate();

		public override void Initialize(SampleEnemy enemy)
		{
			base.Initialize(enemy);   // 基类按 metadata/attack_weight 缓存原始权重

			_consumedUltimates = 0;
			_armed = false;
			_ultUpgraded = false;
			_pendingWeightRestore = false;
			_ultimateInFlight = false;
			_ultRetriggerTimer = 0f;
			_missingWarned = false;
			RefreshThresholdCache();

			BindProgress();
			ConfigureNextAttack();
		}

		public override void _PhysicsProcess(double delta)
		{
			if (_progress == null || !GodotObject.IsInstanceValid(_progress))
				BindProgress();   // 关卡晚于本控制器初始化时也能补上

			if (_ultRetriggerTimer > 0f)
				_ultRetriggerTimer -= (float)delta;

			// 进度升级（与阈值同源的轮询）：跨过 UltimateUpgradeProgress 后原版大招换成 Pro
			bool upgraded = ShouldUpgradeUltimate();
			if (upgraded != _ultUpgraded)
			{
				_ultUpgraded = upgraded;
				Log(upgraded
					? $"进度升级：大招换为 {UltimateProAttackName}"
					: $"进度回退：大招换回 {UltimateAttackName}");
				ConfigureNextAttack();
			}

			// 进度是外界随时可变、且没有事件可订阅的轮询量：只在"武装状态翻转"那一帧重设权重
			// （否则阈值跨过后要等下一招结束才武装，间隔里玩家看不到任何反应）
			bool armed = ShouldTriggerUltimate() && _ultRetriggerTimer <= 0f;
			if (armed != _armed)
			{
				_armed = armed;
				ConfigureNextAttack();
			}

			base._PhysicsProcess(delta);
		}

		protected override void OnChildAttackStarted(EnemyAttackTemplate attack)
		{
			base.OnChildAttackStarted(attack);   // 疲劳逻辑（连续同招降权 / 换招还原）

			if (IsUltimate(attack.Name))
			{
				_ultimateInFlight = true;
				ConsumeUltimate();
				_ultRetriggerTimer = UltimateRetriggerDelay;
			}
			ConfigureNextAttack();
		}

		protected override void OnAttackFinished()
		{
			base.OnAttackFinished();
			_ultimateInFlight = false;
			ConfigureNextAttack();   // 攻击期间进度可能又跨了阈值
		}

		/// <summary>子攻击在 Warmup/Active 被打断时基类会回调这里（Recovery 打断不会走到）：
		/// 大招没放出来 → 指针回退一格并允许立刻重放。用 _ultimateInFlight 兜底，避免"未起手就取消"也被计入。</summary>
		protected override void OnAttackInterrupted(EnemyAttackTemplate attack)
		{
			base.OnAttackInterrupted(attack);

			if (IsUltimate(attack.Name) && _ultimateInFlight)
			{
				_ultimateInFlight = false;
				if (_consumedUltimates > 0) _consumedUltimates--;
				_pendingWeightRestore = false;
				_ultRetriggerTimer = 0f;
				Log($"大招被打断 → 退还一发（已放 {_consumedUltimates} 次）");
			}
			ConfigureNextAttack();
		}

		/// <summary>大招期间玩家跑远也不中断（配合全场覆盖的检测区半径）。</summary>
		protected override bool ShouldInterruptOnPlayerExit() => false;

		/// <summary>
		/// 有"已跨过但没放"的阈值时放宽启动判定：RogueAIOverload 由关卡进度驱动，不该被"玩家必须站在攻击范围内"卡住——
		/// 那两道位置门都属于普攻：①普攻 <see cref="EnemySimpleMeleeAttack"/>.CanStart 要求玩家在它的 AttackArea 里
		/// （模板未配 AttackAreaPath → 回退到根节点 Sprite2D/AttackArea 的 200×6000 窄带）；
		/// ②基类还有朝向锥（控制器未配 MaxAllowedAngleToPlayer → 默认 135°），而本体 LockFacing 不转身，
		/// 玩家绕到背后时连 Attack 状态都进不来。
		/// 这里只放宽位置/朝向，保留必要条件：本体存活、有玩家目标、玩家在检测范围内、控制器自身未在跑/未在冷却、
		/// 且大招本身确实可起手（避免进了 Attack 却起不了手导致的进/出抖动）。
		/// 阈值被消费后（ConsumeUltimate）自动失效，交回基类的正常判定。
		/// </summary>
		public override bool CanStart()
		{
			if (!ShouldTriggerUltimate()) return base.CanStart();

			if (Enemy == null || !GodotObject.IsInstanceValid(Enemy)) return false;
			if (Enemy.IsDead || Enemy.IsDeathSequenceActive) return false;
			if (Enemy.PlayerTarget == null) return false;
			if (!Enemy.IsPlayerWithinDetectionRange()) return false;
			if (IsRunning || IsOnCooldown) return false;

			// 队列里还排着别的招（如普攻）→ 先换成大招，本帧不放行（否则进了 Attack 起手的会是普攻）
			if (!IsUltimate(QueuedAttackName))
			{
				ForceQueueNextAttack("UltimateArmed");
				return false;
			}

			return GetNodeOrNull<EnemyAttackTemplate>(ActiveUltimateName)?.CanStart() ?? false;
		}

		// ── 内部 ──────────────────────────────────────────────────────────

		private void BindProgress()
		{
			if (_progress != null && GodotObject.IsInstanceValid(_progress)) return;

			_progress = KillProgressAscendController.Find(this);
			if (_progress == null)
			{
				if (!_missingWarned)
				{
					_missingWarned = true;
					GD.PushWarning($"{Enemy?.Name ?? Name}: 未找到 KillProgressAscendController（组 kill_progress），"
						+ "进度阈值不会触发（单独测试本体时属正常）");
				}
				return;
			}

			Log($"绑定进度控制器：当前进度 {_progress.Progress:P0}，已放 {_consumedUltimates} 次");
			ConfigureNextAttack();
		}

		/// <summary>"已跨过阈值但还没放"：直接轮询当前进度，天然幂等（本体晚生成、一次跨多个都不会漏/重）。</summary>
		private bool ShouldTriggerUltimate()
		{
			if (_sortedThresholds.Length == 0) return false;
			if (_consumedUltimates >= _sortedThresholds.Length) return false;
			if (_progress == null || !GodotObject.IsInstanceValid(_progress)) return false;   // 无进度源 = 没有大招

			return _progress.Progress >= _sortedThresholds[_consumedUltimates];
		}

		/// <summary>指针前进一格（= 这一发已放出）。一次跨多个阈值时只前进一格，剩下的留给下一发
		/// （由 <see cref="UltimateRetriggerDelay"/> 拉开间隔）——与"每个阈值各一次"的语义一致。</summary>
		private void ConsumeUltimate()
		{
			_consumedUltimates++;
			Log($"大招起手（已放 {_consumedUltimates}/{_sortedThresholds.Length} 次）");
		}

		/// <summary>当前生效的大招名：未升级 = 原版；升级后 = Pro（两者互斥，权重只给这一个）。</summary>
		private string ActiveUltimateName => _ultUpgraded ? UltimateProAttackName : UltimateAttackName;

		/// <summary>进度是否已到"换强化版"的值（与阈值同源，轮询读取）。</summary>
		private bool ShouldUpgradeUltimate()
			=> _progress != null && GodotObject.IsInstanceValid(_progress)
			   && _progress.Progress >= UltimateUpgradeProgress;

		private void ConfigureNextAttack()
		{
			bool armed = ShouldTriggerUltimate() && _ultRetriggerTimer <= 0f;

			// 未武装时两个大招都清零（升级后原版绝不能再被选中）；武装时只给当前生效的那一个
			TrySetAttackWeight(UltimateAttackName, 0f);
			TrySetAttackWeight(UltimateProAttackName, 0f);

			if (armed)
			{
				if (!_pendingWeightRestore)
					Log($"武装大招（{ActiveUltimateName}，进度 {(_progress?.Progress ?? 0f):P0}，已放 {_consumedUltimates} 次）");
				TrySetAttackWeight(ActiveUltimateName, 1f);
				TrySetAttackWeight(MeleeAttackName, 0f);
				_pendingWeightRestore = true;

				// 关键：队列里可能已经排着"当前不可启动"的普攻（EnemySimpleMeleeAttack.CanStart 要求玩家
				// 站在它的判定列内）——排队攻击没进冷却时空闲循环不会重新选招，大招会被永久卡住。
				// 所以武装瞬间把陈旧队列踢掉，让空闲循环按新权重（只有大招有权重）重新选；代价是一次呼吸窗。
				if (!IsRunning && !string.IsNullOrEmpty(QueuedAttackName)
					&& !IsUltimate(QueuedAttackName))
				{
					Log($"清掉陈旧队列（{QueuedAttackName}）→ 让大招上位");
					ForceQueueNextAttack("UltimateArmed");
				}
				return;
			}

			TrySetAttackWeight(UltimateAttackName, 0f);

			// 大招起手后（阈值已消费）：一次性还原被清零的普攻权重，之后交回疲劳逻辑
			if (_pendingWeightRestore)
			{
				RestoreAttackWeight(MeleeAttackName);
				_pendingWeightRestore = false;
			}
		}

		/// <summary>阈值缓存：钳到 0..1、去重、**升序**——进度单调上升、指针只往前走（血阈值那套是降序）。</summary>
		private void RefreshThresholdCache()
		{
			var list = new List<float>();
			if (UltimateProgressThresholds != null)
			{
				foreach (float t in UltimateProgressThresholds)
				{
					float clamped = Mathf.Clamp(t, 0f, 1f);
					if (!list.Contains(clamped)) list.Add(clamped);
				}
			}
			list.Sort((a, b) => a.CompareTo(b));
			_sortedThresholds = list.ToArray();
		}

		/// <summary>该攻击名是不是大招（原版或强化版之一）。</summary>
		private bool IsUltimate(string? attackName)
			=> IsAttack(attackName, UltimateAttackName) || IsAttack(attackName, UltimateProAttackName);

		private static bool IsAttack(string? attackName, string expectedName)
			=> attackName != null && attackName.Equals(expectedName, System.StringComparison.OrdinalIgnoreCase);

		private void Log(string message)
		{
			if (EnableDebugLogs) GD.Print($"[{Enemy?.Name ?? Name}] {message}");
		}
	}
}
