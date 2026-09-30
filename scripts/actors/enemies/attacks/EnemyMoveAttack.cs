using Godot;
using Kuros.Core;
using Kuros.Actors.Enemies.States;
using Kuros.Actors.Heroes.States;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 冲刺攻击（时间控制）：
	/// 1. 玩家进入检测区域后触发预热；
	/// 2. 预热结束锁定方向后，持续冲刺 DashDuration 秒；
	/// 3. 冲刺期间命中玩家即造成伤害 + 击退，冲刺本身不中断；
	/// 4. 时间结束后进入 Recovery。
    /// </summary>
    public partial class EnemyMoveAttack : EnemyAttackTemplate
    {
        // 区域由基类统一提供：TriggerAreaPath（起手检测区；未配置 = 不做额外限制）、
        // AttackAreaPath（命中判定带）、DamageAreaPath（伤害落点；未配置 = AttackArea）。
        // 旧的 DetectionAreaPath / MoveAttackAreaPath 已并入基类。

        [ExportCategory("Dash")]
        /// <summary>冲刺速度 = 基础 Speed × 倍率（倍率语义：基础速度调整时冲刺自动适配，无需同步改绝对值）。</summary>
        [Export(PropertyHint.Range, "0.1,10,0.1")] public float DashSpeedMultiplier = 1.1f;
		[Export(PropertyHint.Range, "0.05,10,0.05")] public float DashDuration = 0.5f; // 冲刺持续时间（秒）
        [Export] public bool LockFacingDuringDash = true;
		[Export(PropertyHint.Range, "0,5,0.01")] public float MinDashTimeBeforeAttack = 0f; // 允许命中前的最短冲刺时间（秒）
		[Export(PropertyHint.Range, "0,5,0.1")] public float SnapshotDelaySeconds = 0f; // 冲刺前等待一段时间再记录玩家位置
		        /// <summary>
        /// 启用后 Dash 阶段通过 NavigationAgent2D 路径点计算方向，实现绕障冲刺。
        /// 禁用则维持原始直线追踪行为。
        /// </summary>
        [Export] public bool UseNavDuringDash = true;

		[ExportCategory("Dash Prop Clear")]
		/// <summary>冲刺期间销毁沿路经过的一次性投掷道具(家具类:天然家具与乱码块件都清,不含投掷武器)。</summary>
		[Export] public bool DestroyFurnitureDuringDash { get; set; } = false;
		/// <summary>清障探测盒长度(世界像素):自敌人中心沿冲刺方向向前延伸。</summary>
		[Export(PropertyHint.Range, "0,600,5")] public float PropClearLength { get; set; } = 140f;
		/// <summary>清障探测盒高度(世界像素):以敌人原点垂直居中。</summary>
		[Export(PropertyHint.Range, "0,600,5")] public float PropClearHeight { get; set; } = 220f;

        [ExportCategory("Effects")]
		[Export] public StringName CooldownStateName = "CooldownFrozen";
		[Export(PropertyHint.Range, "0,10,0.1")] public float DashEndSelfFrozenDuration = 3f;

		private const float PostCooldownDuration = 1.0f;

        private Area2D? _detectionArea;
        private EnemyAttackController? _controller;
        private NavigationAgent2D? _navAgent;
		private bool _playerInsideDetection;

        private Vector2 _dashDirection = Vector2.Right;
		private bool _isDashing;
		private bool _dashFinalized;
		private float _postAttackCooldown;
		private bool _pendingCooldownExit;
		private float _dashTimeElapsed;
		private bool _canAttemptMoveAttack;
		private float _snapshotTimer = 0f;
		private bool _waitingForSnapshot = false;
		private bool _pendingSelfFrozenAfterDash;

		public bool IsDashing => _isDashing;
		public bool IsDashFinished => _dashFinalized;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            _controller = GetParent() as EnemyAttackController;

            _detectionArea = TriggerArea;
            if (_detectionArea != null)
            {
				_detectionArea.Monitoring = true;
                _detectionArea.BodyEntered += OnDetectionAreaBodyEntered;
				_detectionArea.BodyExited += OnDetectionAreaBodyExited;
			}
			else
			{
				GD.PushWarning($"[EnemySmashAttack] DetectionArea not found for {Enemy?.Name ?? Name}, fallback to DetectionRange.");
            }

			SetPhysicsProcess(true);

            // 缓存 NavigationAgent2D，用于 Dash 阶段避障路径跟随
            _navAgent = Enemy?.GetNodeOrNull<NavigationAgent2D>("NavigationAgent2D");
        }

        public override void _ExitTree()
        {
            if (_detectionArea != null)
            {
				var entered = new Callable(this, MethodName.OnDetectionAreaBodyEntered);
				var exited = new Callable(this, MethodName.OnDetectionAreaBodyExited);
				if (_detectionArea.IsConnected(Area2D.SignalName.BodyEntered, entered))
				{
					_detectionArea.BodyEntered -= OnDetectionAreaBodyEntered;
				}

				if (_detectionArea.IsConnected(Area2D.SignalName.BodyExited, exited))
				{
					_detectionArea.BodyExited -= OnDetectionAreaBodyExited;
				}
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
			_postAttackCooldown = 0f;
			_pendingCooldownExit = false;
			_dashTimeElapsed = 0f;
			_canAttemptMoveAttack = MinDashTimeBeforeAttack <= 0f;
			_pendingSelfFrozenAfterDash = false;
			PrepareDashTowardsPlayer();
		}

		protected override void OnWarmupStarted()
		{
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
			Enemy.Velocity = _dashDirection * (Enemy.Speed * DashSpeedMultiplier);

			// 启用动画事件触发时，开放命中窗口供 TriggerAnimationHit 调用
			if (RequireAnimationHitTrigger)
			{
				_animationHitReady = true;
			}
        }

        protected override void OnRecoveryStarted()
        {
            base.OnRecoveryStarted();
			if (_isDashing && DashEndSelfFrozenDuration > 0f)
			{
				_pendingSelfFrozenAfterDash = true;
			}
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
			if (Enemy == null || !GodotObject.IsInstanceValid(Enemy) || !Enemy.IsInsideTree() || Enemy.IsDeathSequenceActive || Enemy.IsDead)
			{
				return;
			}

			// frozen/受击状态下停止所有追踪与冲刺逻辑
			var stateName = Enemy.StateMachine?.CurrentState?.Name;
			if (stateName == "Frozen" || stateName == "CooldownFrozen"
				|| stateName == "Hit" || stateName == "Dying" || stateName == "Dead")
			{
				return;
			}

			// 预热阶段（快照等待 / Warmup）：持续静止——防 MoveAndSlide 惯性/外部移动系统重写
			// （仅 OnWarmupStarted 归零一次不够：后续帧的 Velocity 会被外部移动源重新写入）
			if (_waitingForSnapshot || CurrentPhase == AttackPhase.Warmup)
			{
				Enemy.Velocity = Vector2.Zero;

				// 快照延迟计时
				if (_waitingForSnapshot)
				{
					_snapshotTimer -= (float)delta;
					if (_snapshotTimer <= 0f)
					{
						_waitingForSnapshot = false;
						PrepareDashTowardsPlayer(); // 延迟结束，此时快照玩家位置
					}
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

            // 若启用导航避障，提前设置目标位置让 NavAgent 开始计算路径
            if (UseNavDuringDash && _navAgent != null && Enemy.PlayerTarget != null)
            {
                _navAgent.TargetPosition = Enemy.PlayerTarget.GlobalPosition;
                if (!_navAgent.IsNavigationFinished())
                {
                    Vector2 nextPoint = _navAgent.GetNextPathPosition();
                    Vector2 navDir = (nextPoint - Enemy.GlobalPosition).Normalized();
                    if (!navDir.IsZeroApprox())
                        _dashDirection = navDir;
                }
            }

            if (LockFacingDuringDash && _dashDirection.X != 0)
            {
                Enemy.FlipFacing(_dashDirection.X > 0);
            }

			// 使用时间控制冲刺持续长度
            ActiveDuration = Mathf.Max(DashDuration, 0.05f);
        }

		private bool TryExecuteMoveAttack()
        {
			if (Enemy == null) return false;

            var player = Enemy.PlayerTarget;
			if (player == null)
			{
				return false;
			}

	            if (!IsPlayerInsideMoveAttackZone(player))
            {
				_playerInsideDetection = false;
				return false;
            }

			// 成功命中：伤害 + 击退。
			ApplyMoveAttackDamage(player);
	            ApplyMoveAttackKnockback(player);
			_playerInsideDetection = false;

			// 命中后先保持当前攻击流程，避免动画被立即切到冷却状态。
			return true;
        }

		private bool IsPlayerInsideMoveAttackZone(SamplePlayer player)
        {
			return player.IsHitByArea(AttackArea);
        }

		private void ApplyMoveAttackDamage(SamplePlayer player)
		{
			if (DamageArea == null || Enemy == null) return;

			ApplyAttackAreaMaskOverride(DamageArea);
			DealDamage(DamageArea);
		}

		private void ApplyMoveAttackKnockback(SamplePlayer player)
		{
			if (Enemy == null) return;

			TryApplyPlayerKnockback(
				player,
				KnockbackDistance,
				KnockbackDuration,
				_dashDirection);
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

			// 冻结/受击状态下不触发攻击
			var currentStateName = Enemy.StateMachine?.CurrentState?.Name;
			if (currentStateName == "Frozen" || currentStateName == "CooldownFrozen"
				|| currentStateName == "Hit" || currentStateName == "Dying" || currentStateName == "Dead")
			{
				return;
			}

			if (_controller != null && _controller.PeekQueuedAttack() != this)
			{
				return;
			}

			if (Enemy.StateMachine?.CurrentState?.Name != "Attack")
            {
                Enemy.StateMachine?.ChangeState("Attack");
            }
        }

		private void UpdateDashMovement(double delta)
		{
			if (!_isDashing || Enemy == null || Enemy.IsDeathSequenceActive || Enemy.IsDead) return;

			// 最短冲刺时间计时
			if (!_canAttemptMoveAttack)
			{
				_dashTimeElapsed += (float)delta;
				if (_dashTimeElapsed >= MinDashTimeBeforeAttack)
				{
					_canAttemptMoveAttack = true;
				}
			}

			// 命中检测：不中断冲刺，重叠期间可持续触发（启用动画事件触发时跳过此处）
			// DealDamage 必须在 Player 检测之前无条件调用，确保非 Player 目标也能被处理
			if (!RequireAnimationHitTrigger && _canAttemptMoveAttack && Enemy.PlayerTarget != null)
			{
				ApplyMoveAttackDamage(Enemy.PlayerTarget);
				if (IsPlayerInsideMoveAttackZone(Enemy.PlayerTarget))
				{
					ApplyMoveAttackKnockback(Enemy.PlayerTarget);
				}
			}

			// 实时追踪玩家位置更新冲刺方向
			if (Enemy.PlayerTarget != null)
			{
				Vector2 newDir;

				if (UseNavDuringDash && _navAgent != null)
				{
					_navAgent.TargetPosition = Enemy.PlayerTarget.GlobalPosition;
					if (!_navAgent.IsNavigationFinished())
					{
						Vector2 nextPoint = _navAgent.GetNextPathPosition();
						newDir = (nextPoint - Enemy.GlobalPosition).Normalized();
					}
					else
					{
						newDir = (Enemy.PlayerTarget.GlobalPosition - Enemy.GlobalPosition).Normalized();
					}
					if (newDir.IsZeroApprox()) newDir = _dashDirection;
				}
				else
				{
					Vector2 toPlayer = Enemy.PlayerTarget.GlobalPosition - Enemy.GlobalPosition;
					newDir = toPlayer != Vector2.Zero ? toPlayer.Normalized() : _dashDirection;
				}

				_dashDirection = newDir;
				if (!LockFacingDuringDash && _dashDirection.X != 0)
				{
					Enemy.FlipFacing(_dashDirection.X > 0);
				}
			}

			// 持续冲刺，直到 DashDuration 到期由基类切入 Recovery
			// 清障置于方向刷新之后:取其当帧导航方向(NavDuringDash 下逐帧变向)
			if (DestroyFurnitureDuringDash)
				EnemyDashPropClearer.ClearAhead(Enemy, _dashDirection, PropClearLength, PropClearHeight);
			Enemy.Velocity = _dashDirection * (Enemy.Speed * DashSpeedMultiplier);
		}

		protected override void OnAnimationHit()
		{
			if (Enemy?.PlayerTarget == null) return;
			if (!_canAttemptMoveAttack) return;

			// DealDamage 必须在 Player 检测之前无条件调用：
			// DealDamageFromArea 统一处理 Player + 非 Player（WorldItem 等）
			// 所有阵营目标的伤害。若放在 Player 守卫内，则 Player 不在
			// 区域内时 WorldItem 等其他 target 也会被跳过。
			ApplyMoveAttackDamage(Enemy.PlayerTarget);
			
			if (IsPlayerInsideMoveAttackZone(Enemy.PlayerTarget))
			{
				ApplyMoveAttackKnockback(Enemy.PlayerTarget);
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
			// 清冲刺状态与残留速度：dash 中被 Cancel 打断会跳过 Recovery（_isDashing 在
			// OnRecoveryStarted 才置 false）——残留 true 会让 _PhysicsProcess 的
			// UpdateDashMovement 在后续攻击期间每帧写入 dash 速度（Guard2 近战攻击时移动）
			_isDashing = false;
			_canAttemptMoveAttack = false;
			if (Enemy != null)
				Enemy.Velocity = Vector2.Zero;
			_playerInsideDetection = false;
			if (_pendingSelfFrozenAfterDash)
			{
				_pendingSelfFrozenAfterDash = false;
				if (TryEnterSelfFrozenState())
				{
					return;
				}
			}
			if (_postAttackCooldown <= 0f)
			{
				StartPostCooldown();
			}
    }

		private bool TryEnterSelfFrozenState()
		{
			if (Enemy?.StateMachine == null) return false;

			var frozenState = Enemy.StateMachine.GetNodeOrNull<EnemyFrozenState>("Frozen");
			if (frozenState == null)
			{
				return false;
			}

			frozenState.FrozenDuration = Mathf.Max(DashEndSelfFrozenDuration, 0.1f);
			Enemy.StateMachine.ChangeState("Frozen");
			return true;
		}
}
}
