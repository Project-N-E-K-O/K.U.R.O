using Godot;
using Kuros.Actors.Heroes.States;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 冲刺重击攻击：
	/// 1. 玩家进入检测区域后触发预热；
	/// 2. 预热结束直线冲刺至玩家先前位置；
	/// 3. 冲刺结束若命中，施加冻结并进入后摇冷却。
    /// </summary>
    public partial class EnemySmashAttack : EnemyAttackTemplate
    {
        [ExportCategory("Areas")]
        [Export] public NodePath DetectionAreaPath = new NodePath();
        [Export] public NodePath SmashAreaPath = new NodePath();

        [ExportCategory("Dash")]
        [Export(PropertyHint.Range, "10,2000,10")] public float DashSpeed = 600f;
		[Export(PropertyHint.Range, "0,2000,10")] public float DashDistance = 0f;
        [Export] public bool LockFacingDuringDash = true;
		[Export(PropertyHint.Range, "0,500,1")] public float MinDashDistanceBeforeSmash = 24f;
		[Export(PropertyHint.Range, "0,5,0.1")] public float SnapshotDelaySeconds = 0f; // 冲刺前等待一段时间再记录玩家位置

        [ExportCategory("Effects")]
		[Export(PropertyHint.Range, "0,10,0.1")] public float AppliedFrozenDuration = 5.0f;
		[Export] public StringName CooldownStateName = "CooldownFrozen";

		private const float MinDashDistance = 32f;
		private const float PostCooldownDuration = 1.0f;

        private Area2D? _detectionArea;
        private Area2D? _grabArea;
        private EnemyAttackController? _controller;
		private bool _playerInsideDetection;

        private Vector2 _dashDirection = Vector2.Right;
		private Vector2 _dashTarget;
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

            _grabArea = ResolveArea(SmashAreaPath);
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
			_skipRecoveryGrab = false;
			_postAttackCooldown = 0f;
			_pendingCooldownExit = false;
			_dashPreviousPosition = Enemy?.GlobalPosition ?? Vector2.Zero;
			_dashDistanceTraveled = 0f;
			_canAttemptGrab = MinDashDistanceBeforeSmash <= 0f;
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
        }

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
				return;
			}

			if (!_dashFinalized)
			{
				FinishDash();
			}

			if (!_canAttemptGrab)
			{
				StartPostCooldown();
				_pendingCooldownExit = true;
				return;
			}

			if (!TryExecuteGrab())
			{
				StartPostCooldown();
				_pendingCooldownExit = true;
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

		protected override bool ShouldHoldRecoveryPhase()
		{
			return false;
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
				// 没有玩家目标时朝当前朝向的方向冲刺一个固定距离，避免原地。
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

            if (!IsPlayerInsideGrabZone(player))
            {
				_playerInsideDetection = false;
				return false;
            }

			// 成功命中，施加冻结状态并进入后摇冷却。
            ApplyFrozenState(player);
			_playerInsideDetection = false;

			// 命中后先保持当前攻击流程，避免动画被立即切到冷却状态。
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
                frozenState.FrozenDuration = AppliedFrozenDuration;
				player.StateMachine?.ChangeState("Frozen");
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
			if (_postAttackCooldown > 0f) return;

			bool overlaps = _detectionArea.OverlapsBody(Enemy.PlayerTarget);
			if (overlaps && !_playerInsideDetection)
			{
				_playerInsideDetection = true;
				TryRequestAttackFromDetection("Poll");
			}
			else if (!overlaps && _playerInsideDetection)
			{
				_playerInsideDetection = false;
			}
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
				Enemy.GlobalPosition = _dashTarget;
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
					//StartPostCooldown();
					FinishCooldownState();
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
				if (!_canAttemptGrab && _dashDistanceTraveled >= MinDashDistanceBeforeSmash)
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

		protected override void OnAttackFinished()
		{
			base.OnAttackFinished();
			_playerInsideDetection = false;
			if (_postAttackCooldown <= 0f)
			{
				StartPostCooldown();
			}
			_skipRecoveryGrab = false;
    }
}
}
