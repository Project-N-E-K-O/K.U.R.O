using Godot;
using Kuros.Actors.Heroes.States;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 冲刺抓取攻击：
	/// 1. 玩家进入检测区域后触发预热；
	/// 2. 预热结束直线冲刺至玩家先前位置；
	/// 3. 冲刺结束若命中，施加冻结并进入逃脱判定；
	/// 4. 逃脱失败造成伤害，任一阶段被打断则立即结束。
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

        [ExportCategory("Effects")]
		[Export(PropertyHint.Range, "0,10,0.1")] public float AppliedFrozenDuration = 5.0f;
        [Export(PropertyHint.Range, "0,1000,1")] public int DamageOnEscapeFailure = 20;
		[Export] public StringName CooldownStateName = "CooldownFrozen";

		[ExportCategory("Escape")]
		[Export(PropertyHint.Range, "0,10,0.1")] public float EscapeWindowSeconds = 0.0f;

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
			PrepareDashTowardsPlayer();
		}

		protected override void OnWarmupStarted()
		{
			base.OnWarmupStarted();
			if (Enemy != null)
			{
				Enemy.Velocity = Vector2.Zero;
			}
        }

        protected override void OnActivePhase()
        {
			if (Enemy == null) return;
			_isDashing = true;
			Enemy.Velocity = _dashDirection * DashSpeed;
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
				return;
			}

			if (!_dashFinalized)
			{
				FinishDash();
			}

			if (HasActiveGrab)
			{
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
				ReleasePlayer(applyDamage: true);
        }
			else
			{
				ReleasePlayer(applyDamage: false);
			}

			if (player != null)
			{
				OnEscapeSequenceFinished(player, escaped);
			}

			if (IsRunning)
			{
				Cancel();
			}
		}

		protected virtual void OnEscapeSequenceStarted(SamplePlayer player) { }

		protected virtual void OnEscapeSequenceFinished(SamplePlayer player, bool escaped) { }

		private void PrepareDashTowardsPlayer()
        {
            if (Enemy == null) return;

			Vector2 dashStart = Enemy.GlobalPosition;
			Vector2 recordedTarget;

			if (Enemy.PlayerTarget != null)
			{
				float targetX = Enemy.PlayerTarget.GlobalPosition.X;
				recordedTarget = new Vector2(targetX, dashStart.Y);
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

            if (!IsPlayerInsideGrabZone(player))
            {
				_playerInsideDetection = false;
				return false;
            }

			_grabbedPlayer = player;
            ApplyFrozenState(player);
			_playerInsideDetection = false;

			_isEvaluatingEscape = true;
			_escapeTimer = AppliedFrozenDuration;
			OnEscapeSequenceStarted(player);
			return true;
        }

        private bool IsPlayerInsideGrabZone(SamplePlayer player)
        {
            if (_grabArea != null)
            {
                return _grabArea.OverlapsBody(player);
            }

            return AttackArea != null && AttackArea.OverlapsBody(player);
        }

        private void ApplyFrozenState(SamplePlayer player)
        {
            var frozenState = player.StateMachine?.GetNodeOrNull<PlayerFrozenState>("Frozen");
            if (frozenState != null)
            {
                frozenState.FrozenDuration = AppliedFrozenDuration;
				frozenState.BeginExternalHold();
            player.StateMachine?.ChangeState("Frozen");
			}
        }

		private void ReleasePlayer(bool applyDamage)
		{
			if (_grabbedPlayer == null) return;

			if (applyDamage)
			{
				_grabbedPlayer.TakeDamage(DamageOnEscapeFailure);
				_grabbedPlayer.StateMachine?.ChangeState("Hit");
			}

			var frozenState = _grabbedPlayer.StateMachine?.GetNodeOrNull<PlayerFrozenState>("Frozen");
			frozenState?.EndExternalHold();

			_grabbedPlayer = null;

			StartPostCooldown();
			_pendingCooldownExit = true;
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

			if (Enemy.PlayerTarget != null && IsPlayerInsideGrabZone(Enemy.PlayerTarget))
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
			_isDashing = false;
			if (forceGrab)
			{
				_skipRecoveryGrab = true;
				ForceEnterRecoveryPhase();
				if (!TryExecuteGrab())
				{
					StartPostCooldown();
					FinishCooldownState();
				}
				return;
			}

			ForceEnterRecoveryPhase();
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
			if (_grabbedPlayer == null && _postAttackCooldown <= 0f)
			{
				StartPostCooldown();
			}
			_skipRecoveryGrab = false;
    }
}
}
