using Godot;
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
    public partial class EnemyKickAttack : EnemyAttackTemplate
    {
        [ExportCategory("Areas")]
        [Export] public NodePath DetectionAreaPath = new NodePath();
        [Export] public NodePath KickAttackAreaPath = new NodePath();

        [ExportCategory("Dash")]
        [Export(PropertyHint.Range, "10,2000,10")] public float DashSpeed = 600f;
		[Export(PropertyHint.Range, "0.05,10,0.05")] public float DashDuration = 0.5f; // 冲刺持续时间（秒）
        [Export] public bool LockFacingDuringDash = true;
		[Export(PropertyHint.Range, "0,5,0.01")] public float MinDashTimeBeforeAttack = 0f; // 允许命中前的最短冲刺时间（秒）
		[Export(PropertyHint.Range, "0,5,0.1")] public float SnapshotDelaySeconds = 0f; // 冲刺前等待一段时间再记录玩家位置
		[Export(PropertyHint.Range, "0,9999,1")] public int KickDamage = 25;

        [ExportCategory("Effects")]
		[Export] public StringName CooldownStateName = "CooldownFrozen";

		private const float PostCooldownDuration = 0.1f;


        private Area2D? _detectionArea;
		private Area2D? _kickArea;
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

	            _kickArea = ResolveArea(KickAttackAreaPath);
	            if (_kickArea == null)
            {
	                _kickArea = AttackArea;
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
			{
				return false;
			}

	            if (!IsPlayerInsideKickAttackZone(player))
            {
				_playerInsideDetection = false;
				return false;
            }

			// 成功命中：伤害 + 击退。
			ApplyKickDamage(player);
	            ApplyKickKnockback(player);
			_playerInsideDetection = false;

			// 命中后先保持当前攻击流程，避免动画被立即切到冷却状态。
			return true;
        }

		private bool IsPlayerInsideKickAttackZone(SamplePlayer player)
        {
	            if (_kickArea != null)
            {
				return player.IsHitByArea(_kickArea);
            }

			return player.IsHitByArea(AttackArea);
        }

		private void ApplyKickDamage(SamplePlayer player)
		{
			if (Enemy == null) return;

			int damage = Mathf.Max(1, KickDamage);
			player.TakeDamage(damage, Enemy.GlobalPosition, Enemy);
		}

		private void ApplyKickKnockback(SamplePlayer player)
		{
			if (Enemy == null) return;

			TryApplyPlayerKnockback(
				player,
				KnockbackDistance,
				KnockbackDuration,
				KnockbackSpeed,
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

			if (Enemy.StateMachine?.CurrentState?.Name != "Attack")
            {
                Enemy.StateMachine?.ChangeState("Attack");
            }
        }

		private void UpdateDashMovement(double delta)
		{
			if (!_isDashing || Enemy == null) return;

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

			if (IsPlayerInsideKickAttackZone(Enemy.PlayerTarget))
			{
				ApplyKickDamage(Enemy.PlayerTarget);
				ApplyKickKnockback(Enemy.PlayerTarget);
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

}
}
