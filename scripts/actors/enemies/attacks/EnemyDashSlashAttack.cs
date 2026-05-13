using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 突刺攻击：实时追踪玩家位置并冲刺，
    /// 在接触 DashSlashArea 时停止冲刺并执行后续逻辑。
    /// 索敌逻辑完全照搬 EnemyMoveAttack，支持实时位置更新。
    /// </summary>
    public partial class EnemyDashSlashAttack : EnemyAttackTemplate
    {
        [ExportCategory("Areas")]
        [Export] public NodePath DetectionAreaPath = new NodePath();
        /// <summary>dash 接触停止范围：小圈，用于检测何时停止冲刺。不配置则回退到 DashSlashAreaPath。</summary>
        [Export] public NodePath DashStopAreaPath = new NodePath();
        /// <summary>slash 伤害范围：大圈，用于 hit 帧检测玩家是否在范围内。</summary>
        [Export] public NodePath DashSlashAreaPath = new NodePath();

        [ExportCategory("Dash")]
        [Export(PropertyHint.Range, "10,2000,10")] public float DashSpeed = 600f;
        [Export] public bool LockFacingDuringDash = true;
        [Export(PropertyHint.Range, "0,5,0.1")] public float MinDashTimeBeforeAttack = 0f;

        [ExportCategory("Strike")]
        [Export(PropertyHint.Range, "1,200,1")] public int StrikeDamage = 12;

        private const float PostCooldownDuration = 1.0f;

        private Area2D? _detectionArea;
        private Area2D? _dashStopArea;   // 小圈：dash 接触停止检测
        private Area2D? _dashSlashArea;  // 大圈：slash 伤害范围检测
        private EnemyAttackController? _controller;
        private bool _playerInsideDetection;

        private Vector2 _dashDirection = Vector2.Right;
        private bool _isDashing;
        private bool _dashFinalized;
        private float _postAttackCooldown;
        private bool _pendingCooldownExit;
        private float _dashTimeElapsed;
        private bool _canAttemptStrike;

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
                GD.PushWarning($"[EnemyDashSlashAttack] DetectionArea not found for {Enemy?.Name ?? Name}, fallback to DetectionRange.");
            }

            _dashSlashArea = ResolveArea(DashSlashAreaPath);
            if (_dashSlashArea == null)
            {
                _dashSlashArea = AttackArea;
            }

            // DashStopArea 回退到 DashSlashArea，未配置时两者相同
            _dashStopArea = ResolveArea(DashStopAreaPath);
            if (_dashStopArea == null)
            {
                _dashStopArea = _dashSlashArea;
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
            _postAttackCooldown = 0f;
            _pendingCooldownExit = false;
            _dashTimeElapsed = 0f;
            _canAttemptStrike = MinDashTimeBeforeAttack <= 0f;
        }

        protected override void OnWarmupStarted()
        {
            base.OnWarmupStarted();
            if (Enemy == null) return;
            Enemy.Velocity = Vector2.Zero;
            // Warmup 阶段立即开始冲刺；ShouldHoldWarmupPhase 将持续挂起直到 dash 结束
            _isDashing = true;
            Vector2 toPlayer = Enemy.PlayerTarget != null
                ? (Enemy.PlayerTarget.GlobalPosition - Enemy.GlobalPosition).Normalized()
                : (Enemy.FacingRight ? Vector2.Right : Vector2.Left);
            if (toPlayer == Vector2.Zero) toPlayer = Enemy.FacingRight ? Vector2.Right : Vector2.Left;
            _dashDirection = toPlayer;
            if (LockFacingDuringDash && _dashDirection.X != 0)
            {
                Enemy.FlipFacing(_dashDirection.X > 0);
            }
            Enemy.Velocity = _dashDirection * DashSpeed;
        }

        protected override void OnActivePhase()
        {
            if (Enemy == null) return;
            // dash 已在 Warmup 结束时停止；Active 阶段等待 slash 动画开始播放（不在此处设置命中窗口）
            // _animationHitReady 在 FinishDash() 时已开启，此处作冗余保障
            Enemy.Velocity = Vector2.Zero;
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
            // Recovery 阶段开启命中窗口：slash 动画 hit 帧在 RecoveryDuration 内触发
            // 比在 OnActivePhase 设置更可靠，避免 Active 窗口过短导致 hit 帧错过
            if (RequireAnimationHitTrigger)
            {
                _animationHitReady = true;
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

        protected override bool ShouldHoldWarmupPhase()
        {
            // Warmup 阶段在冲刺期间无限期挂起；FinishDash 将 _isDashing 置 false 后自动推进到 Active
            return _isDashing;
        }

        protected override bool ShouldHoldRecoveryPhase()
        {
            // Recovery 阶段使用 RecoveryDuration 正常倒计时，不挂起。
            // hit 帧在 RecoveryDuration 窗口内触发伤害；窗口到期后攻击自然结束。
            return false;
        }

        private void UpdateDashMovement(double delta)
        {
            if (!_isDashing || Enemy == null || Enemy.IsDeathSequenceActive || Enemy.IsDead) return;

            // 最短冲刺时间计时
            if (!_canAttemptStrike)
            {
                _dashTimeElapsed += (float)delta;
                if (_dashTimeElapsed >= MinDashTimeBeforeAttack)
                {
                    _canAttemptStrike = true;
                }
            }

            // 实时追踪玩家位置
            if (Enemy.PlayerTarget != null)
            {
                Vector2 toPlayer = Enemy.PlayerTarget.GlobalPosition - Enemy.GlobalPosition;
                if (toPlayer != Vector2.Zero)
                {
                    _dashDirection = toPlayer.Normalized();
                    if (LockFacingDuringDash && _dashDirection.X != 0)
                    {
                        Enemy.FlipFacing(_dashDirection.X > 0);
                    }
                }

                if (_canAttemptStrike && IsPlayerInsideDashStopArea(Enemy.PlayerTarget))
                {
                    FinishDash();
                    return;
                }
            }

            // 持续冲刺
            Enemy.Velocity = _dashDirection * DashSpeed;
        }

        private bool IsPlayerInsideDashStopArea(SamplePlayer player)
        {
            if (_dashStopArea != null)
            {
                return player.IsHitByArea(_dashStopArea);
            }

            return player.IsHitByArea(AttackArea);
        }

        private bool IsPlayerInsideDashSlashArea(SamplePlayer player)
        {
            var targetArea = _dashSlashArea ?? AttackArea;
            if (targetArea == null) return true;

            var shapeNode = targetArea.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            if (shapeNode == null)
            {
                return player.IsHitByArea(targetArea);
            }

            Vector2 center = shapeNode.GlobalPosition;
            Vector2 toPlayer = player.GlobalPosition - center;

            if (shapeNode.Shape is CircleShape2D circle)
            {
                Vector2 scale = shapeNode.GlobalScale;
                float rx = circle.Radius * Mathf.Abs(scale.X);
                float ry = circle.Radius * Mathf.Abs(scale.Y);
                if (rx <= 0f || ry <= 0f) return false;
                float nx = toPlayer.X / rx;
                float ny = toPlayer.Y / ry;
                return nx * nx + ny * ny <= 1f;
            }

            if (shapeNode.Shape is RectangleShape2D rect)
            {
                Vector2 half = rect.Size * 0.5f * shapeNode.GlobalScale.Abs();
                return Mathf.Abs(toPlayer.X) <= half.X && Mathf.Abs(toPlayer.Y) <= half.Y;
            }

            return player.IsHitByArea(targetArea);
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

        private void FinishDash()
        {
            if (Enemy == null) return;
            _isDashing = false;
            _dashFinalized = true;
            Enemy.Velocity = Vector2.Zero;
            // slash 动画在此刻开始播放，立即开启命中窗口
            // 不等到 OnActivePhase/OnRecoveryStarted，避免 hit 帧先于窗口到达
            if (RequireAnimationHitTrigger)
            {
                _animationHitReady = true;
            }
            // _isDashing = false 后 ShouldHoldWarmupPhase 返回 false，
            // Warmup 将在下一个 AdvancePhase 推进到 Active
        }

        protected override void OnAnimationHit()
        {
            if (Enemy == null || Enemy.IsDead || Enemy.IsDeathSequenceActive) return;
            if (Enemy.PlayerTarget == null) return;
            if (!IsPlayerInsideDashSlashArea(Enemy.PlayerTarget)) return;
            ExecuteStrike();
        }

        private void ExecuteStrike()
        {
            if (Enemy == null || Enemy.PlayerTarget == null)
            {
                return;
            }

            Enemy.PlayerTarget.TakeDamage(StrikeDamage, Enemy.GlobalPosition, Enemy);

            // 应用击退
            float distance = Mathf.Max(0f, KnockbackDistance);
            if (distance > 0f || KnockbackSpeed > 0f)
            {
                TryApplyPlayerKnockback(
                    Enemy.PlayerTarget,
                    distance,
                    Mathf.Max(KnockbackDuration, 0.01f),
                    KnockbackSpeed,
                    Enemy.FacingRight ? Vector2.Right : Vector2.Left);
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
                // 进入冷却状态
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
        }
    }
}


