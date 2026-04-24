using Godot;
using Kuros.Actors.Heroes.States;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 冲刺抓取攻击：
	/// 1. 玩家进入检测区域后触发预热；
	/// 2. 预热结束直线冲刺至玩家先前位置；
	/// 3. 冲刺结束若命中，施加冻结并进入逃脱判定；
	/// 4. 抓取期间由 Spine hit 关键帧触发伤害结算。
    /// </summary>
    public partial class EnemyChargeGrabAttack : EnemyAttackTemplate
    {
        [ExportCategory("Areas")]
        [Export] public NodePath DetectionAreaPath = new NodePath();
        [Export] public NodePath GrabAreaPath = new NodePath();

        [ExportCategory("Dash")]
        [Export(PropertyHint.Range, "10,2000,10")] public float DashSpeed = 600f;
		[Export(PropertyHint.Range, "0,2000,10")] public float DashDistance = 0f;
        [Export] public bool LockFacingDuringDash = true;
		[Export(PropertyHint.Range, "0,500,1")] public float MinDashDistanceBeforeGrab = 24f;
		[Export(PropertyHint.Range, "0,5,0.1")] public float SnapshotDelaySeconds = 0f; // 冲刺前等待一段时间再记录玩家位置

        [ExportCategory("Effects")]
		[Export(PropertyHint.Range, "0,10,0.1")] public float AppliedFrozenDuration = 5.0f;
        [Export(PropertyHint.Range, "0,1000,1")] public int GrabHitDamage = 1;
		[Export] public StringName CooldownStateName = "CooldownFrozen";

		[ExportCategory("Escape")]
		[Export(PropertyHint.Range, "0,10,0.1")] public float EscapeWindowSeconds = 0.0f;
		[Export] public bool GrantInvincibilityOnEscape = true;
		[Export(PropertyHint.Range, "0,5,0.01")] public float EscapeInvincibilityDuration = 0.0f;

		private const float MinDashDistance = 32f;
		private const float PostCooldownDuration = 1.0f;

        private Area2D? _detectionArea;
        private Area2D? _grabArea;
        private EnemyAttackController? _controller;
		private bool _playerInsideDetection;

        private Vector2 _dashDirection = Vector2.Right;
		private Vector2 _dashTarget;
		private SamplePlayer? _grabbedPlayer;
		private bool _isEvaluatingEscape;
		private float _escapeTimer;
		private bool _isDashing;
		private bool _dashFinalized;
		private bool _skipRecoveryGrab;
		private float _postAttackCooldown;
		private bool _pendingCooldownExit;
        private Vector2 _dashPreviousPosition;
        private float _dashDistanceTraveled;
        private bool _canAttemptGrab;
		private float _snapshotTimer = 0f;
		private bool _waitingForSnapshot = false;
		private bool _pendingSkill3Finisher;
		private bool _grabFrozenDisplacementOriginal = true;
		private bool _hasGrabFrozenDisplacementOverride;

		public bool IsEvaluatingEscape => _isEvaluatingEscape;
		public bool IsDashing => _isDashing;
		public bool IsDashFinished => _dashFinalized;
		public virtual bool AreEscapeCountersCleared => true;
		public bool HasPendingSkill3Finisher => _pendingSkill3Finisher;
		protected float EscapeTimerRemaining => _escapeTimer;

		public void ConsumeSkill3FinisherRequest()
		{
			_pendingSkill3Finisher = false;
		}

        protected override void OnInitialized()
        {
            base.OnInitialized();
            _controller = GetParent() as EnemyAttackController;

            _detectionArea = ResolveArea(DetectionAreaPath);
            if (_detectionArea != null)
            {
				_detectionArea.Monitoring = true;
                _detectionArea.BodyEntered += OnDetectionAreaBodyEntered;
				_detectionArea.BodyExited += OnDetectionAreaBodyExited;
			}
			else
			{
				GD.PushWarning($"[EnemyChargeGrabAttack] DetectionArea not found for {Enemy?.Name ?? Name}, fallback to DetectionRange.");
            }

            _grabArea = ResolveArea(GrabAreaPath);
            if (_grabArea == null)
            {
                _grabArea = AttackArea;
            }

			SetPhysicsProcess(true);
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
			_skipRecoveryGrab = false;
			_postAttackCooldown = 0f;
			_pendingCooldownExit = false;
			_dashPreviousPosition = Enemy?.GlobalPosition ?? Vector2.Zero;
			_dashDistanceTraveled = 0f;
			_canAttemptGrab = MinDashDistanceBeforeGrab <= 0f;
			_pendingSkill3Finisher = false;
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
			Enemy.Velocity = _dashDirection * DashSpeed;

			if (RequireAnimationHitTrigger)
			{
				_animationHitReady = true;
			}
        }

		private bool HasActiveGrab => _grabbedPlayer != null || _isEvaluatingEscape;

        protected override void OnRecoveryStarted()
        {
            base.OnRecoveryStarted();
			_playerInsideDetection = false;
			if (Enemy != null)
			{
				Enemy.Velocity = Vector2.Zero;
			}

			if (_skipRecoveryGrab)
			{
				_skipRecoveryGrab = false;
				if (RequireAnimationHitTrigger && HasActiveGrab)
				{
					_animationHitReady = true;
				}
				return;
			}

			if (!_dashFinalized)
			{
				FinishDash();
			}

			if (HasActiveGrab)
			{
				if (RequireAnimationHitTrigger)
				{
					_animationHitReady = true;
				}
				return;
			}

			if (!_canAttemptGrab)
			{
				// 未达到可抓取条件时，等待攻击流程自然结束后再进入冷却。
				_pendingSkill3Finisher = true;
				return;
			}

			if (!TryExecuteGrab())
			{
				// 未抓到玩家时，不立即切冷却状态，避免直接打断攻击收尾动画。
				_pendingSkill3Finisher = true;
				return;
			}
		}

		public override void _PhysicsProcess(double delta)
		{
			if (Enemy == null || !GodotObject.IsInstanceValid(Enemy) || !Enemy.IsInsideTree())
			{
				return;
			}

			if (!IsEnemyAlive())
			{
				AbortActiveGrabDueToEnemyDeath();
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

			if (_isEvaluatingEscape && _grabbedPlayer != null)
			{
				_escapeTimer -= (float)delta;
				UpdateEscapeSequence(_grabbedPlayer, delta);

				if (_escapeTimer <= 0f)
				{
					ResolveEscape(false);
				}
			}

			if (_postAttackCooldown > 0f)
			{
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
				// 只有当敌人处于冷却相关状态时，才由此处控制移动
				// 否则让状态机自己处理，避免覆盖其他状态的速度设置
				var currentStateName = Enemy?.StateMachine?.CurrentState?.Name;
				if (currentStateName == CooldownStateName || currentStateName == "Attack")
				{
					if (Enemy != null)
					{
						Enemy.Velocity = Vector2.Zero;
						Enemy.MoveAndSlide();
					}
				}
				return;
			}

			UpdateDashMovement(delta);
			UpdateDetectionTracking();
		}

		protected virtual void UpdateEscapeSequence(SamplePlayer player, double delta)
		{
			// 子类实现具体逃脱判定逻辑
		}

		protected override bool ShouldHoldRecoveryPhase()
		{
			return HasActiveGrab;
		}

		protected void ResolveEscape(bool escaped)
		{
			if (_grabbedPlayer == null)
			{
				_isEvaluatingEscape = false;
				return;
			}

			_isEvaluatingEscape = false;

			var player = _grabbedPlayer;

			if (!escaped)
			{
				ReleasePlayer();
        }
			else
			{
				ReleasePlayer();
			}

			if (player != null)
			{
				GrantPlayerInvincibilityAfterEscape(player, escaped);
				OnEscapeSequenceFinished(player, escaped);
			}

			// 抓到后进入挣脱，判定结束（成功/失败）都进入 skill3 收尾；伤害由 hit 帧事件结算。
			_pendingSkill3Finisher = true;

			// 挣脱判定结束后不立即取消攻击，让恢复阶段自然收尾，避免瞬切冷却状态。
		}

		protected virtual void OnEscapeSequenceStarted(SamplePlayer player) { }

		protected virtual void OnEscapeSequenceFinished(SamplePlayer player, bool escaped) { }

		protected virtual void GrantPlayerInvincibilityAfterEscape(SamplePlayer player, bool escaped)
		{
			if (!escaped || !GrantInvincibilityOnEscape)
			{
				return;
			}

			if (player is not Kuros.Actors.Heroes.MainCharacter mainCharacter)
			{
				return;
			}

			float duration = EscapeInvincibilityDuration > 0f
				? EscapeInvincibilityDuration
				: mainCharacter.HitInvincibilityDuration;

			mainCharacter.StartHitInvincibility(duration);
		}

		private void PrepareDashTowardsPlayer()
        {
            if (Enemy == null) return;

			Vector2 dashStart = Enemy.GlobalPosition;
			Vector2 recordedTarget;

			if (Enemy.PlayerTarget != null)
			{
				// Snapshot player position only once at dash start; no realtime retargeting during dash.
				recordedTarget = Enemy.PlayerTarget.GlobalPosition;
			}
			else
			{
				recordedTarget = dashStart + (Enemy.FacingRight ? Vector2.Right : Vector2.Left) * MinDashDistance;
			}

			Vector2 direction = recordedTarget - dashStart;
			if (direction == Vector2.Zero)
			{
				direction = Enemy.FacingRight ? Vector2.Right : Vector2.Left;
            }

			_dashDirection = direction.Normalized();

			float distanceToRecorded = direction.Length();
			float targetDistance = distanceToRecorded;

			if (DashDistance > 0)
			{
				targetDistance = Mathf.Min(distanceToRecorded, DashDistance);
			}

			if (targetDistance < MinDashDistance)
			{
				targetDistance = MinDashDistance;
			}

			_dashTarget = dashStart + _dashDirection * targetDistance;

            if (LockFacingDuringDash && _dashDirection.X != 0)
            {
                Enemy.FlipFacing(_dashDirection.X > 0);
            }

			float dashTime = Mathf.Max(targetDistance / Mathf.Max(DashSpeed, 1f), 0.05f);
            ActiveDuration = dashTime;
			RecoveryDuration = 1.0f;
        }

		private bool TryExecuteGrab()
        {
			if (Enemy == null) return false;

            var player = Enemy.PlayerTarget;
			if (player == null)
			{
				return false;
			}

			if (player is Kuros.Actors.Heroes.MainCharacter mainCharacter && mainCharacter.IsHitInvincible)
			{
				_playerInsideDetection = false;
				return false;
			}

            if (!IsPlayerInsideGrabZone(player))
            {
				_playerInsideDetection = false;
				return false;
            }

			_grabbedPlayer = player;
            ApplyFrozenState(player);
			_playerInsideDetection = false;

			_isEvaluatingEscape = true;
			_escapeTimer = EscapeWindowSeconds > 0f ? EscapeWindowSeconds : AppliedFrozenDuration;
			if (RequireAnimationHitTrigger)
			{
				_animationHitReady = true;
			}
			OnEscapeSequenceStarted(player);
			return true;
        }

        private bool IsPlayerInsideGrabZone(SamplePlayer player)
        {
            if (_grabArea != null)
            {
				return player.IsHitByArea(_grabArea);
            }

			return player.IsHitByArea(AttackArea);
        }

        private void ApplyFrozenState(SamplePlayer player)
        {
            var frozenState = player.StateMachine?.GetNodeOrNull<PlayerFrozenState>("Frozen");
            if (frozenState != null)
            {
				_grabFrozenDisplacementOriginal = frozenState.AllowExternalDisplacementWhileFrozen;
				_hasGrabFrozenDisplacementOverride = true;
				frozenState.AllowExternalDisplacementWhileFrozen = false;
                frozenState.FrozenDuration = AppliedFrozenDuration;
				frozenState.BeginExternalHold();
            player.StateMachine?.ChangeState("Frozen");
			}
        }

		private void ReleasePlayer()
		{
			if (_grabbedPlayer == null) return;

			var frozenState = _grabbedPlayer.StateMachine?.GetNodeOrNull<PlayerFrozenState>("Frozen");
			if (frozenState != null)
			{
				frozenState.EndExternalHold();
				if (_hasGrabFrozenDisplacementOverride)
				{
					frozenState.AllowExternalDisplacementWhileFrozen = _grabFrozenDisplacementOriginal;
				}
			}
			_hasGrabFrozenDisplacementOverride = false;

			_grabbedPlayer = null;

			// 交由 OnAttackFinished 统一进入冷却，避免在释放瞬间切状态打断动画。
		}

		protected override void OnAnimationHit()
		{
			if (_grabbedPlayer == null || !IsEnemyAlive())
			{
				return;
			}

			var sourcePosition = Enemy?.GlobalPosition;
			if (Enemy != null)
			{
				_grabbedPlayer.TakeDamage(GrabHitDamage, sourcePosition, Enemy);
			}
			else
			{
				_grabbedPlayer.TakeDamage(GrabHitDamage, sourcePosition);
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

			if (Enemy.StateMachine.CurrentState?.Name == CooldownStateName)
			{
				Enemy.StateMachine.ChangeState("Walk");
			}
			else if (Enemy.StateMachine.CurrentState?.Name == "Attack")
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
			if (HasActiveGrab) return;
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
			if (HasActiveGrab) return;
			if (_postAttackCooldown > 0f) return;

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
			if (!_isDashing || Enemy == null) return;

			UpdateDashTravelProgress();

			if (_canAttemptGrab && Enemy.PlayerTarget != null && IsPlayerInsideGrabZone(Enemy.PlayerTarget))
			{
				FinishDash(forceGrab: true);
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

			Enemy.Velocity = _dashDirection * DashSpeed;
		}

		private void FinishDash(bool forceGrab = false)
		{
			if (Enemy == null) return;

			_dashFinalized = true;

			if (!forceGrab)
        	{
				//Enemy.GlobalPosition = _dashTarget;
			}

			Enemy.Velocity = Vector2.Zero;
			_dashPreviousPosition = Enemy.GlobalPosition;
			_isDashing = false;
			if (forceGrab)
			{
				_skipRecoveryGrab = true;
				ForceEnterRecoveryPhase();
				_canAttemptGrab = true;
				if (!TryExecuteGrab())
				{
					// 强制收尾时若未抓到玩家，保持在攻击流程内，交由 OnAttackFinished 统一进入冷却。
					_pendingSkill3Finisher = true;
				}
				return;
			}

			ForceEnterRecoveryPhase();
        }

		private void UpdateDashTravelProgress()
		{
			if (Enemy == null) return;
			Vector2 currentPosition = Enemy.GlobalPosition;
			float moved = (_dashPreviousPosition - currentPosition).Length();
			if (moved > 0f)
			{
				_dashDistanceTraveled += moved;
				_dashPreviousPosition = currentPosition;
				if (!_canAttemptGrab && _dashDistanceTraveled >= MinDashDistanceBeforeGrab)
				{
					_canAttemptGrab = true;
				}
			}
		}

        private Area2D? ResolveArea(NodePath path)
        {
            if (path.IsEmpty)
            {
                return null;
            }

            var area = GetNodeOrNull<Area2D>(path);
            if (area != null)
            {
                return area;
            }

            return Enemy?.GetNodeOrNull<Area2D>(path);
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

		private bool IsEnemyAlive()
		{
			return Enemy != null && !Enemy.IsDeathSequenceActive && !Enemy.IsDead;
		}

		private void AbortActiveGrabDueToEnemyDeath()
		{
			if (_grabbedPlayer != null)
			{
				var player = _grabbedPlayer;
				ReleasePlayer();
				OnEscapeSequenceFinished(player, escaped: false);
			}

			_isEvaluatingEscape = false;
			_escapeTimer = 0f;
			_isDashing = false;
			_waitingForSnapshot = false;
			_snapshotTimer = 0f;
			_playerInsideDetection = false;
			_pendingSkill3Finisher = false;
			_skipRecoveryGrab = false;
			_animationHitReady = false;

			if (IsRunning)
			{
				Cancel(clearCooldown: true);
			}
		}

		protected override void OnAttackFinished()
		{
			base.OnAttackFinished();
			_playerInsideDetection = false;
			if (!IsEnemyAlive())
			{
				_skipRecoveryGrab = false;
				return;
			}

			if (_grabbedPlayer == null && _postAttackCooldown <= 0f)
			{
				StartPostCooldown();
			}
			_skipRecoveryGrab = false;
    }
}
}
