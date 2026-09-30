using Godot;
using Kuros.Core;
using Kuros.Core.Events;

namespace Kuros.Actors.Enemies.Attacks
{
	/// <summary>
	/// 弹球攻击。敌人自身化为弹球，朝玩家方向高速冲刺。
	/// 接触碰撞体后按入射角反射，每次命中造成伤害，持续时间结束后收尾。
	/// </summary>
	public partial class EnemyPinballAttack : EnemyAttackTemplate
	{
		// 区域由基类统一提供：TriggerAreaPath（起手检测区，供选招钩子用；未配置 = 不限制）、
		// AttackAreaPath（弹球接触判定带）。旧的 DetectionAreaPath / PinballAreaPath 已并入基类。
		// 本招伤害按接触逐个结算（不按区域），所以不涉及 DamageArea。

		[ExportCategory("Pinball")]
		[Export(PropertyHint.Range, "0,3000,10")] public float PinballMaxSpeed = 800f;
		[Export(PropertyHint.Range, "0,3000,10")] public float PinballMinSpeed = 200f;
		[Export(PropertyHint.Range, "0,3000,10")] public float SpeedDecayPerSecond = 300f;
		[Export(PropertyHint.Range, "0.5,60,0.1")] public float PinballDuration = 3.0f;
		[Export(PropertyHint.Range, "0.05,0.5,0.01")] public float BounceCooldown = 0.1f;
		[Export(PropertyHint.Range, "0,5,0.01")] public float MinBounceTimeBeforeDamage = 0f;
		[Export] public bool AllowWallBounce = true;

		public bool IsStopping { get; private set; }

		private bool _isDashing;
		private Vector2 _dashDirection = Vector2.Right;
		private float _currentSpeed;
		private int _bounceCount;
		private float _bounceCooldownRemaining;
		private bool _canDealDamage;
		private float _dashTimeElapsed;

		protected override void OnInitialized()
		{
			base.OnInitialized();
			SetPhysicsProcess(true);
		}

		/// <summary>选招用的检测：玩家在 TriggerAreaPath 指定的区内（未配置 = 不限制）。</summary>
		public override bool IsPlayerInDetectionRange() => IsPlayerInTriggerArea();

		protected override void OnAttackStarted()
		{
			base.OnAttackStarted();
			IsStopping = false;
			_isDashing = false;
			_bounceCount = 0;
			_bounceCooldownRemaining = 0f;
			_canDealDamage = MinBounceTimeBeforeDamage <= 0f;
			_dashTimeElapsed = 0f;

			ConnectAttackAreaSignals();
			GameActor.AnyDamageTaken += OnAnyDamageTaken;
		}

		// 预热阶段：停止敌人移动，准备弹射
		protected override void OnWarmupStarted()
		{
			base.OnWarmupStarted();
			if (Enemy != null)
				Enemy.Velocity = Vector2.Zero;
		}

		// 生效阶段：计算朝玩家方向、设初速、启动弹射、处理已在区域内的对象
		protected override void OnActivePhase()
		{
			if (Enemy == null) return;

			Vector2 toPlayer = Player != null
				? (Player.GlobalPosition - Enemy.GlobalPosition).Normalized()
				: (Enemy.FacingRight ? Vector2.Right : Vector2.Left);
			if (toPlayer == Vector2.Zero)
				toPlayer = Enemy.FacingRight ? Vector2.Right : Vector2.Left;

			_dashDirection = toPlayer;
			_currentSpeed = PinballMaxSpeed;
			Enemy.Velocity = _dashDirection * _currentSpeed;
			_isDashing = true;

			ProcessInitialOverlaps();
		}

		// 处理信号连接前已处于 PinballArea 内的对象（BodyEntered/AreaEntered 只对新进入者触发）
		private void ProcessInitialOverlaps()
		{
			if (AttackArea == null || Enemy == null) return;

			foreach (var body in AttackArea.GetOverlappingBodies())
				OnAttackAreaBodyEntered(body);

			foreach (var area in AttackArea.GetOverlappingAreas())
				OnAttackAreaAreaEntered(area);
		}

		// 恢复阶段：停止弹射与移动
		protected override void OnRecoveryStarted()
		{
			base.OnRecoveryStarted();
			_isDashing = false;
			if (Enemy != null)
				Enemy.Velocity = Vector2.Zero;
		}

		// 攻击完全结束：断开信号、取消事件订阅
		protected override void OnAttackFinished()
		{
			base.OnAttackFinished();
			// 清冲刺状态与残留速度：dash 中被打断（眩晕/冰冻）会跳过 Recovery，
			// _isDashing 残留 true 会让 _PhysicsProcess 在后续攻击期间持续运行
			_isDashing = false;
			if (Enemy != null)
				Enemy.Velocity = Vector2.Zero;
			GameActor.AnyDamageTaken -= OnAnyDamageTaken;
			DisconnectAttackAreaSignals();
		}

		// 每帧更新：速度衰减、墙壁反弹（GetLastSlideCollision）、超时结束、维护速度
		public override void _PhysicsProcess(double delta)
		{
			if (Enemy == null || Enemy.IsDead || Enemy.IsDeathSequenceActive) return;

			var stateName = Enemy.StateMachine?.CurrentState?.Name;
			if (stateName == "Frozen" || stateName == "CooldownFrozen"
				|| stateName == "Hit" || stateName == "Dying" || stateName == "Dead")
				return;

			if (!_isDashing) return;

			float dt = (float)delta;
			_bounceCooldownRemaining -= dt;
			_dashTimeElapsed += dt;

			if (!_canDealDamage && _dashTimeElapsed >= MinBounceTimeBeforeDamage)
				_canDealDamage = true;

			if (_dashTimeElapsed >= PinballDuration)
			{
				FinishDash();
				return;
			}

			// 速度衰减：每秒减少 SpeedDecayPerSecond，不低于 PinballMinSpeed
			_currentSpeed = Mathf.Max(PinballMinSpeed, _currentSpeed - SpeedDecayPerSecond * dt);

			// 墙壁反弹：利用 MoveAndSlide 物理引擎的实际碰撞法线（全向覆盖）
			if (AllowWallBounce && _bounceCooldownRemaining <= 0f)
			{
				var collision = Enemy.GetLastSlideCollision();
				if (collision != null)
				{
					var collider = collision.GetCollider();
					if (collider is not GameActor)
						Reflect(collision.GetNormal());
				}
			}

			Enemy.Velocity = _dashDirection * _currentSpeed;
		}

		// 弹射期间挂起 Active 阶段（由 _isDashing 控制退出时机）
		protected override bool ShouldHoldActivePhase() => _isDashing;

		// Body 进入 PinballArea：HitArea 守卫 → 解析法线 → 弹射 → 伤害 → 击退
		private void OnAttackAreaBodyEntered(Node body)
		{
			if (!_isDashing || Enemy == null) return;

			if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(body, Enemy)) return;

			// 只对命中目标 HitArea 的接触产生反应（忽略 GrabArea 等非受击区域）
			if (body is GameActor actor && AttackArea != null && !actor.IsHitByArea(AttackArea))
				return;

			Vector2 enemyPos = Enemy.GlobalPosition;
			Vector2 hitPos = body is Node2D nd ? nd.GlobalPosition : enemyPos;
			Vector2 normal = ResolveBounceNormal(body, hitPos);

			if (_bounceCooldownRemaining <= 0f)
				Reflect(normal);

			if (!_canDealDamage) return;

			bool dealt = DamageDispatcher.DealDamage(body, GetDamage(), enemyPos, Enemy,
				DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage, AttackArea, _dashDirection);
			if (!dealt) return;

			TryApplyKnockback(body);
		}

		// Area 进入 PinballArea：HitArea 守卫 → 解析法线 → 弹射 → 伤害 → 击退
		private void OnAttackAreaAreaEntered(Area2D area)
		{
			if (!_isDashing || Enemy == null) return;

			var target = area.Owner ?? area;
			if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(target, Enemy)) return;

			// 只对命中目标 HitArea 的接触产生反应（跳过 GrabArea 等非受击区域）
			if (target is GameActor actor && AttackArea != null && !actor.IsHitByArea(AttackArea))
				return;

			Vector2 enemyPos = Enemy.GlobalPosition;
			Vector2 normal = ResolveBounceNormal(target, area.GlobalPosition);

			if (_bounceCooldownRemaining <= 0f)
				Reflect(normal);

			if (!_canDealDamage) return;

			bool dealt = DamageDispatcher.DealDamage(target, GetDamage(), enemyPos, Enemy,
				DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage, AttackArea, _dashDirection);
			if (!dealt) return;

			TryApplyKnockback(area.Owner);
		}

		// enemy 在弹射期间受到伤害时：弹射速度重置为满速
		private void OnAnyDamageTaken(GameActor victim, GameActor? attacker, int damage)
		{
			if (victim != Enemy || !_isDashing) return;
			_currentSpeed = PinballMaxSpeed;
		}

		// 反射方向计算：优先使用 MoveAndSlide 物理引擎的实际碰撞法线（方向与"谁撞谁"无关），
		// 回退到几何中心近似 (enemyPos - targetPos).Normalized()
		private Vector2 ResolveBounceNormal(Node target, Vector2 targetPos)
		{
			var collision = Enemy?.GetLastSlideCollision();
			if (collision != null)
			{
				var collider = collision.GetCollider();
				if (collider == target || (target is Area2D area && collider == (area.Owner ?? area)))
					return collision.GetNormal();
			}

			Vector2 normal = (Enemy!.GlobalPosition - targetPos).Normalized();
			return normal == Vector2.Zero ? -_dashDirection : normal;
		}

		// 执行一次弹射：计数+1 → 冷却重置 → 反射公式计算新方向 → 速度重置为满速
		private void Reflect(Vector2 normal)
		{
			_bounceCount++;
			_bounceCooldownRemaining = BounceCooldown;

			_dashDirection = _dashDirection - 2 * _dashDirection.Dot(normal) * normal;
			if (_dashDirection == Vector2.Zero)
				_dashDirection = -normal;

			_currentSpeed = PinballMaxSpeed;
			if (Enemy != null)
				Enemy.Velocity = _dashDirection * _currentSpeed;
		}

		// 对被命中目标施加位移驱动击退（duration 内滑完 distance，受击方 Hit 状态消费）
		private void TryApplyKnockback(Node? target)
		{
			if (Enemy == null || target is not GameActor actor) return;
			if (KnockbackDistance <= 0f) return;

			actor.ApplyKnockbackDisplacement(
				(actor.GlobalPosition - Enemy.GlobalPosition).Normalized(),
				KnockbackDistance,
				Mathf.Max(KnockbackDuration, 0.01f));
		}

		// 结束弹射：停止 dash、进入 Recovery
		private void FinishDash()
		{
			if (Enemy == null) return;
			IsStopping = true;
			_isDashing = false;
			Enemy.Velocity = Vector2.Zero;
			ForceEnterRecoveryPhase();
		}

		// 连接接触判定带（基类 AttackAreaPath 解析结果）的 BodyEntered/AreaEntered 信号
		private void ConnectAttackAreaSignals()
		{
			if (AttackArea == null) return;
			AttackArea.BodyEntered += OnAttackAreaBodyEntered;
			AttackArea.AreaEntered += OnAttackAreaAreaEntered;
		}

		// 断开 PinballArea 的 BodyEntered/AreaEntered 信号
		private void DisconnectAttackAreaSignals()
		{
			if (AttackArea == null) return;
			AttackArea.BodyEntered -= OnAttackAreaBodyEntered;
			AttackArea.AreaEntered -= OnAttackAreaAreaEntered;
		}

		// 节点退出时清理信号连接与事件订阅
		public override void _ExitTree()
		{
			GameActor.AnyDamageTaken -= OnAnyDamageTaken;
			DisconnectAttackAreaSignals();
			base._ExitTree();
		}
	}
}
