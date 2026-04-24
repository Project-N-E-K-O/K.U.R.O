using Godot;
using Kuros.Actors.Heroes.States;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 一拳冲刺攻击（极简版）：
    /// 1. 开始攻击时快照一次玩家方向；
    /// 2. 在 Active 阶段按该方向持续冲刺；
    /// 3. Active 结束即停止冲刺，其余逻辑交给基础攻击模板。
    /// </summary>
    public partial class EnemyOnePunchAttack : EnemyAttackTemplate
    {
        [ExportCategory("Areas")]
        [Export] public NodePath DetectionAreaPath = new NodePath();
        [Export] public NodePath OnePunchAttackAreaPath = new NodePath();

        [ExportCategory("Dash")]
        [Export(PropertyHint.Range, "10,2000,10")] public float DashSpeed = 600f;
		[Export(PropertyHint.Range, "0,2000,10")] public float DashDistance = 0f;
        [Export] public bool LockFacingDuringDash = true;
		[Export(PropertyHint.Range, "0,500,1")] public float MinDashDistanceBeforeSmash = 24f;
		[Export(PropertyHint.Range, "0,5,0.1")] public float SnapshotDelaySeconds = 0f; // 冲刺前等待一段时间再记录玩家位置
		[Export(PropertyHint.Range, "0,9999,1")] public int OnePunchDmg = 25;

        private const float MinDashDistance = 32f;
		private const float PostCooldownDuration = 0.5f;

        private Area2D? _detectionArea;
		private Area2D? _onePunchArea;
        private EnemyAttackController? _controller;
		private bool _playerInsideDetection;

        private Vector2 _dashDirection = Vector2.Right;
		private Vector2 _dashTarget;
		private bool _isDashing;
		private bool _dashFinalized;
		private bool _skipRecoveryOnePunch;
		private float _postAttackCooldown;
		private bool _pendingCooldownExit;
        private Vector2 _dashPreviousPosition;
        private float _dashDistanceTraveled;
		private bool _canAttemptOnePunch;
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

            _onePunchArea = ResolveArea(OnePunchAttackAreaPath);
            if (_onePunchArea == null)
            {
                _onePunchArea = AttackArea;
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

			// 使用自己的 DetectionArea 或回退到Enemy.DetectionArea
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
			_skipRecoveryOnePunch = false;
			_postAttackCooldown = 0f;
			_pendingCooldownExit = false;
			_dashPreviousPosition = Enemy?.GlobalPosition ?? Vector2.Zero;
			_dashDistanceTraveled = 0f;
			_canAttemptOnePunch = MinDashDistanceBeforeSmash <= 0f;
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

			if (_skipRecoveryOnePunch)
			{
				_skipRecoveryOnePunch = false;
				return;
			}

			if (!_dashFinalized)
			{
				FinishDash();
			}

			if (!_canAttemptOnePunch)
			{
				//StartPostCooldown();
				_pendingCooldownExit = true;
				return;
			}

			if (!TryExecuteOnePunch())
			{
				//StartPostCooldown();
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
				if (currentStateName == "Attack")
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
				// 只在冲刺开始时快照玩家方向；冲刺过程中不再追踪玩家位置点。
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
			float targetDistance;

			if (DashDistance > 0f)
			{
				targetDistance = DashDistance;
			}
			else
			{
				// 未配置 DashDistance 时，沿方向按当前 ActiveDuration 推导冲刺距离。
				float configuredDuration = Mathf.Max(ActiveDuration, 0.05f);
				targetDistance = DashSpeed * configuredDuration;
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
        }

		private bool TryExecuteOnePunch()
        {
			if (Enemy == null) return false;

            var player = Enemy.PlayerTarget;
			if (player == null)
			{
				return false;
			}

            if (!IsPlayerInsideOnePunchZone(player))
            {
				_playerInsideDetection = false;
				return false;
            }

			// 成功命中，施加伤害并击退。
			ApplyOnePunchDamage(player);
			ApplyOnePunchKnockback(player);
			_playerInsideDetection = false;

			// 命中后先保持当前攻击流程，避免动画被立即切到冷却状态。
			return true;
        }

		private void ApplyOnePunchDamage(SamplePlayer player)
		{
			if (Enemy == null) return;

			int damage = Mathf.Max(1, OnePunchDmg);
			player.TakeDamage(damage, Enemy.GlobalPosition, Enemy);
		}

        private bool IsPlayerInsideOnePunchZone(SamplePlayer player)
        {
            if (_onePunchArea != null)
            {
				return player.IsHitByArea(_onePunchArea);
            }

			return player.IsHitByArea(AttackArea);
        }

		private void ApplyOnePunchKnockback(SamplePlayer player)
		{
			if (Enemy == null) return;

			TryApplyPlayerKnockback(
				player,
				KnockbackDistance,
				KnockbackDuration,
				KnockbackSpeed,
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
				Enemy.Velocity = Vector2.Zero;
			}

			_pendingCooldownExit = true;
		}

		private void FinishCooldownState()
		{
			if (Enemy?.StateMachine == null) return;

			if (Enemy.StateMachine.CurrentState?.Name == "Attack")
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

			UpdateDashTravelProgress();

			if (_canAttemptOnePunch && Enemy.PlayerTarget != null && IsPlayerInsideOnePunchZone(Enemy.PlayerTarget))
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
				_skipRecoveryOnePunch = true;
				ForceEnterRecoveryPhase();
				_canAttemptOnePunch = true;
				if (!TryExecuteOnePunch())
				{
					StartPostCooldown();
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
				if (!_canAttemptOnePunch && _dashDistanceTraveled >= MinDashDistanceBeforeSmash)
				{
					_canAttemptOnePunch = true;
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
			_skipRecoveryOnePunch = false;
    }
}
}

