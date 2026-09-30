using Godot;
using Kuros.Core;
using Kuros.Actors.Enemies.States;
using Kuros.Actors.Heroes.States;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 冲刺踢击攻击：
    /// 1. 玩家进入检测区域后触发预热；
    /// 2. 预热结束锁定方向（可带快照延迟），持续冲刺 DashDuration 秒；
    /// 3. 命中（踢击区与玩家 HitArea 重叠，且过 MinDashTimeBeforeAttack）：
    ///    · 非动画触发（RequireAnimationHitTrigger=false）→ 命中即停，结算伤害+击退后进 Recovery；
    ///    · 动画触发 → 由动画 hit 事件结算，冲刺持续到 DashDuration/抵达终点才停；
    /// 4. 冲刺结束（时长到/抵终点/命中停止）后进入 Recovery。
    /// </summary>
    public partial class EnemyKickAttack : EnemyAttackTemplate
    {
        // 区域由基类统一提供：TriggerAreaPath（触发判定区；未配置 = 不做额外位置限制）
        // 与 DamageAreaPath（伤害区；未配置 = AttackArea）。旧的 DetectionAreaPath / KickAttackAreaPath 已并入基类。

        [ExportCategory("Dash")]
		/// <summary>冲刺速度 = 基础 Speed × 倍率（倍率语义：基础速度调整时冲刺自动适配）。</summary>
		[Export(PropertyHint.Range, "0.1,10,0.1")] public float DashSpeedMultiplier = 2f;
		private float DashSpeed => Enemy?.Speed * DashSpeedMultiplier ?? 0f;
		[Export(PropertyHint.Range, "0.05,10,0.05")] public float DashDuration = 0.5f; // 冲刺持续时间（秒）
        [Export] public bool LockFacingDuringDash = true;
		[Export(PropertyHint.Range, "0,5,0.01")] public float MinDashTimeBeforeAttack = 0f; // 允许命中前的最短冲刺时间（秒）
		[Export(PropertyHint.Range, "0,5,0.1")] public float SnapshotDelaySeconds = 0f; // 冲刺前等待一段时间再记录玩家位置

		[ExportCategory("Dash Prop Clear")]
		/// <summary>冲刺期间销毁沿路经过的一次性投掷道具(家具类:天然家具与乱码块件都清,不含投掷武器)。</summary>
		[Export] public bool DestroyFurnitureDuringDash { get; set; } = false;
		/// <summary>清障探测盒长度(世界像素):自敌人中心沿冲刺方向向前延伸。</summary>
		[Export(PropertyHint.Range, "0,600,5")] public float PropClearLength { get; set; } = 140f;
		/// <summary>清障探测盒高度(世界像素):以敌人原点垂直居中。</summary>
		[Export(PropertyHint.Range, "0,600,5")] public float PropClearHeight { get; set; } = 220f;

        [ExportCategory("Effects")]
		[Export] public StringName CooldownStateName = "CooldownFrozen";
		[Export(PropertyHint.Range, "1,10,1")] public int KnockbackOnHitIndex = 3;

		private const float PostCooldownDuration = 0.1f;


        private Area2D? _detectionArea;
        private EnemyAttackController? _controller;
		private bool _playerInsideDetection;

        private Vector2 _dashDirection = Vector2.Right;
		private Vector2 _dashTarget;
		private bool _isDashing;
		private bool _dashFinalized;
		private float _postAttackCooldown;
		private bool _pendingCooldownExit;
		private float _dashTimeElapsed;
		private bool _canAttemptKickAttack;
		private int _animationHitCount;
		private float _snapshotTimer = 0f;
		private bool _waitingForSnapshot = false;

		public bool IsDashing => _isDashing;
		public bool IsDashFinished => _dashFinalized;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            _controller = GetParent() as EnemyAttackController;

            // 触发判定区走基类：TriggerAreaPath 配了就要求玩家在区内；未配置 = 不做额外限制。
            // （配置了却解析不到时基类诊断会报警告，这里不再自己报"回退"）
            _detectionArea = TriggerArea;
            if (_detectionArea != null)
            {
				_detectionArea.Monitoring = true;
                _detectionArea.BodyEntered += OnDetectionAreaBodyEntered;
				_detectionArea.BodyExited += OnDetectionAreaBodyExited;
			}

			SetPhysicsProcess(true);
        }

        public override void _ExitTree()
        {
            if (_detectionArea != null)
            {
                _detectionArea.BodyEntered -= OnDetectionAreaBodyEntered;
				_detectionArea.BodyExited -= OnDetectionAreaBodyExited;
            }

            base._ExitTree();
        }

        public override bool CanStart()
        {
			if (Enemy == null || Enemy.PlayerTarget == null) return false;
			if (IsRunning || IsOnCooldown) return false;
			if (Enemy.AttackTimer > 0) return false;
			if (_postAttackCooldown > 0f)
			{
				return false;
			}

			// 使用自己的 DetectionArea 或回退到 Enemy.DetectionArea
			bool detectionSatisfied = _detectionArea != null
				? _playerInsideDetection || _detectionArea.OverlapsBody(Enemy.PlayerTarget)
				: Enemy.IsPlayerWithinDetectionRange();

			if (!detectionSatisfied)
			{
				return false;
			}

            AlignFacingWithPlayer();

			Vector2 toPlayer = Enemy.GetDirectionToPlayer();
			if (toPlayer == Vector2.Zero) return false;

			Vector2 facing = Enemy.FacingRight ? Vector2.Right : Vector2.Left;
			float angle = Mathf.RadToDeg(facing.AngleTo(toPlayer));
			return Mathf.Abs(angle) <= MaxAllowedAngleToPlayer;
		}

		protected override void OnAttackStarted()
		{
			base.OnAttackStarted();
			_isDashing = false;
			_dashFinalized = false;
			_animationHitCount = 0;
			_postAttackCooldown = 0f;
			_pendingCooldownExit = false;
			_dashTimeElapsed = 0f;
			_canAttemptKickAttack = MinDashTimeBeforeAttack <= 0f;
			base.OnWarmupStarted();
			if (Enemy != null)
			{
				Enemy.Velocity = Vector2.Zero;
			}

			// 有延迟则等待，无延迟直接快照
			if (SnapshotDelaySeconds > 0f)
			{
				_snapshotTimer = SnapshotDelaySeconds;
				_waitingForSnapshot = true;
			}
			else
			{
				_waitingForSnapshot = false;
				PrepareDashTowardsPlayer();
			}
        }

        protected override void OnActivePhase()
        {
			if (Enemy == null) return;
			_isDashing = true;
			Enemy.Velocity = _dashDirection * DashSpeed;

			// 启用动画事件触发时，开放命中窗口供 TriggerAnimationHit 调用
			if (RequireAnimationHitTrigger)
			{
				_animationHitReady = true;
			}
        }

        protected override void OnRecoveryStarted()
        {
            base.OnRecoveryStarted();
			_playerInsideDetection = false;
			_isDashing = false;
			_dashFinalized = true;
			if (Enemy != null)
			{
				Enemy.Velocity = Vector2.Zero;
			}
		}

		public override void _PhysicsProcess(double delta)
		{
			if (Enemy == null || !GodotObject.IsInstanceValid(Enemy) || !Enemy.IsInsideTree())
			{
				return;
			}

			// 快照延迟计时
			if (_waitingForSnapshot)
			{
				_snapshotTimer -= (float)delta;
				if (_snapshotTimer <= 0f)
				{
					_waitingForSnapshot = false;
					PrepareDashTowardsPlayer(); // 延迟结束，此时快照玩家位置
				}
				return; 
			}

			if (_postAttackCooldown > 0f)
			{
				// 若其他攻击已接管（本攻击未运行但状态为 Attack），
				// 立即放弃冷却追踪，避免 FinishCooldownState 误打断无关攻击。
				var currentStateName = Enemy?.StateMachine?.CurrentState?.Name;
				if (currentStateName == "Attack" && !IsRunning)
				{
					_postAttackCooldown = 0f;
					_pendingCooldownExit = false;
					return;
				}

				_postAttackCooldown -= (float)delta;
				if (_postAttackCooldown <= 0f)
				{
					_postAttackCooldown = 0f;
					if (_pendingCooldownExit)
					{
						FinishCooldownState();
						_pendingCooldownExit = false;
					}
				}
				return;
			}

			UpdateDashMovement(delta);
			UpdateDetectionTracking();
		}

		protected override bool ShouldHoldRecoveryPhase()
		{
			return false;
		}

		private void PrepareDashTowardsPlayer()
        {
            if (Enemy == null) return;

			Vector2 dashStart = Enemy.GlobalPosition;
			Vector2 direction;

			if (Enemy.PlayerTarget != null)
			{
				// 快照玩家位置方向，冲刺期间不实时追踪。
				direction = Enemy.PlayerTarget.GlobalPosition - dashStart;
			}
			else
			{
				direction = Enemy.FacingRight ? Vector2.Right : Vector2.Left;
			}

			if (direction == Vector2.Zero)
			{
				direction = Enemy.FacingRight ? Vector2.Right : Vector2.Left;
            }

			_dashDirection = direction.Normalized();
			float targetDistance = DashSpeed * Mathf.Max(DashDuration, 0.05f);
			_dashTarget = dashStart + _dashDirection * targetDistance;

            if (LockFacingDuringDash && _dashDirection.X != 0)
            {
                Enemy.FlipFacing(_dashDirection.X > 0);
            }
        }

			private bool TryExecuteKickAttack()
	        {
			if (Enemy == null) return false;

	            var player = Enemy.PlayerTarget;
			if (player == null)
				return false;

			// DealDamage 必须在 Player 检测之前无条件调用，
			// 确保非 Player 目标（WorldItem 等）也能被处理
			ApplyAttackAreaMaskOverride(DamageArea);
			DealDamage(DamageArea!);

		            if (!IsPlayerInsideKickAttackZone(player))
	            {
				_playerInsideDetection = false;
				return false;
	            }

			ApplyKickKnockback(player);
			return true;
	        }

		private bool IsPlayerInsideKickAttackZone(SamplePlayer player)
        {
			return player.IsHitByArea(AttackArea);
        }

		private void ApplyKickKnockback(SamplePlayer player)
		{
			if (Enemy == null) return;

			TryApplyPlayerKnockback(
				player,
				KnockbackDistance,
				KnockbackDuration,
				_dashDirection);
		}


        private void OnDetectionAreaBodyEntered(Node body)
        {
            if (Enemy == null || body != Enemy.PlayerTarget) return;

			_playerInsideDetection = true;

			TryRequestAttackFromDetection("SignalEntered");
		}

		private void OnDetectionAreaBodyExited(Node body)
		{
			if (Enemy == null || body != Enemy.PlayerTarget) return;
			_playerInsideDetection = false;
		}

		private void UpdateDetectionTracking()
		{
			if (_detectionArea == null || Enemy?.PlayerTarget == null) return;
			if (_postAttackCooldown > 0f) return;

			bool overlaps = _detectionArea.OverlapsBody(Enemy.PlayerTarget);
			if (overlaps)
			{
				_playerInsideDetection = true;
				TryRequestAttackFromDetection("Poll");
				return;
			}

			_playerInsideDetection = false;
		}

		private void TryRequestAttackFromDetection(string reason)
		{
			if (Enemy == null) return;
			if (Enemy.IsDeathSequenceActive || Enemy.IsDead) return;
            if (IsRunning || IsOnCooldown) return;
			if (Enemy.AttackTimer > 0) return;
			if (_postAttackCooldown > 0f) return;

			if (_controller != null && _controller.PeekQueuedAttack() != this)
			{
				return;
			}
			if (_controller != null && !_controller.CanStart()) return;

			if (Enemy.StateMachine?.CurrentState?.Name != "Attack")
            {
                Enemy.StateMachine?.ChangeState("Attack");
            }
        }

		private void UpdateDashMovement(double delta)
		{
			if (!_isDashing || Enemy == null) return;

			if (DestroyFurnitureDuringDash)
				EnemyDashPropClearer.ClearAhead(Enemy, _dashDirection, PropClearLength, PropClearHeight);

				_dashTimeElapsed += (float)delta;

			// 最短冲刺时间计时
			if (!_canAttemptKickAttack && _dashTimeElapsed >= MinDashTimeBeforeAttack)
			{
				_canAttemptKickAttack = true;
			}

			// 冲刺持续时间到达，停止移动（Active 阶段由 ActiveDuration 计时结束）
			if (_dashTimeElapsed >= DashDuration)
			{
				_dashFinalized = true;
				_isDashing = false;
				Enemy.Velocity = Vector2.Zero;
				return;
			}

			// 命中检测：不中断冲刺，重叠期间可持续触发（启用动画事件触发时跳过此处）
			if (!RequireAnimationHitTrigger && _canAttemptKickAttack && Enemy.PlayerTarget != null && IsPlayerInsideKickAttackZone(Enemy.PlayerTarget))
			{
				FinishDash(forceKick: true);
				return;
			}

			Vector2 toTarget = _dashTarget - Enemy.GlobalPosition;
			float projected = toTarget.Dot(_dashDirection);
			if (projected <= 0f)
			{
				FinishDash();
				return;
			}

			float maxStep = DashSpeed * (float)delta;
			if (toTarget.LengthSquared() <= maxStep * maxStep)
			{
				FinishDash();
				return;
			}

			// 持续冲刺，直到抵达终点或由基类时序切入 Recovery
			Enemy.Velocity = _dashDirection * DashSpeed;
		}

		private void FinishDash(bool forceKick = false)
		{
			if (Enemy == null) return;

			_dashFinalized = true;
			if (!forceKick)
			{
				//Enemy.GlobalPosition = _dashTarget;
			}

			Enemy.Velocity = Vector2.Zero;
			_isDashing = false;

			if (forceKick)
			{
				TryExecuteKickAttack();
				ForceEnterRecoveryPhase();
			}
			// 非强制命中：仅停止移动，Active 阶段由 ActiveDuration 计时自然结束
		}

		protected override void OnAnimationHit()
		{
			if (Enemy?.PlayerTarget == null) return;
			if (!_canAttemptKickAttack) return;

			_animationHitCount++;

			ApplyAttackAreaMaskOverride(DamageArea);
			DealDamage(DamageArea!);

			if (_animationHitCount == KnockbackOnHitIndex && IsPlayerInsideKickAttackZone(Enemy.PlayerTarget))
			{
				ApplyKickKnockback(Enemy.PlayerTarget);
			}
		}

        private void AlignFacingWithPlayer()
        {
            if (Enemy == null) return;
            Vector2 toPlayer = Enemy.GetDirectionToPlayer();
            if (Mathf.Abs(toPlayer.X) > 0.01f)
            {
                Enemy.FlipFacing(toPlayer.X > 0f);
            }
        }

		protected override void OnAttackFinished()
		{
			base.OnAttackFinished();
			_isDashing = false;
			_dashFinalized = true;
			if (Enemy != null)
			{
				Enemy.Velocity = Vector2.Zero;
			}
			_playerInsideDetection = false;
			if (_postAttackCooldown <= 0f)
			{
				StartPostCooldown();
			}
    }

		private void StartPostCooldown()
        {
			if (Enemy == null) return;

			bool starting = _postAttackCooldown <= 0f;
			_postAttackCooldown = PostCooldownDuration;
			Enemy.AttackTimer = Mathf.Max(Enemy.AttackTimer, PostCooldownDuration);
			Enemy.Velocity = Vector2.Zero;

			if (starting)
			{
				if (!CooldownStateName.IsEmpty && Enemy.StateMachine != null)
				{
					Enemy.StateMachine.ChangeState(CooldownStateName);
				}
			}

			_pendingCooldownExit = true;
		}

		private void FinishCooldownState()
		{
			if (Enemy?.StateMachine == null) return;

			// 只有仍处于冷却状态时才负责退出；外部状态已被接管则不干预。
			if (Enemy.StateMachine.CurrentState?.Name == CooldownStateName)
			{
				Enemy.StateMachine.ChangeState("Walk");
			}

			if (IsRunning)
			{
				Cancel();
            }
        }

}
}
