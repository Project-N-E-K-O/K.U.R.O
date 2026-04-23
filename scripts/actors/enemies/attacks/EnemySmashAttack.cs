using Godot;
using Kuros.Actors.Heroes.States;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 冲刺重击攻击（时间控制）：
	/// 1. 玩家进入检测区域后触发预热；
	/// 2. SnapshotDelaySeconds 结束后快照玩家位置并开始冲刺；
	/// 3. 冲刺期间命中玩家即造成伤害、击退并施加定时 Frozen；
	/// 4. 冲刺到快照位置后立刻停下并进入 Recovery。
    /// </summary>
    public partial class EnemySmashAttack : EnemyAttackTemplate
    {
        [ExportCategory("Areas")]
        [Export] public NodePath DetectionAreaPath = new NodePath();
        [Export] public NodePath SmashAreaPath = new NodePath();

        [ExportCategory("Dash")]
        [Export(PropertyHint.Range, "10,2000,10")] public float DashSpeed = 600f;
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float DashDuration = 0.3f; // 冲刺时长（限制最大冲刺距离）
        [Export] public bool LockFacingDuringDash = true;
		[Export(PropertyHint.Range, "0,5,0.01")] public float MinDashTimeBeforeSmash = 0f;
		[Export(PropertyHint.Range, "0,5,0.1")] public float SnapshotDelaySeconds = 0f;
		[Export(PropertyHint.Range, "0,9999,1")] public int SmashDmg = 25;

        [ExportCategory("Effects")]
		[Export(PropertyHint.Range, "0,10,0.1")] public float AppliedStunDuration = 3.0f;
		[Export] public StringName CooldownStateName = "CooldownFrozen";

		private const float PostCooldownDuration = 1.0f;

        private Area2D? _detectionArea;
		private Area2D? _smashArea;
        private EnemyAttackController? _controller;
		private bool _playerInsideDetection;

        private Vector2 _dashDirection = Vector2.Right;
		private Vector2 _dashTarget;
		private bool _isDashing;
		private bool _dashFinalized;
		private float _postAttackCooldown;
		private bool _pendingCooldownExit;
		private float _dashTimeElapsed;
		private bool _canAttemptSmash;
		private float _snapshotTimer;
		private bool _waitingForSnapshot;
		private float _configuredWarmupDuration;

		public bool IsDashing => _isDashing;
		public bool IsDashFinished => _dashFinalized;

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
				GD.PushWarning($"[EnemySmashAttack] DetectionArea not found for {Enemy?.Name ?? Name}, fallback to DetectionRange.");
            }

	            _smashArea = ResolveArea(SmashAreaPath);
	            if (_smashArea == null)
            {
	                _smashArea = AttackArea;
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
			_dashTarget = Enemy?.GlobalPosition ?? Vector2.Zero;
			_dashTimeElapsed = 0f;
			_canAttemptSmash = MinDashTimeBeforeSmash <= 0f;
			_configuredWarmupDuration = WarmupDuration;
			WarmupDuration = Mathf.Max(_configuredWarmupDuration, SnapshotDelaySeconds);
		}

		protected override void OnWarmupStarted()
		{
			base.OnWarmupStarted();
			if (Enemy != null)
			{
				Enemy.Velocity = Vector2.Zero;
			}

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
			// 生成指定时機的特效（OnActive 阯段）
			if (SpawnTiming == EffectSpawnTiming.OnActive)
			{
				SpawnEffectAtEnemy();
			}
			
			if (Enemy == null) return;
			_isDashing = true;
			Enemy.Velocity = _dashDirection * DashSpeed;

			if (RequireAnimationHitTrigger)
			{
				_animationHitReady = true;
			}
        }

        protected override void OnRecoveryStarted()
        {
            // base.OnRecoveryStarted() 会处理 OnRecovery 时机的特效生成
            base.OnRecoveryStarted();
			if (RequireAnimationHitTrigger)
			{
				// hit 关键帧可能落在冲刺结束后的恢复阶段，保持命中窗口直到实际收到 hit。
				_animationHitReady = true;
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
			if (Enemy == null || !GodotObject.IsInstanceValid(Enemy) || !Enemy.IsInsideTree())
			{
				return;
			}

			if (_waitingForSnapshot)
			{
				_snapshotTimer -= (float)delta;
				if (_snapshotTimer <= 0f)
				{
					_waitingForSnapshot = false;
					PrepareDashTowardsPlayer();
				}
				return;
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
            
            // 根据 DashDuration 限制冲刺距离（参考 KickAttack 的设计）
            float targetDistance = DashSpeed * Mathf.Max(DashDuration, 0.05f);
            _dashTarget = dashStart + _dashDirection * targetDistance;

            if (LockFacingDuringDash && _dashDirection.X != 0)
            {
                Enemy.FlipFacing(_dashDirection.X > 0);
            }

			ActiveDuration = Mathf.Max(DashDuration, 0.05f);
			RecoveryDuration = 1.0f;
        }

		private bool IsPlayerInsideSmashZone(SamplePlayer player)
        {
	            if (_smashArea != null)
            {
				return player.IsHitByArea(_smashArea);
            }

			return player.IsHitByArea(AttackArea);
        }

		private void ApplySmashDamage(SamplePlayer player)
		{
			if (Enemy == null) return;

			int damage = Mathf.Max(1, SmashDmg);
			player.TakeDamage(damage, Enemy.GlobalPosition, Enemy);
		}

		private void ApplySmashEffects(SamplePlayer player)
		{
			ApplySmashDamage(player);

			TryApplyPlayerKnockback(
				player,
				KnockbackDistance,
				KnockbackDuration,
				KnockbackSpeed,
				_dashDirection);

			ApplyStunState(player);
		}

		private void ApplyStunState(SamplePlayer player)
        {
            var frozenState = player.StateMachine?.GetNodeOrNull<PlayerFrozenState>("Frozen");
            if (frozenState == null)
            {
				return;
            }

			frozenState.FrozenDuration = AppliedStunDuration;
			player.StateMachine?.ChangeState("Frozen");
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

			if (Enemy.StateMachine?.CurrentState?.Name != "Attack")
            {
                Enemy.StateMachine?.ChangeState("Attack");
            }
        }

		private void UpdateDashMovement(double delta)
		{
			if (!_isDashing || Enemy == null) return;

			if (!_canAttemptSmash)
			{
				_dashTimeElapsed += (float)delta;
				if (_dashTimeElapsed >= MinDashTimeBeforeSmash)
				{
					_canAttemptSmash = true;
				}
			}

			if (!RequireAnimationHitTrigger && _canAttemptSmash && Enemy.PlayerTarget != null && IsPlayerInsideSmashZone(Enemy.PlayerTarget))
			{
				ApplySmashEffects(Enemy.PlayerTarget);
			}

			Vector2 toTarget = _dashTarget - Enemy.GlobalPosition;
			float projected = toTarget.Dot(_dashDirection);
			if (projected <= 0f)
			{
				EndDashAtSnapshot();
				return;
			}

			float maxStep = DashSpeed * (float)delta;
			if (toTarget.LengthSquared() <= maxStep * maxStep)
			{
				EndDashAtSnapshot();
				return;
			}

			Enemy.Velocity = _dashDirection * DashSpeed;
		}

		private void EndDashAtSnapshot()
		{
			if (Enemy == null) return;

			Enemy.GlobalPosition = _dashTarget;
			Enemy.Velocity = Vector2.Zero;
			_isDashing = false;
			_dashFinalized = true;
			ForceEnterRecoveryPhase();
		}

		protected override void OnAnimationHit()
		{
			// 生成指定时機的特效（OnAnimationHit 阯段3）
			if (SpawnTiming == EffectSpawnTiming.OnAnimationHit)
			{
				SpawnEffectAtEnemy();
			}
			
			if (Enemy?.PlayerTarget == null) return;
			if (!_canAttemptSmash) return;

			if (IsPlayerInsideSmashZone(Enemy.PlayerTarget))
			{
				ApplySmashEffects(Enemy.PlayerTarget);
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

		protected override void OnAttackFinished()
		{
			base.OnAttackFinished();
			WarmupDuration = _configuredWarmupDuration;
			_playerInsideDetection = false;
			if (_postAttackCooldown <= 0f)
			{
				StartPostCooldown();
			}
    }
}
}
