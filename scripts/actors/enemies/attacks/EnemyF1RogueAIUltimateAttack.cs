using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
	/// <summary>
	/// Enemy_F1_rogueAI 的大招「过载冲击」：蓄力滑向滑槽**随机一端** → 横扫到**另一端**。
	///   · Charge（Warmup）：循环 attack_warming_up（动画控制器按 <see cref="Phase"/> 选），沿轨道滑到随机选中的一端；
	///     速度 = 该段距离 / <see cref="EnemyAttackTemplate.WarmupDuration"/>（蓄力结束刚好到位），可用 ChargeSpeed 覆盖。
	///   · Dash（Active）：循环 attack_up，从该端横扫到另一端；
	///     速度 = 两端距离 / <see cref="EnemyAttackTemplate.ActiveDuration"/>，可用 DashSpeed 覆盖。
	///   · Settle（Recovery）：停在另一端，交给收招。
	/// <see cref="ReturnOnActive"/> = true（强化版 RogueAIOverloadPro）：Active 段 A→B 后**折返 B→A**，
	///   自动速度按两倍行程算（2×两端距离 / ActiveDuration），两段走完刚好用满 Active。
	/// **本招本身不造成伤害**——伤害由挂在 Effects 里的"跟随本体的伤害领域/光墙"场景负责
	/// （AttackEffectEntry：SpawnTiming = OnActive + LifecycleBinding = OnActiveEnd，无需改这里的代码）。
	///
	/// 两条硬约束：
	///   1. 冲刺期间必须让 RailChaseMovement.ExternalDrive = true —— 否则追击组件在 Attack 状态下会减速抢速度；
	///      用完（含被打断）必须复位，否则永久失去追击。
	///   2. 位移只写 Enemy.Velocity，**不要调 MoveAndSlide** —— EnemyAttackState 每帧已经替模板调过一次，
	///      再调一次会让冲刺速度翻倍。
	/// </summary>
	public partial class EnemyF1RogueAIUltimateAttack : EnemyAttackTemplate
	{
		public enum UltPhase { None, Charge, Dash, Settle }

		[ExportCategory("Timing 时长")]
		/// <summary>蓄力（滑向随机一端）时长，秒；0 = 沿用基类 Timing 的 WarmupDuration。
		/// 基类 Timing 的默认值（0.2/0.15）对本招太短，所以本招自带语义化时长并写回基类字段
		/// ——与 EnemyMoveAttack 的 DashDuration / EnemyPinballAttack 的 PinballDuration 同范式。
		/// 位移速度由它推导（`ChargeSpeed` 为 0 时 = 距离 / 本值）。</summary>
		[Export(PropertyHint.Range, "0,15,0.1")] public float ChargeDuration { get; set; } = 3f;
		/// <summary>横扫（Active）时长，秒；0 = 沿用基类 Timing 的 ActiveDuration。
		/// <see cref="ReturnOnActive"/> 时两段行程共用这个时长（自动速度按两倍行程算）。</summary>
		[Export(PropertyHint.Range, "0,20,0.1")] public float DashDuration { get; set; } = 5f;
		// 收招时长直接用基类 Timing 的 RecoveryDuration（在节点上配），不再另设导出。

		[ExportCategory("Movement")]
		/// <summary>蓄力滑向随机一端的速度；0 = 自动（距离 / WarmupDuration）。</summary>
		[Export(PropertyHint.Range, "0,4000,10")] public float ChargeSpeed { get; set; } = 0f;
		/// <summary>横扫速度；0 = 自动（两端距离 / ActiveDuration；<see cref="ReturnOnActive"/> 时按两倍行程）。</summary>
		[Export(PropertyHint.Range, "0,8000,10")] public float DashSpeed { get; set; } = 0f;
		/// <summary>到位死区（像素）。</summary>
		[Export(PropertyHint.Range, "1,64,1")] public float ArriveDeadzone { get; set; } = 8f;
		/// <summary>Active 段扫到另一端后**折返**回蓄力端（强化版 Pro：A→B→A）。</summary>
		[Export] public bool ReturnOnActive { get; set; } = false;

		/// <summary>当前阶段（动画控制器据此选循环动画）。</summary>
		public UltPhase Phase { get; private set; } = UltPhase.None;

		private SlideRailMount? _rail;
		private RailChaseMovement? _drive;
		private bool _chargeAtFar;
		private float _chargeSpeed;
		private float _dashSpeed;
		private float _dashElapsed;
		private int _dashLeg;   // 0 = 第一段（A→B）；1 = 折返段（B→A，仅 ReturnOnActive）
		private bool _missingRailWarned;

		protected override void OnInitialized()
		{
			// 本招时长写回基类 Timing：状态机的阶段推进、以及下面按"距离 / 时长"推导的位移速度都读那几个字段。
			// 0 = 沿用基类节点上配的值（不覆盖）。
			if (ChargeDuration > 0f) WarmupDuration = ChargeDuration;
			if (DashDuration > 0f) ActiveDuration = DashDuration;

			_rail = SlideRailMount.FindFor(Enemy);
			_drive = Enemy?.GetNodeOrNull<RailChaseMovement>("RailChaseMovement");
		}

		/// <summary>不看距离与朝向角度（本体在玩家正上方，默认角度门会把大招判死）：能不能放由控制器的权重决定。</summary>
		public override bool CanStart()
		{
			if (Enemy == null) return false;
			if (Enemy.IsDeathSequenceActive || Enemy.IsDead) return false;
			return !IsRunning && !IsOnCooldown;
		}

		protected override void OnWarmupStarted()
		{
			base.OnWarmupStarted();     // 基类：生成 OnWarmup 特效 + Velocity 归零
			EnsureRailResolved();

			_chargeAtFar = GD.Randf() < 0.5f;   // 随机蓄力端（Near / Far 各 50%）
			_chargeSpeed = 0f;                  // 首个物理帧里按"距离 / WarmupDuration"锁定一次（见 TickRailMovement）
			_dashSpeed = 0f;
			_dashLeg = 0;
			Phase = UltPhase.Charge;
			SetExternalDrive(true);

			if (_rail == null && !_missingRailWarned)
			{
				_missingRailWarned = true;
				GD.PushWarning($"{Enemy?.Name}: 大招未找到 SlideRailMount，蓄力/横扫不会位移（单独生成测试时属正常）");
			}
		}

		protected override void OnActivePhase()
		{
			base.OnActivePhase();       // 基类：生成 OnActive 特效
			Phase = UltPhase.Dash;
			_dashElapsed = 0f;
			_dashLeg = 0;
		}

		/// <summary>把 Active 挂起到冲刺用尽（由 <see cref="Phase"/> 自己结束，见 _PhysicsProcess）。</summary>
		protected override bool ShouldHoldActivePhase() => Phase == UltPhase.Dash;

		protected override void OnRecoveryStarted()
		{
			base.OnRecoveryStarted();
			Phase = UltPhase.Settle;
			if (Enemy != null) Enemy.Velocity = Vector2.Zero;
		}

		protected override void OnAttackFinished()
		{
			Phase = UltPhase.None;
			SetExternalDrive(false);
			base.OnAttackFinished();    // 基类收尾（写 CD / 还原免疫 / 清绑定特效）放最后
		}

		/// <summary>被打断同样要复位（本体常态免伤，实际极少触发，作兜底）。</summary>
		protected override void OnDamageTakenInterrupt()
		{
			Phase = UltPhase.None;
			SetExternalDrive(false);
		}

		public override void _PhysicsProcess(double delta)
		{
			if (!IsRunning || Enemy == null) return;
			if (Phase != UltPhase.Charge && Phase != UltPhase.Dash) return;

			EnsureRailResolved();
			if (_rail == null) return;

			// 冲刺计时：用满 ActiveDuration 即收手（ShouldHoldActivePhase 随之放开，相位推进到 Recovery）
			if (Phase == UltPhase.Dash)
			{
				_dashElapsed += (float)delta;
				if (_dashElapsed >= ActiveDuration)
				{
					Phase = UltPhase.Settle;
					Enemy.Velocity = Vector2.Zero;
					return;
				}
			}

			float current = RailCoordinate(Enemy.GlobalPosition);
			float target = Phase == UltPhase.Charge ? ChargeEndCoordinate : DashEndCoordinate;
			float distance = target - current;

			if (Mathf.Abs(distance) <= ArriveDeadzone)
			{
				// 强化版：扫到 B 后折返 A —— 不停车，换成折返目标（下一帧按剩余行程反向起步）
				if (Phase == UltPhase.Dash && ReturnOnActive && _dashLeg == 0)
				{
					_dashLeg = 1;
					_dashSpeed = 0f;   // 折返段按剩余时长重新锁速（见下）
					return;
				}

				Enemy.Velocity = Vector2.Zero;
				return;
			}

			// 速度只在各段开始时算一次：每帧按"剩余距离 / 段时长"重算会变成芝诺式收敛，永远差一点到不了端点
			if (Phase == UltPhase.Charge && _chargeSpeed <= 0f)
				_chargeSpeed = ChargeSpeed > 0f
					? ChargeSpeed
					: Mathf.Max(50f, Mathf.Abs(distance) / Mathf.Max(0.05f, WarmupDuration));
			if (Phase == UltPhase.Dash && _dashSpeed <= 0f)
			{
				float legLength = Mathf.Abs(_rail.CarriageFar - _rail.CarriageNear);
				float totalLength = ReturnOnActive ? legLength * 2f : legLength;   // 折返 = 两倍行程
				_dashSpeed = DashSpeed > 0f
					? DashSpeed
					: Mathf.Max(50f, totalLength / Mathf.Max(0.05f, ActiveDuration));
			}

			float speed = Phase == UltPhase.Charge ? _chargeSpeed : _dashSpeed;
			Enemy.Velocity = DirectionAlongRail(distance) * speed;   // 位移交给 EnemyAttackState 的 MoveAndSlide
		}

		// ── 内部 ──────────────────────────────────────────────────────────

		private void EnsureRailResolved()
		{
			if (_rail != null && GodotObject.IsInstanceValid(_rail)) return;

			_rail = SlideRailMount.FindFor(Enemy);
			_rail?.ResolveNow();   // 幂等：限位在滑槽首个物理帧才解析出来
		}

		private void SetExternalDrive(bool value)
		{
			if (_drive != null && GodotObject.IsInstanceValid(_drive))
				_drive.ExternalDrive = value;
		}

		/// <summary>蓄力端（角色语义：Far = 场外端）的世界坐标。</summary>
		private float ChargeEndCoordinate
			=> _chargeAtFar ? _rail!.CarriageFar : _rail!.CarriageNear;

		/// <summary>横扫端：第一段 = 蓄力端的另一端（A→B）；折返段 = 回到蓄力端（B→A）。</summary>
		private float DashEndCoordinate
			=> _dashLeg == 0
				? (_chargeAtFar ? _rail!.CarriageNear : _rail!.CarriageFar)
				: ChargeEndCoordinate;

		private float RailCoordinate(Vector2 worldPosition)
			=> _rail!.CarriageAxis == SlideRailMount.RailAxis.X ? worldPosition.X : worldPosition.Y;

		private Vector2 DirectionAlongRail(float distance)
		{
			float sign = Mathf.Sign(distance);
			return _rail!.CarriageAxis == SlideRailMount.RailAxis.X
				? new Vector2(sign, 0f)
				: new Vector2(0f, sign);
		}
	}
}
